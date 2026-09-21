import fs from "node:fs";
import path from "node:path";
import { parseArgs } from "node:util";
import Database from "better-sqlite3";
import {
  AutoModel,
  AutoTokenizer,
  env,
  pipeline,
} from "@huggingface/transformers";

const stopWords = new Set([
  "a",
  "about",
  "an",
  "and",
  "between",
  "earlier",
  "find",
  "for",
  "from",
  "in",
  "is",
  "of",
  "on",
  "session",
  "sessions",
  "the",
  "to",
  "with",
]);

const { values } = parseArgs({
  options: {
    "cache-dir": { type: "string" },
    index: { type: "string" },
    models: { type: "string" },
    output: { type: "string" },
    queries: { type: "string" },
  },
});

for (const required of ["cache-dir", "index", "output", "queries"]) {
  if (!values[required]) {
    throw new Error(`Missing required --${required} argument.`);
  }
}

const cacheDirectory = path.resolve(values["cache-dir"]);
const indexPath = path.resolve(values.index);
const outputPath = path.resolve(values.output);
const queriesPath = path.resolve(values.queries);
fs.mkdirSync(cacheDirectory, { recursive: true });
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
env.cacheDir = path.join(cacheDirectory, "models");
env.allowLocalModels = true;
env.allowRemoteModels = true;

const allModels = [
  {
    id: "all-minilm-l6-v2-q8",
    modelId: "Xenova/all-MiniLM-L6-v2",
    revision: "751bff37182d3f1213fa05d7196b954e230abad9",
    adapter: "pipeline",
    batchSize: 32,
    dtype: "q8",
    passagePrefix: "",
    queryPrefix: "",
  },
  {
    id: "e5-small-v2-q8",
    modelId: "Xenova/e5-small-v2",
    revision: "02af79985278377e65c724a76275707cb0333c70",
    adapter: "pipeline",
    batchSize: 24,
    dtype: "q8",
    passagePrefix: "passage: ",
    queryPrefix: "query: ",
  },
  {
    id: "bge-small-en-v1.5-q8",
    modelId: "Xenova/bge-small-en-v1.5",
    revision: "ea104dacec62c0de699686887e3f920caeb4f3e3",
    adapter: "pipeline",
    batchSize: 24,
    dtype: "q8",
    passagePrefix: "",
    queryPrefix:
      "Represent this sentence for searching relevant passages: ",
  },
  {
    id: "granite-small-english-r2-q8",
    modelId:
      "onnx-community/granite-embedding-small-english-r2-ONNX",
    revision: "1dc7835ba0cb9c76a3618d0bf0c427c97671b3c8",
    adapter: "sentence-embedding",
    batchSize: 12,
    dtype: "q8",
    passagePrefix: "",
    queryPrefix: "",
  },
];

const selectedModelIds = new Set(
  (values.models ?? allModels.map(model => model.id).join(","))
    .split(",")
    .map(value => value.trim())
    .filter(Boolean),
);
const models = allModels.filter(model => selectedModelIds.has(model.id));
if (models.length !== selectedModelIds.size) {
  const known = new Set(allModels.map(model => model.id));
  const unknown = [...selectedModelIds].filter(model => !known.has(model));
  throw new Error(`Unknown model(s): ${unknown.join(", ")}`);
}

const queryManifest = JSON.parse(fs.readFileSync(queriesPath, "utf8"));
const queries = queryManifest.queries;
if (!Array.isArray(queries) || queries.length === 0) {
  throw new Error("The query manifest contains no queries.");
}
const eligibleModifiedBefore = queryManifest.eligibleModifiedBefore;
if (!eligibleModifiedBefore) {
  throw new Error(
    "The query manifest must define eligibleModifiedBefore.",
  );
}

const database = new Database(indexPath, {
  fileMustExist: true,
  readonly: true,
});
const blocks = database.prepare(
  `
  SELECT
    id,
    session_id AS sessionId,
    session_name AS sessionName,
    modified_time AS modifiedTime,
    message_number AS messageNumber,
    chunk_number AS chunkNumber,
    speaker,
    body AS text,
    retrieval_text AS retrievalText
  FROM blocks
  ORDER BY id;
  `,
).all();
for (let index = 0; index < blocks.length; index++) {
  blocks[index].vectorIndex = index;
}
const eligibleBlocks = blocks.filter(
  block =>
    Date.parse(block.modifiedTime)
    < Date.parse(eligibleModifiedBefore),
);
const corpusSignature = createCorpusSignature(indexPath, blocks);
const blocksById = new Map(blocks.map(block => [block.id, block]));
const ftsStatements = {
  word: database.prepare(
    `
    SELECT blocks_word.rowid AS id, -bm25(blocks_word, 0.5, 1.0) AS score
    FROM blocks_word
    JOIN blocks ON blocks.id = blocks_word.rowid
    WHERE blocks_word MATCH ?
      AND blocks.modified_time < ?
    ORDER BY bm25(blocks_word, 0.5, 1.0)
    LIMIT 150;
    `,
  ),
  trigram: database.prepare(
    `
    SELECT blocks_trigram.rowid AS id, -bm25(blocks_trigram, 0.5, 1.0) AS score
    FROM blocks_trigram
    JOIN blocks ON blocks.id = blocks_trigram.rowid
    WHERE blocks_trigram MATCH ?
      AND blocks.modified_time < ?
    ORDER BY bm25(blocks_trigram, 0.5, 1.0)
    LIMIT 150;
    `,
  ),
};

console.log(
  `Corpus: ${blocks.length.toLocaleString()} blocks, `
    + `${eligibleBlocks.length.toLocaleString()} eligible, `
    + `from ${indexPath}`,
);

const results = [];
for (const modelConfig of models) {
  console.log();
  console.log(`Model: ${modelConfig.id}`);
  const modelStart = performance.now();
  const embedder = await createEmbedder(modelConfig);
  const loadMilliseconds = performance.now() - modelStart;
  console.log(`Loaded in ${(loadMilliseconds / 1000).toFixed(1)}s.`);

  const embeddingCache = await loadOrCreateEmbeddings(
    modelConfig,
    embedder,
    blocks,
    corpusSignature,
    cacheDirectory,
  );
  const queryResults = [];

  for (const queryDefinition of queries) {
    const query = queryDefinition.text;
    const terms = extractTerms(query);
    const matchExpression = createMatchExpression(terms);

    const wordStart = performance.now();
    const wordHits = ftsStatements.word.all(
      matchExpression,
      eligibleModifiedBefore,
    );
    const wordMilliseconds = performance.now() - wordStart;

    const trigramTerms = terms.filter(term => term.length >= 3);
    const trigramStart = performance.now();
    const trigramHits = trigramTerms.length === 0
      ? []
      : ftsStatements.trigram.all(
          createMatchExpression(trigramTerms),
          eligibleModifiedBefore,
        );
    const trigramMilliseconds = performance.now() - trigramStart;

    const embeddingStart = performance.now();
    const queryEmbeddings = await embedder.embedQueries(
      createSemanticQueries(query),
    );
    const embeddingHits = rankEmbeddings(
      embeddingCache.vectors,
      queryEmbeddings,
      eligibleBlocks,
      150,
    );
    const embeddingMilliseconds =
      performance.now() - embeddingStart;

    const exactStart = performance.now();
    const exactHits = eligibleBlocks
      .map(block => ({
        id: block.id,
        score: scoreExact(query, terms, block),
      }))
      .filter(hit => hit.score > 0)
      .sort((left, right) => right.score - left.score)
      .slice(0, 150);
    const exactMilliseconds = performance.now() - exactStart;

    const fusionStart = performance.now();
    const candidates = fuse(
      wordHits,
      trigramHits,
      embeddingHits,
      exactHits,
      blocksById,
      30,
    );
    const fusionMilliseconds = performance.now() - fusionStart;
    queryResults.push({
      id: queryDefinition.id,
      query,
      category: queryDefinition.category,
      timings: {
        wordMilliseconds,
        trigramMilliseconds,
        embeddingMilliseconds,
        exactMilliseconds,
        fusionMilliseconds,
      },
      candidates,
    });
    console.log(
      `  ${queryDefinition.id}: dense=${embeddingMilliseconds.toFixed(1)}ms, `
        + `top=${candidates[0]?.block.sessionId ?? "none"}:`
        + `${candidates[0]?.block.messageNumber ?? "-"}`,
    );
  }

  results.push({
    id: modelConfig.id,
    modelId: modelConfig.modelId,
    revision: modelConfig.revision,
    dtype: modelConfig.dtype,
    dimensions: embeddingCache.dimensions,
    loadMilliseconds,
    indexMilliseconds: embeddingCache.indexMilliseconds,
    reusedEmbeddings: embeddingCache.reused,
    queries: queryResults,
  });
  await embedder.dispose();
}

database.close();
const report = {
  generatedAt: new Date().toISOString(),
  corpus: {
    indexPath,
    signature: corpusSignature,
    blockCount: blocks.length,
    eligibleBlockCount: eligibleBlocks.length,
    eligibleModifiedBefore,
  },
  queryManifest,
  models: results,
};
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2));
console.log();
console.log(`Candidate report written to ${outputPath}`);

async function createEmbedder(config) {
  if (config.adapter === "pipeline") {
    const extractor = await pipeline(
      "feature-extraction",
      config.modelId,
      {
        device: "cpu",
        dtype: config.dtype,
        revision: config.revision,
      },
    );
    return {
      embedPassages: texts => embedWithPipeline(
        extractor,
        texts.map(text => config.passagePrefix + text),
      ),
      embedQueries: texts => embedWithPipeline(
        extractor,
        texts.map(text => config.queryPrefix + text),
      ),
      dispose: async () => extractor.dispose?.(),
    };
  }

  const tokenizer = await AutoTokenizer.from_pretrained(
    config.modelId,
    {
      revision: config.revision,
    },
  );
  const model = await AutoModel.from_pretrained(
    config.modelId,
    {
      device: "cpu",
      dtype: config.dtype,
      revision: config.revision,
    },
  );
  return {
    embedPassages: texts => embedSentenceOutput(
      tokenizer,
      model,
      texts.map(text => config.passagePrefix + text),
    ),
    embedQueries: texts => embedSentenceOutput(
      tokenizer,
      model,
      texts.map(text => config.queryPrefix + text),
    ),
    dispose: async () => model.dispose?.(),
  };
}

async function embedWithPipeline(extractor, texts) {
  const output = await extractor(texts, {
    normalize: true,
    pooling: "mean",
  });
  try {
    return output.tolist();
  } finally {
    output.dispose?.();
  }
}

async function embedSentenceOutput(tokenizer, model, texts) {
  const inputs = await tokenizer(texts, {
    padding: true,
    truncation: true,
  });
  const output = await model(inputs);
  const normalized = output.sentence_embedding.normalize();
  try {
    return normalized.tolist();
  } finally {
    normalized.dispose?.();
    output.sentence_embedding.dispose?.();
  }
}

async function loadOrCreateEmbeddings(
  config,
  embedder,
  corpusBlocks,
  signature,
  root,
) {
  const modelDirectory = path.join(root, config.id);
  const metadataPath = path.join(modelDirectory, "metadata.json");
  const vectorsPath = path.join(modelDirectory, "vectors.f32");
  if (fs.existsSync(metadataPath) && fs.existsSync(vectorsPath)) {
    const metadata = JSON.parse(
      fs.readFileSync(metadataPath, "utf8"),
    );
    if (
      metadata.modelId === config.modelId
      && (
        metadata.revision === undefined
        || metadata.revision === config.revision
      )
      && metadata.dtype === config.dtype
      && metadata.corpusSignature === signature
      && metadata.blockCount === corpusBlocks.length
    ) {
      const buffer = fs.readFileSync(vectorsPath);
      const vectors = new Float32Array(
        buffer.buffer,
        buffer.byteOffset,
        buffer.byteLength / Float32Array.BYTES_PER_ELEMENT,
      );
      if (
        vectors.length
        === metadata.blockCount * metadata.dimensions
      ) {
        console.log("Reusing cached embeddings.");
        return {
          dimensions: metadata.dimensions,
          indexMilliseconds: metadata.indexMilliseconds,
          reused: true,
          vectors,
        };
      }
    }
  }

  fs.mkdirSync(modelDirectory, { recursive: true });
  let vectors;
  let dimensions;
  const start = performance.now();
  for (
    let offset = 0;
    offset < corpusBlocks.length;
    offset += config.batchSize
  ) {
    const batch = corpusBlocks.slice(
      offset,
      offset + config.batchSize,
    );
    const embeddings = await embedder.embedPassages(
      batch.map(block => block.retrievalText),
    );
    dimensions ??= embeddings[0].length;
    vectors ??= new Float32Array(
      corpusBlocks.length * dimensions,
    );
    for (let index = 0; index < embeddings.length; index++) {
      vectors.set(
        embeddings[index],
        (offset + index) * dimensions,
      );
    }

    const completed = Math.min(
      corpusBlocks.length,
      offset + batch.length,
    );
    if (
      completed === corpusBlocks.length
      || completed % 256 === 0
    ) {
      console.log(
        `  Embedded ${completed.toLocaleString()} of `
          + `${corpusBlocks.length.toLocaleString()} blocks.`,
      );
    }
  }

  const indexMilliseconds = performance.now() - start;
  fs.writeFileSync(
    vectorsPath,
    Buffer.from(
      vectors.buffer,
      vectors.byteOffset,
      vectors.byteLength,
    ),
  );
  fs.writeFileSync(
    metadataPath,
    JSON.stringify(
      {
        modelId: config.modelId,
        revision: config.revision,
        dtype: config.dtype,
        corpusSignature: signature,
        blockCount: corpusBlocks.length,
        dimensions,
        indexMilliseconds,
      },
      null,
      2,
    ),
  );
  console.log(
    `Embedded corpus in ${(indexMilliseconds / 1000).toFixed(1)}s.`,
  );
  return {
    dimensions,
    indexMilliseconds,
    reused: false,
    vectors,
  };
}

function createCorpusSignature(databasePath, corpusBlocks) {
  const stats = fs.statSync(databasePath);
  const textLength = corpusBlocks.reduce(
    (total, block) =>
      total + block.retrievalText.length + block.text.length,
    0,
  );
  return [
    stats.size,
    stats.mtimeMs,
    corpusBlocks.length,
    corpusBlocks.at(-1)?.id ?? 0,
    textLength,
  ].join(":");
}

function rankEmbeddings(
  vectors,
  queryVectors,
  corpusBlocks,
  maximumResults,
) {
  const dimensions = queryVectors[0].length;
  const hits = new Array(corpusBlocks.length);
  for (let blockIndex = 0; blockIndex < corpusBlocks.length; blockIndex++) {
    let maximumScore = Number.NEGATIVE_INFINITY;
    const vectorOffset =
      corpusBlocks[blockIndex].vectorIndex * dimensions;
    for (const queryVector of queryVectors) {
      let score = 0;
      for (let dimension = 0; dimension < dimensions; dimension++) {
        score +=
          vectors[vectorOffset + dimension]
          * queryVector[dimension];
      }
      maximumScore = Math.max(maximumScore, score);
    }
    hits[blockIndex] = {
      id: corpusBlocks[blockIndex].id,
      score: maximumScore,
    };
  }
  hits.sort((left, right) => right.score - left.score);
  return hits.slice(0, maximumResults);
}

function fuse(
  wordHits,
  trigramHits,
  embeddingHits,
  exactHits,
  blocksByKey,
  maximumResults,
) {
  const rrfK = 60;
  const ranks = new Map();
  addChannel(wordHits, "word", 1.1);
  addChannel(trigramHits, "trigram", 0.8);
  addChannel(embeddingHits, "embedding", 1.2);
  addChannel(exactHits, "exact", 1.0);

  const ordered = [...ranks.entries()]
    .map(([id, rank]) => ({
      block: createReportBlock(blocksByKey.get(id)),
      hybridScore: rank.score,
      wordRank: rank.wordRank,
      trigramRank: rank.trigramRank,
      embeddingRank: rank.embeddingRank,
      exactRank: rank.exactRank,
      embeddingSimilarity: rank.embeddingSimilarity,
      exactScore: rank.exactScore ?? 0,
    }))
    .sort(
      (left, right) =>
        right.hybridScore - left.hybridScore
        || Date.parse(right.block.modifiedTime)
          - Date.parse(left.block.modifiedTime),
    );
  const sessionCounts = new Map();
  const results = [];
  for (const result of ordered) {
    const sessionCount =
      sessionCounts.get(result.block.sessionId) ?? 0;
    if (sessionCount === 5) {
      continue;
    }

    sessionCounts.set(
      result.block.sessionId,
      sessionCount + 1,
    );
    results.push(result);
    if (results.length === maximumResults) {
      break;
    }
  }
  return results;

  function addChannel(hits, channel, weight) {
    for (let index = 0; index < hits.length; index++) {
      const hit = hits[index];
      const rank = ranks.get(hit.id) ?? { score: 0 };
      const oneBasedRank = index + 1;
      rank.score += weight / (rrfK + oneBasedRank);
      rank[`${channel}Rank`] = oneBasedRank;
      if (channel === "embedding") {
        rank.embeddingSimilarity = hit.score;
      } else if (channel === "exact") {
        rank.exactScore = hit.score;
      }
      ranks.set(hit.id, rank);
    }
  }
}

function createReportBlock(block) {
  return {
    id: block.id,
    sessionId: block.sessionId,
    sessionName: block.sessionName,
    modifiedTime: block.modifiedTime,
    messageNumber: block.messageNumber,
    chunkNumber: block.chunkNumber,
    speaker: block.speaker,
    text: block.text,
  };
}

function extractTerms(query) {
  const matches =
    query.match(/[A-Za-z][A-Za-z0-9_.-]*|\d+(?:\.\d+)*(?:\.x)?/g)
    ?? [];
  const seen = new Set();
  const terms = [];
  for (const match of matches) {
    const term = match.replace(/^[._-]+|[._-]+$/g, "");
    const key = term.toLowerCase();
    if (
      term.length >= 2
      && !stopWords.has(key)
      && !seen.has(key)
    ) {
      seen.add(key);
      terms.push(term);
    }
  }
  return terms;
}

function createMatchExpression(terms) {
  if (terms.length === 0) {
    throw new Error("At least one search term is required.");
  }
  return terms
    .map(term => `"${term.replaceAll("\"", "\"\"")}"`)
    .join(" OR ");
}

function scoreExact(query, terms, block) {
  const searchableText = `${block.sessionName}\n${block.text}`;
  const matchedTerms = terms.filter(
    term => findTermPosition(searchableText, term) >= 0,
  ).length;
  if (matchedTerms === 0) {
    return 0;
  }

  const coverage = matchedTerms / terms.length;
  let score = matchedTerms * 10 + coverage * 30;
  if (
    searchableText.toLowerCase().includes(query.toLowerCase())
  ) {
    score += 30;
  }

  const positions = terms
    .map(term => findTermPosition(searchableText, term))
    .filter(position => position >= 0)
    .sort((left, right) => left - right);
  if (positions.length >= 2) {
    const span = positions.at(-1) - positions[0];
    score += span <= 80
      ? 25
      : span <= 200
        ? 15
        : span <= 500
          ? 8
          : 0;
  }

  if (isEvidenceQuery(query)) {
    const quantitativeMatches = block.text.match(
      /\b\d+(?:\.\d+)?\s*(?:%|x|ms|msec|s|sec|secs|second|seconds|mb|gb|kb|bytes?)\b/gi,
    ) ?? [];
    score += Math.min(quantitativeMatches.length, 10) * 3;
    const tableRows = block.text
      .split("\n")
      .filter(line => [...line].filter(character => character === "|").length >= 2)
      .length;
    score += Math.min(tableRows, 8) * 2;
  }
  return score;
}

function findTermPosition(content, term) {
  const lowerContent = content.toLowerCase();
  const lowerTerm = term.toLowerCase();
  if (!lowerTerm.endsWith(".x")) {
    return lowerContent.indexOf(lowerTerm);
  }

  const prefix = lowerTerm.slice(0, -1);
  let searchStart = 0;
  while (searchStart < lowerContent.length) {
    const matchStart = lowerContent.indexOf(
      prefix,
      searchStart,
    );
    if (matchStart < 0) {
      return -1;
    }
    const digitIndex = matchStart + prefix.length;
    if (
      digitIndex < lowerContent.length
      && /\d/.test(lowerContent[digitIndex])
    ) {
      return matchStart;
    }
    searchStart = matchStart + prefix.length;
  }
  return -1;
}

function isEvidenceQuery(query) {
  const lowerQuery = query.toLowerCase();
  return [
    "benchmark",
    "compare",
    "comparison",
    "deadlock",
    "duration",
    "elapsed",
    "fault",
    "hang",
    "nfe",
    "performance",
    "regression",
    "timing",
  ].some(indicator => lowerQuery.includes(indicator));
}

function createSemanticQueries(query) {
  if (!isEvidenceQuery(query)) {
    return [query];
  }
  return [
    query,
    "Detailed technical evidence answering this request, including "
      + "measurements, tables, concrete timings or counts, comparison, "
      + "root cause, and conclusions: "
      + query,
  ];
}

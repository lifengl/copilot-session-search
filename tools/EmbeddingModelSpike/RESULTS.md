# Embedding Model Comparison

## Summary

This spike compared four alternative local embedding models with the
`bge-micro-v2` model currently used by Copilot Session Search.

The larger BGE and Granite models improved aggregate retrieval quality on the
evaluated technical-conversation searches. Granite produced the strongest
overall ranking and earliest relevant results, while BGE-small offered nearly
the same recall with a smaller model and somewhat lower indexing cost.

The current micro model remained competitive on some evidence-oriented
searches, so these results do not yet justify changing the production model.

## Privacy

The repository does not contain:

- Search questions
- Conversation excerpts
- Session identifiers or metadata
- Retrieved candidate blocks
- Per-query model judgments
- Embedding vectors or model files

Those artifacts remain in local session storage. This document contains only
aggregate measurements and public model information.

## Methodology

The comparison used:

- A fixed local corpus snapshot containing 19,071 conversation blocks
- Eight representative software-engineering history searches
- Identical chunking and retrieval text for every model
- The production word FTS5, trigram FTS5, exact/proximity/evidence scoring,
  reciprocal-rank fusion, and per-session diversification logic
- 384-dimensional embeddings from every model
- Quantized ONNX model weights for the alternative models

For each search, the top 10 results from every retriever were combined and
deduplicated. This produced 15-19 unique candidate blocks per search. A
model-blind Copilot evaluation then scored every candidate in the shared pool.
The same judgment set was used to calculate each model's metrics.

The evaluation used eight Copilot calls in total. The calls consumed 46,240
input tokens, 16,807 output tokens, and 8 AI credits.

## Models

| Evaluation ID | Model | Revision | Embedding dimensions | Approximate q8 model size |
|---|---|---|---:|---:|
| `bge-micro-v2-i8` | [TaylorAI/bge-micro-v2](https://huggingface.co/TaylorAI/bge-micro-v2) | `72908b7` | 384 | 17 MiB |
| `all-minilm-l6-v2-q8` | [Xenova/all-MiniLM-L6-v2](https://huggingface.co/Xenova/all-MiniLM-L6-v2) | `751bff37182d3f1213fa05d7196b954e230abad9` | 384 | 22 MiB |
| `e5-small-v2-q8` | [Xenova/e5-small-v2](https://huggingface.co/Xenova/e5-small-v2) | `02af79985278377e65c724a76275707cb0333c70` | 384 | 34 MiB |
| `bge-small-en-v1.5-q8` | [Xenova/bge-small-en-v1.5](https://huggingface.co/Xenova/bge-small-en-v1.5) | `ea104dacec62c0de699686887e3f920caeb4f3e3` | 384 | 34 MiB |
| `granite-small-english-r2-q8` | [Granite Small English R2 ONNX](https://huggingface.co/onnx-community/granite-embedding-small-english-r2-ONNX) | `1dc7835ba0cb9c76a3618d0bf0c427c97671b3c8` | 384 | 52 MiB |

## Aggregate quality

Scores of 80 or greater were treated as relevant. Scores of 90 or greater
were counted as strong evidence.

| Model | Mean nDCG@10 | Mean pooled Recall@10 | Mean MRR@10 | Mean score@10 | Relevant blocks | Strong blocks |
|---|---:|---:|---:|---:|---:|---:|
| Granite Small English R2 | **0.748** | **0.667** | **0.760** | 55.8 | **26** | 14 |
| BGE-small-en-v1.5 | 0.724 | 0.665 | 0.442 | **56.8** | **26** | **15** |
| E5-small-v2 | 0.699 | 0.458 | 0.435 | 53.5 | 20 | 10 |
| all-MiniLM-L6-v2 | 0.646 | 0.470 | 0.469 | 50.3 | 21 | 9 |
| BGE-micro-v2 | 0.641 | 0.469 | 0.417 | 48.2 | 19 | 12 |

Compared with BGE-micro-v2:

- Granite improved mean nDCG@10 by approximately 16.7%.
- BGE-small improved mean nDCG@10 by approximately 12.9%.
- Granite and BGE-small each retrieved 26 pooled relevant blocks, compared
  with 19 for BGE-micro.
- BGE-small produced the most strong-evidence blocks.
- Granite's substantially higher MRR indicates that it tended to place a
  relevant result earlier.

No model won every search. BGE-micro remained strongest on one
measurement-focused search, while the larger models performed better on
several technical, causal, and context-sensitive searches.

## Performance

The following indexing times were measured using Transformers.js with q8 ONNX
models on the same 8-core, 16-logical-processor machine:

| Model | Full-corpus indexing time | Warm dense-query time |
|---|---:|---:|
| all-MiniLM-L6-v2 | 9.3 minutes | approximately 43-68 ms |
| E5-small-v2 | 18.2 minutes | approximately 62-78 ms |
| BGE-small-en-v1.5 | 16.3 minutes | approximately 68-88 ms |
| Granite Small English R2 | 19.3 minutes | approximately 62-86 ms |

The existing BGE-micro production index previously took approximately 2.3
minutes to build, including history processing. That measurement used the
.NET production implementation rather than Transformers.js, so it is useful
as a directional comparison rather than a controlled runtime benchmark.

Each alternative model produced a 27.9 MiB float-vector cache during the
spike. The production application stores 384-dimensional vectors in an int8
format at 388 bytes per vector, approximately 7 MiB for this corpus. The
quality effect of applying the production int8 vector format to the
alternative models has not yet been measured.

## Interpretation

The results support two conclusions:

1. Increasing encoder capacity can improve retrieval of technical
   conversations even when lexical and deterministic channels remain
   unchanged.
2. Model training domain matters. Granite performed particularly well on
   technical meaning, causality, and context-sensitive searches. BGE-small
   provided a strong broader improvement at lower model and indexing cost.

MiniLM offered little aggregate improvement over the current micro model.
E5-small performed well on some searches but was less consistent overall.

## Recommendation

Do not replace the production model from this eight-query result alone.

If the experiment continues:

1. Shortlist BGE-small-en-v1.5 and Granite Small English R2.
2. Expand the evaluation set to at least 20-30 searches with human relevance
   labels and deliberately difficult same-topic negatives.
3. Repeat model-blind judging to measure judgment variance.
4. Port one shortlisted model to the .NET indexing path and measure q8 model
   inference with the production int8 vector format.
5. Compare complete index time, incremental update time, peak working set,
   warm-query latency, index size, and retrieval quality before integration.

For a balanced desktop default, BGE-small is currently the stronger
cost-versus-quality candidate. Granite is the more promising candidate when
technical semantic quality is prioritized over initial indexing cost.

## Limitations

- The query set contains only eight searches.
- Relevance was judged by one Copilot pass rather than independent human
  assessors.
- Recall is pooled recall over candidates contributed by the evaluated
  models, not absolute recall over every block in the corpus.
- Alternative models used q8 ONNX weights but float output-vector caches.
- Transformers.js indexing times are not directly equivalent to the .NET
  production implementation.
- The corpus and judgments reflect one developer's local history and may not
  generalize to other users.

# Embedding model comparison spike

This spike compares alternative local embedding models while preserving the
application's word FTS, trigram FTS, deterministic scoring, reciprocal-rank
fusion, and per-session diversification.

See [RESULTS.md](RESULTS.md) for the public-safe summary of the first
eight-query comparison.

The candidate generator uses local ONNX inference through Transformers.js:

- `Xenova/all-MiniLM-L6-v2`
- `Xenova/e5-small-v2`
- `Xenova/bge-small-en-v1.5`
- `onnx-community/granite-embedding-small-english-r2-ONNX`

Model revisions and q8 inference are pinned in `compare.mjs`. Embeddings are
derived from private conversation history and must not be committed. Use the
ignored `local\` directory below, or another location outside the repository,
for query manifests, candidate reports, judgments, vectors, and model caches.

## Generate candidate reports

```powershell
Set-Location tools\EmbeddingModelSpike
npm install

node .\compare.mjs `
  --index "$env:LOCALAPPDATA\CopilotSessionSearch\ai-search-index-v1.sqlite" `
  --queries ".\local\queries.json" `
  --output ".\local\candidate-report.json" `
  --cache-dir ".\local\cache"
```

Use `--models` with a comma-separated model ID list to run a subset.

## Judge pooled candidates

The judge reproduces the current `bge-micro-v2` retrieval directly from the
existing index, combines its top candidates with the alternate reports, and
sends one deduplicated, model-blind candidate pool per query to Copilot.

```powershell
dotnet run --project .\EmbeddingModelJudge.csproj --configuration Release -- `
  "$env:LOCALAPPDATA\CopilotSessionSearch\ai-search-index-v1.sqlite" `
  ".\local\queries.json" `
  ".\local\evaluation.json" `
  ".\local\candidate-report-1.json" `
  ".\local\candidate-report-2.json"
```

The detailed report contains private conversation excerpts and must remain
local. A sibling `.summary.json` file contains only aggregate model metrics,
timings, and Copilot usage.

The pooled metrics are Recall@10, nDCG@10, MRR@10, mean Copilot score, and
counts of blocks scoring at least 80 or 90. They are useful for comparing this
fixed query set, but Copilot judgments are not a substitute for human labels.

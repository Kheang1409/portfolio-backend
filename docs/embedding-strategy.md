# Embedding strategy

## Provider and model

Production RAG uses the existing `IEmbeddingService` application boundary with the Infrastructure-only `GeminiEmbeddingService`. The configured text model is `gemini-embedding-001` with 768 dimensions. It uses Gemini's `RETRIEVAL_QUERY` mode for questions and `RETRIEVAL_DOCUMENT` mode plus the document title for indexed chunks. Google documents cosine similarity for this retrieval use case and requires manual normalization when `gemini-embedding-001` is reduced from its native 3072 dimensions; normalization is therefore applied once to every provider response.

The SHA-256 projection has been renamed `DeterministicTestEmbeddingService`. It remains available for offline tests and non-production hosts, but production dependency injection no longer registers it as semantic retrieval.

## Requests, batching, and resilience

`EmbeddingOptions` controls provider, model, dimensions, batch size, request timeout, provider concurrency, retry count, input-size limit, cache TTLs, embedding version, and target index version. Document chunks use `batchEmbedContents`; response order is validated against request order. A semaphore bounds provider concurrency independently of ingestion-worker concurrency.

HTTP 429 and 5xx responses are transient. Retries use `Retry-After` when present or exponential backoff with jitter, with a finite configured attempt count. Configuration/authorization/bad-input failures are permanent. Every request has a linked timeout and propagates caller cancellation. Empty, oversized, incomplete, zero-magnitude, and wrong-dimension results fail before persistence.

Embedding cache keys contain provider, model, embedding version, purpose, and a normalized-content SHA-256 hash. Raw text is not present in keys. Query entries have a short bounded TTL; document entries have a longer TTL. Model or version changes naturally miss the cache.

Metrics include request duration, batch size, requests, failures, cache hits/misses, rate limits, and reindexed chunks. Text, API keys, authorization headers, and raw vectors are never logged.

## Blue/green reindexing

The active index remains `knowledge-v1` until an administrator stages and validates `knowledge-gemini-001-v2`. Chunk IDs are deterministic over document ID, chunk ordinal, and index version, so interrupted staging is resumable without duplicates. Existing chunks are not deleted.

Protected `RagAdmin` operations are:

- `POST /api/admin/knowledge/indexes/stage`
- `POST /api/admin/knowledge/indexes/activate`
- `POST /api/admin/knowledge/indexes/rollback`

Activation refuses an incomplete target and atomically replaces the active descriptor while retaining the prior descriptor. Rollback swaps them. While the legacy SHA index is active, semantic retrieval is deliberately disabled and hybrid degrades to active-index keyword retrieval; vectors from incompatible spaces are never compared.

The current Mongo store performs a bounded application-side scan (500 chunks) with cosine similarity. This is adequate for the portfolio corpus. The historical Atlas index is 64-dimensional and is not used for the real-vector path; introduce a deployment-specific 768-dimensional Atlas index only if measured scale or latency warrants it.

## Evaluation and activation status

Normal tests never call Gemini. The 10-case semantic challenge set demonstrates that SHA projection is not semantic. Live evaluation is opt-in:

```powershell
$env:RUN_LIVE_EMBEDDING_EVAL='1'
$env:GEMINI_API_KEY='<secret>'
dotnet test KaiAssistant.RagEvaluation/KaiAssistant.RagEvaluation.csproj --no-restore --filter Category=LiveEmbedding --logger "console;verbosity=detailed"
```

On 2026-08-23, the secured provider evaluation succeeded with `gemini-embedding-001`: query and document vectors were 768-dimensional, finite, and had L2 norm `1.000000`. A corpus-relevant paraphrase scored `0.6171`, above the unrelated comparison at `0.4842`. The 10-case semantic challenge fixture achieved Recall@1 `1.000`, Recall@5 `1.000`, and MRR `1.000`; query embedding latency was 172.3 ms mean and 189.9 ms p95 for this small run.

The configured live MongoDB database contained zero `Completed` or `Indexed` documents, so staging produced zero chunks and is explicitly not ready for activation. `knowledge-gemini-001-v2` was not activated. Full keyword/semantic/hybrid comparison, score-distribution tuning, cache effectiveness, production retrieval latency, rollback, and post-activation ingestion remain blocked until an active corpus exists.

The default remains the measured keyword path while the legacy index is active. Deterministic reranking is disabled by default because its measured quality gain is zero; the evaluation variant remains available for comparison after live vectors exist.

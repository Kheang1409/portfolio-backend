# Embedding strategy

## Provider and model

The Infrastructure layer implements `IEmbeddingService` with Gemini `gemini-embedding-001` at 768 dimensions. It uses `RETRIEVAL_DOCUMENT` for resume chunks and `RETRIEVAL_QUERY` for assistant questions. Provider responses are normalized before storage and cosine comparison.

Semantic indexing is optional. If embedding generation fails during bootstrap, the backend keeps keyword-searchable chunks and chat falls back to keyword retrieval.

## Requests and caching

`EmbeddingOptions` controls the provider, model, dimensions, request timeout, retry count, input-size limit, cache TTLs, and pipeline version. Embedding cache keys contain a content hash and provider metadata, not raw text.

The active semantic search scans up to 500 chunks in MongoDB. This is adequate for the current portfolio scale; use measured latency and corpus size to decide whether a vector index is needed.

## Data source

Resume records stored in MongoDB are the source for semantic documents and public portfolio content. The startup bootstrap indexes the latest resume record and skips it when its content hash has not changed. Changing the configured embedding version requires reindexing existing resume chunks through the deployment data process.

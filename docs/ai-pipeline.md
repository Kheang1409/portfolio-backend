# AI Pipeline

## End-to-End Orchestration Flow

1. Input sanitization and prompt-injection checks run first.
2. Conversation memory is loaded using conversation ID.
3. Conversation summary is appended when available.
4. RAG context is retrieved from knowledge documents.
5. Semantic cache lookup runs against embedding similarity.
6. If cache miss, orchestrator invokes AI provider.
7. Provider fallback triggers when primary fails or times out.
8. Output is sanitized and persisted to memory/cache.

## RAG Pipeline

- Query is embedded via deterministic embedding service.
- Knowledge documents are scanned and scored by cosine similarity.
- Top-k snippets above similarity threshold are selected.
- Prompt is augmented with concise context lines.
- Model is asked to ground response in provided context.

## Semantic Caching Logic

- Cache key hot-path: hashed prompt to Redis/memory cache.
- Deep path: compare query embedding against recent cached prompt embeddings in MongoDB.
- Hit criteria: best similarity must exceed configured threshold.
- On hit: cached response returned with cache metadata.
- On miss: response generated and written back to hot + semantic stores.

## Fallback Strategy

- Primary provider selected from feature flag + orchestration options.
- Secondary provider used when primary throws, times out, or is unavailable.
- Fallback events are emitted to logs and metrics.
- Response metadata indicates fallback usage.

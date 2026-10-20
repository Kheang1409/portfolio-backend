# AI Pipeline

## End-to-End Orchestration Flow

1. The API validates the request and resolves its conversation ID.
2. The orchestrator loads the bounded conversation history.
3. `AssistantService` selects resume context and retrieves RAG snippets before checking its prompt-level response cache.
4. The prompt cache key includes the question, history, selected resume/RAG context, and request context.
5. The Gemini gateway streams a response on a cache miss; the orchestrator persists the exchange.

## RAG Pipeline

- Keyword retrieval runs first. If keyword evidence is weak, compatible Gemini embeddings and Mongo vector search are used and fused with keyword results.
- A selector applies evidence, chunk-count, estimated-token, and duplicate limits.
- Selected RAG snippets are combined with resume context before prompt construction.
- When no RAG evidence is selected, the prompt can still include relevant resume context.

Retrieved material remains untrusted data. Source identity is retained through selection, and weak/no evidence leaves the original question unaugmented so the model can state that portfolio evidence is insufficient.

## Response Caching

- The active assistant uses exact response caching, not semantic response reuse.
- The active prompt-level key includes the question, conversation history, selected resume/RAG context, and request context.
- The outer orchestrator response cache is intentionally not used for chat because it cannot account for retrieved evidence.
- `SemanticCacheService` is registered but is not called from the active assistant request path.

## Model selection and fallback

- The active chat flow uses `AssistantService`, `ModelOrchestrator`, and the Gemini gateway.
- The gateway can select among configured Gemini model names; the OpenAI provider is a stub and is not registered for chat fallback.
- The separate `AiOrchestratorService` provider-fallback path is not used by the chat endpoint.
- The enhanced tool-calling orchestrator is not registered by active startup; chat does not execute model-selected tools or perform agentic retrieval loops.

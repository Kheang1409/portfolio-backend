# AI Flow Diagram

```mermaid
sequenceDiagram
    participant C as Client
    participant A as AssistantController
    participant O as AssistantOrchestrator
    participant RC as Prompt/context response cache
    participant S as AssistantService
    participant R as RagService
    participant M as ModelOrchestrator
    participant G as GeminiGateway
    participant DB as MongoDB

    C->>A: POST /api/assistant
    A->>O: OrchestrateStreamAsync
    O->>DB: Load bounded conversation history
    O->>S: StreamAsync(question, history)
    S->>DB: Load relevant resume context
    S->>R: BuildAugmentedPromptAsync(question)
    R->>DB: Keyword retrieval; semantic fallback when needed
    DB-->>R: Retrieved chunks
    R-->>S: Selected RAG snippets
    S->>RC: Lookup by question, history, and selected context
    alt Prompt/context cache hit
        RC-->>S: Cached answer
    else Prompt/context cache miss
        S->>M: Build model decision
        M->>G: Generate/stream with configured Gemini model
        G-->>S: Generated response
        S->>RC: Cache by question, history, and selected context
    end
    S-->>O: Response stream
    O->>DB: Persist conversation exchange
    O-->>A: NDJSON response stream

    A-->>C: NDJSON events
```

The outer orchestrator response cache is not used for chat because it cannot account for retrieved evidence. The active flow also does not invoke `SemanticCacheService`, cross-provider fallback, or model-directed tool calls.

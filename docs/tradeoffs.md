# Tradeoffs

## Key Design Decisions

### Clean Architecture Boundaries

- Decision: strict separation between domain, application abstractions, infrastructure implementations, and API transport.
- Benefit: testability, modularity, and safer refactoring.
- Tradeoff: additional interface and wiring overhead.

### MongoDB over Relational Database

- Decision: document model for resumes, knowledge docs, conversation records, and flexible AI metadata.
- Benefit: schema flexibility and fast iteration for AI-centric payloads.
- Tradeoff: fewer relational guarantees and join semantics versus relational designs.

### Redis as Distributed Control Plane

- Decision: Redis for rate limiting, idempotency coordination, and low-latency cache paths.
- Benefit: high-throughput shared state across stateless nodes.
- Tradeoff: added operational dependency and failure-mode complexity.

### Multi-Provider AI Orchestration

- Decision: explicit orchestrator with fallback capability.
- Benefit: improved availability and operational resilience.
- Tradeoff: increased policy complexity and provider drift management.

### Semantic Cache + RAG Combination

- Decision: use semantic reuse before expensive model calls while grounding prompts with retrieved context.
- Benefit: lower cost/latency and better relevance for repeat/similar queries.
- Tradeoff: embedding quality and threshold tuning become system-level concerns.

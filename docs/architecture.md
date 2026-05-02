# Architecture

## High-Level Overview

KaiAssistant is a production-grade AI assistant backend built on .NET 10 with layered architecture and operational controls for high-scale workloads.

Core runtime flow:

1. Request enters API middleware pipeline.
2. Request is validated, correlated, and rate-limited.
3. Assistant application service executes AI flow.
4. Infrastructure integrations provide RAG, cache, provider orchestration, persistence, and messaging.
5. Response is returned with structured telemetry and trace context.

## Clean Architecture

### Domain Layer

- Contains core entities and value-centric behaviors.
- No dependency on API, infrastructure, or external transport frameworks.
- Examples: resume, project, outbox message, AI knowledge entities.

### Application Layer

- Defines use-case interfaces, orchestration contracts, command/query handlers, validators, and DTO translation.
- Depends on abstractions only.
- No direct implementation details for Redis, MongoDB, SMTP, or RabbitMQ.

### Infrastructure Layer

- Implements application abstractions.
- Contains integration code for MongoDB, Redis, RabbitMQ, Gemini provider, and hosted processing loops.
- Encapsulates resilience and external dependency behavior.

### API Layer

- Thin HTTP transport and middleware composition.
- No domain/business decision logic beyond request/response boundaries and HTTP concerns.
- Delegates assistant behavior to application services.

## Component Responsibilities

- AssistantController: single streaming endpoint for assistant chat.
- AssistantService: orchestrates prompt safety, memory, RAG, semantic cache, and model generation.
- AiOrchestratorService: provider routing and fallback execution.
- RagService: retrieval of relevant knowledge snippets.
- SemanticCacheService: embedding-based response reuse.
- ConversationService: short-term memory and summary rollover.
- OutboxProcessorHostedService: reliable asynchronous event delivery.
- RedisRateLimitingMiddleware: distributed request throttling.
- GlobalExceptionMiddleware: consistent API error contracts.

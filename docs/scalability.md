# Scalability

## Current runtime

- MongoDB stores resumes, conversations, visitor events, and searchable resume chunks.
- Redis-backed services support distributed rate limiting and cache operations.
- RAG retrieval is bounded to 500 MongoDB chunks, appropriate for the current portfolio scale.

## Limits before scaling

- RabbitMQ/outbox processing is not registered on the active startup path.
- Measure retrieval latency and database load against the intended deployment before increasing corpus or traffic substantially.
- AI provider latency and availability remain external dependencies. The active chat path selects among configured Gemini models.

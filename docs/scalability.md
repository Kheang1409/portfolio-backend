# Scalability

## Current runtime

- The API uses MongoDB for resumes, conversations, knowledge documents, and ingestion jobs.
- Redis-backed services support rate limiting and cache operations, with configured local fallbacks.
- The knowledge ingestion worker uses Mongo leases to coordinate job claims.
- RAG vector retrieval currently scans a bounded maximum of 500 chunks in the application; this is appropriate only for the small portfolio corpus.

## Limits before scaling

- AWS SAM configures shared, encrypted S3 storage for ingestion files. Local storage remains intended for development only.
- Knowledge administration routes are JWT-protected and require the configured `RagAdmin` role.
- RabbitMQ/outbox processing is not registered on the active startup path.
- No production capacity claim is made for current retrieval, cache, or worker settings; measure with the intended deployment and corpus before increasing traffic.
- AI provider latency and availability remain external dependencies. The active chat path selects among configured Gemini models; it does not use a registered secondary provider.

## Ingestion worker behavior

`DocumentIngestionWorker` claims Mongo jobs with an owner-specific lease and admits no more than `RagIngestion:MaxConcurrency` active jobs per instance. Active leases are renewed conditionally; expired leases can be reclaimed by another worker because uploaded files are stored in shared S3 when running in AWS SAM. Shutdown stops new claims and awaits admitted tasks.

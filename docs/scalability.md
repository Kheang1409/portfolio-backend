# Scalability

## Horizontal Scale Model

- Compute: API is compatible with AWS Lambda hosting and stateless web scaling.
- Cache and control-plane state: Redis supports distributed rate limiting, telemetry counters, and idempotency stores.
- Durable state: MongoDB stores resumes, outbox, conversations, knowledge docs, model health, and evaluations.

## Scale Characteristics

- Stateless API nodes can scale out independently.
- Concurrency-sensitive flows (outbox leasing, idempotency) use distributed coordination.
- Read-heavy assistant workloads benefit from semantic cache and retrieval filtering.

## Bottlenecks and Mitigations

- AI provider latency:
  - Mitigation: timeout + retries + provider fallback + cache reuse.
- Mongo retrieval pressure:
  - Mitigation: targeted indexes, projection queries, candidate limits.
- Redis saturation under spikes:
  - Mitigation: endpoint-specific limits, sliding windows, telemetry visibility.
- Outbox backlog growth:
  - Mitigation: leasing, partitioning, max concurrency, dead-letter after max attempts.

## Runtime Scaling Notes

- Enable Redis and RabbitMQ in production to avoid single-node fallbacks.
- Tune endpoint-specific rate limits by traffic profile.
- Increase outbox partitions and consumers for high event throughput.

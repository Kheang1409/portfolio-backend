# Resilience

## Protection Strategies

- Retries:
  - AI orchestrator execution retries transient failures.
  - Mongo read/write providers retry transient Mongo exceptions.
  - RabbitMQ and SMTP publishers apply bounded retry with backoff.
- Circuit breakers:
  - AI orchestrator, RabbitMQ publisher, SMTP sender include circuit breaking.
- Timeouts:
  - AI provider invocation bounded by configured provider timeout.
  - HTTP and integration operations use explicit timeout controls.
- Fallbacks:
  - Provider fallback from primary to secondary model provider.
  - Rate limiter and telemetry have controlled in-memory fallbacks when Redis is unavailable.

## Failure Scenarios

- Primary AI model unavailable:
  - System triggers fallback provider and records fallback metric/log.
- Redis unavailable:
  - Request limits and telemetry degrade to bounded local fallback when strict mode disabled.
- RabbitMQ unavailable:
  - Outbox processing retries and retains message for later replay.
- Processing poison messages:
  - Outbox dead-letters after max attempts.
- Slow downstream dependencies:
  - Timeout policies fail fast and preserve API responsiveness.

## Observability for Failure Analysis

- Structured logs include correlation and trace context.
- OpenTelemetry spans expose nested operation timings.
- Dashboard-ready counters/histograms capture fallback rates, cache behavior, and outbox health.

## Durable ingestion recovery

Transient ingestion failures return jobs to `Queued` with exponential backoff and jitter. Jobs exceeding `MaxAttempts` become `DeadLetter` with bounded diagnostic error text. A worker crash leaves a lease that expires, allowing another instance to recover the job. One job failure is isolated from other active jobs.

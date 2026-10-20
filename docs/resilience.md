# Resilience

## Protection Strategies

- Retries:
  - AI orchestrator execution retries transient failures.
  - Mongo read/write providers retry transient Mongo exceptions.
  - SMTP publishing applies bounded retry with backoff.
- Circuit breakers:
  - SMTP sender includes circuit breaking; the active chat path uses Gemini gateway/model resilience.
- Timeouts:
  - AI provider invocation bounded by configured provider timeout.
  - HTTP and integration operations use explicit timeout controls.
- Fallbacks:
  - The active chat path can fall back among configured Gemini model names; a secondary provider is not registered for chat.
  - Rate limiter and telemetry have controlled in-memory fallbacks when Redis is unavailable.

## Failure Scenarios

- A configured Gemini model is unavailable:
  - The Gemini gateway can try another configured model and records model health/failure state.
- Redis unavailable:
  - Request limits and telemetry degrade to bounded local fallback when strict mode disabled.
- Slow downstream dependencies:
  - Timeout policies fail fast and preserve API responsiveness.

## Observability for Failure Analysis

- Structured logs include correlation and trace context.
- OpenTelemetry spans expose nested operation timings.
- Dashboard-ready counters/histograms capture fallback rates, cache behavior, and outbox health.

## Ingestion job recovery

Transient ingestion failures return jobs to `Queued` with exponential backoff and jitter. Jobs exceeding `MaxAttempts` become `DeadLetter` with bounded diagnostic error text. A worker crash leaves a lease that expires, allowing another worker to recover the job. This recovery applies to Mongo job state; AWS SAM stores uploaded files in shared S3, while local storage is instance-local.

# Projects and Architecture

## AI-Powered Portfolio

Personal project, February 2025. Built and integrated AI-powered features using LLM APIs for dynamic content and richer user interaction. The portfolio reports a 35% improvement in session duration for more than 150 weekly users.

## Angkor Milk Meal App

Freelance project for AngkorMilk, September 2023. Collaborated with more than 500 users to optimize reporting queries and deliver faster operational insights.

## KaiAssistant API

The portfolio backend is a .NET 10 API with layered domain, application, infrastructure, and API boundaries. It supports streaming assistant responses, multiple AI providers with timeout and fallback behavior, conversation memory, semantic caching, durable MongoDB state, Redis-backed controls, observability, and a versioned retrieval pipeline.

## Reliability and observability

The backend includes an outbox reliability layer with leases, idempotency, retries, backoff, and dead-lettering. OpenTelemetry traces and structured logs cover AI routing, retrieval, cache behavior, ingestion, and operational request metrics.

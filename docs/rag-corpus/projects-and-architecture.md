# Projects and Architecture

## AI-Powered Portfolio

Personal project, February 2025. Built and integrated AI-powered features using LLM APIs for dynamic content and richer user interaction. The portfolio reports a 35% improvement in session duration for more than 150 weekly users.

## Angkor Milk Meal App

Freelance project for AngkorMilk, September 2023. Collaborated with more than 500 users to optimize reporting queries and deliver faster operational insights.

## KaiAssistant API

The portfolio backend is a .NET 10 API with layered domain, application, infrastructure, and API boundaries. It supports streaming assistant responses, conversation memory, keyword-first hybrid RAG with optional Gemini semantic retrieval, MongoDB persistence, Redis-backed controls, observability, and versioned retrieval indexes. The active chat path selects among configured Gemini models; it does not use cross-provider fallback or semantic response caching.

## Reliability and observability

The repository contains an outbox implementation, but it is not registered on the active startup path. Knowledge ingestion uses Mongo-backed job leases; local development uses instance-local file storage, while AWS SAM configures shared S3 storage. Admin upload and index operations require a JWT with the configured `RagAdmin` role. OpenTelemetry traces and structured logs cover AI routing, retrieval, cache behavior, ingestion, and operational request metrics.

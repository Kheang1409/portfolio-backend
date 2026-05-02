# KaiAssistant API

KaiAssistant API is the .NET 10 backend for the portfolio app.

It focuses on the routes and services that the frontend actually uses:

- assistant streaming through `POST /api/assistant`
- contact form processing through `POST /api/contacts`
- visitor analytics ingestion through `POST /api/visits`
- resume retrieval and creation through `POST` and `GET /api/resumes/*`
- health checks through `/api/health`, `/health/ready`, and `/health/live`
- internal diagnostics and simulation routes under `/ops/*`

## Architecture

- Domain layer for core entities.
- Application layer for use cases, DTOs, and orchestration contracts.
- Infrastructure layer for persistence, AI providers, cache, event bus, and external services.
- API layer for controllers, middleware, and startup wiring.

## Runtime Notes

- The assistant controller returns a streaming NDJSON response from a single `POST /api/assistant` endpoint.
- The backend uses Serilog, OpenTelemetry, CORS, response compression, output caching, authentication, and custom middleware.
- Health checks are split between the controller health route and app-level ready/live probes.
- The compose file does not provision MongoDB, so a reachable MongoDB instance is still required for data-backed features.

## Quick Start

### Prerequisites

- .NET SDK 10.
- Docker Desktop.
- MongoDB, if you want the backend features that read or write persisted data.
- Redis, if you want cache-backed flows outside the minimal local setup.

### Environment

Copy the root template and fill in the required values:

```bash
cp ../.env.example ../.env
```

Key variables:

- `GEMINI_API_KEY` - required for assistant features.
- `MONGODB_CONNECTIONSTRING` - MongoDB connection string.
- `MONGODB_DATABASE` - MongoDB database name.
- `REDIS__CONNECTIONSTRING` - Redis connection string.
- `SMTP_*` - optional contact email configuration.
- `ALLOWED_ORIGINS` - CORS origins.

### Run With Docker Compose

```bash
cd ..
docker-compose up -d --build
```

This starts the backend, Redis, and frontend containers. The backend is exposed on `http://localhost:5000` and container port `8080`.

### Run Locally

```bash
cd backend
dotnet restore KaiAssistant.sln
dotnet build KaiAssistant.sln
dotnet run --project KaiAssistant.API
```

### Run Tests

```bash
cd backend
dotnet test KaiAssistant.Tests/KaiAssistant.Tests.csproj
```

## API Examples

### Assistant Stream

```bash
curl -X POST http://localhost:5000/api/assistant \
  -H "Content-Type: application/json" \
  -d '{"message":"Explain the architecture in 3 bullets."}'
```

The response is NDJSON, so each line is an individual stream chunk.

### Assistant Stream With Conversation Memory

```bash
curl -X POST http://localhost:5000/api/assistant \
  -H "Content-Type: application/json" \
  -d '{
    "message":"My name is Alice",
    "context":{
      "metadata":{
        "sessionId":"browser-session-123"
      }
    }
  }'
```

Then ask a follow-up with the same `sessionId`:

```bash
curl -X POST http://localhost:5000/api/assistant \
  -H "Content-Type: application/json" \
  -d '{
    "message":"What is my name?",
    "context":{
      "metadata":{
        "sessionId":"browser-session-123"
      }
    }
  }'
```

The backend resolves or creates a conversation from `sessionId` when `conversationId` is not explicitly sent.

### Contact Form

```bash
curl -X POST http://localhost:5000/api/contacts \
  -H "Content-Type: application/json" \
  -d '{"name":"Kai","email":"kai@example.com","message":"Hello"}'
```

### Visitor Tracking

```bash
curl -X POST http://localhost:5000/api/visits \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"demo","path":"/","userAgent":"curl"}'
```

### Resume Lookup

```bash
curl http://localhost:5000/api/resumes/latest
```

### Health Checks

```bash
curl http://localhost:5000/api/health
curl http://localhost:5000/health/ready
curl http://localhost:5000/health/live
```

## Documentation Index

- `docs/architecture.md`
- `docs/ai-pipeline.md`
- `docs/scalability.md`
- `docs/resilience.md`
- `docs/tradeoffs.md`
- `docs/api-examples.md`
- `docs/diagrams/system-diagram.md`
- `docs/diagrams/ai-flow-diagram.md`

## Troubleshooting

- If assistant requests fail, confirm `GEMINI_API_KEY` is present and the backend can reach the configured Gemini endpoint.
- If data-backed endpoints fail, confirm MongoDB is reachable and the connection string/database name are correct.
- If cache-related flows fail, confirm Redis is reachable and `REDIS__CONNECTIONSTRING` points to the right instance.
- If `/ops/*` routes return 404 in production, confirm ops are enabled in configuration.
- If the assistant does not remember prior chat in local testing, verify requests include `context.metadata.sessionId` and that MongoDB is reachable.

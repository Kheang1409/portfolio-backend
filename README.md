# KaiAssistant API

> Runtime note: the active assistant uses repository-corpus, keyword-first RAG with optional semantic retrieval and a prompt/context-aware exact response cache. Mongo-backed ingestion services and a worker are registered. Local development defaults to instance-local storage; AWS SAM production uses a private, encrypted S3 bucket. Knowledge administration uses JWT Bearer with a required `RagAdmin` role. RabbitMQ/outbox publishing is not part of the active startup path.

KaiAssistant API is the .NET 10 backend for the portfolio app.

It focuses on the routes and services that the frontend actually uses:

- assistant streaming through `POST /api/assistant`
- contact form processing through `POST /api/contacts`
- visitor analytics ingestion through `POST /api/visits`
- resume retrieval through `GET /api/resumes/latest`
- health checks through `/health/ready` and `/health/live`

## Retrieval and knowledge

The assistant uses repository-owned portfolio knowledge from `docs/rag-corpus/`.

```text
query -> keyword retrieval -> strong evidence -> bounded context -> Gemini stream
                           weak evidence -> semantic fallback
```

`KnowledgeBootstrapHostedService` runs once at startup. It hashes supported corpus files, skips unchanged sources, writes keyword chunks to MongoDB, and optionally creates Gemini embeddings for changed content. Embedding failures keep keyword knowledge usable.

`IDocumentIngestionService` is idempotent on a normalized SHA-256 content hash. It normalizes text, splits near sentence boundaries with configurable overlap, generates embeddings, and writes chunks and vectors. Configure `RagIngestion:StorageProvider` as `Local` or `S3`; S3 requires `RagIngestion:S3Bucket` and uses the AWS SDK credential chain. Local development stores files under `App_Data/knowledge`. AWS SAM provisions a private encrypted bucket and grants the Lambda role read/write access.

Knowledge upload and index-management routes require a JWT whose issuer and audience match `RagAdminAuthentication:Authority` and `RagAdminAuthentication:Audience`, and whose configured role claim contains `RagAdmin`. Set `RagAdminAuthentication:RoleClaimType` if the identity provider uses a claim name other than `role`. Production startup rejects missing or non-HTTPS issuer configuration.

For AWS SAM deployments, configure the GitHub Actions repository variables `RAG_ADMIN_JWT_AUTHORITY` and `RAG_ADMIN_JWT_AUDIENCE`; set `RAG_ADMIN_JWT_ROLE_CLAIM_TYPE` if needed. Local development can set the corresponding `RAGADMINAUTHENTICATION__AUTHORITY`, `RAGADMINAUTHENTICATION__AUDIENCE`, and `RAGADMINAUTHENTICATION__ROLECLAIMTYPE` environment variables.

Retrieved content is explicitly marked as untrusted reference data in the model prompt. System and application policy instructions take precedence over both user input and retrieved text.

Retrieval quality is regression-tested offline across keyword, semantic, hybrid, and reranked modes. Run `dotnet test KaiAssistant.RagEvaluation/KaiAssistant.RagEvaluation.csproj --no-restore`; measured baselines and interpretation are in [docs/rag-evaluation.md](docs/rag-evaluation.md).

Real semantic embeddings use Gemini `gemini-embedding-001` at 768 dimensions. Configure `GEMINI_API_KEY`, then stage and evaluate the versioned index before activation. Until a compatible Gemini index is active—or during a transient embedding outage—retrieval safely uses keyword evidence. See [docs/embedding-strategy.md](docs/embedding-strategy.md).

Dependency auditing and supply-chain decisions are documented in [docs/dependency-security.md](docs/dependency-security.md).

## Architecture

- Domain layer for core entities.
- Application layer for use cases, DTOs, and orchestration contracts.
- Infrastructure layer for persistence, Gemini integration, cache, and external services.
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

### Environment

Copy the root template and fill in the required values:

```bash
cp ../.env.example ../.env
```

Key variables:

- `GEMINI_API_KEY` - required for assistant features.
- `MONGODB_CONNECTIONSTRING` - MongoDB connection string.
- `MONGODB_DATABASE` - MongoDB database name.
- `SMTP_*` - optional contact email configuration.
- `ALLOWED_ORIGINS` - CORS origins.

### Run With Docker Compose

```bash
cd ..
docker-compose up -d --build
```

This starts the backend and frontend containers. The backend is exposed on `http://localhost:5000` and container port `8080`.

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
- `docs/rag-architecture.md`
- `docs/rag-evaluation.md`
- `docs/embedding-strategy.md`
- `docs/corpus-management.md`
- `docs/rag-corpus-manifest.json`
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

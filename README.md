# KaiAssistant API

> Runtime note: the assistant builds searchable knowledge from the latest resume stored in MongoDB. Contact and visit data are also persisted in MongoDB. RabbitMQ/outbox publishing is not part of the active startup path.
KaiAssistant API is the .NET 10 backend for the portfolio app.

It focuses on the routes and services that the frontend actually uses:

- assistant streaming through `POST /api/assistant`
- contact form processing through `POST /api/contacts`
- visitor analytics ingestion through `POST /api/visits`
- resume retrieval through `GET /api/resumes/latest`
- health checks through `/health/ready` and `/health/live`

## Retrieval and knowledge

The assistant builds searchable knowledge from the latest resume document stored in MongoDB.

```text
query -> keyword retrieval -> strong evidence -> bounded context -> Gemini stream
                           weak evidence -> semantic fallback
```

`KnowledgeBootstrapHostedService` runs once at startup. It hashes the latest resume content, skips unchanged data, writes keyword chunks to MongoDB, and optionally creates Gemini embeddings. Embedding failures keep keyword knowledge usable.

Retrieved content is explicitly marked as untrusted reference data in the model prompt. System and application policy instructions take precedence over both user input and retrieved text.


Real semantic embeddings use Gemini `gemini-embedding-001` at 768 dimensions. Configure `GEMINI_API_KEY` to enable semantic retrieval. During an embedding outage, retrieval falls back to keyword evidence. See [docs/embedding-strategy.md](docs/embedding-strategy.md).

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
- `docs/embedding-strategy.md`
- `docs/corpus-management.md`
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
# PayPal donation checkout

Donation payments use the PayPal Orders v2 API. The browser receives only the PayPal client ID and redirects/card entry remain inside PayPal Checkout; this application never receives card numbers, security codes, or expiration values. PayPal may show eligible cards based on account approval, buyer region, and transaction risk; “all cards” cannot be guaranteed by the application.

## Sandbox setup

1. Create a PayPal Developer Sandbox REST app and configure backend-only `PayPal__ClientId` and `PayPal__ClientSecret` secrets. Keep `PayPal__Environment=sandbox`.
2. Set `PayPal__Currency`, donation minimum/maximum, optional `PayPal__MerchantId`, `PayPal__WebhookId`, and `PayPal__EmailRetentionDays` in backend configuration. Configure SMTP through the existing `SMTP_*` settings if optional receipts are desired.
3. Add the HTTPS endpoint `{API_BASE_URL}/api/donations/webhook` to the matching Sandbox REST app. Subscribe to `PAYMENT.CAPTURE.COMPLETED`, `PAYMENT.CAPTURE.DENIED`, `PAYMENT.CAPTURE.REFUNDED`, and `PAYMENT.CAPTURE.REVERSED`, then store its generated webhook ID as `PayPal__WebhookId`.
4. Run the frontend and backend locally, use Sandbox buyer accounts/cards, and verify the order, capture, leaderboard opt-in, and receipt outbox. Do not use live credentials or real payments for automated tests.

The production GitHub Actions deployment also requires repository secrets `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, and `PAYPAL_WEBHOOK_ID` (the PayPal-issued webhook ID, not the listener URL). `PAYPAL_MERCHANT_ID` is optional. These values are passed to the SAM parameters that configure the Lambda; setting Lambda variables manually is not a substitute for setting the deployment secrets because a stack update can replace function configuration.

## Production deployment

Store PayPal and SMTP secrets in AWS Secrets Manager/SSM or the deployment platform's secret store; SAM `NoEcho` parameters are only a deployment input and can still be exposed through poorly controlled stack access, so prefer dynamic secret references for production. Apply MongoDB indexes by deploying the API and allowing its index initializer to run (or apply the same index definitions in a controlled migration window). Ensure the database supports unique indexes. Configure a production HTTPS webhook URL and the Live app webhook ID. Switch `PayPal__Environment=live` only after Sandbox verification and PayPal merchant/card eligibility approval; deploy matching Live client ID, secret, merchant ID and webhook ID together. Never place the client secret in frontend or `NEXT_PUBLIC_*` configuration.

Private receipt email addresses are retained separately from the public leaderboard and are deleted from the donation record after successful delivery. The receipt outbox retains its delivery record/email for `PayPal__EmailRetentionDays` to support retries; the collection TTL index removes expired jobs. Keep database backups and access policies within the same retention/privacy policy. The worker is hosted in the API process; for Lambda deployment, validate the runtime's background execution window and configure a scheduled invoker if email retries must continue while no requests are arriving.

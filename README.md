# KaiAssistant API

The .NET 10 backend for Kai's portfolio. It serves resume data, streams assistant responses, accepts contact and visitor requests, and processes PayPal donations. MongoDB is the persistence layer for resumes, assistant knowledge/conversations, contact and visit data, and donation state.

## API surface

| Route | Purpose |
| --- | --- |
| `POST /api/assistant` | Stream an assistant response as NDJSON, grounded in resume knowledge. |
| `POST /api/contacts` | Accept a portfolio contact form submission. |
| `POST /api/visits` | Record visitor analytics. |
| `GET /api/resumes/latest` | Return the latest resume document. |
| `GET /api/donations/config` | Return public checkout configuration. |
| `GET /api/donations/leaderboard` | Return public, opted-in completed donations. |
| `POST /api/donations/orders` | Create or recover a donation order. |
| `POST /api/donations/capture` | Capture and verify an approved order. |
| `POST /api/donations/webhook` | Process PayPal capture/refund/reversal events. |
| `GET /api/health`, `/health/live`, `/health/ready` | Basic, liveness, and readiness checks. |

Operational endpoints are under `/ops/*` and depend on backend configuration. The frontend proxies browser requests through same-origin Next.js routes; see the [frontend README](../frontend/README.md).

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
- `KnowledgeBootstrapHostedService` indexes the latest MongoDB resume at startup; add or update the resume in the configured database for the frontend and assistant to use it.
- PayPal donation state and receipt outbox processing use MongoDB. RabbitMQ is not required for the active donation or assistant paths.
- PayPal settings accept both the .NET `PayPal__PropertyName` variables and legacy flat Lambda variables (`PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, `PAYPAL_WEBHOOK_ID`, and `PAYPAL_MERCHANT_ID`). The .NET section values take precedence when both forms are configured.

## Quick Start

### Prerequisites

- .NET SDK 10.
- Docker Desktop.
- A reachable MongoDB instance for resume, assistant, contact, visit, and donation data.
- `GEMINI_API_KEY` for assistant generation; without it, assistant responses are unavailable.

### Environment

Copy the root template and fill in the required values:

```powershell
Copy-Item ..\.env.example ..\.env
```

Key variables:

- `GEMINI_API_KEY` - required for assistant features.
- `MONGODB_CONNECTIONSTRING` - MongoDB connection string.
- `MONGODB_DATABASE` - MongoDB database name.
- `SMTP_*` - optional contact email configuration.
- `ALLOWED_ORIGINS` - CORS origins.
- `PAYPAL_*` - optional PayPal checkout and webhook configuration.

The frontend needs `BACKEND_API_URL=http://localhost:5000` when run locally. In Docker Compose it is set to `http://backend:8080`.

### Run With Docker Compose

```powershell
cd ..
docker compose up -d --build
```

This starts the backend, frontend, and Redis containers. The backend is exposed on `http://localhost:5000` and container port `8080`; MongoDB is not included and must be configured separately.

### Run Locally

```powershell
cd backend
dotnet restore KaiAssistant.sln
dotnet build KaiAssistant.sln
dotnet run --project KaiAssistant.API
```

### Run Tests

```powershell
cd backend
dotnet test KaiAssistant.Tests/KaiAssistant.Tests.csproj
```

See [testing notes](docs/testing.md) for integration-test details and the full solution command.

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

- [Architecture](docs/architecture.md)
- [AI pipeline](docs/ai-pipeline.md)
- [RAG architecture](docs/rag-architecture.md)
- [Embedding strategy](docs/embedding-strategy.md)
- [Corpus management](docs/corpus-management.md)
- [Scalability](docs/scalability.md)
- [Resilience](docs/resilience.md)
- [Tradeoffs](docs/tradeoffs.md)
- [API examples](docs/api-examples.md)
- [Dependency security and supply-chain policy](docs/dependency-security.md)
- [Testing](docs/testing.md)
- [System diagram](docs/diagrams/system-diagram.md)
- [AI flow diagram](docs/diagrams/ai-flow-diagram.md)

## Troubleshooting

- If `GET /api/resumes/latest` returns 404, add a resume document to the configured MongoDB database.
- If assistant requests fail, confirm `GEMINI_API_KEY` is present and the backend can reach the configured Gemini endpoint.
- If data-backed endpoints fail, confirm MongoDB is reachable and the connection string/database name are correct.
- If cache-related flows fail, confirm Redis is reachable and `REDIS__CONNECTIONSTRING` points to the right instance.
- If `/ops/*` routes return 404 in production, confirm ops are enabled in configuration.
- If the assistant does not remember prior chat in local testing, verify requests include `context.metadata.sessionId` and that MongoDB is reachable.
- If `GET /api/donations/config` returns 404, verify that Lambda has non-empty `PAYPAL_CLIENT_ID` and `PAYPAL_CLIENT_SECRET` variables (or `PayPal__ClientId` and `PayPal__ClientSecret`) and that the deployed application supports those names.

## PayPal donation checkout

Donation payments use the PayPal Orders v2 API. The browser receives only the PayPal client ID and redirects/card entry remain inside PayPal Checkout; this application never receives card numbers, security codes, or expiration values. PayPal may show eligible cards based on account approval, buyer region, and transaction risk; “all cards” cannot be guaranteed by the application.

Each checkout attempt is keyed by a UUID. Retries reuse that key, while the backend recovers the existing order and its persisted amount/currency instead of trusting changed retry payloads. PayPal order/capture requests use stable provider idempotency IDs. Webhook delivery is claimed with an atomic MongoDB lease; completed event IDs are terminal, and only failed or expired processing leases can be reclaimed.

## Sandbox setup

1. Create a PayPal Developer Sandbox REST app and configure backend-only `PAYPAL_CLIENT_ID` and `PAYPAL_CLIENT_SECRET` secrets. Keep `PAYPAL_ENVIRONMENT=sandbox`; never use your personal/live buyer account or real card details for this check.
2. Set `PayPal__Currency`, donation minimum/maximum, optional `PAYPAL_MERCHANT_ID`, `PAYPAL_WEBHOOK_ID`, and `PAYPAL_EMAIL_RETENTION_DAYS` in backend configuration. Configure SMTP through the existing `SMTP_*` settings if optional receipts are desired.
3. Add the HTTPS endpoint `{API_BASE_URL}/api/donations/webhook` to the matching Sandbox REST app. Subscribe to `PAYMENT.CAPTURE.COMPLETED`, `PAYMENT.CAPTURE.DENIED`, `PAYMENT.CAPTURE.REFUNDED`, and `PAYMENT.CAPTURE.REVERSED`, then store its generated webhook ID as `PayPal__WebhookId`.
4. Run the frontend and backend locally, use a separate PayPal Sandbox buyer account, and verify one successful low-value Sandbox donation. Confirm that the server-created amount is shown at PayPal, capture status is `COMPLETED`, the public entry appears only after opting in, the email stays private, and the receipt outbox records delivery (when SMTP is enabled). Then test a cancelled checkout and a declined/failed payment; neither should appear on the leaderboard or send a receipt. Sandbox transactions use test funds and do not charge a real card.

The app's webhook listener must be reachable by PayPal over HTTPS. A local `localhost` URL cannot receive PayPal webhooks; for local testing, either use the deployed Sandbox webhook endpoint with its own webhook ID, or expose the local API through a temporary HTTPS tunnel and register that exact URL in the Sandbox app. The webhook ID belongs to that registered endpoint. Local order creation and capture can be exercised without webhooks, but refund/reversal synchronization requires a reachable registered listener. Automated tests mock PayPal and never create Sandbox or Live payments.

The production GitHub Actions deployment also requires repository secrets `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, and `PAYPAL_WEBHOOK_ID` (the PayPal-issued webhook ID, not the listener URL). `PAYPAL_MERCHANT_ID` is optional. These values are passed to the SAM parameters that configure the Lambda; setting Lambda variables manually is not a substitute for setting the deployment secrets because a stack update can replace function configuration.

## Production deployment

Store PayPal and SMTP secrets in AWS Secrets Manager/SSM or the deployment platform's secret store; SAM `NoEcho` parameters are only a deployment input and can still be exposed through poorly controlled stack access, so prefer dynamic secret references for production. Apply MongoDB indexes by deploying the API and allowing its index initializer to run (or apply the same index definitions in a controlled migration window). Ensure the database supports unique indexes. Configure a production HTTPS webhook URL and the Live app webhook ID. Switch `PayPal__Environment=live` only after Sandbox verification and PayPal merchant/card eligibility approval; deploy matching Live client ID, secret, merchant ID and webhook ID together. Never place the client secret in frontend or `NEXT_PUBLIC_*` configuration.

Private receipt email addresses are retained separately from the public leaderboard and are deleted from the donation record after successful delivery. The receipt outbox retains its delivery record/email for `PayPal__EmailRetentionDays` to support retries; the collection TTL index removes expired jobs. Keep database backups and access policies within the same retention/privacy policy. The worker is hosted in the API process; for Lambda deployment, validate the runtime's background execution window and configure a scheduled invoker if email retries must continue while no requests are arriving.

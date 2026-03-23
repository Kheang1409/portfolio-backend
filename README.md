# KaiAssistant API

Production-ready ASP.NET Core Web API for the portfolio experience, including AI assistant responses, contact delivery, resume APIs, and operational diagnostics.

## Stack

- .NET 10 / ASP.NET Core
- MongoDB (primary data store)
- Google Gemini API (assistant generation)
- Optional Redis (cache/rate-limit support)
- Optional RabbitMQ (outbox publishing scenarios)
- OpenTelemetry + Serilog
- AWS Lambda + API Gateway via SAM

## What Is Implemented

### Public API Endpoints

- `POST /api/assistants/ask`
  - Returns plain text response body.
  - Adds response headers:
    - `X-AI-Model-Used`
    - `X-AI-Latency-Ms`
    - `X-AI-Fallback-Used`
- `POST /api/assistants/stream`
  - NDJSON streaming endpoint.
  - Falls back to buffered mode client-side when streaming is disabled.
- `POST /api/assistants/ask/batch`
  - Feature-flag controlled batch ask endpoint.
- `POST /api/contacts`
  - Sends portfolio contact email via SMTP settings.
- `GET|HEAD /api/health`
  - Basic controller health check.
- `GET /api/resumes/latest`
- `GET /api/resumes/{id}`
- `POST /api/resumes`

### Health Probes

- `GET /health/live`
- `GET /health/ready`
  - Includes readiness checks for MongoDB, Redis, RabbitMQ, and AI provider health checks.

### Ops Endpoints (hidden from Swagger)

Base route: `/ops`

- `GET /ops/health/detailed`
- `GET /ops/outbox`
- `GET /ops/cache`
- `GET /ops/debug/config`
- `GET /ops/resilience`
- `GET /ops/rate-limit`
- `GET /ops/ai-models`
- Load-test hooks:
  - `GET /ops/simulate`
  - `POST /ops/simulate/ai-throttle`
  - `POST /ops/simulate/outbox-delay`
- Recovery hooks:
  - `POST /ops/outbox/replay/{id}`
  - `POST /ops/outbox/replay-failed`
  - `POST /ops/outbox/dead-letter/{id}`

Ops endpoint access behavior:

- Available in Development by default.
- In Production, requires `Ops:EnabledInProduction=true`.
- Optional header auth can be enabled with `OpsSecurity` options:
  - Default header: `X-Ops-Key`

## Configuration

The application supports `appsettings*.json` plus environment variables. The following variables are used directly in code and should be considered the primary deployment contract.

### Required for Typical Runtime

- `MONGODB_CONNECTIONSTRING`
- `MONGODB_DATABASE`
- `ALLOWED_ORIGINS`
- `GEMINI_API_KEY`
- `SMTP_SERVER`
- `SMTP_PORT`
- `SMTP_SENDER_EMAIL`
- `SMTP_RECEIVER_EMAIL`
- `SMTP_SENDER_PASSWORD`

### Common Optional Variables

- `SMTP_ENABLED` (default behavior depends on environment)
- `GEMINI_MODEL_NAMES`
- `GEMINI_ENDPOINT`
- `GEMINI_SYSTEM_PROMPT`
- `GEMINI_PROMPT_MAX_CHARS`
- `GEMINI_INCLUDE_PERSONAL_DETAILS`
- `GEMINI_TEMPERATURE`
- `GEMINI_TOPK`
- `GEMINI_TOPP`
- `GEMINI_MAX_OUTPUT_TOKENS`
- `GEMINI_CANDIDATE_COUNT`
- `REDIS__CONNECTIONSTRING`
  - Supports `rediss://` and uses resilient connection settings.

## Local Development

From repository root:

```bash
docker-compose up -d --build
```

Default local ports:

- Backend: `http://localhost:5000`
- Frontend: `http://localhost:3000`
- Redis: `localhost:6379`

### Run Backend Only (without Docker)

From `backend`:

```bash
dotnet restore
dotnet build
dotnet run --project KaiAssistant.API
```

Swagger is enabled only in Development.

## Testing

From `backend`:

```bash
dotnet test
```

## Deployment (AWS Lambda via SAM)

Template: `infra/template.yaml`
Workflow: `.github/workflows/deploy-lambda.yml`

### SAM Parameters in Use

- `MongoConnectionString`
- `RedisConnectionString`
- `MongoDatabaseName`
- `AllowedOrigins`
- `SmtpSenderEmail`
- `SmtpReceiverEmail`
- `SmtpSenderPassword`
- `GeminiApiKey`

### Required GitHub Secrets for Deploy Workflow

- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `MONGODB_CONNECTIONSTRING`
- `REDIS__CONNECTIONSTRING`
- `MONGODB_DATABASE`
- `ALLOWED_ORIGINS`
- `SMTP_SENDER_EMAIL`
- `SMTP_RECEIVER_EMAIL`
- `SMTP_SENDER_PASSWORD`
- `GEMINI_API_KEY`

## Notes

- API is wired for Lambda hosting (`AddAWSLambdaHosting`) and also runs as a regular ASP.NET Core app locally.
- Feature flags in configuration control optional capabilities such as batching, cache, outbox processing, and streaming behavior.

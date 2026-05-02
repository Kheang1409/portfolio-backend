# API Examples

## Ask

Request:

```bash
curl -X POST http://localhost:8080/api/v2/assistants/ask \
  -H "Content-Type: application/json" \
  -d '{
    "message":"Summarize KaiAssistant architecture in 3 bullets.",
    "history":[],
    "context":{"metadata":{"conversationId":"demo-ask-1"}}
  }'
```

Example response:

```json
"- Clean architecture separates API, application, domain, and infrastructure.\n- AI flow combines RAG, semantic cache, and provider fallback.\n- Reliability uses distributed rate limiting, outbox processing, and telemetry."
```

## Stream (NDJSON)

Request:

```bash
curl -N -X POST http://localhost:8080/api/v2/assistants/stream \
  -H "Content-Type: application/json" \
  -d '{"message":"Explain the outbox design."}'
```

Example stream chunks:

```json
{"type":"delta","messageId":"...","text":"The outbox processor leases..."}
{"type":"delta","messageId":"...","text":"It retries with backoff..."}
{"type":"completed","messageId":"...","latencyMs":182}
```

## SSE

Request:

```bash
curl -N -X POST http://localhost:8080/api/v2/assistants/sse \
  -H "Content-Type: application/json" \
  -d '{"message":"How does semantic caching work?"}'
```

Example event stream:

```text
event: delta
data: {"type":"delta","messageId":"...","text":"Semantic cache compares..."}

event: completed
data: {"type":"completed","messageId":"...","latencyMs":145}
```

## Explain (Signature Endpoint)

Request:

```bash
curl -X POST http://localhost:8080/api/v2/assistants/explain \
  -H "Content-Type: application/json" \
  -d '{
    "message":"Explain how this backend balances latency, reliability, and cost.",
    "context":{"metadata":{"conversationId":"demo-explain-1"}}
  }'
```

Example response:

```json
{
  "summary": "The backend combines retrieval, caching, and provider fallback to reduce latency while preserving reliability.",
  "key_points": [
    "RAG injects relevant context for grounded answers.",
    "Semantic cache prevents repeated expensive model calls.",
    "Fallback orchestration maintains availability when a provider fails."
  ],
  "related_projects": [
    "project:kaiassistant-api",
    "project:outbox-reliability"
  ],
  "confidence": 0.84
}
```

## RAG-Enhanced Query

Request:

```bash
curl -X POST http://localhost:8080/api/v2/assistants/ask \
  -H "Content-Type: application/json" \
  -d '{"message":"Based on the architecture notes, what is the role of Redis?"}'
```

Behavior:

- Retrieves knowledge snippets from knowledge_base_documents.
- Augments prompt with top similarity context.
- Returns grounded response when sufficient context exists.

## Error Scenarios

Validation error:

```bash
curl -X POST http://localhost:8080/api/v2/assistants/ask \
  -H "Content-Type: application/json" \
  -d '{"message":""}'
```

Example response:

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "Validation failed",
  "status": 400,
  "errorCode": "VALIDATION_FAILED",
  "retryable": false,
  "traceId": "...",
  "correlationId": "..."
}
```

Rate-limit error:

```json
{
  "message": "Rate limit exceeded. Please retry later."
}
```

# API Examples

## Assistant stream (NDJSON)

The active assistant endpoint is `POST /api/assistant`. Each response line is a JSON event.

```bash
curl -N -X POST http://localhost:5000/api/assistant \
  -H "Content-Type: application/json" \
  -d '{"message":"Summarize the architecture in three bullets."}'
```

Example stream:

```json
{"type":"delta","messageId":"...","text":"The backend uses..."}
{"type":"completed","messageId":"..."}
```

To retain conversation memory, provide a stable `conversationId` on each request:

```bash
curl -N -X POST http://localhost:5000/api/assistant \
  -H "Content-Type: application/json" \
  -d '{"message":"What did I ask earlier?","conversationId":"demo-conversation"}'
```

If `conversationId` is omitted, the API can resolve one from `context.metadata.sessionId`; otherwise it creates a request-specific conversation identifier.

## Knowledge administration

Knowledge administration endpoints require a valid JWT with the configured issuer and audience and a `RagAdmin` role claim. Configure `RagAdminAuthentication:Authority`, `RagAdminAuthentication:Audience`, and (if needed) `RagAdminAuthentication:RoleClaimType` before use. The same authorization policy protects uploads, job/document status, cancellation, and index stage/activate/rollback.

Upload a document:

```bash
curl -X POST http://localhost:5000/api/admin/knowledge/documents \
  -H "Authorization: Bearer $RAG_ADMIN_TOKEN" \
  -F "file=@architecture.md"
```

Poll job status using the returned `jobId`:

```bash
curl http://localhost:5000/api/admin/knowledge/jobs/$JOB_ID \
  -H "Authorization: Bearer $RAG_ADMIN_TOKEN"
```

Index lifecycle operations use the same bearer token:

```bash
curl -X POST http://localhost:5000/api/admin/knowledge/indexes/stage \
  -H "Authorization: Bearer $RAG_ADMIN_TOKEN"
curl -X POST http://localhost:5000/api/admin/knowledge/indexes/activate \
  -H "Authorization: Bearer $RAG_ADMIN_TOKEN"
```

## Other active endpoints

```text
POST /api/contacts
POST /api/visits
GET  /api/resumes/latest
GET  /api/health
GET  /health/ready
GET  /health/live
```

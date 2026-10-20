# Corpus management

The authoritative RAG corpus is a curated, sanitized subset of portfolio content. The manifest is [docs/rag-corpus-manifest.json](rag-corpus-manifest.json); source files live under [docs/rag-corpus](rag-corpus). It intentionally excludes source code, build output, fixtures, logs, configuration, credentials, and generated dependency data.

The four current documents cover the professional summary, employment history, skills/education, and projects/architecture. Versions are logical `2026-08` identifiers; normalized content hashes remain the idempotency authority.

## Bootstrap status

The upload, job-status, document-verification, and index-management routes are protected by JWT Bearer and the `RagAdmin` role policy. Configure the issuer and audience (`RagAdminAuthentication:Authority` and `RagAdminAuthentication:Audience`) and set `RagAdminAuthentication:RoleClaimType` to the identity provider's role claim name when it is not `role`. The ingestion script requires `KAIASSISTANT_BASE_URL` and `RAG_ADMIN_TOKEN`; the token must have the configured audience and `RagAdmin` role.

## Updates and cleanup

Update a source document, change its logical version, and run the same authenticated bootstrap. The old content remains available until the selected index is activated and the retention period expires. Do not delete retired indexes automatically; cleanup must be an explicitly authorized, dry-run-reviewed operation.

Gemini staging and activation remain blocked until the corpus is ingested and retrieval is evaluated.

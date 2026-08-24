# Corpus management

The authoritative RAG corpus is a curated, sanitized subset of portfolio content. The manifest is [docs/rag-corpus-manifest.json](rag-corpus-manifest.json); source files live under [docs/rag-corpus](rag-corpus). It intentionally excludes source code, build output, fixtures, logs, configuration, credentials, and generated dependency data.

The four current documents cover the professional summary, employment history, skills/education, and projects/architecture. Versions are logical `2026-08` identifiers; normalized content hashes remain the idempotency authority.

## Bootstrap through the admin boundary

Start the API and obtain a token from the configured identity provider with the `RagAdmin` role. Do not add an anonymous or localhost bypass. Then run:

```powershell
$env:KAIASSISTANT_BASE_URL = 'http://localhost:5000'
$env:RAG_ADMIN_TOKEN = '<token supplied securely by the identity provider>'
powershell -ExecutionPolicy Bypass -File .\scripts\ingest-rag-corpus.ps1
```

The script reads the manifest, validates paths and MIME types, uploads each enabled document to `POST /api/admin/knowledge/documents`, polls the durable job endpoint until terminal state, verifies the document, and prints only IDs, statuses, durations, and chunk counts. It never prints the bearer token or document text.

An HTTP `202` is not treated as success. Failed, dead-lettered, cancelled, or incomplete jobs stop the run. Re-running the same manifest exercises the existing content-hash duplicate path and should not create another active document.

## Lifecycle

1. Upload through the authenticated admin endpoint.
2. Poll `GET /api/admin/knowledge/jobs/{jobId}` until completion.
3. Verify `GET /api/admin/knowledge/documents/{documentId}` reports a positive chunk count and completed/indexed status.
4. Run the real-corpus keyword baseline.
5. Stage the Gemini index with `POST /api/admin/knowledge/indexes/stage`.
6. Validate completeness and evaluate semantic, hybrid, and reranked strategies.
7. Activate only when measured gates pass; otherwise retain keyword retrieval.
8. Keep the old index for rollback and use `POST /api/admin/knowledge/indexes/rollback` for a controlled rollback.

No production knowledge is inserted directly into MongoDB. The development demo hosted service seeds only resume data; knowledge ingestion is exclusively an authenticated admin workflow.

## Updates and cleanup

Update a source document, change its logical version, and run the same authenticated bootstrap. The old content remains available until the selected index is activated and the retention period expires. Do not delete retired indexes automatically; cleanup must be an explicitly authorized, dry-run-reviewed operation.

The current environment has no `RagAdmin` token available to this workspace, so the bootstrap has been prepared but not executed here. Gemini staging and activation remain blocked until the corpus is ingested and evaluated.

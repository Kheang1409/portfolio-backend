# Security posture

Dependency auditing, secret handling, and runtime security decisions are documented in [dependency-security.md](dependency-security.md). The API container uses a multi-stage .NET 10 build and runs as a non-root user. Runtime credentials are supplied through environment or deployment secret management, not committed application settings.

The RAG semantic index is not activated while the production corpus is empty. Keyword retrieval remains the safe default, and staged index activation rejects empty or incompatible indexes.

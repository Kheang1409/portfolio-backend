# Security posture

Dependency auditing, secret handling, and runtime security decisions are documented in [dependency-security.md](dependency-security.md). The API container uses a multi-stage .NET 10 build and runs as a non-root user. Runtime credentials are supplied through environment or deployment secret management, not committed application settings.

Resume knowledge is read from the MongoDB resume collection.

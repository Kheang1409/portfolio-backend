# Dependency security

## Policy

NuGet restore auditing is enabled by default. Critical vulnerabilities must block release; high-severity findings require remediation or a documented, time-bound exception; moderate and low findings require review. Audits must run over the complete solution, including transitive packages:

```powershell
dotnet restore KaiAssistant.sln
dotnet list KaiAssistant.sln package --include-transitive
```

No advisory is suppressed with `NoWarn` or `NuGetAuditSuppress`.

## Baseline and remediation

The 2026-08-23 baseline contained two high findings, one moderate SharpCompress finding, and three moderate OpenTelemetry findings:

| Package | Before | Chain | Resolution |
| --- | --- | --- | --- |
| Snappier | 1.0.0 | `MongoDB.Driver 3.7.1` | Upgraded MongoDB.Driver to 3.11.0; resolved 1.3.1 |
| Microsoft.OpenApi | 2.4.1 | `Swashbuckle.AspNetCore 10.1.5` | Upgraded Swashbuckle to 10.2.3; resolved 2.7.5 |
| SharpCompress | 0.30.1 | MongoDB.Driver and Mongo2Go | Upgraded MongoDB.Driver to 3.11.0; resolved 0.48.1 |
| OpenTelemetry.Api | 1.15.0 | OTel package family | Aligned the family to 1.16.0 |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.13.1 | Direct API reference | Aligned the family to 1.16.0 |

The resolved versions are above the patched floors identified by the GitHub advisories. A fresh restore currently emits no NU1902/NU1903 vulnerability warnings.

## Compatibility checks

MongoDB persistence, ingestion, leasing, vector, and repository tests remain part of the solution test run. API startup was exercised against the configured MongoDB instance; Swagger generation returned `200`, retained `/api/assistant`, and represented the admin upload route as `multipart/form-data`. The upload action uses a request DTO to remain compatible with the upgraded Swashbuckle generator while preserving field names and route contracts.

The OpenTelemetry packages are intentionally upgraded as one family. The existing meters and activities remain registered, and API startup completed with the 1.16.0 runtime.

## Secrets and feeds

Production credentials must come from environment or secret management. Development configuration is credential-free; local `.env` values are ignored and must never be committed. Any credential previously present in local development configuration should be rotated externally. NuGet uses the standard HTTPS feed; no feed credentials or private sources are committed.

## Verification

The security-phase verification used an isolated artifacts directory because the IDE-owned `obj` tree was locked:

```powershell
dotnet test KaiAssistant.sln --artifacts-path ..\\.security-artifacts
git diff --check
```

The complete solution passed with 64 normal tests, 3 intentional skips, and 15 RAG evaluation tests. Remaining build output is limited to pre-existing nullable, async-iterator, and ASP.NET analyzer warnings; no package vulnerability warning remains.

## Remaining work

Container scanning and a CI policy gate are not yet wired to a provider-specific scanner. The existing multi-stage, non-root Dockerfile remains unchanged. RAG activation remains blocked independently because the configured corpus is empty.

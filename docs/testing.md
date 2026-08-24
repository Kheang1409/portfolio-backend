# Testing

The test project is included in `KaiAssistant.sln`. Unit tests use deterministic doubles; Mongo integration tests use Mongo2Go with a unique data directory under `%TEMP%/KaiAssistant.Tests/<guid>` and an explicit package-tools search path. They do not use the developer profile as a working directory.

Run the complete suite from PowerShell:

```powershell
dotnet test KaiAssistant.sln --no-restore
```

Integration tests are marked with `Category=Integration` and exercise atomic job claiming, lease renewal, and expired-lease recovery. The worker admission test verifies the configured bound without timing-based sleeps.

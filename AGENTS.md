# AGENTS.md

This file gives coding agents project-specific guidance for this repository.
It applies to the whole repo unless a more specific `AGENTS.md` is added in a
subdirectory.

## Project Overview

IPInfo is a lightweight ASP.NET Core Minimal API that resolves IPv4 location
data from a local QQWry database file (`qqwry.dat`).

Core behaviors:

- `GET /` and `GET /ip` resolve the caller's IPv4 address.
- `GET /ip/{ipV4}` resolves a specific IPv4 address.
- `GET /db-info` returns metadata for the configured database file.
- The API supports reverse proxy scenarios with `X-Forwarded-For`.
- The API has both global and per-client-IP fixed-window rate limits.
- The QQWry database is loaded from disk and hot-reloaded by a hosted service.
- Errors should be returned as RFC 7807-style Problem Details where practical.

## Repository Layout

- `src/` contains the ASP.NET Core application.
- `tests/IPInfo.Tests/` contains xUnit v3 unit and integration tests.
- `src/Program.cs` owns host setup, DI, middleware, rate limiting, forwarded
  headers, and endpoint mapping.
- `src/Services/QqwryDb.cs` parses the QQWry binary database. Treat this code
  carefully: it uses little-endian numeric reads, 24-bit offsets, and CP936
  string decoding.
- `src/Services/QqwryDbProvider.cs` owns the current in-memory database and
  atomically swaps it during reloads.
- `src/Services/QqwryDbWatcher.cs` is the `BackgroundService` that polls for
  database changes.
- `src/Services/IpLookupService.cs` maps database results into the public API
  model.
- `src/Models/IpLocationResult.cs` is the public response DTO.
- `azure-file-share-updater/` contains a small Docker image/script for
  downloading and atomically replacing `qqwry.dat`.
- `compose.yaml` runs the API and updater together.
- `deploy-vm.sh` copies the compose file to a VM deploy directory, pulls images,
  runs an initial database update if needed, and starts the stack.
- `.github/workflows/` builds and pushes the API and updater Docker images to
  Azure Container Registry on pushes to `master`.

## Build And Run

Use PowerShell on Windows unless the task specifically targets the Linux shell
scripts.

Common commands:

```powershell
cd src
dotnet restore
dotnet build
dotnet run
```

The app targets `net10.0`. Do not downgrade the target framework or Docker base
images unless the user explicitly asks for that migration.

Local runs need a QQWry database file. By default the app reads:

```text
/data/qqwry.dat
```

Override it with configuration, for example:

```powershell
$env:DBPath = "E:\path\to\qqwry.dat"
dotnet run --project src\IPInfo.csproj
```

Rate limiting defaults live in `src/appsettings.json`:

- `RateLimiting:PerIpPerSecond` defaults to `5`.
- `RateLimiting:GlobalPerSecond` defaults to `10`.

Database reload polling can be configured with:

- `IpDb:ReloadIntervalSeconds`, defaulting to `60` in code.

## Architecture Guidelines

- Keep the app as a Minimal API unless there is a clear reason to introduce
  controllers.
- Keep route handlers thin. Put lookup, parsing, reload, and file concerns in
  services.
- Keep `Program.cs` readable. If endpoint or registration logic grows, extract
  cohesive extension methods rather than mixing unrelated concerns inline.
- Preserve the current public endpoint paths and JSON contract unless the user
  asks for a breaking change.
- `IpLocationResult.Area` is currently returned as an empty string, while the
  QQWry second location string is exposed as `Isp`. Do not "clean this up"
  casually; it is part of the current response shape shown in `README.md`.
- The service is IPv4-only today. If IPv6 support is added, update validation,
  `ResolveClientIpV4`, service naming, README examples, and tests/docs together.
- Keep `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` because
  QQWry strings require CP936/GBK decoding.
- Be cautious with `QqwryDb` offset arithmetic. Add focused tests or a small
  fixture before changing binary parsing logic.

## Operational Guidelines

- The app intentionally accepts forwarded headers from any proxy by clearing
  `KnownIPNetworks` and `KnownProxies`; this supports Docker/Kubernetes style
  deployments. Revisit this only with deployment context.
- `X-Forwarded-For` handling uses the leftmost value as the original client.
  Preserve this behavior unless changing proxy trust semantics deliberately.
- The database availability middleware returns `503` when `qqwry.dat` is absent
  or unavailable. Do not let missing database files become unhandled exceptions
  for normal API requests.
- `QqwryDbProvider` rejects suspiciously small files to avoid reading partially
  written databases. Keep this protection when changing reload behavior.
- The updater writes to a temp file and uses `mv` for atomic replacement. Keep
  that pattern; it works with the app-side reload guard.
- Avoid logging secrets or full request payloads. Existing lookup logs include
  client IP, query IP, location fields, and user agent.

## Docker And Deployment Notes

- `src/Dockerfile` builds the API image from the `src` context.
- `azure-file-share-updater/Dockerfile` builds the updater image from the
  updater directory.
- `compose.yaml` expects `/opt/docker/ip-info` on the host and mounts it to
  `/data`.
- The API container reads `/data/qqwry.dat` as read-only.
- The updater container writes `/data/qqwry.dat` and companion metadata files
  such as `.sha256`, `.version`, and `.updated_at`.
- Do not commit real database files, generated deployment data, or registry
  credentials.

## Testing And Verification

For code changes, at minimum run:

```powershell
dotnet build src\IPInfo.csproj
```

For parser or service changes, run the xUnit v3 test project:

```powershell
dotnet test tests\IPInfo.Tests\IPInfo.Tests.csproj
```

Tests should use small generated fixtures and must not commit real `qqwry.dat`
files.

Endpoint tests use `WebApplicationFactory<Program>` with generated fixture
databases. Keep `public partial class Program;` available for this integration
test entry point.

When endpoint behavior changes, verify manually with a real or fixture
`qqwry.dat`:

```powershell
$env:DBPath = "E:\path\to\qqwry.dat"
dotnet run --project src\IPInfo.csproj
```

Then exercise:

```powershell
curl.exe http://localhost:5163/
curl.exe http://localhost:5163/ip/8.8.8.8
curl.exe http://localhost:5163/db-info
```

Adjust the port to match the launch profile or `ASPNETCORE_URLS` in use.

For Docker-oriented changes, build the relevant image from the same context used
by the GitHub workflow:

```powershell
docker build -f src\Dockerfile src
docker build -f azure-file-share-updater\Dockerfile azure-file-share-updater
```

## Style Guidelines

- Follow the existing C# style: file-scoped namespaces, nullable enabled,
  implicit usings, small sealed service classes, and primary constructors where
  already used.
- Prefer structured logging with message templates over string interpolation.
- Prefer `Results.Problem` or Problem Details-compatible JSON for API errors.
- Prefer built-in ASP.NET Core features over new dependencies.
- Keep comments useful and short. The existing code uses comments to mark major
  sections and explain non-obvious binary/database safeguards.
- Do not reformat unrelated files or churn generated Docker/solution metadata.

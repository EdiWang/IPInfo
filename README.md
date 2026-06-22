# IPInfo

A lightweight ASP.NET Core minimal API that resolves IPv4 geolocation information using the [QQWry](https://github.com/metowolf/qqwry.dat) IP database (`qqwry.dat`).

## Features

- Look up geolocation (country, area, ISP) for the caller's own IP or any specific IPv4 address
- Supports reverse proxy environments via `X-Forwarded-For` header
- Built-in rate limiting (per-IP and global)
- Returns structured JSON responses with [RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) Problem Details on errors
- Hot-reloads `qqwry.dat` automatically when the file changes on disk
- Liveness and database readiness health checks
- Docker-ready

## API Endpoints

| Method | Path            | Description                          |
|--------|-----------------|--------------------------------------|
| GET    | `/`             | Look up the caller's own IP          |
| GET    | `/ip`           | Look up the caller's own IP          |
| GET    | `/ip/{ipV4}`    | Look up a specific IPv4 address      |
| GET    | `/db-info`      | Return public database file metadata |
| GET    | `/health/live`  | Process liveness check               |
| GET    | `/health/ready` | Database readiness check             |

### Example Response

```json
{
  "queryIp": "8.8.8.8",
  "country": "美国",
  "area": "",
  "isp": "Google LLC"
}
```

### `/db-info` Response

```json
{
  "fileName": "qqwry.dat",
  "sizeMb": 10.42,
  "lastUpdatedUtc": "2025-01-01T00:00:00Z"
}
```

`/db-info` is public but does not expose the full configured database path. If
the database is unavailable, it returns `HTTP 503`.

### Error Response (RFC 7807)

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Bad Request",
  "status": 400,
  "detail": "'999.999.999.999' is not a valid IPv4 address."
}
```

## Rate Limiting

| Limit         | Default |
|---------------|---------|
| Per-IP/second | 5       |
| Global/second | 10      |

When exceeded, the API returns `HTTP 429 Too Many Requests`.

Per-IP limiting uses the same IPv4 resolution as lookup logging:

1. The leftmost `X-Forwarded-For` IPv4 address, when present and valid.
2. The remote connection IP, including IPv4-mapped IPv6 addresses.

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `DBPath` | `/data/qqwry.dat` | Path to the local QQWry database file |
| `RateLimiting:PerIpPerSecond` | `5` | Per-client IPv4 requests per second |
| `RateLimiting:GlobalPerSecond` | `10` | Global requests per second |
| `IpDb:ReloadIntervalSeconds` | `60` | Database reload polling interval |

`IpDb:ReloadIntervalSeconds` must be positive. Invalid values fall back to the
default interval and are logged as warnings.

## Health Checks

`/health/live` reports whether the process can answer requests. It does not
require the database.

`/health/ready` requires `qqwry.dat` to be loaded and is used by Docker Compose
and the VM deployment script.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A copy of `qqwry.dat` (available from [metowolf/qqwry.dat](https://github.com/metowolf/qqwry.dat))

### Run Locally

```bash
# Clone the repository
git clone https://github.com/ediwang/IPInfo.git
cd IPInfo

# Point DBPath at a local qqwry.dat file
DBPath=/path/to/qqwry.dat dotnet run --project src/IPInfo.csproj
```

On PowerShell:

```powershell
$env:DBPath = "E:\path\to\qqwry.dat"
dotnet run --project src\IPInfo.csproj
```

Then verify:

```bash
curl http://localhost:5163/ip/8.8.8.8
curl http://localhost:5163/db-info
curl http://localhost:5163/health/live
curl http://localhost:5163/health/ready
```

### Run with Docker

```bash
docker run -d -p 8080:8080 -v /path/to/qqwry.dat:/data/qqwry.dat:ro ediwang/ipinfo
```

### Run with Docker Compose

`compose.yaml` runs both the API and the database updater. It expects the host
data directory at `/opt/docker/ip-info` and mounts it to `/data`.

```bash
docker compose up -d
docker compose ps
curl http://localhost:8000/health/ready
```

### Deploy to a VM

`deploy-vm.sh` copies `compose.yaml` into the deploy directory, pulls images,
runs an initial database update if `qqwry.dat` is missing, starts the stack, and
waits for `/health/ready`.

```bash
./deploy-vm.sh
```

Override the deploy directory when needed:

```bash
DEPLOY_DIR=/opt/docker/ip-info ./deploy-vm.sh
```

## Testing

```powershell
dotnet build src\IPInfo.csproj
dotnet test src\IPInfo.slnx
```

Tests use generated QQWry fixtures and do not require a real database file.

## Operations Notes

- Missing, unreadable, or invalid database files return `HTTP 503` for normal
  API requests.
- If a previously loaded database briefly disappears during hot reload polling,
  the API keeps serving from the in-memory database for a few consecutive checks
  before marking the database unavailable.
- In Docker Compose deployments, the updater keeps `qqwry.dat` readable by the
  API container's non-root user.
- `/health/live` stays available when the database is missing.
- `/health/ready` returns unhealthy when the database is missing.
- Lookup logs include client IP, query IP, location fields, and user agent.
- Do not commit real database files, deployment data, or registry credentials.

## License

This project is licensed under the terms of the [LICENSE](LICENSE) file.

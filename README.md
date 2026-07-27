# IPInfo

A lightweight ASP.NET Core minimal API that resolves IPv4 geolocation information using one or more local IP databases.

## Features

- Detect the caller's IPv4 or IPv6 address
- Look up geolocation (country, area, ISP) for the caller's own IPv4 address or any specific IPv4 address
- Supports configurable database providers, including QQWry and MaxMind GeoLite2 City
- Supports reverse proxy environments via `X-Forwarded-For` header
- Built-in rate limiting (per-IP and global)
- Returns structured JSON responses with [RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) Problem Details on errors
- Hot-reloads configured database files automatically when they change on disk
- Liveness and database readiness health checks
- Docker-ready

## API Endpoints

| Method | Path            | Description                          |
|--------|-----------------|--------------------------------------|
| GET    | `/`             | Detect the caller's IP and look up IPv4 geolocation |
| GET    | `/ip`           | Detect the caller's IP and look up IPv4 geolocation |
| GET    | `/ip/{ipV4}`    | Look up a specific IPv4 address      |
| GET    | `/db-info`      | Return public database file metadata |
| GET    | `/health/live`  | Process liveness check               |
| GET    | `/health/ready` | Database readiness check             |

### Example Response

```json
{
  "queryIp": "8.8.8.8",
  "clientIpV4": "8.8.8.8",
  "clientIpV6": null,
  "country": ["美国", "United States"],
  "area": ["", "California Mountain View"],
  "isp": ["Google LLC", ""]
}
```

The `country`, `area`, and `isp` arrays contain one value per configured
database provider, in configured order. The response intentionally does not
include provider names or data-source labels.

When the caller is detected as IPv6-only, the API returns the IPv6 address with
empty location fields because `qqwry.dat` is an IPv4 database:

```json
{
  "queryIp": "2001:db8::8",
  "clientIpV4": null,
  "clientIpV6": "2001:db8::8",
  "country": [""],
  "area": [""],
  "isp": [""]
}
```

### `/db-info` Response

```json
{
  "databases": [
    {
      "fileName": "qqwry.dat",
      "sizeMb": 10.42,
      "lastUpdatedUtc": "2025-01-01T00:00:00Z",
      "available": true
    },
    {
      "fileName": "GeoLite2-City.mmdb",
      "sizeMb": 64.12,
      "lastUpdatedUtc": "2025-01-01T00:00:00Z",
      "available": true
    }
  ]
}
```

`/db-info` is public but does not expose the full configured database path. If
all configured databases are unavailable, it returns `HTTP 503`.

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

Per-IP limiting uses the same client IP resolution as lookup logging:

1. The leftmost `X-Forwarded-For` IP address, when present and valid.
2. The remote connection IP.

IPv4-mapped IPv6 addresses are normalized to IPv4.

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `DBPath` | `/data/qqwry.dat` | Legacy path to the local QQWry database file, used when `IpDatabases:Providers` is not configured |
| `IpDatabases:Providers:{n}:Type` | | Database provider type: `Qqwry` or `MaxMindGeoLite2City` |
| `IpDatabases:Providers:{n}:Path` | | Local database file path for the provider |
| `IpDatabases:Providers:{n}:Locale` | `zh-CN` | Preferred MaxMind localized name locale |
| `RateLimiting:PerIpPerSecond` | `5` | Per-client IP requests per second |
| `RateLimiting:GlobalPerSecond` | `10` | Global requests per second |
| `IpDb:ReloadIntervalSeconds` | `60` | Database reload polling interval |

`IpDb:ReloadIntervalSeconds` must be positive. Invalid values fall back to the
default interval and are logged as warnings.

Example multi-provider configuration:

```json
{
  "IpDatabases": {
    "Providers": [
      {
        "Type": "Qqwry",
        "Path": "/data/qqwry.dat"
      },
      {
        "Type": "MaxMindGeoLite2City",
        "Path": "/data/GeoLite2-City.mmdb",
        "Locale": "zh-CN"
      }
    ]
  }
}
```

## Health Checks

`/health/live` reports whether the process can answer requests. It does not
require the database.

`/health/ready` requires all configured database providers to be loaded and is
used by Docker Compose and the VM deployment script.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A copy of `qqwry.dat` (available from [metowolf/qqwry.dat](https://github.com/metowolf/qqwry.dat))
- Optional: a MaxMind GeoLite2 City `.mmdb` file and MaxMind license key for automated updates

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

`compose.yaml` enables QQWry and MaxMind GeoLite2 City. Set
`MAXMIND_LICENSE_KEY` in the shell or environment file before running compose so
the updater can download `GeoLite2-City.mmdb`.

### Deploy to a VM

`deploy-vm.sh` copies `compose.yaml` into the deploy directory, pulls images,
runs an initial database update, starts the stack, and waits for
`/health/ready`.

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
  API requests when no configured provider is available.
- If a previously loaded database briefly disappears during hot reload polling,
  the API keeps serving from the in-memory database for a few consecutive checks
  before marking the database unavailable.
- In Docker Compose deployments, the updater keeps database files readable by
  the API container's non-root user.
- `/health/live` stays available when the database is missing.
- `/health/ready` returns unhealthy when any configured database is missing.
- Lookup logs include client IP, query IP, location fields, and user agent.
- Do not commit real database files, deployment data, or registry credentials.

## License

This project is licensed under the terms of the [LICENSE](LICENSE) file.

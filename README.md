# IPInfo

A lightweight ASP.NET Core minimal API that resolves IPv4 geolocation information using the [QQWry](https://github.com/metowolf/qqwry.dat) IP database (`qqwry.dat`).

## Features

- Look up geolocation (country, area, ISP) for the caller's own IP or any specific IPv4 address
- Supports reverse proxy environments via `X-Forwarded-For` header
- Built-in rate limiting (per-IP and global)
- Returns structured JSON responses with [RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) Problem Details on errors
- Hot-reloads `qqwry.dat` automatically when the file changes on disk
- Docker-ready

## API Endpoints

| Method | Path          | Description                        |
|--------|---------------|------------------------------------|
| GET    | `/`           | Look up the caller's own IP        |
| GET    | `/ip`         | Look up the caller's own IP        |
| GET    | `/ip/{ipV4}`  | Look up a specific IPv4 address    |
| GET    | `/db-info`    | Return database file metadata      |

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
  "path": "/data/qqwry.dat",
  "sizeMb": 10.42,
  "lastUpdatedUtc": "2025-01-01T00:00:00Z"
}
```

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

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A copy of `qqwry.dat` (available from [metowolf/qqwry.dat](https://github.com/metowolf/qqwry.dat))

### Run Locally

```bash
# Clone the repository
git clone https://github.com/ediwang/IPInfo.git
cd IPInfo

# Place qqwry.dat in /data/ or set DBPath in appsettings.json
dotnet run
```

### Run with Docker

```bash
docker run -d -p 8080:8080 -v /path/to/qqwry.dat:/data/qqwry.dat:ro ediwang/ipinfo
```

## License

This project is licensed under the terms of the [LICENSE](LICENSE) file.
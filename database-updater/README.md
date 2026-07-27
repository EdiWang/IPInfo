# IPInfo Database Updater

This directory builds the small updater image used by `compose.yaml`. The image
downloads one or more configured IP databases and atomically replaces the target
files in the mounted data directory.

## Run locally

```bash
mkdir -p ./data
docker run --rm -v ./data:/data ediwang/ipinfo-updater
```

By default, the updated database is written to `./data/qqwry.dat`.

## Configuration

| Environment variable | Default | Description |
|----------------------|---------|-------------|
| `DATABASES` | `qqwry` | Comma-separated database IDs to update. Built-in IDs: `qqwry`, `geolite2-city`. |
| `DATA_DIR` | `/data` | Mounted directory that receives the database file and metadata. |
| `USER_AGENT` | `ipinfo-updater/1.0` | HTTP user agent for download requests. |
| `LOCK_FILE` | `$DATA_DIR/.update.lock` | Lock file used to avoid concurrent writes. |
| `QQWRY_URL` | `https://github.com/metowolf/qqwry.dat/releases/latest/download/qqwry.dat` | QQWry download URL. |
| `TARGET_NAME` | `qqwry.dat` | Legacy QQWry target file name. |
| `MAXMIND_LICENSE_KEY` | | MaxMind license key used to download GeoLite2 City when `geolite2-city` is enabled. |
| `GEOLITE2_CITY_URL` | | Optional explicit GeoLite2 City tar.gz download URL. Overrides `MAXMIND_LICENSE_KEY`. |

Each database can also be configured with normalized per-ID variables. The ID is
uppercased and non-alphanumeric characters become `_`. For example,
`geolite2-city` uses:

| Environment variable | Description |
|----------------------|-------------|
| `GEOLITE2_CITY_TYPE` | Download type. Use `tar-gz-mmdb` for MaxMind archives or `raw` for direct files. |
| `GEOLITE2_CITY_URL` | Download URL. |
| `GEOLITE2_CITY_TARGET_NAME` | Target file name in `DATA_DIR`. |
| `GEOLITE2_CITY_MIN_BYTES` | Minimum accepted downloaded file size. |

## Write Pattern

The script downloads each configured database into a temporary directory,
validates that the file is not suspiciously small, copies it to a temp target in
`DATA_DIR`, then uses `mv` to replace the live database atomically.

If one database update fails, the script logs an `ERROR` and continues with the
remaining databases. A partial success exits successfully; the script exits with
failure only when every attempted database update fails.

The data directory is kept traversable by non-root containers, and generated
database/metadata files are written as world-readable. The API image runs as a
non-root user and needs read access to the mounted database file.

For each target file it also writes:

- `qqwry.dat.sha256`
- `qqwry.dat.version`
- `qqwry.dat.updated_at`

The API container mounts the same data directory read-only and hot-reloads the
database when the file changes.

## Compose Scheduling

In `compose.yaml`, the updater runs once on container start and then runs daily
at `SCHEDULE_TIME` using the host timezone mounted from `/etc/localtime`.

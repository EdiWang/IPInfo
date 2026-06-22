# IPInfo Database Updater

This directory builds the small updater image used by `compose.yaml`. The image
downloads the latest QQWry database and atomically replaces `qqwry.dat` in the
mounted data directory.

## Run locally

```bash
mkdir -p ./data
docker run --rm -v ./data:/data ediwang/ipinfo-updater
```

The updated database is written to `./data/qqwry.dat`.

## Configuration

| Environment variable | Default | Description |
|----------------------|---------|-------------|
| `QQWRY_URL` | `https://github.com/metowolf/qqwry.dat/releases/latest/download/qqwry.dat` | Download URL. Keep this on the latest release unless intentionally testing another source. |
| `DATA_DIR` | `/data` | Mounted directory that receives the database file and metadata. |
| `TARGET_NAME` | `qqwry.dat` | Database file name. |
| `USER_AGENT` | `qqwry-updater/1.0` | HTTP user agent for the download request. |
| `LOCK_FILE` | `$DATA_DIR/.update.lock` | Lock file used to avoid concurrent writes. |

## Write Pattern

The script downloads into a temporary directory, validates that the file is not
suspiciously small, copies it to a temp target in `DATA_DIR`, then uses `mv` to
replace the live database atomically.

The data directory is kept traversable by non-root containers, and generated
database/metadata files are written as world-readable. The API image runs as a
non-root user and needs read access to the mounted database file.

It also writes:

- `qqwry.dat.sha256`
- `qqwry.dat.version`
- `qqwry.dat.updated_at`

The API container mounts the same data directory read-only and hot-reloads the
database when the file changes.

## Compose Scheduling

In `compose.yaml`, the updater runs once when `/data/qqwry.dat` is missing and
then runs daily at `SCHEDULE_TIME` using the host timezone mounted from
`/etc/localtime`.

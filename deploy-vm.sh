#!/usr/bin/env bash
set -euo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-/opt/docker/ip-info}"
COMPOSE_SOURCE="${COMPOSE_SOURCE:-./compose.yaml}"
UPDATER_IMAGE="ediwang.azurecr.io/ipinfo-updater:latest"

if [ "$(id -u)" -eq 0 ] && [ -n "${SUDO_USER:-}" ] && [ -z "${DOCKER_CONFIG:-}" ]; then
  SUDO_USER_HOME="$(getent passwd "${SUDO_USER}" | cut -d: -f6)"
  if [ -f "${SUDO_USER_HOME}/.docker/config.json" ]; then
    export DOCKER_CONFIG="${SUDO_USER_HOME}/.docker"
    echo "Using Docker credentials from ${DOCKER_CONFIG}."
  fi
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: docker is not installed or not in PATH." >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "ERROR: Docker Compose v2 is required. Install the docker compose plugin first." >&2
  exit 1
fi

if [ ! -f "${COMPOSE_SOURCE}" ]; then
  echo "ERROR: compose file not found: ${COMPOSE_SOURCE}" >&2
  exit 1
fi

mkdir -p "${DEPLOY_DIR}"
chmod 0755 "${DEPLOY_DIR}"

COMPOSE_SOURCE_PATH="$(realpath "${COMPOSE_SOURCE}")"
COMPOSE_TARGET_PATH="$(realpath -m "${DEPLOY_DIR}/compose.yaml")"

if [ "${COMPOSE_SOURCE_PATH}" = "${COMPOSE_TARGET_PATH}" ]; then
  echo "compose.yaml is already in ${DEPLOY_DIR}; skipping copy."
else
  install -m 0644 "${COMPOSE_SOURCE_PATH}" "${COMPOSE_TARGET_PATH}"
fi

cd "${DEPLOY_DIR}"

echo "Pulling images from ACR..."
docker compose pull

echo "Running one initial database update..."
docker run --rm \
  -e DATA_DIR=/data \
  -e DATABASES="${DATABASES:-qqwry,geolite2-city}" \
  -e MAXMIND_LICENSE_KEY="${MAXMIND_LICENSE_KEY:-}" \
  -v "${DEPLOY_DIR}:/data" \
  "${UPDATER_IMAGE}" || true

for database_file in \
  "${DEPLOY_DIR}/qqwry.dat" \
  "${DEPLOY_DIR}/GeoLite2-City.mmdb"; do
  if [ -e "${database_file}" ]; then
    chmod 0644 "${database_file}"
  fi
done

for metadata_file in \
  "${DEPLOY_DIR}/qqwry.dat.sha256" \
  "${DEPLOY_DIR}/qqwry.dat.updated_at" \
  "${DEPLOY_DIR}/qqwry.dat.version" \
  "${DEPLOY_DIR}/GeoLite2-City.mmdb.sha256" \
  "${DEPLOY_DIR}/GeoLite2-City.mmdb.updated_at" \
  "${DEPLOY_DIR}/GeoLite2-City.mmdb.version"; do
  if [ -e "${metadata_file}" ]; then
    chmod 0644 "${metadata_file}"
  fi
done

echo "Starting IPInfo stack..."
docker compose up -d

echo "Waiting for IPInfo readiness..."
for attempt in $(seq 1 30); do
  if docker compose exec -T ipinfo curl -fsS http://127.0.0.1:8080/health/ready >/dev/null; then
    echo "IPInfo readiness check passed."
    break
  fi

  if [ "${attempt}" -eq 30 ]; then
    echo "ERROR: IPInfo readiness check failed after ${attempt} attempts." >&2
    docker compose ps >&2
    docker compose logs --tail=100 ipinfo >&2
    exit 1
  fi

  sleep 2
done

echo
docker compose ps
echo
echo "Done. Main app should listen on http://127.0.0.1:8000"

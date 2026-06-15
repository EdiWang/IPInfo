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

if [ ! -s "${DEPLOY_DIR}/qqwry.dat" ]; then
  echo "qqwry.dat not found; running one initial database update..."
  docker run --rm \
    -e DATA_DIR=/data \
    -e TARGET_NAME=qqwry.dat \
    -v "${DEPLOY_DIR}:/data" \
    "${UPDATER_IMAGE}"
fi

echo "Starting IPInfo stack..."
docker compose up -d

echo
docker compose ps
echo
echo "Done. Main app should listen on http://127.0.0.1:8000"

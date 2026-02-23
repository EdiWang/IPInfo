#!/usr/bin/env bash
set -euo pipefail

# Fixed download URL (can also be overridden via environment variable)
QQWRY_URL="${QQWRY_URL:-https://github.com/metowolf/qqwry.dat/releases/latest/download/qqwry.dat}"

DATA_DIR="${DATA_DIR:-/data}"
TARGET_NAME="${TARGET_NAME:-qqwry.dat}"
USER_AGENT="${USER_AGENT:-qqwry-updater/1.0}"
LOCK_FILE="${LOCK_FILE:-$DATA_DIR/.update.lock}"

mkdir -p "$DATA_DIR"

TARGET_PATH="$DATA_DIR/$TARGET_NAME"
TMP_DIR="$(mktemp -d)"
cleanup() { rm -rf "$TMP_DIR"; }
trap cleanup EXIT

log() { echo "[$(date -u +'%Y-%m-%dT%H:%M:%SZ')] $*"; }

sha256_file() {
  sha256sum "$1" | awk '{print $1}'
}

# Use a lock to prevent concurrent writes from corrupting the file
exec 200>"$LOCK_FILE"
flock -n 200 || { log "Another update is running, exit."; exit 0; }

log "Downloading: $QQWRY_URL"
RAW="$TMP_DIR/qqwry.dat"
curl -fsSL --retry 5 --retry-delay 2 -A "$USER_AGENT" "$QQWRY_URL" -o "$RAW"

# Basic validation: non-empty and reasonable size (qqwry database is generally much larger than 100KB)
SIZE=$(stat -c%s "$RAW" 2>/dev/null || stat -f%z "$RAW")
if [[ "$SIZE" -lt 102400 ]]; then
  log "ERROR: downloaded file too small ($SIZE bytes), abort."
  exit 3
fi

NEW_SHA="$(sha256_file "$RAW")"
log "New sha256=$NEW_SHA size=$SIZE"

# If the existing file exists and has the same hash, skip replacement
if [[ -f "$TARGET_PATH" ]]; then
  OLD_SHA="$(sha256_file "$TARGET_PATH" || true)"
  if [[ "$OLD_SHA" == "$NEW_SHA" ]]; then
    log "No change (sha256 same)."
    date -u +'%Y-%m-%dT%H:%M:%SZ' > "$DATA_DIR/$TARGET_NAME.updated_at"
    echo "$NEW_SHA" > "$DATA_DIR/$TARGET_NAME.sha256"
    echo "${NEW_SHA:0:12}" > "$DATA_DIR/$TARGET_NAME.version"
    exit 0
  fi
fi

# Atomic replacement (write to a temp file first, then mv)
TMP_TARGET="$DATA_DIR/.${TARGET_NAME}.tmp"
cp -f "$RAW" "$TMP_TARGET"
sync || true
mv -f "$TMP_TARGET" "$TARGET_PATH"

date -u +'%Y-%m-%dT%H:%M:%SZ' > "$DATA_DIR/$TARGET_NAME.updated_at"
echo "$NEW_SHA" > "$DATA_DIR/$TARGET_NAME.sha256"
echo "${NEW_SHA:0:12}" > "$DATA_DIR/$TARGET_NAME.version"

log "Update done. replaced $TARGET_PATH"
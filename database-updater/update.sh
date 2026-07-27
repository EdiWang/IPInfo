#!/usr/bin/env bash
set -euo pipefail
umask 022

DATA_DIR="${DATA_DIR:-/data}"
DATABASES="${DATABASES:-qqwry}"
USER_AGENT="${USER_AGENT:-ipinfo-updater/1.0}"
LOCK_FILE="${LOCK_FILE:-$DATA_DIR/.update.lock}"

mkdir -p "$DATA_DIR"
chmod 755 "$DATA_DIR"

TMP_DIR="$(mktemp -d)"
cleanup() { rm -rf "$TMP_DIR"; }
trap cleanup EXIT

log() { echo "[$(date -u +'%Y-%m-%dT%H:%M:%SZ')] $*"; }

sha256_file() {
  sha256sum "$1" | awk '{print $1}'
}

normalize_key() {
  echo "$1" | tr '[:lower:]' '[:upper:]' | sed -E 's/[^A-Z0-9]+/_/g; s/^_+//; s/_+$//'
}

get_var() {
  local name="$1"
  local fallback="${2:-}"
  printf '%s' "${!name:-$fallback}"
}

default_url() {
  local id="$1"
  case "$id" in
    qqwry)
      printf '%s' "${QQWRY_URL:-https://github.com/metowolf/qqwry.dat/releases/latest/download/qqwry.dat}"
      ;;
    geolite2-city)
      if [[ -n "${GEOLITE2_CITY_URL:-}" ]]; then
        printf '%s' "$GEOLITE2_CITY_URL"
      elif [[ -n "${MAXMIND_LICENSE_KEY:-}" ]]; then
        printf '%s' "https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-City&license_key=${MAXMIND_LICENSE_KEY}&suffix=tar.gz"
      else
        printf ''
      fi
      ;;
    *)
      printf ''
      ;;
  esac
}

default_type() {
  local id="$1"
  case "$id" in
    geolite2-city) printf 'tar-gz-mmdb' ;;
    *) printf 'raw' ;;
  esac
}

default_target_name() {
  local id="$1"
  case "$id" in
    geolite2-city) printf 'GeoLite2-City.mmdb' ;;
    qqwry) printf '%s' "${TARGET_NAME:-qqwry.dat}" ;;
    *) printf '%s' "$id" ;;
  esac
}

default_min_bytes() {
  local id="$1"
  case "$id" in
    geolite2-city) printf '102400' ;;
    qqwry) printf '102400' ;;
    *) printf '1' ;;
  esac
}

download_raw() {
  local url="$1"
  local output="$2"
  curl -fsSL --retry 5 --retry-delay 2 -A "$USER_AGENT" "$url" -o "$output"
}

download_tar_gz_mmdb() {
  local url="$1"
  local output="$2"
  local archive="$TMP_DIR/archive-$RANDOM.tar.gz"
  local extract_dir="$TMP_DIR/extract-$RANDOM"

  mkdir -p "$extract_dir"
  curl -fsSL --retry 5 --retry-delay 2 -A "$USER_AGENT" "$url" -o "$archive"
  tar -xzf "$archive" -C "$extract_dir"

  local mmdb
  mmdb="$(find "$extract_dir" -type f -name '*.mmdb' | head -n 1)"
  if [[ -z "$mmdb" ]]; then
    log "ERROR: archive did not contain an .mmdb file."
    return 4
  fi

  cp -f "$mmdb" "$output"
}

replace_if_changed() {
  local raw="$1"
  local target_path="$2"
  local target_name="$3"
  local min_bytes="$4"

  if [[ ! -f "$raw" ]]; then
    log "ERROR: downloaded file for $target_name was not created."
    return 3
  fi

  local size
  if ! size=$(stat -c%s "$raw" 2>/dev/null); then
    size=$(stat -f%z "$raw")
  fi
  if [[ "$size" -lt "$min_bytes" ]]; then
    log "ERROR: downloaded file for $target_name is too small ($size bytes), aborting this database."
    return 3
  fi

  local new_sha
  new_sha="$(sha256_file "$raw")"
  log "$target_name new sha256=$new_sha size=$size"

  if [[ -f "$target_path" ]]; then
    local old_sha
    old_sha="$(sha256_file "$target_path" || true)"
    if [[ "$old_sha" == "$new_sha" ]]; then
      log "$target_name unchanged (sha256 same)."
      date -u +'%Y-%m-%dT%H:%M:%SZ' > "$DATA_DIR/$target_name.updated_at"
      echo "$new_sha" > "$DATA_DIR/$target_name.sha256"
      echo "${new_sha:0:12}" > "$DATA_DIR/$target_name.version"
      chmod 644 "$DATA_DIR/$target_name.updated_at" "$DATA_DIR/$target_name.sha256" "$DATA_DIR/$target_name.version"
      return 0
    fi
  fi

  local tmp_target="$DATA_DIR/.${target_name}.tmp"
  cp -f "$raw" "$tmp_target"
  sync || true
  mv -f "$tmp_target" "$target_path"
  chmod 644 "$target_path"

  date -u +'%Y-%m-%dT%H:%M:%SZ' > "$DATA_DIR/$target_name.updated_at"
  echo "$new_sha" > "$DATA_DIR/$target_name.sha256"
  echo "${new_sha:0:12}" > "$DATA_DIR/$target_name.version"
  chmod 644 "$DATA_DIR/$target_name.updated_at" "$DATA_DIR/$target_name.sha256" "$DATA_DIR/$target_name.version"

  log "Update done. replaced $target_path"
}

update_database() {
  local id="$1"
  local key
  key="$(normalize_key "$id")"

  local type
  local url
  local target_name
  local min_bytes

  type="$(get_var "${key}_TYPE" "$(default_type "$id")")"
  url="$(get_var "${key}_URL" "$(default_url "$id")")"
  target_name="$(get_var "${key}_TARGET_NAME" "$(default_target_name "$id")")"
  min_bytes="$(get_var "${key}_MIN_BYTES" "$(default_min_bytes "$id")")"

  if [[ -z "$url" ]]; then
    log "ERROR: no download URL configured for database '$id'."
    return 2
  fi

  local target_path="$DATA_DIR/$target_name"
  local raw="$TMP_DIR/$target_name.raw"

  log "Updating database '$id' -> $target_path"
  case "$type" in
    raw)
      download_raw "$url" "$raw" || return $?
      ;;
    tar-gz-mmdb)
      download_tar_gz_mmdb "$url" "$raw" || return $?
      ;;
    *)
      log "ERROR: unsupported database type '$type' for '$id'."
      return 2
      ;;
  esac

  replace_if_changed "$raw" "$target_path" "$target_name" "$min_bytes"
}

# Use a lock to prevent concurrent writes from corrupting files.
exec 200>"$LOCK_FILE"
flock -n 200 || { log "Another update is running, exit."; exit 0; }

successes=0
failures=0

IFS=',' read -ra database_ids <<< "$DATABASES"
for raw_id in "${database_ids[@]}"; do
  id="$(echo "$raw_id" | xargs)"
  if [[ -z "$id" ]]; then
    continue
  fi

  if update_database "$id"; then
    successes=$((successes + 1))
  else
    rc="$?"
    failures=$((failures + 1))
    log "ERROR: database '$id' update failed with exit code $rc."
  fi
done

log "Database update finished. successes=$successes failures=$failures"

if [[ "$successes" -eq 0 && "$failures" -gt 0 ]]; then
  exit 1
fi

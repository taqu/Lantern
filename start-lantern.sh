#!/usr/bin/env bash
# Lantern startup script for Linux.
# On first run, calls 'Lantern.Web setup' to create directories, generate secrets,
# and provision the initial administrator. Subsequent runs start the server directly.
# Press Ctrl+C to stop.
#
# Usage: ./start-lantern.sh [--data-dir /path/to/data]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_EXE="$SCRIPT_DIR/app/Lantern.Web"

if [[ ! -x "$APP_EXE" ]]; then
    echo "ERROR: Lantern executable not found or not executable: $APP_EXE" >&2
    exit 1
fi

# Determine data root (can be overridden via --data-dir flag or environment variable).
DATA_ROOT="${LANTERN_DATA_ROOT:-}"
EXTRA_ARGS=()

for arg in "$@"; do
    case "$arg" in
        --data-dir=*) DATA_ROOT="${arg#*=}" ;;
        *) EXTRA_ARGS+=("$arg") ;;
    esac
done

# If --data-dir was passed separately (not =), capture the next value.
for i in "${!@}"; do
    if [[ "${@:$i:1}" == "--data-dir" ]]; then
        DATA_ROOT="${@:$((i+1)):1}"
    fi
done

DATA_ROOT_DEFAULT="${DATA_ROOT:-/var/lib/lantern}"

# ── First-run detection ───────────────────────────────────────────────────────
# Setup is required if the master key file has not been created yet.
MASTER_KEY_FILE="/etc/lantern/secrets/meili-master-key.txt"
if [[ -n "$DATA_ROOT" ]]; then
    # Rootless: secrets live alongside data
    MASTER_KEY_FILE="$DATA_ROOT/secrets/meili-master-key.txt"
fi

if [[ ! -f "$MASTER_KEY_FILE" ]]; then
    echo "==> First-run setup..."
    SETUP_ARGS=(setup)
    if [[ -n "$DATA_ROOT" ]]; then
        SETUP_ARGS+=(--data-dir "$DATA_ROOT")
    fi
    "$APP_EXE" "${SETUP_ARGS[@]}"
    echo
fi

# ── Read application URL from config ─────────────────────────────────────────
CONFIG_FILE="/etc/lantern/appsettings.json"
if [[ -n "$DATA_ROOT" ]]; then
    CONFIG_FILE="$DATA_ROOT/../config/appsettings.json"
fi
LANTERN_URL="http://127.0.0.1:5000"
if [[ -f "$CONFIG_FILE" ]]; then
    URL_VALUE=$(python3 -c "import json,sys; d=json.load(open('$CONFIG_FILE')); print(d.get('Urls','').split(';')[0].rstrip('/'))" 2>/dev/null || true)
    if [[ -n "$URL_VALUE" ]]; then
        LANTERN_URL="$URL_VALUE"
    fi
fi

# ── Launch Lantern ────────────────────────────────────────────────────────────
echo "==> Starting Lantern..."
START_ARGS=()
if [[ -n "$DATA_ROOT" ]]; then
    START_ARGS+=(--data-dir "$DATA_ROOT")
fi

"$APP_EXE" "${START_ARGS[@]+"${START_ARGS[@]}"}" &
LANTERN_PID=$!

# ── Graceful shutdown on SIGTERM/SIGINT ──────────────────────────────────────
cleanup() {
    echo
    echo "==> Stopping Lantern (PID $LANTERN_PID)..."
    kill -TERM "$LANTERN_PID" 2>/dev/null || true
    wait "$LANTERN_PID" 2>/dev/null || true
    echo "    Lantern stopped."
}
trap cleanup SIGTERM SIGINT

# ── Wait for readiness ────────────────────────────────────────────────────────
HEALTH_URL="$LANTERN_URL/api/health"
DEADLINE=$(( $(date +%s) + 120 ))
READY=false

while [[ $(date +%s) -lt $DEADLINE ]]; do
    if ! kill -0 "$LANTERN_PID" 2>/dev/null; then
        echo "ERROR: Lantern process exited unexpectedly." >&2
        exit 1
    fi
    HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 3 "$HEALTH_URL" 2>/dev/null || echo "000")
    if [[ "$HTTP_STATUS" =~ ^[2-3] ]]; then
        READY=true
        break
    fi
    sleep 1
done

echo "    OK   Lantern running at: $LANTERN_URL"
if ! $READY; then
    echo "    WARN Did not confirm health endpoint within 120 s. Lantern may still be starting."
fi
echo "    Press Ctrl+C to stop."
echo

# ── Wait for the process to exit ─────────────────────────────────────────────
wait "$LANTERN_PID"
EXIT_CODE=$?
if [[ $EXIT_CODE -ne 0 && $EXIT_CODE -ne 143 ]]; then
    echo "WARN: Lantern exited with code $EXIT_CODE." >&2
fi

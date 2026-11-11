#!/usr/bin/env bash
# One-time development environment setup for Lantern on Linux.
# Safe to run multiple times — existing secrets and configuration are preserved.
#
# Usage: ./scripts/setup-dev.sh [--admin-username <name>] [--meili-exe <path>] [--reset-admin]
#
# Prerequisites:
#   .NET 10 SDK
#   tools/meilisearch/meilisearch  (run download-meilisearch.sh to fetch)
#   Optional: resources/granite-embedding-97M-multilingual-r2.gguf for hybrid search

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB_ROOT="$REPO_ROOT/src/Lantern.Web"
APP_DATA="$WEB_ROOT/App_Data"
SECRETS_DIR="$APP_DATA/secrets"
MEILI_DATA="$APP_DATA/meilisearch"
MEILI_KEY_FILE="$SECRETS_DIR/meilisearch-master-key.txt"
MEILI_API_KEY_FILE="$SECRETS_DIR/meili-api-key.txt"
LOCAL_CONFIG="$WEB_ROOT/appsettings.Development.Local.json"
DB_PATH="$APP_DATA/database/lantern.db"

ADMIN_USERNAME="admin"
MEILI_EXE="$REPO_ROOT/tools/meilisearch/meilisearch"
RESET_ADMIN=false

# ── Argument parsing ──────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case "$1" in
        --admin-username) ADMIN_USERNAME="$2"; shift 2 ;;
        --meili-exe)      MEILI_EXE="$2"; shift 2 ;;
        --reset-admin)    RESET_ADMIN=true; shift ;;
        *) echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

# ── Helpers ───────────────────────────────────────────────────────────────────
ok()   { echo "  [OK]  $*"; }
warn() { echo "  [WARN] $*"; }

# ── --reset-admin mode ───────────────────────────────────────────────────────
if $RESET_ADMIN; then
    echo ""
    echo "Lantern development — reset administrator password"
    echo "==================================================="
    echo ""

    if [[ ! -f "$DB_PATH" ]]; then
        echo "ERROR: Development database not found at: $DB_PATH" >&2
        echo "Run ./scripts/setup-dev.sh (without --reset-admin) first." >&2
        exit 1
    fi

    echo "Resetting administrator password (this may build the project)..."
    export ASPNETCORE_ENVIRONMENT=Development
    export LANTERN_DEV_RESET_ADMIN=1
    NEW_PASSWORD=$(dotnet run --project "$WEB_ROOT" --no-launch-profile 2>&1 \
        | grep -v -E '^\s*(Build|Warning|Error|info|warn|fail|crit|\[)' \
        | sed '/^\s*$/d' | tail -1) || true
    unset LANTERN_DEV_RESET_ADMIN
    EXIT_CODE=$?

    if [[ $EXIT_CODE -ne 0 || -z "$NEW_PASSWORD" ]]; then
        echo "" >&2
        echo "ERROR: Password reset failed (exit code $EXIT_CODE)." >&2
        exit 1
    fi

    # Persist new password via User Secrets equivalent (write to secrets JSON)
    dotnet user-secrets set "DevAdmin:Password" "$NEW_PASSWORD" --project "$WEB_ROOT" > /dev/null

    EXISTING_USER=$(dotnet user-secrets list --project "$WEB_ROOT" 2>/dev/null \
        | grep '^DevAdmin:Username' | sed 's/.*= *//' | head -1 || echo "admin")

    echo ""
    echo "  Administrator password reset successfully."
    echo ""
    echo "  Development administrator credentials"
    echo "  Username : $EXISTING_USER"
    echo "  Password : $NEW_PASSWORD"
    echo ""
    echo "  Save the password now. It will not be shown again."
    echo "  Existing sessions have been invalidated."
    echo ""
    exit 0
fi

# ── Normal setup ──────────────────────────────────────────────────────────────
echo ""
echo "Lantern development setup"
echo "========================="
echo ""

# ── 1. Check .NET SDK ─────────────────────────────────────────────────────────
echo -n "Checking .NET SDK..."
if ! command -v dotnet &>/dev/null; then
    echo " NOT FOUND"
    echo "ERROR: dotnet SDK is not installed or not on PATH. Install .NET 10 SDK." >&2
    exit 1
fi
SDK_VERSION=$(dotnet --version 2>&1 | tr -d '[:space:]')
if [[ "$SDK_VERSION" != 10.* ]]; then
    echo " WARNING: found $SDK_VERSION, Lantern requires .NET 10 SDK."
else
    echo " $SDK_VERSION"
fi

# ── 2. Check Meilisearch binary ───────────────────────────────────────────────
echo -n "Checking Meilisearch binary..."
if [[ -x "$MEILI_EXE" ]]; then
    ok "found: $MEILI_EXE"
else
    echo " NOT FOUND"
    warn "Meilisearch not found at: $MEILI_EXE"
    warn "Without Meilisearch, the app will start but search will be unavailable."
fi

# ── 3. Check optional GGUF model ──────────────────────────────────────────────
MODEL_PATH="$REPO_ROOT/resources/granite-embedding-97M-multilingual-r2.gguf"
echo -n "Checking embedding model..."
if [[ -f "$MODEL_PATH" ]]; then
    ok "found"
else
    echo " not present (hybrid search disabled)"
fi

# ── 4. Create App_Data structure ──────────────────────────────────────────────
echo "Creating development data directories..."
for DIR in "$APP_DATA" "$SECRETS_DIR" "$MEILI_DATA" \
           "$APP_DATA/database" "$APP_DATA/keys" "$APP_DATA/logs"; do
    if [[ ! -d "$DIR" ]]; then
        mkdir -p "$DIR"
        echo "  Created: $DIR"
    fi
done
# Restrict secrets directory to owner only.
chmod 700 "$SECRETS_DIR"

# ── 5. Generate Meilisearch master key ────────────────────────────────────────
if [[ -f "$MEILI_KEY_FILE" ]]; then
    echo "Meilisearch master key: already exists (preserved)"
else
    # 32 cryptographically random bytes → base64
    KEY=$(dd if=/dev/urandom bs=32 count=1 2>/dev/null | base64 | tr -d '\n')
    printf '%s' "$KEY" > "$MEILI_KEY_FILE"
    chmod 600 "$MEILI_KEY_FILE"
    echo "Meilisearch master key: generated"
fi

# ── 6. Write appsettings.Development.Local.json with absolute paths ───────────
if [[ -f "$LOCAL_CONFIG" ]]; then
    echo "appsettings.Development.Local.json: already exists (preserved)"
else
    cat > "$LOCAL_CONFIG" <<EOF
{
  "Meilisearch": {
    "ApiKeyFile": "$MEILI_API_KEY_FILE"
  },
  "MeilisearchProcess": {
    "Managed": true,
    "ExecutablePath": "$MEILI_EXE",
    "DataDirectory": "$MEILI_DATA",
    "MasterKeyFile": "$MEILI_KEY_FILE"
  }
}
EOF
    echo "appsettings.Development.Local.json: created"
fi

# ── 7. Configure development admin credentials via User Secrets ───────────────
echo "Configuring development admin credentials..."
EXISTING_USER=$(dotnet user-secrets list --project "$WEB_ROOT" 2>/dev/null \
    | grep '^DevAdmin:Username' | sed 's/.*= *//' | head -1 || true)

if [[ -n "$EXISTING_USER" ]]; then
    echo "  Admin User Secrets: already set (preserved)"
    echo "  Username : $EXISTING_USER"
    echo "  Password : (stored in User Secrets — use --reset-admin to change)"
else
    # Generate a random 20-char password (15 random bytes → base64, trim padding)
    PASSWORD=$(dd if=/dev/urandom bs=15 count=1 2>/dev/null | base64 | tr -d '=\n')

    dotnet user-secrets set "DevAdmin:Username" "$ADMIN_USERNAME" --project "$WEB_ROOT" > /dev/null
    dotnet user-secrets set "DevAdmin:Password" "$PASSWORD"       --project "$WEB_ROOT" > /dev/null

    echo ""
    echo "  Development administrator credentials"
    echo "  Username : $ADMIN_USERNAME"
    echo "  Password : $PASSWORD"
    echo ""
    echo "  Save the password now. It is stored in User Secrets and will not be shown again."
    echo "  The account is created on first application startup."
    echo "  To change the password later: ./scripts/setup-dev.sh --reset-admin"
    echo ""
fi

# ── 8. Trust dev HTTPS certificate ────────────────────────────────────────────
echo "Checking ASP.NET Core HTTPS development certificate..."
if dotnet dev-certs https --check 2>&1 | grep -q "No valid certificate"; then
    echo "  Trusting development certificate..."
    dotnet dev-certs https --trust > /dev/null 2>&1 && echo "  Certificate trusted." \
        || warn "Could not trust dev cert automatically. Run: dotnet dev-certs https --trust"
else
    echo "  Certificate already trusted."
fi

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "Setup complete."
echo ""
echo "Development ports:"
echo "  Lantern Web  : https://localhost:7108  (http://localhost:5271)"
echo "  Meilisearch  : http://127.0.0.1:7702"
echo "  Embedding    : http://127.0.0.1:7703  (when enabled)"
echo ""
echo "Next steps:"
echo "  VS Code : open workspace, press F5 (Lantern Development)"
echo "  CLI     : ./scripts/start-dev.sh  (create this if needed)"
echo ""

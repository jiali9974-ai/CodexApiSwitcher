#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
WORKSPACE=$(dirname "$SCRIPT_DIR")
case "$(uname -m)" in
  arm64) RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "Unsupported macOS architecture: $(uname -m)" >&2; exit 1 ;;
esac
EXECUTABLE=${1:-"$WORKSPACE/outputs/cross-platform/$RID/Codex API Switcher.app/Contents/MacOS/CodexApiSwitcher"}
if [ ! -x "$EXECUTABLE" ]; then
  echo "macOS executable is missing or not executable: $EXECUTABLE" >&2
  exit 1
fi
APP_BUNDLE=$(CDPATH= cd -- "$(dirname -- "$EXECUTABLE")/../.." && pwd)

PYTHON=python3
if [ -x "/Users/jiali/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3" ]; then
  PYTHON=/Users/jiali/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3
fi

TEST_ROOT=$(mktemp -d "${TMPDIR:-/tmp}/codex-api-switcher-macos-test.XXXXXX")
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$TEST_ROOT/bundle-cache"
if [ "${CAS_TEST_REAL_KEYCHAIN:-0}" != "1" ]; then
  mkdir -p "$TEST_ROOT/bin" "$TEST_ROOT/fake-keychain"
  export FAKE_KEYCHAIN_DIR="$TEST_ROOT/fake-keychain"
  printf '%s\n' \
    '#!/bin/sh' \
    'set -eu' \
    'action=$1; shift' \
    'account=""; password=""' \
    'while [ "$#" -gt 0 ]; do' \
    '  case "$1" in' \
    '    -a) account=$2; shift 2 ;;' \
    '    -w) if [ "$action" = "add-generic-password" ]; then password=$2; shift 2; else shift; fi ;;' \
    '    *) shift ;;' \
    '  esac' \
    'done' \
    'case "$action" in' \
    '  add-generic-password) printf "%s" "$password" > "$FAKE_KEYCHAIN_DIR/$account" ;;' \
    '  find-generic-password) cat "$FAKE_KEYCHAIN_DIR/$account" ;;' \
    '  delete-generic-password) rm -f "$FAKE_KEYCHAIN_DIR/$account" ;;' \
    '  *) exit 2 ;;' \
    'esac' > "$TEST_ROOT/bin/security"
  chmod +x "$TEST_ROOT/bin/security"
  PATH="$TEST_ROOT/bin:$PATH"
  export PATH
fi
CODEX_ROOT="$TEST_ROOT/codex root"
STARTUP_FILE="$TEST_ROOT/com.codex-api-switcher.startup.plist"
SNAPSHOT="$TEST_ROOT/ui.png"
UI_REPORT="$TEST_ROOT/ui-smoke.txt"
SINGLE_REPORT="$TEST_ROOT/single-instance.txt"
PROFILE_NAME=relay.example.test

account_for_path() {
  "$PYTHON" -c 'import os, sys; print(os.path.abspath(sys.argv[1]).lower(), end="")' "$1" | shasum -a 256 | awk '{print $1}'
}

cleanup() {
  if [ -n "${DEFAULT_ACCOUNT:-}" ]; then
    security delete-generic-password -s com.codex-api-switcher.credentials -a "$DEFAULT_ACCOUNT" >/dev/null 2>&1 || true
  fi
  if [ -n "${PROFILE_ACCOUNT:-}" ]; then
    security delete-generic-password -s com.codex-api-switcher.credentials -a "$PROFILE_ACCOUNT" >/dev/null 2>&1 || true
  fi
  rm -rf "$TEST_ROOT"
}
trap cleanup EXIT INT TERM

mkdir -p "$CODEX_ROOT/sessions/2026/06/29" "$CODEX_ROOT/sqlite"
CONFIG="$CODEX_ROOT/config.toml"
ROLLOUT="$CODEX_ROOT/sessions/2026/06/29/rollout-user-1.jsonl"
DATABASE="$CODEX_ROOT/sqlite/state_5.sqlite"
printf '%s\n' \
  'model_provider = "openai"' \
  'model = "gpt-5.5"' \
  'disable_response_storage = true' \
  '' \
  '[mcp_servers.example]' \
  'command = "example"' > "$CONFIG"
printf '%s\n' \
  '{"type":"session_meta","payload":{"id":"user-1","model_provider":"openai"}}' \
  '{"type":"event_msg","payload":{"type":"user_message","message":"conversation body stays unchanged"}}' > "$ROLLOUT"
"$PYTHON" - "$DATABASE" "$ROLLOUT" <<'PY'
import sqlite3, sys
database, rollout = sys.argv[1:]
connection = sqlite3.connect(database)
connection.execute("create table threads(id text primary key, rollout_path text not null, source text not null, first_user_message text not null, has_user_event integer not null default 0, model_provider text not null)")
connection.execute("insert into threads values(?,?,?,?,?,?)", ("user-1", rollout, "vscode", "hello", 0, "openai"))
connection.commit()
connection.close()
PY

DEFAULT_ACCOUNT=$(account_for_path "$CODEX_ROOT/api-switcher/credential.dat")
PROFILE_TOKEN=$(printf '%s' "profile:$PROFILE_NAME" | shasum -a 256 | awk '{print substr($1,1,16)}')
PROFILE_ACCOUNT=$(account_for_path "$CODEX_ROOT/api-switcher/profiles/$PROFILE_TOKEN.dat")

PLATFORM=$("$EXECUTABLE" --platform-info --root "$CODEX_ROOT")
case "$PLATFORM" in
  "OS=macOS; SecretStore=macOS Keychain; Runtime="*) ;;
  *) echo "Platform adapter failed: $PLATFORM" >&2; exit 1 ;;
esac

HOTKEY=$("$EXECUTABLE" --normalize-hotkey --hotkey "Command+Shift+C")
[ "$HOTKEY" = "Shift+Cmd+C" ] || { echo "macOS hotkey normalization failed: $HOTKEY" >&2; exit 1; }

"$EXECUTABLE" --switch-third-party --root "$CODEX_ROOT" \
  --url "https://relay.example.test/v1/responses" --model "relay-model" --key "macos-keychain-test-token" >/dev/null
grep -q '^model_provider = "custom"$' "$CONFIG"
grep -q '^base_url = "https://relay.example.test/v1"$' "$CONFIG"
grep -q '^\[mcp_servers.example\]$' "$CONFIG"
if grep -q 'macos-keychain-test-token' "$CONFIG"; then
  echo "Plaintext API key leaked into config.toml" >&2
  exit 1
fi

HELPER="$CODEX_ROOT/api-switcher/codex-api-switcher-auth-helper"
[ -x "$HELPER" ] || { echo "Stable macOS credential helper was not installed" >&2; exit 1; }
TOKEN=$("$HELPER" --emit-token --root "$CODEX_ROOT")
[ "$TOKEN" = "macos-keychain-test-token" ] || { echo "Keychain helper round-trip failed" >&2; exit 1; }

DB_STATE=$("$PYTHON" - "$DATABASE" <<'PY'
import sqlite3, sys
row = sqlite3.connect(sys.argv[1]).execute("select model_provider, has_user_event from threads").fetchone()
print(f"{row[0]},{row[1]}")
PY
)
[ "$DB_STATE" = "custom,1" ] || { echo "SQLite history sync failed: $DB_STATE" >&2; exit 1; }
META=$("$PYTHON" - "$ROLLOUT" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as stream:
    print(json.loads(stream.readline())["payload"]["model_provider"])
PY
)
[ "$META" = "custom" ] || { echo "JSONL history sync failed: $META" >&2; exit 1; }

"$EXECUTABLE" --switch-official --root "$CODEX_ROOT" --model "official-model" >/dev/null
grep -q '^model_provider = "openai"$' "$CONFIG"
if grep -q '^\[model_providers.custom' "$CONFIG"; then
  echo "Custom provider remained after official switch" >&2
  exit 1
fi

ARMOR_STATUS=$("$EXECUTABLE" --armor-status --root "$CODEX_ROOT")
case "$ARMOR_STATUS" in
  *未启用*) ;;
  *) echo "Armor status before enabling failed: $ARMOR_STATUS" >&2; exit 1 ;;
esac
"$EXECUTABLE" --enable-armor --root "$CODEX_ROOT" >/dev/null
grep -q '^model_instructions_file = "\./gpt5.5-unrestricted.md"$' "$CONFIG"
grep -q '^\[MODE: UNRESTRICTED\]$' "$CODEX_ROOT/gpt5.5-unrestricted.md"
ARMOR_STATUS=$("$EXECUTABLE" --armor-status --root "$CODEX_ROOT")
case "$ARMOR_STATUS" in
  *已启用*) ;;
  *) echo "Armor status after enabling failed: $ARMOR_STATUS" >&2; exit 1 ;;
esac
"$EXECUTABLE" --restore-armor --root "$CODEX_ROOT" >/dev/null
if grep -q '^model_instructions_file' "$CONFIG"; then
  echo "Armor model_instructions_file remained after restore" >&2
  exit 1
fi
if [ -e "$CODEX_ROOT/gpt5.5-unrestricted.md" ]; then
  echo "Armor instructions file remained after restore" >&2
  exit 1
fi

CODEX_API_SWITCHER_STARTUP_FILE="$STARTUP_FILE" "$EXECUTABLE" --enable-startup --root "$CODEX_ROOT" >/dev/null
plutil -lint "$STARTUP_FILE" >/dev/null
grep -q '<string>/usr/bin/open</string>' "$STARTUP_FILE"
grep -q '<string>--startup-launch</string>' "$STARTUP_FILE"
[ "$(CODEX_API_SWITCHER_STARTUP_FILE="$STARTUP_FILE" "$EXECUTABLE" --show-startup --root "$CODEX_ROOT")" = "Enabled" ]
CODEX_API_SWITCHER_STARTUP_FILE="$STARTUP_FILE" "$EXECUTABLE" --disable-startup --root "$CODEX_ROOT" >/dev/null
[ ! -e "$STARTUP_FILE" ]

/usr/bin/open -W "$APP_BUNDLE" --args --render-ui "$SNAPSHOT" --root "$CODEX_ROOT"
[ -s "$SNAPSHOT" ] || { echo "Avalonia macOS UI snapshot was not created" >&2; exit 1; }

if [ "${CAS_TEST_REAL_KEYCHAIN:-0}" = "1" ]; then
  /usr/bin/open -W "$APP_BUNDLE" --args --ui-smoke-test "$UI_REPORT" --root "$CODEX_ROOT"
  grep -q '^RESULT: PASS$' "$UI_REPORT"
  for step in save-profile-button delete-profile-button switch-third-party-button switch-official-button reset-config-button repair-sidebar-button armor-reminder-button armor-button restore-armor-button rollback-button launch-codex-button close-codex-button; do
    grep -q "^PASS $step " "$UI_REPORT"
  done
fi

INSTANCE_SCOPE="macos-test-$(date +%s)-$$"
/usr/bin/open -W "$APP_BUNDLE" --args --single-instance-smoke-report "$SINGLE_REPORT" --instance-scope "$INSTANCE_SCOPE" --root "$CODEX_ROOT" &
OPEN_PID=$!
sleep 2
"$EXECUTABLE" --instance-scope "$INSTANCE_SCOPE" --root "$CODEX_ROOT"
wait "$OPEN_PID"
grep -q '^PASS: existing macOS CAS instance received activation.$' "$SINGLE_REPORT"

echo "PASS: macOS Keychain, helper, API switching, history sync, startup plist, hotkey mapping, single-instance activation, and Avalonia UI."

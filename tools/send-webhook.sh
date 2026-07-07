#!/usr/bin/env bash
# Send a test webhook to a SuaveHooks capture URL.
#
# Usage:
#   ./tools/send-webhook.sh <capture-url> [options]
#
# Examples:
#   ./tools/send-webhook.sh http://localhost:8080/u/USER_ID/my-endpoint
#   ./tools/send-webhook.sh http://localhost:8080/u/USER_ID/my-endpoint --method POST --preset stripe
#   ./tools/send-webhook.sh http://localhost:8080/u/USER_ID/my-endpoint --body '{"event":"test"}'
#   ./tools/send-webhook.sh http://localhost:8080/u/USER_ID/my-endpoint --hmac-secret whsec_test --body '{"ok":true}'
#
# Options:
#   --method METHOD       HTTP method (default: POST)
#   --body JSON           Request body (default: generic test payload)
#   --preset NAME         Use built-in payload: generic, stripe, github
#   --query STRING        Query string, e.g. 'foo=bar&baz=1'
#   --hmac-secret SECRET  Add X-Hub-Signature-256 header (GitHub-style)
#   --file PATH           Read body from file
#   -h, --help            Show this help

set -euo pipefail

METHOD="POST"
BODY=""
PRESET="generic"
QUERY=""
HMAC_SECRET=""
BODY_FILE=""

GENERIC_BODY='{
  "event": "test.webhook",
  "message": "Hello from SuaveHooks send-webhook tool",
  "timestamp": "2026-01-01T00:00:00Z"
}'

STRIPE_BODY='{
  "id": "evt_test_webhook",
  "type": "checkout.session.completed",
  "data": { "object": { "id": "cs_test_123", "amount_total": 2000, "currency": "usd" } }
}'

GITHUB_BODY='{
  "ref": "refs/heads/main",
  "repository": { "full_name": "suavehooks/demo" },
  "pusher": { "name": "test-user" }
}'

usage() {
  sed -n '2,18p' "$0"
  exit "${1:-0}"
}

[[ $# -lt 1 ]] && usage 1
[[ "$1" == "-h" || "$1" == "--help" ]] && usage 0

URL="$1"
shift

while [[ $# -gt 0 ]]; do
  case "$1" in
    --method) METHOD="$2"; shift 2 ;;
    --body) BODY="$2"; shift 2 ;;
    --preset) PRESET="$2"; shift 2 ;;
    --query) QUERY="$2"; shift 2 ;;
    --hmac-secret) HMAC_SECRET="$2"; shift 2 ;;
    --file) BODY_FILE="$2"; shift 2 ;;
    -h|--help) usage 0 ;;
    *) echo "Unknown option: $1" >&2; usage 1 ;;
  esac
done

if [[ -n "$BODY_FILE" ]]; then
  BODY="$(cat "$BODY_FILE")"
elif [[ -z "$BODY" ]]; then
  case "$PRESET" in
    generic) BODY="$GENERIC_BODY" ;;
    stripe)  BODY="$STRIPE_BODY" ;;
    github)  BODY="$GITHUB_BODY" ;;
    *) echo "Unknown preset: $PRESET (use generic, stripe, github)" >&2; exit 1 ;;
  esac
fi

if [[ -n "$QUERY" ]]; then
  [[ "$QUERY" != \?* ]] && QUERY="?$QUERY"
  URL="${URL}${QUERY}"
fi

CURL_ARGS=(-sS -w "\n\nHTTP %{http_code} in %{time_total}s\n" -X "$METHOD" -H "User-Agent: SuaveHooks-send-webhook/1.0" -H "X-SuaveHooks-Test: true")

if [[ "$METHOD" != "GET" && "$METHOD" != "DELETE" && -n "$BODY" ]]; then
  CURL_ARGS+=(-H "Content-Type: application/json" -d "$BODY")
fi

if [[ -n "$HMAC_SECRET" ]]; then
  SIG="sha256=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$HMAC_SECRET" | awk '{print $2}')"
  CURL_ARGS+=(-H "X-Hub-Signature-256: $SIG")
fi

echo "→ $METHOD $URL"
echo "---"
curl "${CURL_ARGS[@]}" "$URL"

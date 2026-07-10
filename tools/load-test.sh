#!/usr/bin/env bash
# Simple webhook ingestion load test for SuaveHooks capture URLs.
#
# Usage:
#   ./tools/load-test.sh <capture-url> [options]
#
# Examples:
#   ./tools/load-test.sh http://localhost:8080/u/USER_ID/my-endpoint
#   ./tools/load-test.sh http://localhost:8080/u/USER_ID/my-endpoint --requests 200 --concurrency 20
#   ./tools/load-test.sh http://localhost:8080/u/USER_ID/my-endpoint --requests 200 --concurrency 20 --delay 0.25

set -euo pipefail

URL=""
REQUESTS=100
CONCURRENCY=10
DELAY=0

usage() {
  sed -n '2,10p' "$0"
  exit 0
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage ;;
    --requests) REQUESTS="$2"; shift 2 ;;
    --concurrency) CONCURRENCY="$2"; shift 2 ;;
    --delay) DELAY="$2"; shift 2 ;;
    http*) URL="$1"; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

if [[ -z "$URL" ]]; then
  echo "Capture URL required." >&2
  usage
fi

BODY='{"event":"load.test","source":"load-test.sh"}'
TMPDIR="${TMPDIR:-/tmp}"
RESULTS="$TMPDIR/suavehooks-load-$$"
mkdir -p "$RESULTS"

echo "Load test: $REQUESTS requests, concurrency $CONCURRENCY"
if [[ "$DELAY" != "0" ]]; then
  echo "Delay between launches: ${DELAY}s"
fi
echo "Target: $URL"
echo ""

launch_request() {
  index="$1"
  code=$(curl -sS -o /dev/null -w "%{http_code}" -X POST -H "Content-Type: application/json" -H "X-SuaveHooks-Test: true" -d "$BODY" "$URL" || true)
  [[ -n "$code" ]] || code=000
  printf '%s\n' "$code" > "$RESULTS/code-$index.txt"
}

active=0
for index in $(seq 1 "$REQUESTS"); do
  launch_request "$index" &
  active=$((active + 1))

  if [[ "$active" -ge "$CONCURRENCY" ]]; then
    wait -n || true
    active=$((active - 1))
  fi

  if [[ "$DELAY" != "0" ]]; then
    sleep "$DELAY"
  fi
done

wait || true

cat "$RESULTS"/code-*.txt > "$RESULTS/codes.txt"

OK=$(grep -c '^200$' "$RESULTS/codes.txt" || true)
ERR=$((REQUESTS - OK))
echo "Completed: $REQUESTS requests"
echo "  200 OK: $OK"
echo "  errors: $ERR"
rm -rf "$RESULTS"

if [[ "$ERR" -gt 0 ]]; then
  exit 1
fi

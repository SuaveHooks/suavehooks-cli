#!/usr/bin/env bash
# Forward SuaveHooks captures to a local development server.
#
# Usage:
#   ./tools/listen.sh --server URL --endpoint ID --local URL --api-key KEY
#
# Uses the suavehooks CLI from this repo (dotnet run). After installing a release
# binary, you can call `suavehooks listen ...` directly.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

if command -v suavehooks >/dev/null 2>&1; then
  exec suavehooks "$@"
fi

exec dotnet run --project "$ROOT/src/SuaveHooks.Cli/SuaveHooks.Cli.fsproj" -- "$@"

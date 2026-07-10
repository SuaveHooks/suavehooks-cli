# suavehooks-cli

Public client tools for [SuaveHooks](https://github.com/ademar/SuaveHooks) — the hosted webhook capture platform.

This repository contains tools that run **on your machine** and talk to a SuaveHooks deployment via the REST API. The platform itself is hosted separately; nothing here needs to be deployed to your server.

## What's included

| Component | Description |
|-----------|-------------|
| **SuaveHooks.Mcp** | MCP server for Cursor / Claude Desktop |
| **SuaveHooks.Cli** | `suavehooks` CLI (`listen`, `send`) |
| **tools/** | Shell helpers: `send-webhook.sh`, `load-test.sh`, `listen.sh` |

## Downloads

Pre-built binaries are attached to [GitHub Releases](https://github.com/SuaveHooks/suavehooks-cli/releases/latest):

| Asset | Platform |
|-------|----------|
| `suavehooks-mcp-osx-arm64.tar.gz` | macOS (Apple Silicon) |
| `suavehooks-mcp-linux-x64.tar.gz` | Linux x64 |
| `suavehooks-mcp-linux-arm64.tar.gz` | Linux arm64 |
| `suavehooks-mcp-win-x64.zip` | Windows x64 |
| `suavehooks-cli.tar.gz` | Shell scripts + `mcp.json.example` |

Hosted SuaveHooks sites link here from **`/docs/mcp`**.

## MCP server (Cursor / Claude)

1. Create an API key in your SuaveHooks dashboard (**Settings → API Keys**).
2. Download the MCP binary for your OS from Releases and extract it.
3. Add to your MCP config (see `tools/mcp.json.example`):

```json
{
  "mcpServers": {
    "suavehooks": {
      "command": "/path/to/SuaveHooks.Mcp",
      "env": {
        "SUAVEHOOKS_API_URL": "https://your-app.example.com",
        "SUAVEHOOKS_API_KEY": "sh_your_api_key_here"
      }
    }
  }
}
```

**Tools:** `list_endpoints`, `create_endpoint`, `list_requests`, `get_request`, `replay_request`.

### Run from source

```bash
export SUAVEHOOKS_API_URL=https://your-app.example.com
export SUAVEHOOKS_API_KEY=sh_your_key
dotnet run --project src/SuaveHooks.Mcp
```

## CLI

### Forward captures to localhost

```bash
./tools/listen.sh \
  --server https://your-app.example.com \
  --endpoint YOUR_ENDPOINT_ID \
  --local http://localhost:3000/webhooks \
  --api-key sh_your_api_key
```

Or:

```bash
dotnet run --project src/SuaveHooks.Cli -- listen \
  --server https://your-app.example.com \
  --endpoint YOUR_ENDPOINT_ID \
  --local http://localhost:3000/webhooks \
  --api-key sh_your_api_key
```

### Send a test webhook

```bash
./tools/send-webhook.sh https://your-app.example.com/u/USER_ID/my-endpoint --preset stripe
dotnet run --project src/SuaveHooks.Cli -- send --url https://your-app.example.com/u/USER_ID/my-endpoint --preset github-push
```

### Load test

```bash
./tools/load-test.sh https://your-app.example.com/u/USER_ID/my-endpoint --requests 100 --concurrency 10
```

## Environment variables

| Variable | Description |
|----------|-------------|
| `SUAVEHOOKS_API_URL` | Base URL of your SuaveHooks deployment (no trailing slash) |
| `SUAVEHOOKS_API_KEY` | API key from `/api-keys` (`sh_…`) |

## Releasing

Tag a version to build and publish release assets:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## License

MIT

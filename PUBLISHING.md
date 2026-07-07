# Publishing this repository

This directory is the **public** `suavehooks-cli` repo. It was extracted from the private SuaveHooks platform repository.

## First-time setup on GitHub

```bash
cd /Users/ademar/work/suavehooks-cli
git init
git add .
git commit -m "Initial public release of MCP server and CLI tools"
git branch -M main
git remote add origin git@github.com:ademar/suavehooks-cli.git
git push -u origin main
```

Create the repository on GitHub as **public** before pushing.

## First release (required for /docs/mcp download links)

```bash
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions will attach MCP binaries and the CLI scripts tarball to the release.

## Platform repo

The private SuaveHooks app points download links to:

`https://github.com/ademar/suavehooks-cli/releases/latest/download`

Override on the platform with `GITHUB_RELEASES_BASE` if you use a fork or org.

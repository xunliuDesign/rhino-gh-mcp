# Plugins

Two compiled binaries live inside Rhino at runtime:

| Plugin | Type | Listens on | Source |
|--------|------|------------|--------|
| `rhino_gh_mcp.gha` | Grasshopper assembly (C#) | loopback HTTP `:9999` | [./grasshopper/](./grasshopper/) |
| `rhino_gh_mcp.rhp` | Rhino plugin (C#) | loopback TCP `:9876` | [./rhino/](./rhino/) |

For v0.1.0 the **Grasshopper plugin source from `_archive project/rhino_gh_mcp-main/rhino_gh_mcp/`
is the canonical implementation** — the v1 Python server is wire-compatible
with it. The Rhino plugin is being rewritten clean in `./rhino/` (the v0 one
in `_archive project/rhino_gh_mcp-main/rhino_script_client/` mixed in a
StreamDiffusion experiment we're dropping).

## Build (v0 .gha, until v1 lands)

```bash
cd "_archive project/rhino_gh_mcp-main/rhino_gh_mcp"
dotnet build -c Release
# .gha will be at bin/Release/net7.0-windows/rhino_gh_mcp.gha (Windows)
# Drop it in: ~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper/Libraries/  (Mac)
#         or: %APPDATA%\Grasshopper\Libraries\  (Windows)
```

## Distribution roadmap

Once the v1 plugin work in `./grasshopper/` and `./rhino/` lands we'll publish
both as a single Yak package — installable from Rhino via:

```
_PackageManager → search "rhino-gh-mcp"
```

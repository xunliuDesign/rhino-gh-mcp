# Plugins

Two compiled binaries live inside Rhino at runtime:

| Plugin | Type | Listens on | Source |
|--------|------|------------|--------|
| `rhino_gh_mcp.gha` | Grasshopper assembly (C#) | loopback HTTP `:9999` | [./grasshopper/](./grasshopper/) |
| `rhino_gh_mcp.rhp` | Rhino plugin (C#) | loopback TCP `:9876` | [./rhino/](./rhino/) |

**Grasshopper plugin (v0.1.0)** — fresh build under [`./grasshopper/`](./grasshopper/).
Targets Rhino 8 only (net7.0 + net7.0-windows). All v0 command handlers ported.

**Rhino plugin** — being rewritten in [`./rhino/`](./rhino/). The v0 source in
`_archive project/rhino_gh_mcp-main/rhino_script_client/` mixes the MCP service
with an unrelated StreamDiffusion experiment we are dropping. P1 of the
roadmap.

## Build the Grasshopper plugin

```bash
cd plugins/grasshopper
dotnet build -c Release
# Mac:     bin/Release/net7.0/RhinoGhMcp.gha
# Windows: bin/Release/net7.0-windows/RhinoGhMcp.gha
```

Install path:
- **Mac**: `~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper/Libraries/`
- **Windows**: `%APPDATA%\Grasshopper\Libraries\`

Right-click the copied `.gha` and Unblock / Open-anyway on first launch, then
restart Rhino.

## Distribution roadmap

Once the v1 plugin work in `./grasshopper/` and `./rhino/` lands we'll publish
both as a single Yak package — installable from Rhino via:

```
_PackageManager → search "rhino-gh-mcp"
```

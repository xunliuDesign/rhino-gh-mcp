# Plugins

Two compiled binaries live inside Rhino at runtime:

| Plugin | Type | Listens on | Source |
|--------|------|------------|--------|
| `rhino_gh_mcp.gha` | Grasshopper assembly (C#) | loopback HTTP `:9999` | [./grasshopper/](./grasshopper/) |
| `rhino_gh_mcp.rhp` | Rhino plugin (C#) | loopback TCP `:9876` | [./rhino/](./rhino/) |

**Grasshopper plugin (v0.1.0)** — fresh build under [`./grasshopper/`](./grasshopper/).
Targets Rhino 8 only (net7.0 + net7.0-windows). All v0 command handlers ported.

**Rhino plugin (v0.1.0)** — fresh build under [`./rhino/`](./rhino/). Clean
carve-out from the v0 archive (StreamDiffusion experiment dropped). Hosts
the TCP-JSON listener on `127.0.0.1:9876` that the Python `rhino_*` tools
talk to. Single net7.0 target works on both Mac and Windows.

## Build & install everything

Each plugin ships its own `reinstall.sh` that builds + installs + handles
the macOS Rhino-is-still-running quirk:

```bash
cd plugins/grasshopper && ./reinstall.sh          # GH plugin
cd plugins/rhino       && ./reinstall.sh          # Rhino plugin (first time: see plugins/rhino/README.md)
```

After installing, restart Rhino. Then:
- Open Grasshopper → drag **rhino-gh-mcp Server (v1)** onto a canvas, set `Run = True`
- In Rhino's command line, type `_ToggleMcpService` to start the listener

You can now run the smoke tests under [../examples](../examples).

## Distribution roadmap

Once the v1 plugin work in `./grasshopper/` and `./rhino/` lands we'll publish
both as a single Yak package — installable from Rhino via:

```
_PackageManager → search "rhino-gh-mcp"
```

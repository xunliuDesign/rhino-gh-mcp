# rhino-gh-mcp

> An MCP server + Rhino/Grasshopper plugins that give an LLM detailed, tiered
> control over a Rhino 8 modeling session — for parametric design, architectural
> research, and teaching.

**Status:** v0.1.x — Python MCP server, Grasshopper `.gha` plugin, and
Rhino `.rhp` plugin all build clean and pass smoke tests. The Skills library,
Streamable HTTP transport, and `.mcpb` packaging land next.

> **Picking up the project?** Read [`docs/handoff.md`](./docs/handoff.md) first —
> it's the single canonical "what is this and where are we" document.

## What this is

Three things in one repo:

1. **`server/`** — A Python MCP server (FastMCP / `mcp` SDK ≥ 1.20). It exposes
   tiered tool surfaces (parameter-only, curated-group, full-authority) to any
   MCP client (Claude Desktop, Claude Code, Cursor, custom web UI later).
2. **`plugins/grasshopper/`** and **`plugins/rhino/`** — C# plugins that host
   the in-Rhino/in-Grasshopper command bridges. The MCP server talks to them
   over loopback HTTP (GH, port 9999) and TCP-JSON (Rhino, port 9876).
3. **`skills/`** — Anthropic Agent Skills bundles. Each skill describes one
   architectural workflow (landform from contours, façade panelization, zoning
   envelope, …) and tells the LLM which tools to use in what order.

## The control hierarchy

The server picks one of three policies at startup; the policy filters which
tools are advertised to the LLM:

| Level | Name | What the LLM can do | Use case |
|-------|------|---------------------|----------|
| **L1** | `parameter` | Read canvas state, change slider/panel/toggle values, recompute, capture viewport | Student exploration, design space sweeps, presentations |
| **L2** | `curated` | L1 + place components from a named allow-list, wire them, manage user-objects | Domain workflows (e.g. terrain), Skills-driven recipes |
| **L3** | `full` | L2 + place any component, inject Python 3 / IronPython 2 / C# script components, execute arbitrary code in Rhino | Research, agent workflows, expert mode |

Pick the policy via `--policy {parameter|curated|full}` or the
`RHINO_GH_MCP_POLICY` environment variable.

## Quick start (development)

```bash
# 1. Install the server
cd server
uv sync                   # creates .venv, installs deps

# 2. Build & install the Grasshopper plugin
cd ../plugins/grasshopper
dotnet build -c Release
# Mac:     cp bin/Release/net7.0/RhinoGhMcp.gha          ~/Library/Application\ Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper/Libraries/
# Windows: copy bin\Release\net7.0-windows\RhinoGhMcp.gha %APPDATA%\Grasshopper\Libraries\

# Launch Rhino 8, open Grasshopper, drop the MCP Server component on the
# canvas (under the MCP tab), wire a Boolean Toggle = True to Run.

# 3. Wire the MCP server into your client. For Claude Desktop, add to
#    ~/Library/Application Support/Claude/claude_desktop_config.json:
#
#    {
#      "mcpServers": {
#        "rhino-gh": {
#          "command": "uv",
#          "args": ["--directory", "<absolute-path>/server", "run", "rhino-gh-mcp"]
#        }
#      }
#    }

# 4. Restart Claude Desktop. The `rhino-gh` tools should appear.
```

## Project layout

```
rhino-gh-mcp/
├── server/                        # Python MCP server
│   ├── src/rhino_gh_mcp/
│   │   ├── server.py              # FastMCP app, tool registration
│   │   ├── config.py              # Policy + transport selection
│   │   ├── bridges/               # HTTP→GH, TCP→Rhino
│   │   ├── policies/              # L1/L2/L3 tool filters
│   │   └── tools/                 # Tool implementations
│   └── pyproject.toml
├── plugins/
│   ├── grasshopper/               # C# .gha (component bridge)
│   └── rhino/                     # C# .rhp (Rhino command bridge)
├── skills/                        # Agent Skills bundles
│   └── landform-from-contours/
├── mcpb/                          # .mcpb (Desktop Extension) manifest
├── docs/
├── examples/
└── _archive project/              # v0 reference implementation (gitignored)
```

## Roadmap

| Phase | Status | Scope |
|-------|--------|-------|
| **P0** | ✅ | Clean skeleton, FastMCP server, all 15 GH commands ported as Python tools, fresh `.gha` builds Mac+Windows |
| **P1** | ⏳ | Rhino `.rhp` clean carve-out from archive, Streamable HTTP transport, viewport capture wired through |
| **P2** | ⏳ | Policy enforcement at tool-registration time, first Skill (`landform-from-contours`) wired up |
| **P3** | ⏳ | Rhino 8 Script-component injection (Python 3 + C#) |
| **P4** | ⏳ | Surface expansion (sliders/panels/toggles/value-lists as first-class tools, baking, runtime messages, canvas screenshot) |
| **P5** | ⏳ | Skills library: massing, façade, structural grid, daylighting, zoning envelope |
| **P6** | ⏳ | Web frontend over Streamable HTTP transport |
| **P7** | ⏳ | RAG for zoning text → 3D envelope → environmental metrics |
| **P8** | ⏳ | Student-facing TA bundle |

## License

MIT — see [LICENSE](./LICENSE).

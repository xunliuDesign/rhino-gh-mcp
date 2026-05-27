# rhino-gh-mcp

> An MCP server + Rhino/Grasshopper plugins that let an LLM read and drive a
> live Rhino 8 modeling session — for parametric design, architectural
> research, and teaching.

**Status**: v0.1.6 — Python MCP server, Grasshopper `.gha` plugin, and
Rhino `.rhp` plugin all build clean and pass smoke tests on Mac and
Windows. The capability gate, widget read/write, and first Skill
(`landform`) are live. Streamable HTTP transport and `.dxt` packaging
are next.

---

## What it does

You install three things — a Python MCP server, a Grasshopper plugin, and
a Rhino plugin — wire your favorite MCP client (Claude Desktop, Claude
Code, Cursor, …) at the server, and you can then talk to the LLM in
natural language about your model: *"add a slider called radius with
range 0-50, then make me a circle with that radius, then bake it to the
current layer"*. The LLM uses the tools below to read your canvas, place
and wire Grasshopper components, change parameters, capture the viewport,
and run Rhino commands.

Three workflow modes ship in v1, all enforced on the **canvas** as
visible component inputs (not hidden in a config file):

| Canvas flag | What it gates |
|---|---|
| `AllowParameters` | The LLM can adjust sliders, toggles, value-lists, panels |
| `AllowComponents` | The LLM can place, wire, and remove components |
| `AllowScripting` | The LLM can write Python/C# script components and execute code |
| `ComponentScope` (0/1/2) | When components are allowed: curated allow-list / GH defaults only / anything installed |

You can flip any of these mid-conversation; the change propagates within
about 3 seconds. No restart.

See [docs/architecture.md](docs/architecture.md) for the full picture and
[docs/handoff.md](docs/handoff.md) for the current status / next steps.

---

## Prerequisites

- **Rhino 8** (Mac or Windows). Rhino 7 is **not** supported — we depend
  on the Rhino 8 .NET 7 runtime.
- **Python 3.11 or 3.12**.
- **[`uv`](https://docs.astral.sh/uv/)** for Python environment + run.
  - Windows: `winget install --id=astral-sh.uv`
  - Mac: `brew install uv` (or `curl -LsSf https://astral.sh/uv/install.sh | sh`)
- **.NET 7+ SDK** (for building the C# plugins from source).
  - Windows: `winget install Microsoft.DotNet.SDK.8`
  - Mac: `brew install --cask dotnet-sdk`
- **Git** for cloning.
- **An MCP client.** Claude Desktop, Claude Code, or Cursor all work.

---

## Install — 5 minutes if you have the prerequisites

### 1. Clone

```bash
git clone https://github.com/xunliuDesign/rhino-gh-mcp.git
cd rhino-gh-mcp
```

### 2. Set up the Python server

```bash
cd server
uv sync           # creates .venv, installs deps + dev deps
uv run pytest -q  # 27 tests should pass — sanity check
cd ..
```

### 3. Build + install the Grasshopper plugin

**Windows:**
```powershell
cd plugins\grasshopper
.\reinstall.ps1
```

**Mac:**
```bash
cd plugins/grasshopper
./reinstall.sh
```

Both scripts: force-quit Rhino if running → `dotnet build -c Release` →
copy the resulting `.gha` to your Grasshopper `Libraries` folder.
First-time PowerShell users may need to run
`Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned`
once.

### 4. Build + install the Rhino plugin

```powershell
# Windows
cd plugins\rhino
.\reinstall.ps1
```

```bash
# Mac
cd plugins/rhino
./reinstall.sh
```

**First time only:** Rhino requires explicit registration. The script
will print the path to the built `.rhp` and tell you to drag it onto
Rhino's main window (or use `_PluginManager → Install...`). After the
first registration, subsequent rebuilds auto-update in place.

### 5. Connect a client

**Claude Code** — already supported by the project-scoped
[`.mcp.json`](./.mcp.json) in the repo root. Start a Claude Code session
inside the repo directory and approve the trust prompt. The `rhino-gh`
server will be available in that session.

**Claude Desktop (classic builds)** — add to
`~/Library/Application Support/Claude/claude_desktop_config.json` (Mac)
or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "rhino-gh": {
      "command": "uv",
      "args": [
        "--directory",
        "/absolute/path/to/rhino-gh-mcp/server",
        "run",
        "rhino-gh-mcp",
        "--policy",
        "curated"
      ]
    }
  }
}
```

Restart Claude Desktop. The `rhino-gh` tools should appear in the tools
panel.

> **Note for the Cowork preview build of Claude Desktop**: that build
> doesn't read `mcpServers` from `claude_desktop_config.json`. It uses
> the Extensions / `.dxt` system instead. `.dxt` packaging is on the
> roadmap; for now, use Claude Code with this build.

**Cursor / other clients** — point them at the same command and args as
the Claude Desktop block above.

### 6. Bring up both bridges in Rhino

1. Launch Rhino 8.
2. In Rhino's command line, type `_ToggleMcpService` and press Enter.
   You should see *"rhino-gh-mcp: MCP service running on 127.0.0.1:9876"*.
3. Open Grasshopper (`_Grasshopper`).
4. In the Grasshopper ribbon, find the **MCP** tab → **Server** group →
   drop the **rhino-gh-mcp Server (v1)** component onto a fresh canvas.
5. Right-click the component's `Run` input → set persistent data to
   `True` (or wire a Boolean Toggle = True). The `Status` output should
   say `Server On 127.0.0.1:9999`.

### 7. Smoke-test from your terminal (optional but recommended)

```bash
cd server
uv run python ../examples/smoke_test_bridge.py       # GH bridge
uv run python ../examples/smoke_test_rhino.py        # Rhino bridge
uv run python ../examples/verify_capability_gate.py  # the canvas flags
```

If those pass, your MCP client should be able to talk to the bridges too.

---

## Using it

In a fresh chat with your MCP client, prime it once:

```
You have access to a Rhino 8 + Grasshopper session through the rhino-gh
MCP server. Before doing anything, call gh_status and rhino_status to
verify both bridges are up, then gh_canvas_summary and
rhino_get_scene_info to see what's there. Work in small steps; recompute
and capture the viewport between meaningful changes.
```

Then describe what you want in plain language. The LLM picks the right
tools. Examples:

- *"Build a parametric tower: circular footprint, adjustable radius and
  floor count, each floor a slab, then a slider for a twist angle."*
- *"Use the landform skill — make me a hill in the middle of the canvas."*
- *"List my layers and put a 5×5 grid of spheres on layer 'Layer 01'."*
- *"What's currently selected in Grasshopper? Show me its parameters."*

For guided workflows (Skills), see [skills/README.md](skills/README.md).
First-party skills:

- **`landform`** — parametric terrain via the bundled MCP landscape
  user-objects.
- **`ladybug-environmental`** — environmental analysis using Ladybug
  Tools (sun path, sun hours, wind rose). Requires the Ladybug
  Grasshopper plugin to be installed.

---

## Repository layout

```
rhino-gh-mcp/
├── server/                          # Python MCP server (uv project)
│   ├── src/rhino_gh_mcp/
│   │   ├── server.py                # FastMCP app + lifespan
│   │   ├── config.py                # CLI + policy presets
│   │   ├── capabilities.py          # Soft capability gate (v0.1.5+)
│   │   ├── bridges/                 # HTTP→GH, TCP→Rhino
│   │   ├── policies/                # Legacy shim (back-compat)
│   │   └── tools/                   # Tool implementations
│   ├── tests/                       # pytest (no Rhino required)
│   └── pyproject.toml
├── plugins/
│   ├── grasshopper/                 # .gha (C# / net7.0 + net7.0-windows)
│   └── rhino/                       # .rhp (C# / net7.0)
├── examples/                        # Bridge-level smoke + verify scripts
├── skills/                          # Anthropic Agent Skills bundles
│   ├── landform/
│   └── ladybug-environmental/
├── mcpb/manifest.json               # Desktop Extension manifest (in progress)
├── docs/
│   ├── architecture.md
│   ├── handoff.md                   # Status + session log
│   ├── performance-notes.md
│   └── generalization.md
└── .mcp.json                        # Project-scoped Claude Code MCP config
```

---

## Troubleshooting

**"Grasshopper bridge unreachable at http://127.0.0.1:9999"**
The MCP Server component isn't running on the GH canvas (no canvas
open, or `Run` is false). Drop one on a fresh canvas and set Run=True.

**"Rhino bridge unreachable at 127.0.0.1:9876"**
You haven't run `_ToggleMcpService` after launching Rhino, or you ran
it twice and toggled the listener off.

**"Capability denied by canvas: set the `AllowScripting` input ..."**
Working as intended — the LLM is asking for a capability that's off on
your canvas. Either flip that toggle on the component or tell the LLM
to use a less-privileged path.

**Tool returns nothing / hangs**
Check `gh_get_runtime_messages` on the relevant component. Most failures
are wiring issues (wrong input name) that surface as runtime errors but
no exception.

**PowerShell "script cannot be loaded" on Windows**
One-time fix: `Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned`.

**`uv sync` doesn't install pytest**
Make sure you're on `uv ≥ 0.5` so the `[dependency-groups]` table is
honored. Older versions need `uv sync --extra dev`.

---

## Roadmap

| Phase | Status | Scope |
|-------|--------|-------|
| **P0** | ✅ | Clean skeleton, FastMCP server, all 15 GH commands ported, `.gha` builds Mac+Windows |
| **P1** | ✅ | Rhino `.rhp` clean carve-out, viewport capture, smoke tests pass on Windows + Mac |
| **P2** | ✅ | Soft capability gate, canvas-side controls, first Skill (`landform`) |
| **P3** | ⏳ | Rhino 8 Script-component injection (Python 3 + C# into the modern Script component) |
| **P4** | 🟡 | Surface expansion — done: list_toggles/value_lists, canvas_summary, find_components, set_toggle, select_value_list, list_blocks, set_view. Remaining: bake_to_rhino, group_components, measure_distance |
| **P5** | 🟡 | Skills library — done: landform, ladybug-environmental (draft). Planned: massing, façade, structural grid, daylighting |
| **P6** | ⏳ | Streamable HTTP transport + a thin web frontend |
| **P7** | ⏳ | `.dxt` packaging for one-click Claude Desktop install |
| **P8** | ⏳ | Student-facing teaching bundle |

---

## License

MIT — see [LICENSE](./LICENSE).

---

## Contributing

Pull requests welcome, especially:

- New Skills for architectural workflows (see [skills/README.md](skills/README.md)
  for the format).
- Bug reports against specific Rhino 8 builds (paste the
  `rhino_version` from `rhino_status`).
- Bridge handlers for command shapes you wish existed.

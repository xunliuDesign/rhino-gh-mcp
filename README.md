# rhino-gh-mcp

> An MCP server + Rhino/Grasshopper plugins that let an LLM read and drive a
> live Rhino 8 modeling session — for parametric design, architectural
> research, and teaching.

**Status**: v0.2.3 — five-Scenario canvas surface (Inspect / Tune /
Coach / Execute / Author), Coach-mode canvas highlights, Skills v2
schema with Execute-mode `allow_tools` gating, and auto-wired
Toggle + Scenario value-list on component drop. Two-click install via
the [`.mcpb` Desktop Extension](https://github.com/modelcontextprotocol/mcpb)
for Claude Desktop and [Yak packages](https://developer.rhino3d.com/guides/yak/)
for Rhino Package Manager. See Quick start below.

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

v0.2 surfaces a single **Scenario** dropdown on the canvas component —
pick what you're trying to do, the engine derives the underlying
capability flags:

| Scenario | What you're trying to do | What the AI can touch |
|---|---|---|
| **Inspect** | "Explain this definition to me" | Read only — no writes |
| **Tune** | "Adjust the parameters I already have" | Sliders / toggles / value-lists |
| **Coach** (default) | "Walk me through edits, show me what changed" | Tune + add curated components, with **canvas highlights** on every change |
| **Execute** | "Follow a Skill recipe, no improvisation" | Only the tools the active Skill declares |
| **Author** | "Free hands" | Full canvas + scripting |

You can flip the Scenario mid-conversation; the change propagates within
about 3 seconds. The v0.1 component is still in the ribbon as
`rhino-gh-mcp Server (v1)` for backwards compatibility with saved `.gh`
files that already use it.

See [docs/architecture.md](docs/architecture.md) for the full picture,
[docs/v0.2-redesign.md](docs/v0.2-redesign.md) for the Scenario design,
and [docs/handoff.md](docs/handoff.md) for the current status.

---

## Quick start — three files, three clicks

> For designers and architecture students. No `git clone`, no `uv`,
> no `dotnet`. You need: **Rhino 8** installed, **Claude Desktop**
> installed, and **Python 3.11+** on your PATH. About 5 minutes.

### Step 0 — Download these three files

Go to the [latest GitHub release](https://github.com/xunliuDesign/rhino-gh-mcp/releases/latest)
and download the three artifacts (replace `0.2.3` with whatever the
current release version is):

| File | What it is | Where it goes |
|---|---|---|
| `rhino-gh-mcp-0.2.3.mcpb` | Claude Desktop extension (Python MCP server bundled) | Claude Desktop |
| `rhinogh-mcp-grasshopper-0.2.3-rh8_0-any.yak` | Grasshopper canvas plugin (`.gha`) | Rhino Package Manager |
| `rhinogh-mcp-rhino-0.1.1-rh8_0-any.yak` | Rhino document plugin (`.rhp`) | Rhino Package Manager |

That's everything you need on disk.

### Step 1 — Install the Rhino + Grasshopper plugins

Drag each `.yak` file onto an open Rhino 8 window. Rhino's Package
Manager opens and installs the package — confirm the prompt. Do both
files, then restart Rhino.

*Alternatively*: once `yak push` is done (see
[docs/packaging-status.md](docs/packaging-status.md)), run
`_PackageManager` in Rhino and search **rhino-gh-mcp** to install
both packages from the McNeel server.

### Step 2 — Install the Python MCP server in Claude Desktop

Double-click `rhino-gh-mcp-0.2.3.mcpb`. Claude Desktop opens the
Extensions panel with the install prompt. Confirm. First launch
pip-installs ~5 Python dependencies (~30 s, one-time).

The extension surfaces four settings: control-level preset
(`parameter` / `curated` / `full`), Grasshopper bridge port
(default 9999), Rhino bridge port (default 9876), log level.

### Step 3 — Bring up both bridges inside Rhino

1. Run `_ToggleMcpService` in Rhino's command line. You should see
   *"rhino-gh-mcp: MCP service running on 127.0.0.1:9876"*.
2. Open Grasshopper (`_Grasshopper`).
3. Find the **MCP** ribbon tab → **Server** group → drop the
   **rhino-gh-mcp Server (v2)** component on a fresh canvas.
4. A **Boolean Toggle** (Run, off) and a **Value List**
   (Scenario, Coach selected) appear auto-wired to the left — flip
   the Toggle to **True** and `Status` should read
   `Server On 127.0.0.1:9999 | Scenario: Coach | Skill: (none)`.

### Step 4 — Restart Claude Desktop and try a prompt

Restart Claude Desktop so it picks up the extension. In a fresh chat:

> Call `gh_status` and `rhino_status` to confirm both bridges are
> alive, then `gh_canvas_summary` and `rhino_get_scene_info` to see
> what's there. Then add a slider called `radius` with range 0–50 and
> default 10, place a `Circle` component reading from that slider,
> recompute, and capture the viewport so I can see the result.

If both status checks succeed and a circle appears on the canvas,
you're done.

### Example prompts to get a feel for each Scenario

Switch the Value List on the v2 component to the matching Scenario,
then ask:

**Coach** (the default — best for learning Grasshopper with an AI partner):
> Bracket your work in a turn: call `gh_begin_turn` first. Build me
> a parametric tower: a circular footprint with a radius slider, a
> floor count slider, each floor a slab, and a twist angle slider.
> When you're done call `gh_end_turn` so the components you added
> light up on the canvas.

**Execute** (set ActiveSkill = `landform` on the v2 component first):
> Use the landform skill to build a single circular hill in the
> middle of the canvas. Height around 30, radius around 60.

**Tune** (no new components — just adjust what's there):
> Walk through every slider on the canvas and tell me what it
> controls, then bump each one by ~20 % and recompute so I can
> see the effect.

**Inspect** (no writes at all):
> Explain this Grasshopper definition to me end-to-end — what does
> each cluster of components do, where does the data flow, and
> what would I change first to make the output less symmetric?

**Author** (use only when you trust the AI to write code):
> Write me a Python script component that takes a list of points
> and returns the convex hull as a closed curve.

---

## Alternate quick start — let an AI install it from source

If the `.mcpb` / Yak path above doesn't fit your setup — e.g. you want
to hack on the bridge code, or you're on a build of Rhino that doesn't
have Package Manager set up — you can still let an AI assistant do the
full source install. You install one assistant app, drop the repo on
disk, and ask the AI to do the rest.

**1. Install an MCP-capable assistant.** [Claude Desktop](https://claude.ai/download)
(Mac or Windows) is the simplest. Claude Code (inside VS Code) and the
ChatGPT desktop app's MCP support also work.

**2. Make sure Rhino 8 is already installed.** This project only
supports Rhino 8 — Rhino 7 will not work.

**3. Download this repo to a stable location** (e.g. `Documents/rhino-gh-mcp`).
The easiest way without Git: go to the
[GitHub page](https://github.com/xunliuDesign/rhino-gh-mcp) → green
**Code** button → **Download ZIP** → unzip.

**4. Point the assistant at the unzipped folder.**

- *Claude Desktop:* open the folder picker / "Add project" panel and
  select the unzipped repo. The assistant can then read files from it.
- *Claude Code:* open a session inside that folder (`claude` from a
  terminal in the folder, or open the folder in VS Code with the
  Claude extension).

**5. Paste the setup prompt below.** Fill in your OS where indicated.

> Please read `README.md` and `docs/handoff.md` in this project, then
> install rhino-gh-mcp on my machine. I'm on **macOS** *(or: Windows)*.
> Do whatever you can without my involvement: install Python
> dependencies with `uv`, build the Grasshopper `.gha` and Rhino `.rhp`
> from source, copy the `.gha` into my Grasshopper Libraries folder,
> register the `.rhp` with Rhino, and add this MCP server to my Claude
> Desktop config so it appears as a tool source. When you're done, tell
> me exactly which steps I need to do manually inside Rhino (e.g. start
> the listener, drop the canvas component, restart the assistant).

The assistant will run `uv sync`, build both plugins with `dotnet
build`, copy each plugin to its OS-specific install path, register the
Rhino plugin via Rhino's settings file, and edit your MCP config. Then
it'll tell you the small handful of things only you can do — same as
steps 3 and 4 of the two-click quick start above.

If anything goes wrong, paste the assistant's error message back at it
and point it at the **Troubleshooting** and **Install** sections below.

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

**First time only:** Rhino requires explicit registration before
`_ToggleMcpService` becomes a recognized command.

- **Windows:** drag the built `.rhp` (the script prints the absolute
  path) onto Rhino's main window — Rhino will prompt to load and trust
  the plug-in. After the first registration, subsequent rebuilds
  auto-update in place.
- **Mac:** the drag-onto-window path is unreliable on Rhino 8 macOS —
  dropping inside the viewport opens the file as 3D geometry, and the
  Plug-in Manager dialog has no Install button. The reliable path is
  to (a) put the `.rhp` at
  `~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/RhinoGhMcpRhino (3f88bb55-3368-4204-9d0a-55911c9349ee)/RhinoGhMcpRhino.rhp`
  and (b) add a registry entry for it inside the
  `<child key="PlugInRegistry"><child key="6">` block of
  `~/Library/Application Support/McNeel/Rhinoceros/8.0/settings/settings-Scheme__Default.xml`
  (with `LoadMode=1`, `IsDotNETPlugIn=True`, and `FileName` pointing at
  the absolute path above). The AI install path described in the Quick
  start above does both of these for you.

### 5. Connect a client

**Claude Code** — already supported by the project-scoped
[`.mcp.json`](./.mcp.json) in the repo root. Start a Claude Code session
inside the repo directory and approve the trust prompt. The `rhino-gh`
server will be available in that session.

**Claude Desktop** — add an `mcpServers` block to your config file:

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

On Mac, `uv` should be the absolute path (`/Users/<you>/.local/bin/uv`
or similar) — the GUI app doesn't always inherit your shell's `PATH`.

Config file location depends on the Claude build you have installed:

| Build | Mac | Windows |
|---|---|---|
| Claude Desktop (classic) | `~/Library/Application Support/Claude/claude_desktop_config.json` | `%APPDATA%\Claude\claude_desktop_config.json` |
| Unified Anthropic Claude app (with Cowork / Claude Code integration) | `~/Library/Application Support/Claude-3p/claude_desktop_config.json` | `%APPDATA%\Claude-3p\claude_desktop_config.json` |

If you have both directories present, use the `Claude-3p` one — the
preferences file inside plain `Claude/` is rewritten by the app and
will strip unknown keys. Restart the assistant after editing.

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
├── mcpb/                            # .mcpb / .dxt Desktop Extension source
│   ├── manifest.json                # MCPB v0.3 manifest
│   ├── bootstrap.py                 # First-launch dep installer
│   └── requirements.txt             # Server runtime deps
├── scripts/
│   ├── build-mcpb.sh                # → dist/rhino-gh-mcp-<v>.mcpb
│   ├── build-yak.sh                 # → dist/rhinogh-mcp-*-<v>-*.yak
│   └── sync.{sh,ps1}                # One-shot dev sync after pulling
├── dist/                            # Built install packages (gitignored binaries)
├── docs/
│   ├── architecture.md
│   ├── handoff.md                   # Status + session log
│   ├── packaging-status.md          # .mcpb + Yak release status / next steps
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
| **P7** | ✅ | `.mcpb` Desktop Extension + Yak packages — see `scripts/build-mcpb.sh`, `scripts/build-yak.sh`, [docs/packaging-status.md](docs/packaging-status.md) |
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

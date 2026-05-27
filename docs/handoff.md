# Project handoff & status snapshot

> Last updated: 2026-05-20, after commit `7270aef`.
>
> This document is the canonical "what is this and where are we" reference.
> A fresh Claude Code session on any machine should be able to read this top
> to bottom and pick up the project without context.

## 0. TL;DR

`rhino-gh-mcp` is an MCP server + Rhino/Grasshopper plugins that give an LLM
tiered control of a Rhino 8 modeling session. The Python server is in
[`/server`](../server), the C# plugins in [`/plugins`](../plugins), and the
LLM-side workflow recipes in [`/skills`](../skills). v0.1.0+ skeleton is
complete and end-to-end testable through smoke scripts in
[`/examples`](../examples); the Claude Desktop integration is the last
unverified link.

## 1. Goals (the why)

Three concrete outcomes drive every decision in this repo:

1. **Open, sophisticated, easy-to-use MCP for Rhino/Grasshopper.** An LLM
   should be able to read the GH canvas, change sliders, place components,
   wire them, write script components, and drive Rhino directly — without
   the user having to write any glue per session.
2. **Research infrastructure.** Future projects need to live on top of this:
   a substrate for agent-driven architectural analyses that combine
   parametric design with domain knowledge.
3. **Teaching platform.** A "virtual TA" that students can use to learn
   Rhino/Grasshopper interactively. The capability gate is designed
   around this — students start with `AllowParameters` only, graduate to
   `+ AllowComponents`, then `+ AllowScripting` as they progress.

## 2. The capability model (v0.1.5+)

**All tools are now always advertised** to the LLM. Whether a call is
actually allowed is decided at runtime by two orthogonal axes set on the
canvas-side `rhino-gh-mcp Server` component:

| Axis | Inputs | Effect |
|---|---|---|
| **Capabilities** | `AllowParameters` / `AllowComponents` / `AllowScripting` (Booleans) | What category of action the LLM may take |
| **Scope** | `ComponentScope` (0=curated / 1=defaults / 2=all) + `CategoryFilter` (text, used when scope=0) | When components may be placed, which ones |

The Python server caches the canvas state for ~3 s and re-queries via the
`get_capabilities` bridge command. Flipping a toggle on the canvas
propagates to the LLM within one cache TTL — no server restart needed.

Calls that violate a capability return a clean, LLM-readable refusal
naming the input to flip:

> Capability denied by canvas: set the `AllowScripting` input on the
> rhino-gh-mcp Server component to True.

The CLI `--policy {parameter|curated|full}` flag still exists but now
sets the **default** capability state at startup. The canvas overrides
whenever it's reachable. The three presets are kept as a convenience:

| Preset | allow_params | allow_components | allow_scripting | component_scope |
|---|---|---|---|---|
| `parameter` | ✅ | ❌ | ❌ | curated |
| `curated` (default) | ✅ | ✅ | ❌ | curated |
| `full` | ✅ | ✅ | ✅ | all |

Total registered tools: **39** (under any preset — registration is unconditional).

Implementation: [`server/src/rhino_gh_mcp/capabilities.py`](../server/src/rhino_gh_mcp/capabilities.py) (the dataclass + provider) and [`server/src/rhino_gh_mcp/tools/_gate.py`](../server/src/rhino_gh_mcp/tools/_gate.py) (the `@gated` decorator).
Tests verify both the runtime gate and the preset mapping —
[`server/tests/test_smoke.py`](../server/tests/test_smoke.py).

## 3. Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  MCP client: Claude Desktop / Claude Code / Cursor / web UI  │
│  Loads Skills (workflows: landform, ladybug, façade, ...)    │
└────────────────────────────┬─────────────────────────────────┘
                             │ MCP (stdio or streamable-http)
┌────────────────────────────▼─────────────────────────────────┐
│  rhino-gh-mcp Python server (FastMCP, mcp>=1.20)             │
│  • Policy filter (L1/L2/L3) at tool-registration time        │
│  • Two bridges, lazy-connected                               │
└────────────┬───────────────────────────────┬─────────────────┘
             │ HTTP /                        │ TCP one-shot
             │ :9999                         │ :9876
┌────────────▼──────────────┐     ┌──────────▼──────────────────┐
│ RhinoGhMcp.gha            │     │ RhinoGhMcpRhino.rhp         │
│ (Grasshopper plugin)      │     │ (Rhino plugin)              │
│ MCP Server component on   │     │ MCPService TCP listener     │
│ the canvas; UI-thread     │     │ on _ToggleMcpService;       │
│ marshalled command        │     │ UI-thread marshalled        │
│ dispatch                  │     │ command dispatch            │
└───────────────┬───────────┘     └───────────────┬─────────────┘
                │                                 │
                └──── shared GH ↔ Rhino doc ──────┘
```

### Why two plugins not one

- Grasshopper and Rhino have different threading models, different UI
  lifetimes, and different SDK affordances. Keeping them split lets each
  side use the threading model it's already good at.
- The LLM can drive Rhino-only workflows (mesh cleanup, baking, named
  commands) without needing Grasshopper open.

### Why HTTP for GH and TCP for Rhino

- GH's `.gha` component lifecycle is stable enough for a long-lived HTTP
  listener (the component itself manages start/stop).
- Rhino's listener has historically been flakier on macOS (the v0 archive
  had an HTTP service that deadlocked under load). One-shot TCP — read
  until EOF, dispatch on UI thread, write reply, close — is more robust.

### Why a policy filter, not runtime checks

If a tool isn't registered, the LLM never sees it in `tools/list`. Stronger
than runtime guards — the model can't even *try* to call something out of
tier.

## 4. Current state — what works, what doesn't

### What works end-to-end

- ✅ Python MCP server boots, registers correct tool count per policy
- ✅ Smoke tests: `uv run pytest` — 9/9 pass (server-side, no Rhino needed)
- ✅ Grasshopper `.gha` v0.1.5 — builds on Mac (`net7.0` and `net7.0-windows`
  targets), installs via `plugins/grasshopper/reinstall.sh`, hosts HTTP on
  9999, 21 commands wired. v0.1.5 is the capability-gate refactor: 4 new
  canvas inputs (AllowParameters, AllowComponents, AllowScripting,
  ComponentScope) replace the old SetParameterMode input; a new
  `get_capabilities` bridge command lets the Python server poll the live
  canvas state; mutating handlers refuse with a clean knob-name message
  when their capability is off.
- ✅ Rhino `.rhp` v0.1.1 — 9 commands. v0.1.1 added `list_blocks`
  (enumerate InstanceDefinitions) and `set_view` (apply named view or
  switch to standard projection Top/Front/Right/Left/Back/Bottom/Perspective).
- ✅ Bridge smoke tests in [`/examples`](../examples):
  - `smoke_test_bridge.py` — hits every GH command
  - `smoke_test_rhino.py` — hits every Rhino command
- ✅ Both `.gha` and `.rhp` self-report version via `is_server_available`
  so you can sanity-check what's actually loaded.
- ✅ Repo public at https://github.com/xunliuDesign/rhino-gh-mcp

### What's done but not yet user-verified

- ⏳ `.rhp` installation on the user's machine (built but never run inside
  Rhino yet — the user is migrating to Windows before testing this half)
- ⏳ Claude Desktop wire-up end-to-end (`docs/getting-started.md` has the
  config; never executed)

### Not implemented yet (planned)

| Phase | Scope |
|---|---|
| **P3** | Rhino 8 Script-component injection wired through the .gha (`gh_write_script_py3` / `_cs` currently send a `script_language` flag the .gha ignores; needs Script component creation + property setting) |
| **P4** | Tool surface expansion: ✅ list_toggles, ✅ list_value_lists, ✅ canvas_summary, ✅ find_components, ✅ set_toggle, ✅ select_value_list, ✅ list_blocks, ✅ set_view; remaining: bake_to_rhino, group_components, measure_distance |
| **P5** | Skills library: 5–6 workflows (massing, façade, structural grid, daylighting), each with `.ghuser` files where relevant |
| **P6** | Streamable HTTP transport + a thin web frontend |
| **P7** | Domain-specific RAG → 3D model → env metrics agent loop (planned application built on this substrate; not part of this repo) |
| **P8** | Student-TA Skill bundle in `parameter` mode |

## 5. Tech stack snapshot

| Concern | Choice | Rationale |
|---|---|---|
| Server language | Python 3.11+ | FastMCP / mcp SDK is best-supported in Python |
| MCP SDK | `mcp[cli] >= 1.20` | Current as of late 2025 |
| Transport | stdio (default) + streamable-http (P6) | stdio for Claude Desktop; HTTP for web UI |
| Package manager | `uv` | User preference; fast and reproducible |
| Bridges | `httpx` (sync) for GH, `socket` for Rhino | Sync is fine — tool calls are inherently req/resp |
| C# target | `net7.0` (+ `net7.0-windows` for .gha only) | Rhino 8 ships net7.0 runtime |
| Rhino version | 8.x only | Rhino 7 / IronPython 2 dropped on purpose |
| Plugin packaging | dotnet build → `.gha` / `.rhp`; future: `.yak` + `.mcpb` | One-click install in P-late |
| License | MIT | Standard, friendly for student adoption |

## 6. Directory layout

```
rhino-gh-mcp/
├── server/                          # Python MCP server (uv project)
│   ├── src/rhino_gh_mcp/
│   │   ├── server.py                # FastMCP app + lifespan
│   │   ├── config.py                # Policy + transport
│   │   ├── __main__.py              # CLI entrypoint
│   │   ├── bridges/                 # HTTP→GH, TCP→Rhino
│   │   ├── policies/                # L1/L2/L3 tool allow-lists
│   │   └── tools/                   # Tool implementations
│   ├── tests/test_smoke.py          # Policy filter + registration tests
│   └── pyproject.toml
├── plugins/
│   ├── grasshopper/                 # .gha (C# / net7.0[+-windows])
│   │   ├── RhinoGhMcp.csproj
│   │   ├── src/McpServerComponent.cs
│   │   ├── src/RhinoGhMcpInfo.cs
│   │   ├── reinstall.sh             # Mac install helper
│   │   └── reinstall.ps1            # Windows install helper
│   └── rhino/                       # .rhp (C# / net7.0)
│       ├── RhinoGhMcpRhino.csproj
│       ├── src/{Plugin,McpService,ToggleMcpServiceCommand,AssemblyInfo}.cs
│       ├── reinstall.sh             # Mac install helper
│       └── reinstall.ps1            # Windows install helper
├── examples/
│   ├── smoke_test_bridge.py         # GH bridge tests
│   └── smoke_test_rhino.py          # Rhino bridge tests
├── skills/
│   └── landform-from-contours/      # First Skill (SKILL.md + userobjects/)
├── mcpb/manifest.json               # Desktop Extension manifest (placeholder)
├── docs/
│   ├── architecture.md
│   ├── getting-started.md
│   └── handoff.md                   # THIS DOCUMENT
└── _archive project/                # v0 reference (gitignored)
```

## 7. Operating the project — commands cheat sheet

### Day-to-day (any OS)

```bash
# Server tests + smoke
cd server
uv sync
uv run pytest
uv run rhino-gh-mcp --help
uv run rhino-gh-mcp --policy full     # L3 surface

# Bridge smoke tests (need running plugins)
uv run python ../examples/smoke_test_bridge.py
uv run python ../examples/smoke_test_rhino.py --policy full
```

### Mac

```bash
cd plugins/grasshopper && ./reinstall.sh   # builds + installs .gha
cd plugins/rhino       && ./reinstall.sh   # builds + first-time install instructions
```

In Rhino: `_ToggleMcpService` to start the listener.
In Grasshopper: drag the **rhino-gh-mcp Server (v1)** component onto a canvas, set `Run = True`.

### Windows

See [§8 below](#8-migrating-to-windows).

## 8. Migrating to Windows

### One-time setup

Install on the Windows machine:

1. **Git for Windows** — https://git-scm.com/download/win
2. **.NET 7+ SDK** — `winget install Microsoft.DotNet.SDK.8` (8 works fine, builds net7.0 targets)
3. **Python 3.11 or 3.12** — `winget install Python.Python.3.12`
4. **`uv`** — `winget install --id=astral-sh.uv`
5. **Rhino 8** — already installed for the user
6. **Claude Code or Claude Desktop** — whichever client you'll drive the MCP from

### Clone & build

```powershell
cd $HOME\Desktop          # or wherever
git clone https://github.com/xunliuDesign/rhino-gh-mcp.git
cd rhino-gh-mcp\server
uv sync
uv run pytest             # 9/9 pass — sanity check
```

### Install plugins

```powershell
cd ..\plugins\grasshopper
.\reinstall.ps1           # builds + copies .gha to %APPDATA%\Grasshopper\Libraries\

cd ..\rhino
.\reinstall.ps1           # builds; first-time: drag-drop the .rhp onto Rhino
```

In Rhino: `_ToggleMcpService`. In Grasshopper: drop **rhino-gh-mcp Server (v1)** on a canvas, `Run = True`.

### Claude Desktop config on Windows

Edit `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "rhino-gh": {
      "command": "uv",
      "args": [
        "--directory",
        "C:\\Users\\<you>\\Desktop\\rhino-gh-mcp\\server",
        "run",
        "rhino-gh-mcp",
        "--policy",
        "curated"
      ]
    }
  }
}
```

Replace `<you>` with your username. Restart Claude Desktop.

### Resuming work with Claude Code on Windows

1. Install Claude Code: https://www.anthropic.com/claude-code
2. Clone the repo, `cd` into it
3. Open this very file (`docs/handoff.md`) and paste its first half into the
   Claude Code session to ground it — or just ask Claude Code "read docs/handoff.md".
4. The most useful follow-up prompts to start with:
   - *"Run the smoke tests and tell me what's not working yet."*
   - *"P3 next — wire script_language switching for `gh_write_script_py3` and `_cs` through the .gha so the Rhino 8 Script component is targetable."*
   - *"Build the first useful Skill: package the landform user-objects from \_archive project/ into skills/landform-from-contours/userobjects/."*

## 9. Key conventions established

- **Tool naming**: `<bridge>_<verb>_<noun>`, e.g. `gh_add_component`,
  `rhino_capture_viewport`. The bridge prefix matters because the policy
  allow-lists key off the tool name.
- **All Rhino doc / GH canvas mutations** marshal to the UI thread inside
  the C# plugins. The bridge waits up to 5–15s for the UI dispatcher to
  complete and returns a timeout error otherwise. Never block the listener
  thread on a doc operation.
- **Reply shape**: `{"status": "success"|"error", "result": <any>, "message": "..."}`.
  The Python tool layer normalizes any "error" reply to a plain string the
  LLM can read.
- **Component GUIDs**: v0 used `f1795527-...`. v1 uses `005a98bf-...`. They
  are intentionally distinct so the two `.gha`s coexist if both are
  installed (helpful during migration). The v1 component is nicknamed
  `MCPv1` in the canvas.
- **Plugin versions**: bump on every backward-compatible change; the version
  is reported by `is_server_available` so you can verify with a single
  network call which build is live.
- **Component icon and identity changes mean ribbon-cache refresh.** Mac
  Grasshopper sometimes caches ribbon thumbnails — drag the component once
  to force a rebuild, or fully `Cmd+Q` Rhino and reopen. The `reinstall.sh`
  scripts handle the full-quit step.

## 10. Open decisions / things to revisit

- **Plugin distribution.** Currently the user builds locally. Once stable,
  publish via Yak (`yak push`) so install becomes `_PackageManager → search`.
- **Project name.** `rhino-gh-mcp` is descriptive but generic. We discussed
  alternatives (`archgrasp`, `gh-conduit`, `parametrik`) but haven't picked
  one. Defer until before public-launch announcement.
- **`.mcpb` packaging.** The manifest in `mcpb/manifest.json` is a stub —
  needs the actual `mcpb pack` workflow once the user installs the `mcpb`
  CLI. One-click install in Claude Desktop is the user-facing payoff.
- **Streamable HTTP transport for web UI.** The CLI already accepts
  `--transport http --port 8765`; FastMCP exposes the `streamable-http`
  transport, but no client has tested it. Validate when starting P6.
- **Skill canonical format.** `skills/landform-from-contours/SKILL.md` is
  the first one. Pattern for the rest:
  - `name` + `description` frontmatter
  - `policy` field (parameter / curated / full)
  - `curated_group` field (component category allow-list)
  - Step-by-step strategy in the body
  - Bundled assets (`userobjects/`, `examples/`, etc.) referenced relatively

## 11. Quick links

- **Repo**: https://github.com/xunliuDesign/rhino-gh-mcp
- **Architecture diagram**: [`architecture.md`](./architecture.md)
- **Getting started**: [`getting-started.md`](./getting-started.md)
- **Policy registry (the L1/L2/L3 source of truth)**:
  [`server/src/rhino_gh_mcp/policies/base.py`](../server/src/rhino_gh_mcp/policies/base.py)
- **All bridge command handlers**: GH side in
  [`plugins/grasshopper/src/McpServerComponent.cs`](../plugins/grasshopper/src/McpServerComponent.cs),
  Rhino side in
  [`plugins/rhino/src/McpService.cs`](../plugins/rhino/src/McpService.cs)

---

## Session log — 2026-05-20 (Mac)

What we did, in order, so anyone reading later understands the historical sequence:

1. Designed v1 from scratch with the user (planning conversation, no code yet)
2. Set up the Python server skeleton, policies, bridges, tool modules, smoke tests
3. Initialized git, pushed to `xunliuDesign/rhino-gh-mcp`
4. Ported the v0 `.gha` (`_archive project/rhino_gh_mcp-main/rhino_gh_mcp/`) into a fresh
   `plugins/grasshopper/` project — renamed namespace, dropped Rhino 7 / net48 target,
   added Port input + Version output, new ComponentGuid so v1 is visibly distinct
5. Fixed install path glob for Mac (Libraries folder has a profile-UUID suffix)
6. Added bridge handlers that the Python tools referenced but v0 didn't implement:
   `bypass_filter`, `set_slider_range`, `get_runtime_messages`, `capture_canvas`
7. Wrote `examples/smoke_test_bridge.py` — exercises every GH bridge command without Claude
8. Found and fixed the "version still shows v0.1.0 after restart" symptom — root cause
   was Rhino on macOS not quitting on Cmd+W. Added `reinstall.sh` that detects + force-quits
9. Made `is_server_available` self-report plugin version + assembly location so the
   running plugin can be inspected via the bridge
10. Carved a clean `.rhp` out of the v0 `rhino_script_client/` archive — dropped the
    StreamDiffusion experiment, implemented all 7 handlers, single net7.0 target
11. Wrote `examples/smoke_test_rhino.py` — Rhino-side bridge smoke
12. Wrote this handoff doc + Windows PowerShell mirrors of the reinstall scripts

End of Mac session. Next session: Windows.

## Session log — 2026-05-20 (Windows)

First Windows session after landing on the new machine. Server-side
additions only; the user hasn't yet run Rhino on this machine.

1. Cloned, `uv sync`, `uv run pytest` — 9/9 green
2. Noticed `gh_list_sliders` / `gh_list_panels` expected fields
   (`value`/`min`/`max`) that the .gha `GetParamInfo` never emitted — latent
   stubs. Wrote new tools assuming the gap would be fixed at the .gha
   side too.
3. Added 4 new L1 read tools in `tools/gh_read.py`:
   `gh_list_toggles`, `gh_list_value_lists`, `gh_canvas_summary`,
   `gh_find_components` — all composables over `get_context(simplified=True)`,
   no new bridge commands required.
4. Updated `policies/base.py` to include the new names in PARAMETER_TOOLS
   (inherited by CURATED / FULL).
5. Wrote `tests/test_gh_read_composables.py` — 9 unit tests with a stub
   bridge, covering filter logic, summary aggregation, search bounds, and
   bridge-error pass-through. All 18 tests pass.
6. Fixed a small papercut: `pyproject.toml` had dev deps under
   `[project.optional-dependencies]`, so `uv sync` didn't install pytest.
   Moved to PEP-735 `[dependency-groups]` — now `uv sync` is enough.
7. Extended `GetParamInfo` on the `.gha` side to emit widget value/state
   (slider value/min/max/decimalPlaces, toggle value, value-list
   items/selectedItems/listMode, panel userText). Bumped plugin version
   0.1.2 → 0.1.3. Built cleanly for both `net7.0` and `net7.0-windows`
   targets. **Untested in Rhino yet** — user verifies on next launch.
8. Updated this doc + tool counts (L1: 17→21, L2: 23→27, L3: 31→35).

Second Windows pass — landed `.gha` v0.1.4 + `.rhp` v0.1.1, verified live:

9. Added two direct-write handlers on the GH side: `set_toggle_value`
   (top-level Boolean Toggle's `.Value`) and `set_value_list_selection`
   (matches `item` against ListItems by index OR Name). The generic
   `set_component_parameter` couldn't reach these because it only knows
   how to attach sources to component inputs, not to flip a widget that
   IS the canvas object.
10. Added two read/view handlers on the Rhino side: `list_blocks`
    (enumerate `InstanceDefinitions` with name/id/object_count/update_type)
    and `set_view` (apply a named view by name, or set the active
    viewport to one of seven canned projections: Top/Front/Right/Left/
    Back/Bottom/Perspective).
11. Bumped plugin versions: `.gha` 0.1.3 → 0.1.4, `.rhp` 0.1.0 → 0.1.1.
12. Wrote four new Python tools (`gh_set_toggle`, `gh_select_value_list`,
    `rhino_list_blocks`, `rhino_set_view`), all L1. Updated
    `policies/base.py` and `tests/test_smoke.py` count minimums.
13. Found and fixed the Windows-only reinstall.ps1 bug: it was looking
    for the Rhino .rhp install in `%APPDATA%\McNeel\Rhinoceros\8.0\Plug-ins`
    (the macOS path). On Windows, Rhino registers drag-installed plugins
    in the registry (`HKCU\Software\McNeel\Rhinoceros\8.0\Plug-Ins\<guid>\
    PlugIn\FileName`) and loads the .rhp from wherever it was when first
    registered. Updated the script to consult the registry first and
    skip the file copy when the build output IS the registered path.
14. Extended `examples/verify_widget_values.py` to cover the new write
    paths (set_toggle, select_value_list by index + by name) and the
    Rhino-side reads (list_blocks, set_view standards + error rejection).
    All 16/16 checks pass against a live Rhino on this machine.

Third Windows pass — soft-gate refactor (2026-05-21):

15. Refactored the policy system end to end. The old hard-tier approach
    (server-side, "don't register tools above tier") had two problems
    the user surfaced explicitly: (a) the active tier was buried in a
    CLI flag instead of visible on the canvas where it belongs, and
    (b) changing tier required a server restart, breaking ongoing
    conversations. Replaced with a soft-gate capability model.

16. Two orthogonal axes: Capabilities (AllowParameters /
    AllowComponents / AllowScripting) say WHAT the LLM may do.
    ComponentScope + CategoryFilter say WHICH components are
    placeable when AllowComponents is on (0=curated, 1=GH defaults,
    2=all). The user's proposal — and it shipped as proposed.

17. `.gha` v0.1.5 input redesign: dropped SetParameterMode (niche
    v0 cruft per user agreement), added 4 new inputs above. New
    `get_capabilities` bridge command returns the live state. Dispatch
    routes through a capability gate that refuses mutating commands
    with a knob-name-in-message refusal.

18. Python soft-gate plumbing: new `capabilities.py` (dataclass +
    provider with 3 s TTL cache), new `tools/_gate.py` (@gated
    decorator). Every tool now registers unconditionally; the gate
    runs at call time. Old `policies/base.py` kept as a back-compat
    shim. CLI `--policy` becomes a preset that seeds the default
    capability state (the canvas overrides when reachable).

19. Verified end-to-end on live Rhino: with AllowScripting=False on
    the canvas, `rhino_execute_code` is cleanly denied; flip the
    toggle to True on the canvas (no restart), the same call ~3 s
    later creates a real Rhino sphere. The whole point of the
    redesign — dynamic, canvas-driven, no restart — works.

20. Tests: rewrote `test_smoke.py` for the new model. All 27 tests
    pass. Plugin v0.1.4 → v0.1.5.

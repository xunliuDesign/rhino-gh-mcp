# Changelog

All notable changes to **rhino-gh-mcp** (MCP server + Rhino/Grasshopper plugins).

Versioning follows the Grasshopper `.gha` plugin. The Rhino `.rhp`
plugin and Python server have their own internal versions and only
bump when their surface changes. See [docs/architecture.md](docs/architecture.md)
for the split.

Format inspired by [Keep a Changelog](https://keepachangelog.com).

---

## [0.2.5] — 2026-06-16

### Fixed

- **`gh_add_component`** now prefers non-obsolete proxies. Grasshopper
  keeps deprecated component versions around as `Obsolete=true` for
  backwards compatibility — and `FirstOrDefault` was picking whichever
  proxy came first in the enumeration, which depended on assembly load
  order and frequently selected the "Old" version. Filter on `Obsolete`
  first, fall back only when every match is obsolete.

### Changed

- **`gh_list_available_components`** now reports each proxy's
  `Obsolete` flag so the LLM can see + avoid deprecated versions when
  composing prompts.

### Note on Grasshopper 1 vs 2

This server runs against **Grasshopper 1** — the canvas everyone has
shipped with Rhino 6 / 7 / 8. Grasshopper 2 (Rhino 9 WIP, technology
preview in Rhino 8) is a separate API and is **not currently
supported**. McNeel's official RhinoMCP ships parallel GH1 + GH2 tool
sets; adding GH2 here is a future v0.3+ effort.

---

## [0.2.4] — 2026-06-15

### Added

- **`gh_canvas_outline()`** — ultra-compact digest of the live canvas.
  Connected-components clustering on the wire graph; each cluster
  reports component-type histogram, bounding box, endpoints, and
  input widgets. Target: ~1 KB even for 60+ component canvases.
  Replaces the `gh_get_context simplified → 111 KB → subagent dispatch`
  chain that previously cost ~7 minutes on a mid-sized definition.
- **`gh_file_outline(file_path)`** — same shape but reads a `.gh` / `.ghx`
  off disk into a *temporary* in-memory `GH_Document`. Never touches
  the user's canvas; disposes the temp doc after parsing. Works for
  both modern zip-format `.gh` and raw-XML `.ghx`.
- **`gh_cluster_flow(cluster_id)`** — drill-down on one cluster from the
  most recent outline. Returns stage-based topology (topological depth
  within the cluster, type histogram per stage, widget nicknames).
  ~250–500 chars per cluster.
- Bridge-side cluster cache mapping `cluster_id → List<Guid>`, populated
  by either outline call, consumed by `cluster_flow` with zero re-walk.
- All three new tools registered under `_READ_TOOLS` — safe in Inspect
  / Tune scenarios.

### Fixed

- **`gh_add_slider`** now records the new slider's GUID into the active
  Coach turn, and returns `instance_guid` in the response. Without this,
  `gh_end_turn` reported `changed_count: 0` and the new slider was never
  visually highlighted.
- **`gh_merge_definition`** / `gh_load_skill_reference` now return the
  correct `loaded_guids` list. Root cause: `GH_Document.MergeDocument`
  transfers objects out of the source document, so the post-merge
  enumeration was empty. Snapshot `(guid, originalPivot)` tuples before
  merging, then look up via `doc.FindObject` after merging.

### Performance

- Type-name abbreviation: `GH_NumberSlider` → `NumSlider`,
  `Component_X` → `X`, `Param_X` → `X`, trailing `"Component"` stripped.
  Roughly halves the size of type-name overhead in outline responses.

---

## [0.2.3] — 2026-06-15

### Added — eight productivity tools

| Capability bucket | Tool | Purpose |
|---|---|---|
| read | `gh_get_component_output` | Read a component's output volatile-data tree (branches + string values). |
| param-write | `gh_set_panel_content` | Replace text on an existing GH Panel. |
| param-write | `gh_move_component` | Reposition a single canvas object. |
| param-write | `gh_organize_components` | Auto-layout left→right by data-flow depth. Fixes "everything piled on top of each other". |
| component-write | `gh_bake_to_rhino` | Bake a component's outputs into the active Rhino doc; optional layer. |
| component-write | `gh_reference_rhino_object` | Drop a Curve / Brep / Mesh / Point / Surface / Geometry param on the canvas referencing a Rhino object (the "right-click → Set one X" workflow). |
| component-write | `gh_add_panel` | Add a GH Panel with arbitrary text — inline notes / labels. |
| component-write | `gh_group_components` | Wrap components in a Group (visual cluster + optional label + color). |

### Changed

- `landform` Skill's `allow_tools` widened to include `gh_bake_to_rhino`,
  `gh_add_panel`, `gh_group_components`, `gh_organize_components`,
  `gh_move_component`, `gh_reference_rhino_object` — Execute mode now
  has the verbs needed for a complete build+annotate+tidy workflow.

---

## [0.2.2] — 2026-06-15

### Added

- **`gh_merge_definition(file_path, pivot_x?, pivot_y?)`** — merge any
  `.gh` / `.ghx` file from disk into the current canvas without
  replacing it. Unlike `gh_load_skill_reference`, the file path is
  arbitrary (not Skill-scoped). Use it to inspect / edit a downloaded
  file alongside the MCP Server.

---

## [0.2.1] — 2026-06-15

### Added

- **`McpServerComponentV2.AddedToDocument`** — on a *fresh* ribbon drop
  (no inputs have sources yet), auto-place and auto-wire:
  - `GH_BooleanToggle` "Run" → input 0, default False
  - `GH_ValueList` "Scenario" → input 1, five labeled items
    (Inspect / Tune / Coach / Execute / Author), default Coach.
  The "no existing sources" guard prevents re-creation on file load,
  copy-paste of fully connected groups, and undo/redo.

### Fixed

- Version-string display trims trailing `.0` parts. .NET assembly
  versions are always 4-part (e.g. `0.2.1.0`); now displayed as the
  semver-shaped `0.2.1`, preserving at least `major.minor`.

---

## [0.2.0] — 2026-06-15

The big redesign: **Scenarios over capabilities**, **Skills schema v2**,
**Coach-mode visual highlighting**, **Execute-mode skill gating**.

### Added — Scenario surface

- **`McpServerComponentV2`** — new canvas component (fresh GUID, coexists
  with v0.1 on the ribbon for backward compatibility).
- One **Scenario** dropdown replaces v0.1's four-flag panel:
  - `Inspect` — read only
  - `Tune` — sliders / toggles / value-lists only
  - `Coach` (default) — Tune + curated component placement, with
    **canvas-side visual highlights** on every AI change
  - `Execute` — only the tools declared by the active Skill
  - `Author` — full freedom incl. scripting
- Advanced overrides retained for power users (`OverrideAllowParameters`,
  `OverrideAllowComponents`, `OverrideAllowScripting`,
  `OverrideComponentScope`).
- `ActiveSkill` input on the v2 component reports to the server so
  Execute-mode gating knows which Skill is in force.

### Added — Skills schema v2

- New `Skill` dataclass in `server/src/rhino_gh_mcp/skills.py` with typed
  frontmatter: `modes`, `required_plugins`, `required_capabilities`,
  `allowed_categories`, `required_components`, `reference_examples`,
  `images`, `prompts`, `commands`, `allow_tools`.
- New tools: `gh_list_skills` (typed manifest), `gh_load_skill_reference`,
  `gh_introspect_definition`.
- Existing Skills (`landform`, `ladybug-environmental`) migrated to v2
  frontmatter while preserving body markdown.

### Added — Coach mode

- Turn tracking: `gh_begin_turn` / `gh_end_turn` / `gh_dismiss_highlights`.
- Bridge hooks `GH_Canvas.CanvasPostPaintObjects` lazily on first
  `EndTurn`, draws a 3 px teal ring (`Color.FromArgb(255, 0, 200, 200)`)
  around every component touched in the turn. Distinct from selection
  (white), warning (yellow), error (red).
- `skills/_shared/coach-system-prompt.md` — Coach-mode prompt fragment
  injected into the MCP `instructions` (self-gated on `scenario=coach`).

### Added — Execute mode

- Decorator-side gating: when `scenario=execute` AND `active_skill` is
  set, the `@gated` decorator restricts tool calls to the union of:
  - the Skill's `allow_tools` allowlist (real MCP tool names — the
    workhorse), and
  - synthetic `gh_skill_command_<verb>` placeholders for each entry in
    `commands:` (future dynamic registration target).
- Denial message: `"Skill {name} doesn't support {tool} — change
  Scenario to Coach or Author for full freedom."`

### Added — Tests

- 47 new pytest tests covering scenarios mapping, turn tracking, and
  Skills v2 schema parsing. Total: 84 / 84 passing.

### Documentation

- `docs/v0.2-redesign.md` — full design document.
- `docs/v0.2-testing-checklist.md` — step-by-step morning-test plan.
- README rewritten: Quick Start leads with the v0.2 Scenario table and
  five tailored example prompts (one per Scenario).

---

## [0.1.8] — 2026-06-15

### Added — Packaging for two-click install

- **`dist/rhino-gh-mcp-0.1.8.mcpb`** — Claude Desktop extension (MCPB
  v0.3) bundling the Python server + Skills + a first-launch
  pip-installer bootstrap.
- **`dist/rhinogh-mcp-grasshopper-0.1.8-rh8_0-any.yak`** + **`dist/rhinogh-mcp-rhino-0.1.1-rh8_0-any.yak`** —
  Rhino Package Manager packages for the two C# plugins.
- `mcpb/manifest.json` + `mcpb/bootstrap.py` + `mcpb/requirements.txt`
  — `.mcpb` package sources.
- `plugins/*/manifest.yml` — Yak metadata committed at source.
- `scripts/build-mcpb.sh` + `scripts/build-yak.sh` — reproducible builds.
- `docs/packaging-status.md` — release runbook (yak push, GitHub
  Release attachment, Mac + Windows test checklist).

### Changed

- README rewritten to lead with the two-click install path; "build
  from source" demoted to alternate quick-start.

---

## [0.1.7] — 2026-06-15

### Added — v0.1.7 diagnostic handlers

- `inspect_type` + `read_script_source` bridge commands behind
  `AllowScripting` for introspecting Rhino 8 script-component runtime
  types.

### Fixed — bridge bug-fix bundle (RA + co-pilot pass)

- **`gh_connect_components`** accepts floating `IGH_Param`s as **source
  AND target** — Panels, Curves, Surfaces, Breps now wire correctly.
- **`gh_connect_components`** gains `append: bool = False` for
  multi-source merges; duplicate `(source, target)` pairs are guarded
  against. Response shape upgraded to `{message, append, source_count}`
  — **breaking change** for callers parsing the literal `"Connected."`.
- **`gh_set_slider`** moves the slider thumb directly (invariant-culture
  parse, clamp to range) instead of spawning an orphan panel wired to
  the slider's targets.
- **`gh_set_component_parameter`** writes `PersistentData` directly for
  typed inputs (Integer / Number / Boolean / String / Interval) instead
  of inserting a panel.
- **`gh_add_component`** timeout raised from 5 s → 15 s; shared
  `canceled` flag + post-`doc.AddObject` recheck remove phantom
  components when proxy enumeration was slow.
- **New tool `gh_set_expression_formula`** writes the formula property
  on Expression / Variable Expression / Evaluate components via
  reflection (the formula is a class property, not a wired input).
- **`GetContext`** / **`GetSelected`** / **`GetObjects`** use an
  `IGH_DocumentObject` fallback so Galapagos solver, Gene Pool, and
  other `GH_ActiveObject`-only classes are visible to the bridge.

---

## Earlier history (v0.1.6 and prior)

See `git log d838c5c..HEAD --oneline`. Highlights: soft capability gate
landing on the canvas (v0.1.5), Skills library scaffolding (v0.1.6),
Rhino `.rhp` carve-out (v0.1.0+), `.gha` ported to net7.0 for Mac+Win
(P0).

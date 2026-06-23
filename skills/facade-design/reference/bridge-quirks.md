# rhino-gh-mcp Bridge Quirks

Cross-cutting bridge-version-specific behaviors that have bitten LLM builds.
These are NOT about Grasshopper itself — they're about the
`rhino-gh-mcp` bridge's name resolution, persistent-data handling, and
gating. Update when the bridge version bumps; current notes apply to
**plugin v0.2.4** unless marked otherwise.

## Component name traps

Bridge name resolution is fuzzy and sometimes picks the wrong variant.
**Always check the returned `kind` after placement.** If it's not what
you expected, remove and try an alternate name.

| What you ask for | What you might get | Why | Fix |
|---|---|---|---|
| `Extrude` | `Extrude Linear` (Component_ExtrudeLinear) — takes Line `Axis`, not Vector `Direction` | Bridge prefers newer variant by nickname match | Use `Extr` (legacy nickname) — returns `Component_Extrude` with Vector input |
| `Triangle Panels A` | `not found in ANY scope` | LunchBox only has the `B` variant in this build | Use `Triangle Panels B` (note: no `LB ` prefix needed) |
| `LB Triangle Panels A` / `LB Surface Panels` | `not found in ANY scope` | LunchBox component names don't take the `LB ` prefix | Try without prefix, or use `Triangle Panels B` / `Quad Panels A` / `Hexagon Cells` / `Diamond` / `Staggered Quads` |
| `Divide Domain²` (unicode ²) | Client times out; component IS placed but client-side retry creates duplicates | Bridge parses but hangs the JSON pipe on the ² | Send ONE call, accept the timeout, verify via `gh_find_components("Divide Domain")`. Or use `Divide Surface` for point grid instead of domain subdivision |
| `Scale` | `Component_Scale` (uniform) — correct usually | Bridge resolves correctly | Confirm via `kind`. Don't assume — `Scale NU` is a different component |
| `Scale NU` | `Component_ScaleNU` (non-uniform X/Y/Z) — correct | Bridge resolves correctly | Only use when you genuinely need non-uniform scaling |
| `Area` | `Component_AreaProperties_OBSOLETE` | Bridge prefers obsolete kind by name match | Functionally identical for Area/Centroid output. Leave it, or try `Surface AreaMoments` for non-obsolete variant |
| `Minimum` | `Component_Min_OBSOLETE` | Same | Functionally identical for `min(A, B)`. Leave it |
| `Sort List` | `Component_SortList_OBSOLETE` | Same | Functionally identical |
| `List Item` | `Component_ListItem_OBSOLETE_ASWELL` | Same | Functionally identical |
| `Reparameterize` | `not found in ANY scope` | Not a standalone component | Use a different domain-extraction path |
| `Merge` | `Component_MergeN` (Merge Multiple — dynamic Stream 0/1/...) | OK but auto-expanding inputs | Use `append=True` on subsequent wires to same stream to grow N |
| `Boundary Surfaces` | `Component_BoundarySurfaces` — correct | OK | This is the differencing component — feed multiple closed nested curves and it builds a planar Brep with holes natively |

## Persistent data: the Panel-mode fallback

Setting persistent data on a geometry-reference param (`Param_Brep`,
`Param_Curve`, `Param_Surface`) via:

```
gh_set_component_parameter(param_guid, "", "<rhino-guid>")
```

falls back to **Panel mode**: the bridge creates a `GH_Panel` widget
with the GUID text and wires it into the param's data input. The param
auto-resolves the panel text as a Rhino-doc geometry reference.

**Known limitations**:
1. **Only the first GUID is resolved** if you pass a newline-separated
   multi-GUID string. So you can't pack N hosts into one param this way.
   For N hosts, use N separate refs + `Merge Multiple`.
2. **Each persistent-data set creates a new Panel** — re-setting the
   same param overwrites the text but the Panel stays. Cleanup happens
   when you remove the param.

## MCPv2 Server gates

The v2 server (`McpServerComponentV2`) uses a `Scenario` value-list to
choose a permission preset. Five scenarios exist:

| Scenario | Allows writes? | Allows scripting? |
|---|---|---|
| Inspect | No | No |
| Tune | Slider/parameter changes only | No |
| Coach | No (read-only with feedback) | No |
| **Execute** | **Yes** | No |
| **Author** | **Yes** | Yes |

For any session that places components or runs scripts, the user must
have `Scenario = Execute` or `Author`. The bridge will silently fail
component placement and parameter sets in restrictive modes.

V2 also has `OverrideAllowParameters / Components / Scripting /
ComponentScope` inputs for fine-grained overrides — these win over the
Scenario preset.

## ComponentScope & CategoryFilter

Even with writes allowed, plugin components are gated by
`ComponentScope`:

| Scope | Value | What's placeable |
|---|---|---|
| Curated | 0 | Only components in `CategoryFilter` allow-list |
| Defaults | 1 | All stock Grasshopper components |
| All | 2 | Stock + every installed plugin |

LunchBox / Pufferfish / Weaverbird / Ladybug all require **scope=all**.
If `gh_add_component("Triangle Panels B")` returns `not found in ANY
scope` but you know LunchBox is installed, the scope is too tight.

`gh_add_any_component` bypasses `CategoryFilter` (when scope=all) for an
explicit "I know this is a plugin and I want it" intent.

## Broken / stubbed tools (this build)

- `gh_capture_canvas` — returns `"GH_Canvas.GenerateHiResImage not
  available in this Grasshopper build."`. Use `rhino_capture_viewport`
  for visual confirmation instead.
- `gh_execute_code` — returns `"execute_code is not supported in C#
  MCP server."`. Use `gh_write_script_py3` for CPython operations on a
  placed script component.
- `gh_update_script` / `gh_write_script_py2` — bridge reports
  `"Script updated."` but the code change may not actually take effect
  on Rhino 8's `ZuiPythonComponent`. **Prefer `gh_write_script_py3`**
  and verify with `gh_get_runtime_messages` after recompute (look for
  stale output as the tell).
- `rhino_execute_code` — gated by `AllowScripting` (Scenario=Author or
  explicit override). Returns `"capability denied"` otherwise.

## Result-size cap

`gh_get_context` exceeds the bridge's response cap on busy canvases
(>~50 components). Use:
- `gh_canvas_summary` for overview / error counts / widget list
- `gh_find_components(query)` for kind-based GUID lookup
- `gh_get_objects([guids])` for detailed inputs/outputs/sources/targets
  on a specific cluster

These three together cover what `gh_get_context` does without hitting
the cap.

## Pivot/position fields not exposed

`gh_get_objects` returns the full component schema including
inputs/outputs/sources/targets but **does NOT include the on-canvas
pivot (X, Y) position**. To identify which of N same-kind components is
"the one at (500, 300)", you have to rely on creation order:
`gh_find_components` returns results in creation order, so the
n-th-placed instance is at index n in the find result.

This matters when batching identical components (e.g. 4 Brep refs in
sequence). For deterministic identification, place sequentially OR
nickname each immediately after placement via
`gh_set_component_parameter(guid, "NickName", "[my-tag]")`.

## "Probe then verify" pattern

The defensive default for ANY component placement that might trap on
naming:

```
1. gh_add_component(intended_name)
2. gh_find_components(intended_name) — get GUID
3. gh_get_objects([guid]) — read kind, confirm not _OBSOLETE / not wrong variant
4. If wrong: gh_remove_node(guid), try alternate name
5. If right: proceed to wire
```

Cheaper than building a long chain on the wrong component and
discovering at recompute time.

## When the bridge updates

This file dates fast. Re-validate every quirk on bridge version bumps.
Quick smoke test:
- Place `Triangle Panels B`, `Scale`, `Extrude`, `Area`, `Minimum`,
  `Pull Point`, `Boundary Surfaces`, `Divide Domain²`
- Confirm each returned `kind` matches the expected (non-OBSOLETE,
  non-Linear for Extrude)
- Update this file's tables where the resolution has shifted

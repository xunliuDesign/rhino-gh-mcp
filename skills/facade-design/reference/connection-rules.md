# Connection Rules & Tool Playbook

The mechanics that turn a recipe into a working canvas. Read this once
before your first big build of the session; the per-typology recipes
assume it.

## `gh_connect_components` semantics

```
gh_connect_components(
    source_guid,    # GUID of the upstream component / param / slider
    source_output,  # name of the output port; "" for sliders
    target_guid,    # GUID of the downstream component / param
    target_input,   # name of the input port
    append=False,   # False = replace existing wires; True = add alongside
)
```

### Port addressing

- **By name (preferred)** — pass the input/output **name** (the long
  label), e.g. `"Geometry"`, `"Source plane"`. Lowercase / spaces are
  case-sensitive in some bridge versions; verify the live name with
  `gh_get_objects(target_guid)` before relying on a guess.
- **By nickname** — components may have nicknames like `"G"` for
  Geometry. The bridge accepts either; resolve by reading the live port
  list rather than memorizing.
- **Sliders** have one unnamed output — pass `""` for `source_output`.

If `gh_connect_components` returns `"input not found"` or `"output
not found"`:

1. `gh_get_objects([guid], context_depth=0)` on the offending end.
2. Read the `inputs` / `outputs` arrays from the response.
3. Retry with the exact name from the live read.

This is the single most common write-side failure. Don't guess twice
— always read.

### The `append` flag

Default `append=False` replaces any existing wire on the target input
(the legacy behavior). Set `append=True` when you want to merge:

```
# Wiring two slider sources into a single Merge input
gh_connect_components(slider_a, "", merge_guid, "Data", append=False)
gh_connect_components(slider_b, "", merge_guid, "Data", append=True)
```

Closed catalog bug 8 (already shipped in the installed .gha). A
duplicate-pair guard prevents double-counting the same `(source,
target)` — re-sending the same pair is a no-op.

### Tiling-specific uses

The Orient component's `B` (target plane) input often needs a list from
two sources (main wall planes + window-surround planes, or two halves of
a host). Use `append=True` for the second source. The list flattens
inside Orient.

## Grafting / list situations in tiling

The Orient component is the most common offender:

| Situation | Right tree shape | Common wrong shape | Fix |
|---|---|---|---|
| 1 module → N target planes | `A`: one Plane (flat). `B`: list of N Planes (flat in one branch). `G`: one Geometry (flat). | `B` grafted (N branches, 1 plane each) — Orient outputs only 1 | `Flatten` before B input |
| N module variants → N target planes | `A`: list of N source planes. `B`: list of N target planes. `G`: list of N geometries. All same length, same tree. | Mismatched tree depths → Orient does cross-product or single output | Align all three trees with `Graft` / `Flatten` / `Path Mapper` until they match |
| Attractor per-cell scale on tiled modules | `Scale.Factor` input must match the data tree of Orient's output | Single scalar Factor → all modules same scale (defeating attractor) | Graft the factor list to match Orient output tree depth |

The general rule: **inputs whose tree shapes don't match get
short-circuited.** When a component outputs fewer items than expected,
the input trees are the first thing to check. `gh_get_objects` on the
component and look at the input list's tree depth in the response.

## Placing components — gating by ComponentScope

`gh_add_component(name, x, y)` is gated by the .gha's `ComponentScope`
input (set via `gh_set_component_parameter` on the rhino-gh-mcp Server
component on the canvas):

| Scope | Value | What's placeable |
|---|---|---|
| Curated | 0 | Only components in the canvas's CategoryFilter allow-list |
| Defaults | 1 | All stock Grasshopper components |
| All | 2 | All stock components + every installed plugin |

For this skill (`recommended_scope: defaults`), the vanilla recipes
work. Plugin assists and Ladybug-driven sun routing need scope `all`.

### Verifying availability before relying on a name

```
1. gh_list_available_components(refresh=True)  # listing — possibly stale
2. Search the listing for the component name.
3. If found, gh_add_component(name) directly.
4. If not found in listing:
   a. Probe: gh_add_component(name) once.
   b. If add succeeds, the listing was stale — proceed.
   c. If add fails, the component is gated or absent:
      - Check ComponentScope; if 0 or 1, ask user to flip to 2.
      - If 2 and still fails, the plugin isn't installed — fall back to
        the native recipe in the same file and note in the report.
```

### `gh_add_any_component` vs `gh_add_component`

Functionally identical when ComponentScope=2; intent-explicit. Use
`gh_add_any_component` when you're *deliberately* placing a
plugin/category-filtered component and want the trace to show the
escape from the curated allow-list.

## Reading inputs before wiring (the verifying step)

The single discipline that prevents most write-side bugs:

```
# 1. Place the component
ge_guid = gh_add_component("Orient", 600, 100)

# 2. Read its live inputs/outputs (don't guess)
info = gh_get_objects([ge_guid])
# response includes inputs: [
#   {"name": "G", "nickname": "G", "tree_depth": 0, ...},
#   {"name": "A", "nickname": "A", ...},
#   {"name": "B", "nickname": "B", ...},
# ]

# 3. Wire by the real names
gh_connect_components(module_guid, "Geometry", ge_guid, "G")
gh_connect_components(base_plane_guid, "", ge_guid, "A")
gh_connect_components(target_planes_guid, "Planes", ge_guid, "B")
```

Step 2 catches version-to-version port renames (e.g. "Source plane" vs
"A" vs "Source Plane" — all valid in different builds). Don't skip it.

## Scripting escalation rules

The script-component tools (`gh_write_script_py3`, `gh_write_script_py2`,
`gh_write_script_cs`, `gh_update_script`, `gh_execute_code`) are gated by
the `allow_scripting` capability. The skill requests it in frontmatter
because the `tessellation` typology requires it.

### When to escalate

| Trigger | Reach for |
|---|---|
| Voronoi, Delaunay, image-driven fields | `gh_write_script_py3` (a single tessellation node) |
| Graph nearing 15–20 components for a single typology | `gh_write_script_py3` (compress the math) |
| Reading objects by GUID / layer with semantic filters | `gh_write_script_py3` (uses RhinoCommon directly) |
| Tight inner loop over many cells with per-cell math (>~500 cells) | `gh_write_script_py3` or `gh_write_script_cs` for speed |
| Operations the bridge doesn't expose (e.g. cluster manipulation) | `gh_execute_code` (but see caveat) |

### When NOT to escalate

- Anything the graph does cleanly in <10 components. Stay in graph.
- Per-cell rotation / scale / attractor drivers — `Remap Numbers` + the
  attractor recipe is the right move.
- Simple list operations (Range, Series, Bounds, Flatten). Use the
  native components; don't write a script for them.

### Which script flavour

| Tool | Language | Use for |
|---|---|---|
| `gh_write_script_py3` | CPython 3.9 (Rhino 8) | **Default**. Real numpy / math, modern syntax. |
| `gh_write_script_py2` | IronPython 2.7 | Legacy GHPython component compatibility. **No numpy.** |
| `gh_write_script_cs` | C# 8 (Rhino 8) | RhinoCommon-heavy code, performance-critical inner loops. |
| `gh_execute_code` | IronPython 2.7 in the .gha context | Direct canvas manipulation; **bridge stub on the current build — returns "execute_code is not supported in C# MCP server."** Avoid. |

### Caveats on the current build

Per the 2026-06-11 bridge verification:

- `gh_execute_code` is a stub — returns `"execute_code is not supported
  in C# MCP server."`. Avoid; use `gh_write_script_py3` for anything
  that needs CPython.
- `gh_update_script` and `gh_write_script_py2` report `"Script updated."`
  but the code change may not take effect on Rhino 8's
  `ZuiPythonComponent`. **Prefer `gh_write_script_py3`** and verify with
  `gh_get_runtime_messages(guid)` after recompute — if the runtime
  shows the *old* output, the write didn't apply.
- `gh_capture_canvas` raises `"GH_Canvas.GenerateHiResImage not
  available in this Grasshopper build."` — use `rhino_capture_viewport`
  for visual checks.

### Script component placement pattern

```
# 1. Place the script container
script_guid = gh_add_component("Python 3 Script", x, y)

# 2. Define inputs/outputs and write code
gh_write_script_py3(
    instance_guid=script_guid,
    code=open_python_3_source_string,
    description="facade voronoi tessellation",
    param_definitions=[
        {"type": "input",  "name": "surface",    "type_hint": "Surface"},
        {"type": "input",  "name": "seed_count", "type_hint": "int"},
        {"type": "input",  "name": "seed_seed",  "type_hint": "int"},
        {"type": "input",  "name": "cell_inset", "type_hint": "float"},
        {"type": "output", "name": "cells",      "type_hint": "Curve"},
        {"type": "output", "name": "centroids",  "type_hint": "Point3d"},
    ],
)

# 3. Wire inputs — read live names first
info = gh_get_objects([script_guid])
# ... wire surface_guid → "surface", etc.

# 4. Recompute; check runtime messages on the script
gh_recompute()
msgs = gh_get_runtime_messages(script_guid)
# Look for "Script updated successfully" vs older output stuck.
```

## Reading the canvas state — cheap vs expensive

| Tool | Cost | When |
|---|---|---|
| `gh_status` | trivial | First call of any session |
| `gh_canvas_summary` | low | Quick digest — counts, widgets, error/warning counts |
| `gh_find_components(query)` | low | Locate by name substring |
| `gh_list_sliders` / `gh_list_panels` / `gh_list_toggles` / `gh_list_value_lists` | low | Find specific widgets by type |
| `gh_get_objects([guid, ...])` | medium | Read specific components' full info (inputs, outputs, runtime messages) |
| `gh_get_objects(.., context_depth=1)` | medium | Same + neighbors (one wire hop) |
| `gh_get_context()` | high | Full canvas — every object, wire, tree. Use only when editing an existing complex graph |
| `gh_list_available_components(refresh=True)` | medium-high | Available-to-place listing — refresh occasionally stale-cached |

Default cadence:

- Once per session: `gh_status`, `gh_canvas_summary`, `rhino_status`,
  `rhino_get_scene_info`.
- Per component placement: `gh_get_objects([new_guid])` to read live
  ports before wiring.
- Per recompute: `gh_canvas_summary` for the error/warning counts;
  `gh_get_runtime_messages(guid)` on each red node.

## Setting persistent data on params

For `Surface` / `Brep` / `Curve` / `Point` params that need a Rhino-doc
referenced object:

```
gh_set_component_parameter(
    param_guid,
    param_name="",      # empty string for the param's own data
    value="<rhino-guid>",
)
```

For top-level number / boolean / string inputs (e.g. a Toggle component
or a numeric input expecting a constant):

```
gh_set_component_parameter(param_guid, "", "true")     # for Boolean Toggle
gh_set_component_parameter(slider_guid, "", "42")      # equivalent to gh_set_slider
gh_set_component_parameter(comp_guid, "Distance", "1.5")
```

The bridge auto-detects the parameter type per the 2026-06-11 bugfix
(modes: integer, number, boolean, string, interval). For variant /
Point / Vector / Plane / Colour / GenericObject inputs, the bridge
falls back to an orphan-panel approach — this is *not* the right path
for setting a Plane; instead, build the Plane on the canvas (`XY Plane`,
`Construct Plane`, etc.) and wire it.

## Slider lifecycle

```
# Place a slider with a name and range
slider_guid = gh_add_slider(
    name="[facade] fin_depth",
    min_value=0.05,
    max_value=0.6,
    value=0.25,
    position_x=400, position_y=300,
    integer=False,
)

# Later: change just the value
gh_set_slider(slider_guid, 0.35)

# Or change just the range (e.g. after computing host bbox)
gh_set_slider_range(slider_guid, 0.05, 1.0)
```

When the user sees the final canvas, the sliders' names are how they
navigate. Naming convention: `[facade] <driver>` for the design dials;
`[base] <dimension>` for the host placeholder dimensions; `[module]
<dim>` for module-side params. Consistency matters more than
specific prefixes.

## Cleanup

`gh_remove_node(guid)` removes a single node. Use in two cases:

- After a probe (`gh_add_component(name)` to check availability) →
  remove the probe immediately.
- On typology swap → identify the prior facade's nodes via
  `gh_find_components("[facade]")` and remove each.

Always recompute after a batch of removals to clear any dangling
references.

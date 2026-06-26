# Host Ingest — bringing existing Rhino geometry into Grasshopper

When the user's prompt references existing Rhino geometry — "this
surface", "my building", "the selected face", "the objects on layer
X" — the GH chain needs to *reference* that geometry, not recreate
it from sliders. This file is the canonical map of how to do that.

**The authoring rule:** use these native parameter components — NEVER
a Python script. Native param components keep the canvas legible
(the user can see what's wired), survive Rhino-side edits to the
host (re-referencing the same GUID picks up the updated geometry
automatically), and don't require `AllowScripting`. A script that
calls `Rhino.RhinoDoc.ActiveDoc.Objects.Find(guid)` does the same
thing far worse — it's invisible to the canvas reader, brittle to
refactoring, and silently returns empty when the GUID lookup fails.

## The three canonical ingest patterns

Pick one of these three based on how the host geometry is identified
in the prompt:

### Pattern 1 — Single host by GUID → `Param_Brep`

When the user references one specific Rhino object (a brep, a
polysurface, a named host) and you have its GUID from
`rhino_get_objects_with_metadata`.

```
Param_Brep  ──→  (downstream chain)
   ↑
   persistent data set to the host's GUID
```

**Bridge mechanics:**

```python
# 1. Place the param
gh_add_component("Brep")           # returns Param_Brep GUID

# 2. Set persistent data to the host GUID
gh_set_component_parameter(
    param_brep_guid,
    "",                            # empty target = the param itself
    "<host-rhino-guid>"
)
```

The bridge writes the GUID via Panel-mode fallback (creates an
internal GH_Panel) — `Param_Brep` resolves the panel text and pulls
in the Rhino geometry. From the user's perspective on the canvas,
the param shows "Set one Brep" with the host's name underneath.

**For Param_Surface or Param_Curve:** identical mechanics, just place
a `Surface` (param) or `Curve` (param) instead of `Brep`. Use
`Param_Surface` for a single planar/trimmed surface; use
`Param_Curve` for a single curve (attractor curve, boundary, edge
profile).

### Pattern 2 — Hosts on a named layer → `Geometry Pipeline`

When the user references a layer ("everything on the Louvers
layer", "the kinetic facade objects"), use `Geometry Pipeline`. It
queries the Rhino doc continuously by layer, so adds/removes to the
layer in Rhino automatically reflect on the canvas.

```
Geometry Pipeline  ──→  (downstream chain)
       ↑
       Filter parameter set to layer name (e.g. "Louvers")
```

**Bridge mechanics:**

```python
gh_add_component("Geometry Pipeline")   # returns the component's GUID

# Configure the Filter input via the canvas-side parameter setting
# (Geometry Pipeline reads its layer filter from a right-click setting,
#  not a wired input — use rhino_execute_code or a one-shot script to
#  configure if gh_set_component_parameter doesn't expose it)
```

**Why prefer Geometry Pipeline:**
- Auto-tracks Rhino layer changes (add a brep to the layer → it
  appears in the GH chain on next recompute)
- One component handles N hosts of unknown count (no need to know
  GUID-by-GUID upfront)
- Filter by layer name, object type, color, attributes — all built in

**Use case:** the user's typical workflow is "I have N facade
surfaces on layer 'Facades', generate the panels." Geometry Pipeline
is the one-line ingest.

### Pattern 3 — Multiple discrete hosts by GUID → N × `Param_Brep` + `Merge Multiple`

When the user references several specific objects by name or
selection and you have a list of GUIDs (but they're not all on the
same layer, so Geometry Pipeline doesn't fit).

```
Param_Brep (GUID 1)  ──┐
Param_Brep (GUID 2)  ──┤
Param_Brep (GUID 3)  ──┼──→  Merge Multiple  ──→  (downstream chain)
Param_Brep (GUID 4)  ──┘
```

**Bridge mechanics:**

```python
# Place N param refs, one per host
brep_refs = []
for guid in host_guids:
    p = gh_add_component("Brep")
    gh_set_component_parameter(p, "", guid)
    brep_refs.append(p)

# Place Merge Multiple
merge = gh_add_component("Merge")     # returns Component_MergeN

# Wire each ref into the merge's dynamic streams
for i, ref in enumerate(brep_refs):
    gh_connect_components(
        ref, "",                     # ref's output
        merge, "",                   # merge's input — auto-grows
        append=(i > 0)               # append for streams after the first
    )
```

**Why not just one Param_Brep with multiple GUIDs?** The bridge's
Panel-mode persistent-data fallback only resolves the **first** GUID
of a multi-line string. Subsequent GUIDs are silently dropped. See
`bridge-quirks.md` § Persistent data.

### Pattern 4 — Bounding curve only → `Curve` param → `Param_Surface` (implicit conversion)

When the host is defined by a curve (not a surface or Brep) — e.g.
the user has drawn a boundary on the Rhino doc and wants a facade
generated within it.

```
Curve (param, GUID-ref to closed planar curve)
       ↓
Param_Surface       (implicit Curve→Surface conversion — fills the curve)
       ↓
(downstream panelizer / extrude chain)
```

`Param_Surface` accepts a closed planar curve and silently emits the
planar surface bounded by it. From there, panelizers and
extrude chains work as if a real Surface had been the ingest.

## Choosing between Patterns 1, 2, and 3

| User prompt | Pattern | Why |
|---|---|---|
| "Apply louvers to **this surface**" (with one object selected) | Pattern 1 | Single GUID, no layer involved |
| "Generate a facade on **layer 'Walls'**" | Pattern 2 | Layer-based query — Geometry Pipeline |
| "Panel **these four faces**" (multiple selected) | Pattern 3 | Multiple discrete GUIDs, not necessarily same layer |
| "Use **the curve I drew** as the boundary" | Pattern 4 | Curve-defined host |
| Nothing explicit, but `rhino_get_objects_with_metadata` shows breps on a layer matching the prompt's typology name | Pattern 2 | Layer match implies the user intends that layer as the host |

## What NOT to do — the script anti-pattern

When a host exists, the wrong instinct is:

```python
# DO NOT DO THIS
gh_write_script_py3(
    script_guid,
    "...",
    code='''
import Rhino
guids = [...]  # GUIDs hardcoded or passed in
hosts = [Rhino.RhinoDoc.ActiveDoc.Objects.Find(g).Geometry for g in guids]
# operate on hosts...
'''
)
```

This is wrong for five reasons:

1. **Invisible on the canvas.** A reader sees a script block; they
   can't tell what host it references without opening the script.
   Param_Brep with "Set one Brep" is self-documenting.
2. **Brittle to refactoring.** If a recipe later wants to add a
   second host, you have to edit code instead of dropping in another
   Param_Brep + wiring it.
3. **Requires AllowScripting** (Scenario = Author). Param-based
   ingest works in Execute mode.
4. **Silent failure modes.** A bad GUID returns empty; the script
   computes happily on an empty list and downstream Null cascades
   without obvious cause. Param_Brep with a stale GUID at least
   shows up as orange on the canvas.
5. **Defeats the parametric model.** The whole point of GH is the
   editable graph — scripts hide everything they do.

If you find yourself drafting a Python script to read host geometry,
stop. One of the four patterns above handles your case.

## Verifying ingest worked

After setting persistent data on any `Param_Brep` / `Param_Surface` /
`Param_Curve`:

1. **`gh_recompute`**
2. **`gh_get_runtime_messages(param_guid)`** — should be empty. If it
   reports `"Data conversion failed"` or `"Geometry collection
   contains a null"`, the GUID isn't resolving (typo, deleted Rhino
   object, layer hidden).
3. **`gh_get_objects([param_guid])`** — read the param's output.
   Should show "Brep" / "Surface" / "Curve" with the actual geometry
   data preview, not "Null" or "empty list".

For `Geometry Pipeline`: same checks, plus
4. **`rhino_get_objects_with_metadata`** filtered to the target layer
   — confirm the Rhino-side count matches the param's output count.

## Multi-host extension of any recipe

Every recipe in every task skill is authored on a single host (one
Rectangle in stage 01). To extend to N hosts: replace stage 01's
`Rectangle → Param_Surface` with one of the patterns above. The rest
of the recipe is **tree-aware** — panelizers emit one branch per
host, downstream operations preserve branch structure, extrude
broadcasts per branch. No other recipe changes needed.

The recipe didn't say "do this in a loop" or "duplicate the chain N
times" because it doesn't have to. Pattern 2 or 3 plus the rest of
the recipe gives you the full multi-host build for free.

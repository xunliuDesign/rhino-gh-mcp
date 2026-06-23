# Geometry Ingest — Reading a Host from the Rhino Doc

The contract this file is responsible for delivering: **one base surface
with a verified outward normal, available on the Grasshopper canvas via a
Surface or Brep param.** Everything downstream — every typology recipe in
`typologies.md` — assumes this contract. Don't move past it until it
holds.

## Recognition sequence

In order, never skipping a step:

### 1. `rhino_status`

Confirm the Rhino bridge is reachable. If not, ask the user to start
Rhino with the plugin loaded (`_ToggleMcpService`).

### 2. `rhino_get_scene_info`

Gives a lightweight overview. Read:

- `length_units` — drives the defaults / ranges row to use (see
  `typologies.md` § Unit selection rule).
- A list of layers and a small sample of objects per layer — enough to
  tell whether the user has a few objects (easy) or hundreds (harder
  recognition).

### 3. `rhino_get_layers`

The full layer list. Layers often carry the semantic labels you need:
`building`, `mass`, `facade`, `site`, `context`, `existing`. If a layer
clearly says "facade" or "building", filter the next read to it.

### 4. `rhino_get_objects_with_metadata`

The detailed read. Pass filters when you can:

```python
rhino_get_objects_with_metadata(
    filters={"layer": "building*"},  # wildcards supported
    metadata_fields=["short_id", "type", "bbox", "layer", "name"],
)
```

Use the result to classify each candidate object by type and bbox. The
type vocabulary in this server includes `Brep`, `Extrusion`, `Surface`,
`Curve`, `Mesh`, `Point`, `BlockInstance` (and more).

### 5. Viewport capture (optional but cheap)

`rhino_set_view(standard="Perspective")` then `rhino_capture_viewport`
to **eye-check** that the host you identified looks like a building, not
a stray section line. Skip this only if `rhino_get_objects_with_metadata`
returned exactly one obvious candidate.

## Host taxonomy

Match the candidate's `type` field to a handling rule.

### Planar Surface / single Brep face

The simplest case. Use directly as the base surface. Verify outward
normal (see below).

### Closed massing (Brep solid / polysurface with no openings)

A solid box / extrusion / massing model — what the user means when they
say "this building". Multiple faces; pick the facade face(s) by normal
orientation.

**Default policy (no clarifying Q):**
- Compute the bounding box of the solid.
- For each Brep face: compute its outward normal at the face centroid
  (use `Evaluate Surface` at the face's UV-midpoint).
- Reject faces whose normal is near-vertical (|normal.Z| > 0.8 → top or
  bottom).
- Of the remaining, **the largest face** is the default facade face.
- Tag the others as "side faces — say so if you want them too".

**Clarifying Q triggered** when:
- 4 or more non-horizontal faces with areas within ±10% of each other
  (e.g. a simple square tower) — ask which face or "all four".
- The largest face is suspiciously narrow (aspect > 5:1) — could be a
  thin wall the user doesn't actually want clad.

Implementation: in the canvas, place a `Brep` param for the mass,
extract `Deconstruct Brep` → `Faces`, compute each face's area and
centroid normal in a small `Python 3 Script` component, then `List
Item` at the chosen index.

### Open polysurface (multiple surfaces, not joined into a solid)

Treat each as a candidate face. If they share an obvious "facade" layer,
include all; otherwise pick by the largest non-horizontal rule.

### Boundary curve (closed planar curve)

Needs to be turned into a face first:
- `Boundary Surfaces` if planar and simple.
- `Patch` or `Planar Surface` if it has interior holes.
- `Loft` between this and another parallel curve if the user wants an
  extruded wall.

Default: `Boundary Surfaces`. Tell the user "I used your boundary curve
as the wall plane".

### Mesh

Two conversion paths:
- **Mesh → Brep** via `Mesh to Brep` for simple low-poly massing
  geometry.
- **QuadRemesh → Brep** for organic / scanned meshes — gives a cleaner
  surface but loses sharp features.

Default: `Mesh to Brep` for low-poly (face count < 500), QuadRemesh
otherwise. State the choice in the report.

### Block instance (`rhino_list_blocks`)

A block instance suggests one of two things:
- The user has a **module** they want tiled (most common — a window
  unit, panel, etc.). Tag as the module role.
- The user has a **whole building as a block** for organization. Tag as
  the host role, and explode it (`_ExplodeBlock`) to extract the host
  geometry — OR keep it as a block and tile facade elements *around*
  the block instance.

To disambiguate without asking: a block whose bbox is comparable to the
scene bbox (within 50% of the largest dimension) → host. A block whose
bbox is small (< 10% of any candidate host bbox) → module. If both
sizes are present and unclear, build for the larger as host and the
smaller as module, and report both.

## Outward-normal verification — the recurring failure mode

Every host needs a verified outward normal before the facade graph
consumes it. Without it, the typology builds the facade *into* the
building.

### For a single surface / face

1. Evaluate the surface at its UV midpoint → point + normal.
2. Get the surface's bbox center; compare to the point.
3. If `Vector(bbox_center → point).Dot(normal) < 0`, the normal points
   inward — `Flip` the surface before downstream.

### For a face extracted from a closed mass

1. Evaluate the face at its UV midpoint → point + normal.
2. Get the **mass's** bbox center (not the face's).
3. Same dot product check; same `Flip` if inward.

### Native chain

```
Surface  ->  Evaluate Surface(u=0.5, v=0.5)  ->  Point + Normal
            |
            ->  Volume centroid (Brep → Volume)
            ->  Vector 2Pt(from centroid to point)
            ->  Dot(this vector, normal)
            ->  Smaller Than(0)  ->  bool
            ->  Stream Filter(true=Flip(surface),
                              false=surface)  ->  oriented surface
```

This is a 6-component subgraph. Place once per host; cache the result
in a clearly-nicknamed `Surface` param `[base] facade_oriented`.

### Or just do it in script

If the script-escalation path is open, a 6-line Python 3 component:

```python
import Rhino.Geometry as rg
# inputs: surface (Surface), mass (Brep optional)
# output: oriented (Surface)

u_mid = surface.Domain(0).Mid
v_mid = surface.Domain(1).Mid
p = surface.PointAt(u_mid, v_mid)
n = surface.NormalAt(u_mid, v_mid)
center = (mass.GetBoundingBox(True).Center
          if mass else surface.GetBoundingBox(True).Center)
if rg.Vector3d(p - center) * n < 0:
    surface.Reverse(0, True)
    surface.Reverse(1, True)
oriented = surface
```

## GUID handoff — the bridge between `rhino_*` and `gh_*`

Once you've identified the host in the Rhino doc and verified its
normal, the host has to land on the Grasshopper canvas. Two supported
mechanisms.

### Default — GH-side geometry reference param

The lightest path. Works for any host the user picked.

1. `gh_add_component("Surface")` → returns the GUID of a floating
   `Surface` param on the canvas. (Use `"Brep"` for non-rectangular
   hosts.)
2. `gh_set_component_parameter(param_guid, "", "<rhino-object-guid>")`
   to set the persistent data to the Rhino object's GUID.
3. The param now resolves to the Rhino geometry on every recompute.
4. Wire the param's output into the typology recipe's `Surface` input
   and continue.

**Caveat:** the bridge's `gh_set_component_parameter` writes the GUID
as a string. The `Surface` param will resolve it on the GH side as a
referenced object — provided the GUID is valid in the active Rhino
document. Verify with `gh_get_runtime_messages` after recompute; if
the param shows "1. Data conversion failed from Guid to Surface",
the GUID was wrong or the object is on a hidden layer.

**Equivalent user-mediated path:** ask the user to right-click the
canvas param and "Set one Surface" / "Set one Brep" — pick in the
viewport. Use this when the GUID-by-string method shows "Data
conversion failed" repeatedly (a sign of a bridge-version mismatch).

### Fallback — Python 3 script pulling the object

Use when the GUID-by-string path is flaky, OR when the user has many
matching objects and wants a per-name / per-layer pickup.

1. `gh_add_component("Python 3 Script")` → returns GUID.
2. `gh_write_script_py3(guid, code, param_definitions=...)` to define
   the inputs (a string `target_layer` or `target_guid_str`) and the
   output (`Surface` or `Brep`).
3. The script body:

```python
import Rhino
import scriptcontext as sc

# inputs: target_layer (str)   -- e.g. "building::facade"
# outputs: surface (Brep)

doc = sc.doc  # active Rhino document
results = []
for obj in doc.Objects:
    if obj is None or obj.IsHidden:
        continue
    layer = doc.Layers[obj.Attributes.LayerIndex].FullPath
    if layer == target_layer or layer.endswith("::" + target_layer):
        geom = obj.Geometry
        if isinstance(geom, (Rhino.Geometry.Brep,
                             Rhino.Geometry.Surface,
                             Rhino.Geometry.Extrusion)):
            if isinstance(geom, Rhino.Geometry.Extrusion):
                geom = geom.ToBrep()
            results.append(geom)

# If multiple, return the largest by bbox volume — the host
if results:
    results.sort(
        key=lambda b: b.GetBoundingBox(True).Volume,
        reverse=True,
    )
    surface = results[0]
else:
    surface = None
```

4. Wire the script's `surface` output into the typology recipe.

**Why this fallback is sometimes the better default:** if the host is a
trim region of a larger Brep, or a face the user wants extracted by a
named criterion (layer, attribute), the script handles it in one place
without a chain of `Deconstruct Brep` + `List Item` decisions.

### Choose by criterion

| Situation | Mechanism |
|---|---|
| Single, unambiguous host the user picked or named | GH geometry reference param |
| User asks "use the building on layer `mass`" | Python 3 fallback |
| User has only one Brep in the doc | GH geometry reference param |
| Multiple candidates, semantic filter (layer / name) needed | Python 3 fallback |
| Host is a mesh that needs conversion | Python 3 fallback (do the conversion inline) |

State which mechanism you used in the report — "I wired your `building`
layer's largest Brep into the canvas via a Python 3 script" — so the
user can swap to a manual right-click pick later if they want.

## Caching the contract

After ingest, the canvas should have:

- A nicknamed param `[base] host` of type `Surface` or `Brep` holding the
  oriented host. Set the nickname via
  `gh_set_component_parameter(guid, "NickName", "[base] host")` after
  add.
- (If the host was a mass) a nicknamed param `[base] facade_face` of
  type `Surface` holding the extracted-and-oriented facade face.

Every typology recipe consumes `[base] facade_face` (or `[base] host`
if it's already a single surface). The router never needs to re-derive
this — once cached, follow-up prompts (typology swaps, slider changes)
reuse it.

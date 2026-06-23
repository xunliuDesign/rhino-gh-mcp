# Example — Voronoi Tessellation (Script-Escalation Path)

The `tessellation` typology — the canonical case for reaching past the
graph into `gh_write_script_py3`.

## Prompt

> *"I want a honeycomb screen on this curved facade."*

Synonym map: "honeycomb" → `tessellation`. The user said honeycomb
specifically, not Voronoi — but honeycomb is one tessellation variant
(hexagonal). For a *parametric* honeycomb the script approach scales
better; for a strict regular hex grid, a native `Hexagonal Pattern`
component does it in one node.

Decision: prompt says "honeycomb" (regular) — start with the native
hex grid. If the user follows up with "more organic", swap to the
Voronoi script. Document this fork in the report so the user knows
the option.

For the trace below, use the **Voronoi script** path (covers the
script-escalation pattern fully; the hex-grid path is the simpler
variant the skill picks by default and is mentioned at the end).

## Preflight

```
gh_status()           → ok
rhino_status()        → ok
rhino_get_scene_info()
  → {"length_units": "Meters",
     "layers": ["Default", "facade"]}
```

## Ingest host

User said "this curved facade" — assume a Surface on the facade layer.

```
rhino_get_objects_with_metadata(
    filters={"layer": "facade"},
    metadata_fields=["short_id", "type", "bbox"],
)
→ [{"short_id": "s1", "type": "Surface", "bbox": {...}}]
```

A single Surface. Easy host — no face extraction needed, just wire
it in.

```
surf_g = gh_add_component("Surface", 100, 100)
gh_set_component_parameter(surf_g, "", "<rhino-guid-of-s1>")
gh_set_component_parameter(surf_g, "NickName", "[base] facade_face")
```

Outward-normal check still applies. For a single surface, place the
6-component oriented-surface subgraph from `reference/geometry-ingest.md`
§ Outward-normal verification — or fold it into the tessellation
script's preamble. Doing the latter keeps the canvas cleaner.

## Tessellation sliders

```
seed_count_g = gh_add_slider("[facade] seed_count",
                              10, 400, 60, 400, 50, integer=True)
seed_seed_g  = gh_add_slider("[facade] seed_seed",
                              0, 9999, 42, 400, 90, integer=True)
inset_g      = gh_add_slider("[facade] cell_inset",
                              0, 0.3, 0.08, 400, 130)
extrude_g    = gh_add_slider("[facade] cell_extrude",
                              0, 0.5, 0.06, 400, 170)
```

## Voronoi script

```
voronoi_g = gh_add_component("Python 3 Script", 700, 100)
gh_write_script_py3(
    voronoi_g,
    code="""
import Rhino.Geometry as rg
import random

# inputs:
#   surface    : Surface (host)
#   seed_count : int  (number of Voronoi seeds)
#   seed_seed  : int  (RNG seed)
#   cell_inset : float (0-0.3, fraction of cell radius)
# outputs:
#   cells      : Curve list (closed boundary curves, on the surface)
#   centroids  : Point3d list (cell centers, on the surface)

random.seed(seed_seed)

# Step 1: sample seed_count UVs in (0,1)^2
seeds = [(random.random(), random.random())
         for _ in range(seed_count)]

# Step 2: Voronoi in UV via a simple half-plane intersection
# (Bowyer-Watson is faster but verbose; the half-plane approach is
# O(n^2) but readable, fine for <400 cells.)
def clip_polygon(poly, line_start, line_end):
    # Sutherland-Hodgman: clip polygon `poly` (list of (x,y) tuples)
    # against the half-plane to the LEFT of (line_start -> line_end).
    def inside(p):
        return ((line_end[0]-line_start[0]) * (p[1]-line_start[1])
                - (line_end[1]-line_start[1]) * (p[0]-line_start[0])
                >= 0)
    def intersect(p1, p2):
        x1,y1=p1; x2,y2=p2; x3,y3=line_start; x4,y4=line_end
        denom = (x1-x2)*(y3-y4) - (y1-y2)*(x3-x4)
        if abs(denom) < 1e-12:
            return p1
        t = ((x1-x3)*(y3-y4) - (y1-y3)*(x3-x4)) / denom
        return (x1 + t*(x2-x1), y1 + t*(y2-y1))
    out = []
    for i in range(len(poly)):
        a = poly[i]
        b = poly[(i+1) % len(poly)]
        if inside(a):
            out.append(a)
            if not inside(b):
                out.append(intersect(a, b))
        elif inside(b):
            out.append(intersect(a, b))
    return out

def voronoi_cell(seeds, idx, bbox):
    poly = [bbox[0], (bbox[1][0], bbox[0][1]),
            bbox[1], (bbox[0][0], bbox[1][1])]
    px, py = seeds[idx]
    for j, (qx, qy) in enumerate(seeds):
        if j == idx:
            continue
        # Perpendicular bisector between p and q
        mx = (px + qx) * 0.5
        my = (py + qy) * 0.5
        # Direction perpendicular to (q-p), oriented so "inside" =
        # the side containing p
        dx = qy - py
        dy = -(qx - px)
        line_start = (mx, my)
        line_end = (mx + dx, my + dy)
        # Make sure p is on the "inside" of the half-plane
        if ((line_end[0]-line_start[0]) * (py-line_start[1])
            - (line_end[1]-line_start[1]) * (px-line_start[0])) < 0:
            line_start, line_end = line_end, line_start
        poly = clip_polygon(poly, line_start, line_end)
        if not poly:
            return []
    return poly

bbox = ((0.0, 0.0), (1.0, 1.0))
cells_uv = []
centroids_uv = []
for i in range(seed_count):
    cell = voronoi_cell(seeds, i, bbox)
    if not cell or len(cell) < 3:
        continue
    cells_uv.append(cell)
    cx = sum(p[0] for p in cell) / len(cell)
    cy = sum(p[1] for p in cell) / len(cell)
    centroids_uv.append((cx, cy))

# Step 3: Inset toward centroid by cell_inset
cells_uv_inset = []
for cell, (cx, cy) in zip(cells_uv, centroids_uv):
    cell_inset_pts = [
        (px + (cx - px) * cell_inset,
         py + (cy - py) * cell_inset)
        for (px, py) in cell
    ]
    cells_uv_inset.append(cell_inset_pts)

# Step 4: Map UV polygons to the surface
u_dom = surface.Domain(0)
v_dom = surface.Domain(1)
def uv_to_surf(u, v):
    su = u_dom.T0 + u * (u_dom.T1 - u_dom.T0)
    sv = v_dom.T0 + v * (v_dom.T1 - v_dom.T0)
    return surface.PointAt(su, sv)

cells = []
for cell in cells_uv_inset:
    pts3d = [uv_to_surf(u, v) for (u, v) in cell]
    pts3d.append(pts3d[0])  # close
    cells.append(rg.PolylineCurve(pts3d))

centroids = [uv_to_surf(u, v) for (u, v) in centroids_uv]
""",
    description="Voronoi tessellation on a parametric surface",
    param_definitions=[
        {"type":"input", "name":"surface",    "type_hint":"Surface"},
        {"type":"input", "name":"seed_count", "type_hint":"int"},
        {"type":"input", "name":"seed_seed",  "type_hint":"int"},
        {"type":"input", "name":"cell_inset", "type_hint":"float"},
        {"type":"output","name":"cells",      "type_hint":"Curve"},
        {"type":"output","name":"centroids",  "type_hint":"Point3d"},
    ],
)

# Wire inputs
gh_connect_components(surf_g, "", voronoi_g, "surface")
gh_connect_components(seed_count_g, "", voronoi_g, "seed_count")
gh_connect_components(seed_seed_g, "", voronoi_g, "seed_seed")
gh_connect_components(inset_g, "", voronoi_g, "cell_inset")
```

## Cell extrusion

The script returns closed boundary curves. Build cell faces with
`Boundary Surfaces`, then extrude by `cell_extrude` along each cell's
local normal:

```
bs_g = gh_add_component("Boundary Surfaces", 900, 100)
gh_connect_components(voronoi_g, "cells", bs_g, "Edges")

# Per-cell normal via Evaluate Surface at the centroid's UV — but the
# script returned 3D centroids. For the extrusion, use the host
# surface's normal at the centroid (approximate). Simplification:
# use a constant outward direction; for a flat host this is fine,
# for a curved host you want per-cell normals. The clean version
# returns normals from the script too — leaving as TODO and using
# Unit Y for now (works for facades on XZ plane).

unity_g = gh_add_component("Unit Y", 900, 200)
gh_connect_components(extrude_g, "", unity_g, "Factor")

extrude_op_g = gh_add_component("Extrude", 1100, 100)
gh_connect_components(bs_g, "Surfaces", extrude_op_g, "Base")
gh_connect_components(unity_g, "Vector", extrude_op_g, "Direction")
```

(For a curved host where the normal varies, extend the script to
output per-cell normals and use `Move` per cell instead of a uniform
`Extrude`. Note in the report.)

## Closing

```
gh_recompute()
gh_canvas_summary()
gh_get_runtime_messages(voronoi_g)
  → expect "Script updated successfully" and no errors
  → if you see *no* update message even after a fresh write, the
    bridge stub for Rhino 8 ZuiPythonComponent may have swallowed
    the write. Re-place the script component (gh_remove_node +
    gh_add_component) and retry.

# Sanity: 60 seeds → expect 50-60 cells (boundary clipping kills a
# few). Check cell count via gh_get_objects([bs_g]) → output count.

rhino_set_view(standard="Perspective")
rhino_capture_viewport(max_size=1000)
```

## Report

> Built a Voronoi tessellation screen on the host surface.
>
> Typology: tessellation (Voronoi variant) — "honeycomb" mapped to
> tessellation. Picked Voronoi for organic cell variation; for a
> *strict regular hex grid* say "make it regular hex" and I'll swap
> to the native `Hexagonal Cells` component (one node, no script).
>
> Sliders:
> - `[facade] seed_count` = 60 (10 – 400)
> - `[facade] seed_seed` = 42 (0 – 9999) — change to get a different
>   organic pattern at the same density
> - `[facade] cell_inset` = 0.08 (0 – 0.3) — joint reveal between cells
> - `[facade] cell_extrude` = 0.06 m (0 – 0.5)
>
> Judgment calls:
> - Used `gh_write_script_py3` for the Voronoi — the native Voronoi
>   component is harder to control on a parametric surface. The
>   script returns ~50-60 cells from 60 seeds (some are clipped at
>   the boundary).
> - The cell extrusion direction is currently fixed to +Y; on a
>   flat XZ-facing wall this is correct. For a curved host, ask
>   me to switch to per-cell surface normals.
>
> If `allow_scripting` were denied: I'd substitute the native
> `Voronoi` on a flat `Divide Surface` random point grid — flatter
> result, but fully graph-based.

## Strict-hex variant (the simpler default the skill could have picked)

If the prompt had said "regular honeycomb" specifically, the skill
should skip the script and use:

```
hex_g = gh_add_component("Hexagonal Cells", 700, 100)
gh_connect_components(surf_g, "", hex_g, "Surface")
gh_connect_components(u_count_g, "", hex_g, "U Count")
gh_connect_components(v_count_g, "", hex_g, "V Count")
```

(`Hexagonal Cells` is a LunchBox component. If LunchBox isn't
installed, fall back to the script + `cell_inset = 0` + seeds laid
out in a regular grid in UV. Note the fallback in the report.)

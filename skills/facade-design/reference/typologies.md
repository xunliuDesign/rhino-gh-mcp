# Typologies — Synonym Map, Defaults, Ranges, Recipes

Read this once to seed the inference. The rest of the time, look up the
row you need from the tables, apply, build, recompute.

## Synonym / intent → typology map

Walk the user's prompt and route the first word that hits.

| User says… | Typology id | Notes |
|---|---|---|
| louver, louvers, fin, fins, blade, blades, brise-soleil, sunshade, sun-shade, slat, slats, vertical fin, horizontal slat | `louvers` | Default sub-variant: horizontal slats unless the prompt says vertical |
| screen, perforated, perforation, punched, speckled, dotted, pattern (used in a holes sense), mashrabiya, moucharabieh, jali, lattice screen | `perforated-attractor` | "Lattice" near "screen" → here; "lattice" near "structure" → diagrid |
| honeycomb, cellular, cells, Voronoi, voronoi, organic cells, hex, hexagonal, Delaunay | `tessellation` | Script-escalation path |
| grid (used in panels sense), curtain wall, curtainwall, panels, panel grid, cladding, panelization, panelisation, tiles, tiled, modular | `panelized` | Default sub-variant: quad |
| diamond panels, diamond cladding, rhombus panels | `panelized` (diamond) | One row; the diamond defaults table row |
| diagrid, triagrid, triangulated mesh, structural lattice, web, structural web | `diagrid` | "Web" only when paired with structural language |
| folded, origami, faceted, faceting, kinked, pleated | `folded` | The fold-louver topology family |
| kinetic, responsive, operable, dynamic, movable, opening / closing | `kinetic` | Wraps another typology with a `state` slider |
| skinning, wrapping, envelope, cladding, clad, skin | depends — re-walk the rest of the prompt for the typology word; if nothing matches, default `panelized` | "Skin / wrap / clad" tells you it's a facade task; the typology comes from the other prompt words |

If multiple matches: take the most specific one. "A perforated diagrid"
→ `perforated-attractor` applied to a diagrid base — that's a compound,
build the diagrid first (panel cell = triangulated mesh), then layer the
perforation driver on. State the compound in the report.

If nothing matches but the prompt mentions facade, cladding, skin, or
wrap: pick `panelized` as the conservative default. It's the foundation
most other typologies build on, and it's the easiest to swap to a
different typology on a follow-up.

## Drivers — defaults & ranges, per typology

The tables below give the *minimum useful* set of slider drivers per
typology. Pick the row that matches the doc's `length_units`.

### 1. `panelized` / curtain-wall — quad / diamond / triangle panels

| Driver | mm default | mm range | m default | m range | ft default | ft range | Type |
|---|---|---|---|---|---|---|---|
| `u_count` | 8 | 2 – 40 | 8 | 2 – 40 | 8 | 2 – 40 | int |
| `v_count` | 5 | 2 – 30 | 5 | 2 – 30 | 5 | 2 – 30 | int |
| `inset` (joint reveal, per side) | 25 | 0 – 150 | 0.025 | 0 – 0.15 | 0.08 | 0 – 0.5 | float |
| `panel_thickness` (if extruded) | 30 | 5 – 200 | 0.030 | 0.005 – 0.2 | 0.1 | 0.02 – 0.7 | float |

Canonical chain:

```
Surface  ->  Divide Domain²(U=u_count, V=v_count)  ->  Isotrim  ->
Scale (about Area centroid by (1 - 2*inset/cell_size))  ->  preview
```

For diamond: same Isotrim grid → `Deconstruct Brep` → midpoint of each
edge → `PolyLine` (closed) → `Boundary Surfaces`.

For triangle: split each quad along the diagonal (two `PolyLine`
3-points per cell).

### 2. `louvers` / brise-soleil — strip-and-rotate

| Driver | mm default | mm range | m default | m range | ft default | ft range | Type |
|---|---|---|---|---|---|---|---|
| `fin_count` | 24 | 4 – 80 | 24 | 4 – 80 | 24 | 4 – 80 | int |
| `fin_depth` | 250 | 50 – 600 | 0.25 | 0.05 – 0.6 | 0.8 | 0.16 – 2.0 | float |
| `fin_angle_deg` | 25 | -45 – 60 | 25 | -45 – 60 | 25 | -45 – 60 | float |
| `fin_thickness` | 15 | 3 – 60 | 0.015 | 0.003 – 0.06 | 0.05 | 0.01 – 0.2 | float |

Sub-variants:
- **Horizontal slats** (default for "louver" / "brise-soleil" /
  "sunshade"): subdivide V (height) only. `Divide Domain²(U=1,
  V=fin_count)` → `Isotrim` → per-strip top edge → rotate the strip
  surface about that edge by `fin_angle_deg` → extrude outward by
  `fin_depth`. `Orientation (P) = XZ Plane` for the final `Extrude
  Linear` if you go that route.
- **Vertical fins** (when prompt says "vertical fin", "blade"):
  subdivide U only. `Divide Domain²(U=fin_count, V=1)` → `Isotrim` →
  per-strip vertical centerline → axis is `Unit Z` through that
  midpoint → rotate by `fin_angle_deg` → extrude along surface normal
  by `fin_depth`. `Orientation (P) = YZ Plane` for the final `Extrude
  Linear`.

Sun-responsive variant: replace the constant `fin_angle_deg` slider's
output with a per-strip angle list driven by either:
- Ladybug's `LB SunPath.vectors` (if installed) — `Angle(Vector1=
  surface normal at strip, Vector2=sun_vector)` → per-strip angle → wire
  into rotate.
- Manual proxy: `sun_azimuth_deg` (0–360, default 180) and
  `sun_altitude_deg` (0–90, default 30). Build the sun vector:
  `Unit Z(Factor=1)` → `Rotate Axis(Angle=Radians(sun_altitude_deg),
  Axis=Unit Y)` → `Rotate Axis(Angle=Radians(sun_azimuth_deg),
  Axis=Unit Z)` → `Negative` → "sun direction".

### 3. `perforated-attractor` — panel grid with variable hole size

| Driver | mm default | mm range | m default | m range | ft default | ft range | Type |
|---|---|---|---|---|---|---|---|
| `u_count` | 20 | 4 – 60 | 20 | 4 – 60 | 20 | 4 – 60 | int |
| `v_count` | 12 | 4 – 40 | 12 | 4 – 40 | 12 | 4 – 40 | int |
| `hole_min` (radius) | 20 | 5 – 200 | 0.02 | 0.005 – 0.2 | 0.06 | 0.015 – 0.7 | float |
| `hole_max` (radius) | 200 | 20 – 500 | 0.20 | 0.02 – 0.5 | 0.6 | 0.07 – 1.7 | float |
| `attractor_strength` | 1.0 | 0.1 – 5.0 | 1.0 | 0.1 – 5.0 | 1.0 | 0.1 – 5.0 | float |
| `attractor_pt_x` | half host width | bbox X bounds | — | — | — | — | float |
| `attractor_pt_y` | host front offset | -bbox / +bbox | — | — | — | — | float |
| `attractor_pt_z` | half host height | bbox Z bounds | — | — | — | — | float |

Constraint that must hold: `hole_max < min(cell_width, cell_height)` —
clamp at build time. The skill should compute `min_cell_dim =
min(bbox_width / u_count, bbox_height / v_count)` and call
`gh_set_slider_range(hole_max_guid, hole_min_default, 0.45 *
min_cell_dim)`.

Canonical chain:

```
Surface  ->  Divide Domain²  ->  Isotrim  ->  panel grid
panel  ->  Area  ->  centroid points (panel anchors)
attractor_point  ->  Distance  ->  panel anchors  ->  Bounds  ->  Remap
                                                       Numbers into
                                                       (hole_min,
                                                       hole_max)
panel anchor + remapped radius  ->  Circle  ->  per-panel hole curves
panel surface + hole curve  ->  Surface Split  ->  cull the inner half
                                                  -> perforated panels
```

For the simplest visualization, you can substitute `Surface Split` with
`Boundary Surfaces` of the outer rectangle minus the hole. For complex
hole shapes (Polygon, Star), swap `Circle` for the polygon component
with the same radius driver.

### 4. `diagrid` — triangulated structural mesh

| Driver | mm default | mm range | m default | m range | ft default | ft range | Type |
|---|---|---|---|---|---|---|---|
| `u_count` | 12 | 3 – 30 | 12 | 3 – 30 | 12 | 3 – 30 | int |
| `v_count` | 8 | 3 – 24 | 8 | 3 – 24 | 8 | 3 – 24 | int |
| `member_radius` | 60 | 10 – 200 | 0.06 | 0.01 – 0.2 | 0.2 | 0.03 – 0.7 | float |

Canonical chain:

```
Surface  ->  Divide Surface(U=u_count, V=v_count)  ->  point grid
point grid  ->  Mesh Triangulation (or Delaunay Mesh)  ->  triangulated
                                                          mesh
triangulated mesh  ->  Mesh Edges  ->  edge lines  ->  Pipe(radius=
                                                       member_radius)
                                                       ->  diagrid
                                                       members
```

Triangulation pattern: by default, `Mesh Triangulation` produces the
classic diagonal pattern. For an X-pattern (more equal diagonals), use
the script-escalation path; for native, accept the default and note in
the report.

### 5. `tessellation` (Voronoi / hex / Delaunay) — script-escalation

| Driver | default | range | Type |
|---|---|---|---|
| `seed_count` | 60 | 10 – 400 | int |
| `seed_seed` | 42 | 0 – 9999 | int |
| `cell_inset` (joint reveal factor) | 0.08 | 0 – 0.3 | float |
| `cell_extrude` | mm: 60 / m: 0.06 / ft: 0.2 | scaled | float |

Native `Voronoi` exists in Grasshopper for both 2D and 3D, but
controlling it on a parametric surface (not a flat domain) is fiddly.
The cleanest path is a Python 3 script that:

1. Reads the base Surface and a seed_count + seed_seed.
2. Samples seed_count random UVs in `(0,1) × (0,1)`.
3. Computes Voronoi on those UVs in 2D.
4. Maps each cell boundary back through the surface (`Surface.PointAt`
   per polyline vertex) → 3D boundary curves.
5. Insets each by `cell_inset` toward its centroid (uniform inward
   offset).
6. Returns a `DataTree[Curve]` of cell boundaries and a separate flat
   list of cell centroids for any downstream attractor drivers.

Wire the script output into `Boundary Surfaces` → optional `Extrude` by
`cell_extrude` for a relief tile.

Script skeleton (drop into a `gh_add_component("Python 3 Script")`
component and write via `gh_write_script_py3`):

```python
import Rhino.Geometry as rg
import random
import scriptcontext as sc

# inputs: surface (Surface), seed_count (int), seed_seed (int),
#         cell_inset (float)
# outputs: cells (Curve), centroids (Point3d)

random.seed(seed_seed)
uvs = [(random.random(), random.random()) for _ in range(seed_count)]

# Voronoi in UV space using a simple Bowyer-Watson is heavy; the
# pragmatic move is to use Rhino's Voronoi via the Grasshopper
# library — call sc.doc.ModelAbsoluteTolerance and a 2D Voronoi
# implementation. Below is a brute-force Lloyd-relaxed alternative
# that stays in pure Rhino.Geometry. For large seed_counts this is
# slow; for design-time counts (<400) it's fine.

# Bounding rectangle in UV
boundary = rg.Polyline([
    rg.Point3d(0,0,0), rg.Point3d(1,0,0),
    rg.Point3d(1,1,0), rg.Point3d(0,1,0), rg.Point3d(0,0,0)
]).ToNurbsCurve()

# Naive Voronoi: for each seed, half-plane intersect with neighbours
# (omitted here for brevity in the SKILL.md — the actual file written
# to a script component should implement it; for a faster path,
# delegate to Grasshopper's Voronoi component via a sibling node and
# let the script just produce seed points and do the inset).

cells = []
centroids = []
# ... populate cells (list of closed Curves in UV space), centroids
# (list of Point3d in UV space) ...

# Inset each cell toward its centroid
cells_inset = []
for cell, c in zip(cells, centroids):
    cell.DivideByCount(40, True)
    # build inset polyline manually
    pts = [cell.PointAt(t) for t in cell.DivideByCount(40, True)]
    inset_pts = [
        rg.Point3d(p.X + (c.X - p.X)*cell_inset,
                   p.Y + (c.Y - p.Y)*cell_inset, 0)
        for p in pts
    ]
    inset_pts.append(inset_pts[0])
    cells_inset.append(rg.PolylineCurve(inset_pts))

# Map back to surface
cells_3d = []
for cell in cells_inset:
    pts3d = []
    pts = cell.ToPolyline()
    for p in pts:
        sp = surface.PointAt(p.X, p.Y)
        pts3d.append(sp)
    cells_3d.append(rg.PolylineCurve(pts3d))

centroids_3d = [surface.PointAt(c.X, c.Y) for c in centroids]

cells = cells_3d
centroids = centroids_3d
```

If `allow_scripting` is denied at the canvas, substitute the simpler
native `Voronoi` on a `Divide Surface` point grid with random
displacement — flatter and less organic, but graph-only. Note the
fallback in the report.

### 6. `folded` / faceted — strip + edge-fold + Loft

| Driver | mm default | mm range | m default | m range | ft default | ft range | Type |
|---|---|---|---|---|---|---|---|
| `fold_count` (strips) | 14 | 4 – 40 | 14 | 4 – 40 | 14 | 4 – 40 | int |
| `fold_angle_deg` | 20 | 0 – 60 | 20 | 0 – 60 | 20 | 0 – 60 | float |
| `fold_axis` (vertical / horizontal) | "vertical" | enum | — | — | — | — | value-list |

Canonical chain (vertical strips, mid-edge fold — the classic
diamond-aperture screen): identical to the fold-louver recipe in
`../parametric-facade/references/louvers-and-fins.md` § Step 3.B. The
defaults table above seeds it. Read that reference for the full
component-by-component build; the skill's job here is to apply the
defaults and connect.

### 7. `kinetic` / responsive — `state` slider over another typology

`kinetic` is a wrapper, not a base typology. Pick the underlying family
from the prompt (usually `louvers` or `panelized`), build it, then:

| Driver | default | range | Type |
|---|---|---|---|
| `state` | 0.5 | 0 – 1 | float |
| (underlying typology's drivers) | (see above) | (see above) | (see above) |

Wire `state` as a global multiplier on the per-cell variation driver
(e.g. multiply `fin_angle_deg` by `state` for an opening/closing
louver, or `hole_max` by `state` for a panel screen that opens up).
Avoid native scalar `Multiplication` — use `Remap Numbers` into a
`(0, fin_angle_deg)` domain driven by `state` instead.

The `state` slider's name should be `[facade] state` so the user
sees it as the headline dial.

## Compound prompts

When the user names two typologies, build the base typology first, then
layer the second on:

- "Perforated diagrid" → diagrid mesh as base; per-cell perforation
  driver on triangular cells. The perforation table above applies, just
  with `hole_max < min(triangle edge length)`.
- "Folded louvers that open with the sun" → folded louver as base;
  kinetic `state` slider driven by sun angle (Ladybug if present, else
  proxy).
- "Voronoi screen with hole gradient" → tessellation as base;
  perforation driver inside each cell.

## Compound vs. confused prompts

If the prompt names two compatible typologies, build compound. If it
names two *incompatible* ones (e.g. "diagrid louvers" — louvers are
strips, diagrid is triangulated mesh, they don't compose), pick the
more-specific one (here: diagrid) and mention the choice in the report.
Don't ask.

## Unit selection rule

```
units = rhino_get_scene_info()["length_units"]
if units in ("mm", "Millimeters"):  use mm column
elif units in ("m", "Meters"):       use m column
elif units in ("ft", "Feet"):        use ft column
elif units in ("in", "Inches"):      multiply ft column by 12
else:                                use m column and flag in report
```

Pin the units assumption in the report so a unit mismatch is caught at
review time, not at fabrication time.

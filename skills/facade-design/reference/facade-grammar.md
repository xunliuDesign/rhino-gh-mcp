# Facade Construction Grammar

The compositional skeleton underlying almost every facade typology.
Use this when no canonical recipe matches the prompt — pick one option
from each step, wire them together, build.

The 7 named recipes in this skill are **presets** of this grammar.
Novel typologies are new compositions.

## The 5-step skeleton

```
[host surface(s)]
    1. PANELIZE   → cells (or strips, or points)
    2. SAMPLE A FIELD per cell → values (distance / angle / position / noise)
    3. TRANSFORM per cell using the values → modified cells
    4. COMBINE → planar regions / mesh edges / oriented module copies
    5. THICKEN → 3D output
```

A complete chain = one row chosen from each step. The result is a
parametric facade with sliders on every driver.

## Step 1 — PANELIZE

Subdivide the host into discrete units to operate on per-cell.

| Option | Components | When to use | Used in recipes |
|---|---|---|---|
| Quad grid | LunchBox `Quad Panels A` *(QuadA)* — or native `Divide Domain² + Isotrim` | Default regular panel grid | panelized, perforated (quad variant) |
| Triangle grid | LunchBox `Triangle Panels B` *(TriB)* | Alternating up/down triangles | panelized (triangle), perforated (triangle) |
| Hex grid | LunchBox `Hexagon Cells` *(HexCells)* | Honeycomb / cellular | panelized (hex), perforated (hex) |
| Diamond grid | LunchBox `Diamond` | Rotated square pattern | panelized (diamond) |
| Staggered (running-bond) | LunchBox `Staggered Quads` *(QuadStag)* | Brick-laid layouts | panelized (staggered), brick facade |
| Horizontal strips | `Divide Domain²(U=1, V=N) + Isotrim` | Louvers, slats, brise-soleil, cornices | louver (horizontal), folded (horizontal) |
| Vertical strips | `Divide Domain²(U=N, V=1) + Isotrim` | Vertical fins, blades | louver (vertical), folded (vertical) |
| UV point grid (not panels) | `Divide Surface` (outputs Points + Normals + uvP) | Diagrid members, point-driven patterns | diagrid |
| Random / Voronoi cells | `Python 3 Script` (script-escalation) | Variable-density tessellation | tessellation |

**Tree shape note**: most panelizers output a tree with one branch per
host. Within each branch, cells appear as a list. Downstream operations
need to match this tree shape (see `connection-rules.md` § tree
alignment).

## Step 2 — SAMPLE A FIELD per cell

Compute a scalar (or vector) value per cell from some field that
shapes the facade.

| Option | Components | What it measures | Used in recipes |
|---|---|---|---|
| Distance to attractor curve | `Area` (centroid) → `Pull Point(Geometry=curve)` → `Distance` | Per-cell distance to a Rhino curve; supports lists (min across curves) | perforated-attractor |
| Distance to attractor point | `Distance` (Point A=centroid, Point B=attractor) | Same idea with a point instead of a curve | perforated (point variant) |
| Distance clamped to a max | …→ `Minimum(distance, clamp)` | Caps far values so close-range gradient isn't squashed | perforated-attractor |
| Sun direction at cell | Ladybug `LB SunPath.vectors` or proxy: `Unit Z(altitude) → Rotate Axis(azimuth)` | Per-cell sun vector for solar-responsive louvers | louver (sun-responsive) |
| Cell position component | `Deconstruct Point(centroid)` → take X, Y, or Z | Gradient by absolute position (e.g. taller panels higher up) | (any height-driven variant) |
| Surface normal at cell | `Brep Closest Point(host, centroid)` → `Normal` | Used for per-cell extrude direction OR per-cell rotation | every recipe (extrude direction) |
| Noise / random | `Python 3 Script` with random seed; or `Random(seed)` for scalar | Stochastic patterns, brick-jitter, organic variation | (novel typologies) |
| Constant (no field) | (skip this step — pass slider directly into TRANSFORM) | Uniform-across-facade variation (just paneled, no gradient) | panelized base |

## Step 3 — TRANSFORM per cell using the value(s)

Apply the per-cell value to modify each cell's geometry.

| Option | Components | Effect | Used in recipes |
|---|---|---|---|
| Remap field → driver | `Bounds + Construct Domain + Remap Numbers` | Normalizes raw field values into a slider-controlled range. Reversed Construct Domain inverts (small distance → large output) | perforated-attractor, kinetic |
| Uniform scale about centroid | `Scale(Geometry=cell, Center=centroid, Factor=value)` | Cell shrinks/grows uniformly | perforated-attractor, panelized (inset) |
| Non-uniform scale | `Scale NU(X, Y, Z)` | Anisotropic scaling — different in U vs V | (rare; folded patterns) |
| Rotate about an axis | `Rotate Axis(Geometry=cell, Axis=edge_line, Angle=value)` | Strip / panel rotation around an edge or centerline | louver, kinetic louver |
| Offset along normal | `Amplitude(normal, value)` → `Move` | Fold, push out, brick offset | folded, brick-jitter facades |
| Build a closed curve (polygon, circle) at centroid | `Polygon(plane, radius, segments)` / `Circle(plane, radius)` | The **invented-shape anti-pattern** — only legitimate when shape SHOULD differ from cell (e.g. round holes in triangle panels — uncommon) | (avoid by default — use scaled cell instead) |

**Anti-pattern reminder**: for perforated facades, never use `Polygon`
for the hole shape — use a scaled copy of the cell itself. See
`perforated-attractor/recipe.md` § Anti-patterns.

## Step 4 — COMBINE

Assemble the transformed cells into a planar region, mesh, or module set.

| Option | Components | Output | Used in recipes |
|---|---|---|---|
| Planar region with holes | `Boundary Surfaces(Edges=[outer curve, inner scaled curve, ...])` | One planar Brep per cell with a hole. **Replaces `Surface Split + Sort + List Item` (anti-pattern)** | perforated-attractor |
| Loft between edges | `Loft(Curves=[original_edge, offset_edge])` | Faceted strip surface | folded |
| Mesh from points | `Mesh Triangulation(Points)` or `Delaunay Mesh` | Triangulated mesh | diagrid, tessellation |
| Mesh edges | `Mesh Edges` | Curves per triangle edge | diagrid |
| Orient module to target planes | `Orient(G=module, A=base_plane, B=target_planes)` | Copies a module onto each cell's frame | tile-module-on-host |
| Region difference (2D) | `Region Difference(A=outer, B=inner)` then `Boundary Surfaces` | Alternative to Boundary Surfaces' multi-curve trick | (rare alternative) |

## Step 5 — THICKEN

Turn the planar 2D output into a 3D wall, member, or volumetric form.

| Option | Components | When to use | Notes |
|---|---|---|---|
| Extrude along a vector | `Extrude(Base=region, Direction=normal × thickness)` (legacy — name `"Extr"`) | Default for planar perforated panels, panelized walls | Always use host-aware normal per `extrude-direction.md` |
| Extrude along an axis line | `Extrude Linear(Profile, Axis=line)` | When you need a Line-based axis with orientation control | Trickier wiring — see `bridge-quirks.md` |
| Pipe along curves | `Pipe(Curve, Radius)` | For diagrid members, lattice rods | Single radius slider or per-edge attractor-driven |
| Cap planar holes | `Cap Holes` / `Brep.CapPlanarHoles` | Closes open-ended extrusions into solids | When boolean ops are downstream |
| Boolean difference (volumetric) | `Solid Difference(A, B)` | When you want a 3D punching tool to cut through a wall | Heavy; prefer Boundary Surfaces 2D approach |

## Worked composition examples

**Perforated-attractor (triangular)** is the canonical pick from each step:
- PANELIZE: Triangle grid (`TriB`)
- SAMPLE: Distance to attractor curve, clamped (`Pull Point → Minimum`)
- TRANSFORM: Remap → Uniform scale of cell about centroid
- COMBINE: Boundary Surfaces (outer + scaled inner)
- THICKEN: Extrude along per-cell normal × thickness

**Hypothetical fish-scale shingle facade**:
- PANELIZE: Staggered quads (`QuadStag`) for the brick-bond layout
- SAMPLE: (skip — uniform pattern)
- TRANSFORM: Per-cell arc curve through 3 points on the cell edges
  (need a `Python 3 Script` for the arc construction, OR `Arc 3Pt`
  with corner points)
- COMBINE: Loft each arc into a small shell, OR `Boundary Surfaces`
  on the arc + cell edge
- THICKEN: Offset each shell outward by shingle thickness

That's a typology that doesn't have a canonical recipe but can be
described from the grammar in one paragraph. The LLM should be able to
build it from this description plus the component options above.

**Hypothetical noise-driven brick offset facade**:
- PANELIZE: Staggered quads
- SAMPLE: Noise per cell centroid (Python 3 Script with Perlin or
  random seed)
- TRANSFORM: Move each cell outward along normal by `Amplitude(normal,
  noise_value × max_offset)`
- COMBINE: (skip — cells are already 3D-positioned discrete units)
- THICKEN: Extrude each cell by brick depth along normal

Same skeleton, different picks, different result.

## When the grammar doesn't cover it

If you can't express the prompt as picks from steps 1–5, you're in
genuinely novel territory. Drop to `novel-typology-protocol.md`
(prototype-and-iterate) and capture the result as a new recipe if
successful.

## Cross-references

- Canonical recipes that use this grammar: `perforated-attractor/`,
  `louver/`, `panelized/`, `diagrid/`, `tessellation/`, `folded/`,
  `kinetic/`
- Recipe modification patterns (closest recipe + swap): `recipe-modification-patterns.md`
- Novel-typology fallback: `novel-typology-protocol.md`
- Bridge component-name traps: `bridge-quirks.md`
- Extrude direction patterns: `extrude-direction.md`
- Universal rules (multi-host = one graph, named waypoints, scale
  factors not radii): `perforated-attractor/recipe.md`

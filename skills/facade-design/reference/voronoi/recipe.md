# Voronoi — Recipe

> **Source canvas:** `Voronoi.gh` (this folder).
> **Status:** validated (transcribed by structural inspection 2026-06-23).
>
> **Two parallel variants on one canvas.** Per skill author's note 4,
> this typology must be documented as two distinct variants — the
> single `Voronoi.gh` canvas authors both side-by-side:
>
> 1. **Variant A — Regular Voronoi**: every Voronoi cell from the host
>    rectangle is kept, including the perimeter cells that
>    `Planar Voronoi` clipped against the rectangle boundary. The
>    silhouette is therefore the rectangle itself, with the boundary
>    cells showing visibly clipped (straight-along-the-rectangle) edges.
>    Renders: `Voronoi (Arctic).png`, `Voronoi (Ghosted).png`.
> 2. **Variant B — Free-Edge Voronoi**: an extra
>    `Curve | Curve → List Length → Dispatch` filter removes every
>    cell whose curve actually intersects the rectangle boundary, so
>    only the **fully-interior** cells (their natural Voronoi polygons,
>    untouched by the clip) survive. The silhouette is therefore
>    ragged / organic — there is no rectangular outline — because the
>    perimeter cells have been discarded. Renders:
>    `Voronoi Free Edge (Arctic).png`,
>    `Voronoi Free Edge (Ghosted).png`.
>
> **Both variants share the same chain idiom** (Rect → Surface →
> Populate 3D → Planar Voronoi[Boundary=host edges] → cell + offset →
> two-stream Boundary Surfaces → Extrude). They are authored as two
> physically-independent chains (no wires cross between them); the
> seed-distribution and Voronoi-generation upstream is **conceptually
> shared but instantiated twice**. Variant B simply inserts three
> extra components (`Curve | Curve`, `List Length`, `Dispatch`) between
> `Planar Voronoi.Cells` and the downstream `Param_Curve` waypoint.
>
> **No script components.** Despite this being the SKILL's named
> *script-escalation typology*, the reference canvas demonstrates the
> **fully native** Voronoi chain — `Component_PlanarVoronoi` does the
> generative work that would otherwise require Python. The
> placeholder's "Python 3 Script" framing applies only to cases where
> native PlanarVoronoi can't handle the input (graded density,
> non-planar / curved host, UV-space Voronoi on a NURBS surface). For
> a planar host, this native chain IS the recipe — no scripting
> required.

## What it builds

A planar rectangular host, populated with random points, tessellated
into Voronoi cells, each cell turned into a planar perforated ring
(cell outer curve + inward-offset inner curve as a hole), then
extruded along +Z by a thickness slider. The two variants differ in
which cells survive: Variant A keeps all of them (rectangle
silhouette, perimeter cells visibly truncated); Variant B keeps only
the interior cells (organic silhouette, no rectangle outline).

This is the **tessellation foundation chain** — the typology that
visually matches the "graded triangle/polygon mesh" target image (see
the user-shared target image in session 2026-06-17). Where
perforated-attractor produces uniform-shape cells with variable holes,
Voronoi produces variable-shape cells with uniform-fraction holes (the
offset distance is constant; only cell *shape* varies).

## Variant A — Regular Voronoi (cells clipped to host)

### The 5 stages

```
01. Rectangle (host) ──→ 02. Populate 3D (seeds) ──→ 03. Planar Voronoi (with Boundary)
        │                                                  │
        └──→ Brep Edges → Join Curves ──→ Voronoi.Boundary │
                                                           ↓
                                                      Cells (closed curves, clipped)
                                                           ↓
                       04. Per-cell offset → two-stream Boundary Surfaces (cell + offset)
                                                           ↓
                                              05. Extrude (Unit Z × depth)
```

### Components (exact kinds)

| Stage | Component | GUID (Variant A) | Kind | Notes |
|---|---|---|---|---|
| 01 | `Rectangle` | `56a1e66b` | `Component_Rectangle` | Demo host. Replace with `Param_Brep` / `Param_Surface` for production |
| 01 | `Surface` (param) | `48823fc4` | `Param_Surface` | Implicit Curve → Surface (same idiom as panelized recipe) |
| 01 | `Brep Edges` | `249c2e40` | `Component_BRepEdges` | `.Naked` output = the rectangle's four edges as a list |
| 01 | `Join Curves` | `72d936fe` | `Component_JoinCurves` | Joins the four edges into one closed curve → feeds `Voronoi.Boundary` |
| 02 | `Populate 3D` | `3e96e6e4` | `Component_PopulateBox` | Random point seeding inside the surface's bounding region. **Item access; no density weighting** — the SKILL's graded-density variant requires the script-escalation override |
| 03 | `Planar Voronoi` | `bb1ff5a9` | `Component_PlanarVoronoi` | Native Voronoi on the seed points, clipped to `Boundary`. **`.Cells` = closed planar curves, one per seed point.** Cells whose natural Voronoi polygon extended past the boundary get *clipped* — their boundary segment becomes straight along the rectangle edge |
| 03 | `Curve` (waypoint) | `e7be8903` | `Param_Curve` | Named waypoint — `Voronoi.Cells` direct passthrough |
| 04 | `Offset Curve Loose` | `17e35845` | `Component_OffsetCurveLoose` | Offsets each cell curve inward by `Distance`. **`Loose` variant** (not `Offset Curve`) because cell curves are polylines — loose offset handles polyline corners without producing arc fillets |
| 04 | `Boundary Surfaces` (offset → surfaces) | `74e81a23` | `Component_BoundarySurfaces` | Converts the offset curves to planar Breps — used as the *inner* shape source via the sort/pick step |
| 04 | `Area` | `724f7b8a` | `Component_AreaProperties` | Reads area of each offset Brep — used as the sort key |
| 04 | `Sort List` | `d982b8b5` | `Component_SortList` | Per-branch sort of the offset Breps by area |
| 04 | `List Item` | `b388baa2` | `Component_ListItemVariable` | Per-branch `i=0` pick — selects the single inner Brep per cell (handles degenerate self-intersecting offsets that would otherwise produce multiple sub-surfaces) |
| 04 | `Curve` (waypoint) | `cba042cd` | `Param_Curve` | Named waypoint — the picked inner Brep, fed as second edge stream into the cell-and-hole Boundary Surfaces |
| 04 | `Boundary Surfaces` (cell + hole) | `c57a920b` | `Component_BoundarySurfaces` | **The differencing step.** Takes the original cell curve + the picked inner Brep as nested closed inputs → emits a planar Brep with a hole (same "Boundary Surfaces trick" as perforated-attractor) |
| 05 | `Unit Z` | `47ec4c69` | `Component_UnitVectorZ` | Extrude direction = (0,0,1) × Factor |
| 05 | `Extrude` (legacy) | `d1643d6c` | `Component_Extrude` | Legacy `Component_Extrude` taking a `Vector` directly. **NOT** `Extrude Linear` — see [`../bridge-quirks.md`](../bridge-quirks.md) § Component name traps |

### Wiring (Variant A)

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 1 | `Rect_X_A` slider (=10, range 0–10, int) | → | `Rectangle 56a1e66b.X Size` | |
| 2 | `Rect_Y_A` slider (=5, range 0–10, int) | → | `Rectangle 56a1e66b.Y Size` | Anisotropic — host is 10×5 |
| 3 | `Rectangle 56a1e66b.Rectangle` | → | `Surface 48823fc4` | Implicit Curve → Surface |
| 4 | `Surface 48823fc4` | → | `Populate 3D 3e96e6e4.Region` | Surface defines the seed region |
| 5 | `Surface 48823fc4` | → | `Brep Edges 249c2e40.Brep` | Extract perimeter |
| 6 | `Cell Amount` slider `7a828230` (=30, range 0–110, int) | → | `Populate 3D 3e96e6e4.Count` | Seed count |
| 7 | `Populate 3D 3e96e6e4.Population` | → | `Planar Voronoi bb1ff5a9.Points` | Seeds |
| 8 | `Brep Edges 249c2e40.Naked` | → | `Join Curves 72d936fe.Curves` | Four edges → one closed curve |
| 9 | `Join Curves 72d936fe.Curves` | → | `Planar Voronoi bb1ff5a9.Boundary` | Clip target |
| 10 | `Planar Voronoi bb1ff5a9.Cells` | → | `Curve` waypoint `e7be8903` | Named waypoint |
| 11 | `Curve` waypoint `e7be8903` | → | `Boundary Surfaces c57a920b.Edges` | **First** edge stream (outer cell curves) |
| 12 | `Curve` waypoint `e7be8903` | → | `Offset Curve Loose 17e35845.Curve` | |
| 13 | `Offset_A` slider `6d26aacb` (=0.158, range 0–1, float) | → | `Offset Curve Loose 17e35845.Distance` | Inward offset distance |
| 14 | `Offset Curve Loose 17e35845.Curve` | → | `Boundary Surfaces 74e81a23.Edges` | Offset curves → planar Breps |
| 15 | `Boundary Surfaces 74e81a23.Surfaces` | → | `Area 724f7b8a.Geometry` | |
| 16 | `Boundary Surfaces 74e81a23.Surfaces` | → | `Sort List d982b8b5.Values A` | |
| 17 | `Area 724f7b8a.Area` | → | `Sort List d982b8b5.Keys` | Sort key = area |
| 18 | `Sort List d982b8b5.Values A` | → | `List Item b388baa2.List` | |
| 19 | `List Item b388baa2.i` | → | `Curve` waypoint `cba042cd` | Picks index 0 per branch (default) |
| 20 | `Curve` waypoint `cba042cd` | → | `Boundary Surfaces c57a920b.Edges` | **Second** edge stream (inner hole shape) — appended |
| 21 | `Boundary Surfaces c57a920b.Surfaces` | → | `Extrude d1643d6c.Base` | Cell-with-hole Breps |
| 22 | `Depth_A` slider `ddb5eefc` (=0.5, range 0–1, float) | → | `Unit Z 47ec4c69.Factor` | Thickness |
| 23 | `Unit Z 47ec4c69.Unit vector` | → | `Extrude d1643d6c.Direction` | (0,0,1) × 0.5 |

### Sliders (Variant A, as authored)

| Slider | GUID | Value | Min | Max | Type | Drives |
|---|---|---|---|---|---|---|
| `Rect X` | `77207eb7` | 10 | 0 | 10 | int | `Rectangle 56a1e66b.X Size` |
| `Rect Y` | `f835f966` | 5 | 0 | 10 | int | `Rectangle 56a1e66b.Y Size` |
| `Cell Amount` | `7a828230` | 30 | 0 | 110 | int | `Populate 3D 3e96e6e4.Count` |
| `Offset distance` | `6d26aacb` | 0.158 | 0 | 1 | float | `Offset Curve Loose 17e35845.Distance` |
| `Extrude depth` | `ddb5eefc` | 0.5 | 0 | 1 | float | `Unit Z 47ec4c69.Factor` |

## Variant B — Free-Edge Voronoi (interior cells only, ragged silhouette)

Variant B's chain is **identical to Variant A** except for one
divergence: between `Planar Voronoi.Cells` and the `Param_Curve`
waypoint, three extra components filter out boundary-touching cells.
Everything upstream (Rectangle / Populate / Planar Voronoi) and
everything downstream (Offset / Boundary Surfaces / Extrude) is the
same idiom — separate instances, identical wiring pattern.

### The divergence — Curve | Curve → List Length → Dispatch

```
Planar Voronoi 1269eb46.Cells ──┬──→ Curve | Curve ee1d92e7.Curve B
                                │              ↑
       (Join Curves 1c909a7c) ──┴──→ Curve | Curve ee1d92e7.Curve A
                                                 ↓
                                            .Points (tree — intersection points per cell)
                                                 ↓
                                          List Length 99e2662b.Length
                                                 ↓
                                          Dispatch 2715c7a0.Dispatch pattern
                                                 ↑
       Planar Voronoi 1269eb46.Cells ────────────┴──→ Dispatch 2715c7a0.List
                                                 ↓
                                          .List B (cells with 0 intersections — interior only)
                                                 ↓
                                          (continues into Curve waypoint 5dbe0971
                                           — same as Variant A's e7be8903)
```

**How the filter works.** `Curve | Curve` (`Component_CurveIntersection`)
tests every Voronoi cell curve against the rectangle's joined-edge
boundary curve. For each cell, the `.Points` output is a tree branch
holding the intersection points (empty if the cell is fully interior,
non-empty if its boundary segment crosses or touches the rectangle).
`List Length` per-branch yields the count: `0` for interior cells, `≥1`
for boundary-touching cells. `Dispatch` coerces that count to boolean:
non-zero → True → `List A` (boundary cells, **discarded** — `List A`
has no downstream wires); zero → False → `List B` (interior cells,
**kept**). The kept cells are the ones whose natural Voronoi polygon
fits entirely inside the rectangle and was therefore *not clipped* by
`Planar Voronoi.Boundary`.

The visual consequence: every kept cell has its natural-Voronoi edges
(no straight rectangle-aligned segments). The silhouette of the
extruded assembly is therefore the irregular outer envelope formed by
those interior cells — ragged, organic, no rectangular outline. This
is the "free-edge" reading: each kept cell's *every* edge is a free
Voronoi edge, not a clipped one.

### Components (Variant B)

Same kinds as Variant A, separate instances. Only the three filter
components are unique to Variant B:

| Stage | Component | GUID (Variant B) | Kind | Notes |
|---|---|---|---|---|
| 01 | `Rectangle` | `455a1633` | `Component_Rectangle` | Demo host. **10×8** (different from Variant A's 10×5) |
| 01 | `Surface` | `b49e6395` | `Param_Surface` | |
| 01 | `Brep Edges` | `edbb2f0a` | `Component_BRepEdges` | |
| 01 | `Join Curves` | `1c909a7c` | `Component_JoinCurves` | Closed boundary for both PlanarVoronoi *and* CurveIntersection |
| 02 | `Populate 3D` | `e9bf680c` | `Component_PopulateBox` | |
| 03 | `Planar Voronoi` | `1269eb46` | `Component_PlanarVoronoi` | |
| **03b** | **`Curve | Curve`** | **`ee1d92e7`** | **`Component_CurveIntersection`** | **Unique to Variant B.** Tests each cell against the boundary curve |
| **03b** | **`List Length`** | **`99e2662b`** | **`Component_ListLength`** | **Unique to Variant B.** Per-branch count of intersection points |
| **03b** | **`Dispatch`** | **`2715c7a0`** | **`Component_Dispatch`** | **Unique to Variant B.** `List B` (zero-intersection cells) = kept; `List A` discarded |
| 03 | `Curve` (waypoint) | `5dbe0971` | `Param_Curve` | Receives `Dispatch.List B` (interior cells only) |
| 04 | `Offset Curve Loose` | `a74b6ac7` | `Component_OffsetCurveLoose` | |
| 04 | `Boundary Surfaces` (offset → surfaces) | `741322f7` | `Component_BoundarySurfaces` | |
| 04 | `Area` | `9d54b6d4` | `Component_AreaProperties` | |
| 04 | `Sort List` | `d6a671ea` | `Component_SortList` | |
| 04 | `List Item` | `e4fa71ec` | `Component_ListItemVariable` | |
| 04 | `Curve` (waypoint) | `d567b528` | `Param_Curve` | |
| 04 | `Boundary Surfaces` (cell + hole) | `9ad8159a` | `Component_BoundarySurfaces` | |
| 05 | `Unit Z` | `a701bb8e` | `Component_UnitVectorZ` | |
| 05 | `Extrude` | `3b447175` | `Component_Extrude` | |

### Wiring (Variant B — only differences from A)

Wires 1–9 (Rectangle → Surface → Brep Edges/Populate → Join Curves →
PlanarVoronoi) are structurally identical to Variant A, just on the B
instances. The new wires:

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 10a | `Planar Voronoi 1269eb46.Cells` | → | `Curve | Curve ee1d92e7.Curve B` | Cell curves as test set |
| 10b | `Join Curves 1c909a7c.Curves` | → | `Curve | Curve ee1d92e7.Curve A` | Rectangle boundary as reference |
| 10c | `Planar Voronoi 1269eb46.Cells` | → | `Dispatch 2715c7a0.List` | Cells as dispatch payload |
| 10d | `Curve | Curve ee1d92e7.Points` | → | `List Length 99e2662b.List` | Intersection points per cell |
| 10e | `List Length 99e2662b.Length` | → | `Dispatch 2715c7a0.Dispatch pattern` | Pattern: 0 → List B, ≥1 → List A |
| 10f | `Dispatch 2715c7a0.List B` | → | `Curve` waypoint `5dbe0971` | **Interior cells only** continue into Stage 04 |
| 10g | `Dispatch 2715c7a0.List A` | → | *(unwired — boundary cells discarded)* | |

From wire 11 onward (Curve waypoint → Offset → Boundary Surfaces → Extrude),
Variant B mirrors Variant A's wiring 11–23 on its own component instances.

### Sliders (Variant B, as authored)

| Slider | GUID | Value | Min | Max | Type | Drives |
|---|---|---|---|---|---|---|
| `Rect X` | `6f4b00f8` | 10 | 0 | 10 | int | `Rectangle 455a1633.X Size` |
| `Rect Y` | `7e55b553` | 8 | 0 | 10 | int | `Rectangle 455a1633.Y Size` |
| `Cell Amount` | `1c7dcd7e` | 43 | 0 | 110 | int | `Populate 3D e9bf680c.Count`. **Higher than Variant A's 30** — compensates for boundary cells being discarded by Dispatch |
| `Offset distance` | `8f584871` | 0.2 | 0 | 1 | float | `Offset Curve Loose a74b6ac7.Distance` |
| `Extrude depth` | `1c5618fe` | 0.5 | 0 | 1 | float | `Unit Z a701bb8e.Factor` |

## The Boundary Surfaces trick (same as perforated-attractor)

The two-stream `Boundary Surfaces.Edges` input is the same idiom
named in `../perforated-attractor/recipe.md` § "The Boundary Surfaces
trick". When given multiple closed curves per branch — outer cell
curve + inner offset curve — `Boundary Surfaces` interprets the outer
as the panel boundary and the inner as a hole, producing a single
planar Brep with a hole. **No `Surface Split` needed.** The
intermediate sort/pick (Area → Sort List → List Item i=0) handles
the degenerate case where `Offset Curve Loose` produces self-
intersecting curves for thin cells — those would otherwise yield
multiple sub-surfaces from `Boundary Surfaces`, and the pick selects
the dominant one per branch.

This is the same **cell-offset-from-panel** idiom catalogued in
`../recipe-modification-patterns.md` § Canonical patterns. Voronoi
cells are just irregular polygons; the per-cell hole-from-offset
chain is identical to what panelized's joint-reveal variant and
perforated-attractor's hole-from-scale both use.

## Extrude direction — host-normal generalization

Both variants use `Unit Z` × thickness because the demo Rectangles
lie on World XY (their normals ARE +Z). This is **Pattern A**
(axis-aligned host) from `../extrude-direction.md`.

When transcribing this recipe for a non-XY host, **read what's wired
to `Extrude.Direction`** — do not assume Unit Z (per skill author's
note 1 and the authoring callout at the top of
`../extrude-direction.md`). For host orientations:

| Host orientation | Replace `Unit Z` with | Pattern |
|---|---|---|
| Flat on XY (this canvas) | `Unit Z(Factor=depth)` | A |
| Vertical wall facing +X | `Unit X(Factor=depth)` | A |
| Oblique single host | `Evaluate Surface → Frame → Deconstruct Plane → Z-Axis → Amplitude(depth)` | B |
| Multi-host / curved host | `Brep Closest Point → Normal → Amplitude(depth)` (per cell) | C |

See `../extrude-direction.md` for the full diagrams.

## Host-ingest variant (production replacement for stage 01)

The canvas authors a placeholder rectangular base from sliders. For
production (apply Voronoi tessellation to an existing Rhino building
face), replace stage 01:

```
[Rhino host Brep] (GUID)
        ↓
Param_Brep (persistent data = GUID via gh_set_component_parameter)
        ↓
Deconstruct Brep → Faces → List Item(0)  (extract the face you want)
        ↓
Brep Edges → Join Curves               (boundary for PlanarVoronoi)
        ↓                              ↓
   .Naked                       Planar Voronoi.Boundary
        ↓                              ↑
Param_Surface ──→ Populate 3D.Region   |
                       ↓                |
                  Voronoi.Points        |
                       ↓                |
                  Planar Voronoi  ←─────┘
```

See `../geometry-ingest.md` § GUID handoff for the param-mode and
script-mode variants of the host reference.

## When to use Variant A vs Variant B

| Goal | Variant | Reason |
|---|---|---|
| Facade panel that fits inside a defined rectangular wall opening | **A** | Rectangular silhouette matches the opening; clipped perimeter cells are acceptable / desirable |
| Sculptural / organic screen that "tears" away from a rectangular host | **B** | Ragged interior-only silhouette reads as a torn / fragmented edge condition |
| Tessellation pattern where the rectangle is irrelevant and only natural-Voronoi cells should appear | **B** | Same — discarding boundary-touching cells removes the rectangle from the read |
| Material-efficient build (more cells per unit area, no gaps in the rectangle) | **A** | All cells survive; coverage = full rectangle |

The two variants are not "alternatives in a slider sense" — they
produce structurally different geometry families. Pick based on the
desired silhouette, not on a continuous parameter.

## Variant: graded-density / curved-host (script-escalation)

This is where the SKILL's *script-escalation typology* tag applies.
The native `Component_PlanarVoronoi` requires:
- **Planar input** (the `Boundary` is a closed planar curve)
- **Uniform density** (the seed distribution from `Populate 3D` is
  uniformly random over the bounding region)

For:
- A **curved host** (a NURBS surface where the panels need to wrap),
  *or*
- An **attractor-graded density** (smaller cells near a feature curve
  / point, larger cells far away),

native components don't suffice. Drop to a Python 3 Script
(`gh_write_script_py3`) implementing:

```python
# inputs: surface (Surface), attractor (Curve or Point, optional),
#         seed_count (int), seed_seed (int), cell_inset (float, 0..0.5),
#         density_strength (float, 0..5 — 0 = uniform)
# outputs: cells (DataTree[Curve]), centroids (DataTree[Point3d])

import Rhino.Geometry as rg
import random

random.seed(seed_seed)

# 1. Sample UV seeds, rejection-sampled against an inverse-distance
#    density field if density_strength > 0
uvs = []
while len(uvs) < seed_count:
    u, v = random.random(), random.random()
    if density_strength > 0 and attractor is not None:
        pt3d = surface.PointAt(u, v)
        d = pt3d.DistanceTo(closest_on_attractor(attractor, pt3d))
        prob = 1.0 / (1.0 + d * density_strength)
        if random.random() > prob:
            continue
    uvs.append((u, v))

# 2. Compute 2D Voronoi on the UV samples (Bowyer-Watson or similar)
voronoi_cells_uv = compute_voronoi_uv(uvs)  # algorithm — see Rhino.Geometry samples

# 3. Optional inward inset per cell (via cell_inset)
inset_cells_uv = [offset_inward(c, cell_inset) for c in voronoi_cells_uv]

# 4. Map each UV polyline back to 3D via surface.PointAt(u, v)
cells = []
for cell in inset_cells_uv:
    pts_3d = [surface.PointAt(p.X, p.Y) for p in cell.ToPolyline()]
    cells.append(rg.PolylineCurve(pts_3d))
centroids = [surface.PointAt(c.X, c.Y) for c in [cell_centroid(c) for c in inset_cells_uv]]
```

The native chain in this canvas IS the recipe for planar / uniform
cases — the script is the fallback for the two cases above.

For the **density-graded variant**, the SKILL's typology mapping puts
this under perforated-attractor's "field source swap" pattern (per
`../recipe-modification-patterns.md` § Field source swaps): instead of
Voronoi cells with constant offset, run perforated-attractor's chain
with `Triangle Panels B` swapped to whichever panelizer matches.
Native Voronoi + attractor-graded *count* is a misfit — use the
attractor recipe on regular panels instead.

## Multi-surface batching

`Component_PlanarVoronoi` accepts one boundary at a time (item access
on `Boundary`). For N hosts, build the chain ONCE on a list of
boundaries; `Planar Voronoi` will produce one tree branch per host.

```
[List of N host face GUIDs] (one read via rhino_get_objects_with_metadata)
        ↓
Param_Brep (persistent data = list of N GUIDs)
        ↓
Deconstruct Brep → Faces → List Item(0)   (per host)
        ↓
Brep Edges → Join Curves                  (per host — tree)
        ↓
Planar Voronoi.Boundary (tree, one branch per host)
        ↑
Populate 3D.Population (one seed cluster per host — branch-aligned)
        ↓
Voronoi.Cells (tree — one branch per host, N cells per branch)
        ↓
(rest of chain — tree-aware)
```

See `../perforated-attractor/recipe.md` § Multi-surface batching for
the persistent-data caveats — same bridge limitations apply
(multi-GUID Brep ref via persistent data is currently broken on
v0.2.4; use Merge Multiple of N single-GUID refs, or Geometry
Pipeline by layer).

For Variant B (the interior-cell filter), the
`Curve | Curve → List Length → Dispatch` chain is also tree-aware —
each branch's cells get tested against that branch's boundary, and
`Dispatch.List B` returns one branch of interior cells per host.

## Anti-patterns

| Anti-pattern | Why wrong | Use instead |
|---|---|---|
| `Offset Curve` (non-loose) for cell-to-hole offset | Voronoi cells are polylines; `Offset Curve` produces arc fillets at corners → hole shape no longer matches cell shape | `Offset Curve Loose` (`Component_OffsetCurveLoose`) — preserves polyline corners |
| Skipping `Brep Edges → Join Curves` and feeding `Rectangle.Rectangle` straight into `Planar Voronoi.Boundary` | Works for a Rectangle (already closed), but fails the moment you swap to a multi-segment host where `Brep Edges.Naked` returns 4+ separate edges | Always go through Brep Edges + Join Curves — same idiom works for both rectangles and arbitrary planar hosts |
| Hard-coded `Unit Z` extrude for non-XY hosts | Extrudes Voronoi cells straight up instead of outward through the wall | Read what's wired on the canvas; for non-XY hosts use Pattern A/B/C in `../extrude-direction.md` |
| Treating Variant A and Variant B as "the same recipe with a slider" | They differ by component topology (three filter components present or absent), not slider values | Pick one variant at design time; document the choice in the report |
| `Curve Closest Point` to test cell-in-boundary | Returns a distance, not an intersection count — can't distinguish "touching the boundary" from "inside but close to it" | `Curve | Curve` (`Component_CurveIntersection`) → `List Length` → Dispatch — counts actual intersection points |
| Wiring `Dispatch.List A` instead of `List B` for the free-edge filter | `List A` keeps boundary cells (discarding interior) — the inverse of Variant B; produces a ring of perimeter cells with a hole in the middle, not a clean interior tessellation | `Dispatch.List B` — keeps interior cells (zero-intersection branches) |
| Using Python 3 script for a planar uniform-density build | Script-escalation is reserved for *curved hosts* or *density-graded* Voronoi (per the SKILL's escalation triggers). Native `Component_PlanarVoronoi` handles the planar uniform case at full performance | Use the native chain above; reserve scripting for the variant cases in § "Variant: graded-density / curved-host" |
| `Voronoi` (native, 3D Voronoi on a populated box) instead of `Planar Voronoi` | 3D Voronoi produces volumetric Voronoi regions — not 2D cells | `Planar Voronoi` (`Component_PlanarVoronoi`) — 2D cells on a closed planar boundary |

## Fallback when scripting denied

This recipe's native chain works under **all** capability scopes
(`AllowScripting` is not required) — `Component_PlanarVoronoi` is a
standard component. The only sub-variant that needs scripting is the
graded-density / curved-host fallback above; for the canvas's actual
authored variants (A and B), `AllowComponents = True` is sufficient.

## Cross-references

- Source canvas: [`Voronoi.gh`](./Voronoi.gh)
- Reference renders:
  - Variant A: [`Voronoi (Arctic).png`](./Voronoi%20%28Arctic%29.png), [`Voronoi (Ghosted).png`](./Voronoi%20%28Ghosted%29.png)
  - Variant B: [`Voronoi Free Edge (Arctic).png`](./Voronoi%20Free%20Edge%20%28Arctic%29.png), [`Voronoi Free Edge (Ghosted).png`](./Voronoi%20Free%20Edge%20%28Ghosted%29.png)
- Boundary Surfaces trick (shared with perforated-attractor): [`../perforated-attractor/recipe.md`](../perforated-attractor/recipe.md) § The Boundary Surfaces trick
- Cell-offset-from-panel pattern: [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Canonical patterns
- Extrude direction (host-aware): [`../extrude-direction.md`](../extrude-direction.md)
- Host ingest (production stage 01): [`../geometry-ingest.md`](../geometry-ingest.md)
- Component name traps (Extrude vs Extrude Linear, Offset vs Offset Loose): [`../bridge-quirks.md`](../bridge-quirks.md) § Component name traps
- Composition: Voronoi + hole gradient → [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Compound recipes
  ("Voronoi screen with hole gradient" — Voronoi cells become the panel cells; apply Scale + Boundary Surfaces per cell per the perforated-attractor recipe)

## Verification

- `gh_canvas_summary` → 0 errors / 0 warnings (canvas was clean at inspection — 61 objects, 12 sliders, 2× `Component_PlanarVoronoi`, 1× `Component_CurveIntersection` + 1× `Component_Dispatch` + 1× `Component_ListLength` confirming Variant B's filter is present)
- `gh_get_runtime_messages(extrude_guid)` on either Extrude → no red/orange messages
- Visual — capture perspective and compare against the four bundled renders:
  - Variant A clean: [`Voronoi (Arctic).png`](./Voronoi%20%28Arctic%29.png)
  - Variant A ghosted: [`Voronoi (Ghosted).png`](./Voronoi%20%28Ghosted%29.png)
  - Variant B clean: [`Voronoi Free Edge (Arctic).png`](./Voronoi%20Free%20Edge%20%28Arctic%29.png)
  - Variant B ghosted: [`Voronoi Free Edge (Ghosted).png`](./Voronoi%20Free%20Edge%20%28Ghosted%29.png)
- Silhouette sanity:
  - Variant A → rectangular outline; perimeter cells have visibly straight edges along the rectangle border
  - Variant B → ragged organic outline; every visible cell edge is a free Voronoi edge (no straight rectangle-aligned segments)
- Cell-count sanity: Variant B's Cell Amount default (43) is higher than Variant A's (30) — this compensates for Dispatch discarding ~30–40% of cells (the boundary ring). If Variant B's render looks too sparse, raise its `Cell Amount` slider; if Variant A's looks too dense, lower its slider.

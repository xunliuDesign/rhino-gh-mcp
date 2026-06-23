# Kinetic — Recipe

> **Source canvas:** `Kinetic.gh` (this folder).
> **Status:** validated (transcribed by structural inspection 2026-06-23).
>
> Kinetic differs from the other typologies: it is **not a wrapper layer
> on another recipe** (the original v1 framing). It is its own canonical
> chain — vertical-wall triangles → area-filtered Dispatch → per-cell
> tetrahedron extrude → 3 lateral faces rotated open → thickness along
> per-face normal. The "kinetic" intuition (state = closed↔open) is
> embodied in the `Opening Angle` slider; sweeping it from 0 → max
> animates the petal opening.

## What it builds

A vertical wall (XZ or YZ plane) paneled into triangles. Each triangle
in the filtered subset is "popped" outward into a tetrahedron (apex
distance = `Protrusion Amount` along the surface normal), then its
three lateral faces hinge outward around their base edges by
`Opening Angle` to form a three-petal aperture. Each rotated face is
then thickened by `Thickness` along its own normal so the geometry has
material depth.

Triangles that fail the area filter remain flat against the base wall
(unmoved) — they read as solid background between the open kinetic
panels.

The renders `Kinetic (Arctic).png`, `Kinetic (Ghosted).png`, and
`Kinetic (Close Up).png` show the resulting geometry.

## The 5 stages (per author's canvas labels)

```
01. Base Surface (XZ/YZ Plane, vertical)
        ↓
02. Panelize + Area Filter (#<Area for second number of SmallerThan)
        ↓
03. Key Sliders (Protrusion / Opening Angle / Thickness)
        ↓
04. Faces of Base Triangle (extrude-to-point + lateral-face decomposition)
        ↓
05. Adding Thickness (Evaluate Surface → normal → Amplitude → Extrude)
```

The canvas has scribble labels at each stage. The "Must be #<Area"
note on stage 2 is a **tuning hint** — the threshold slider feeding
SmallerThan.Second Number must be set BELOW a typical panel area or
Dispatch sends every panel into the "no-kinetic" branch and nothing
opens. See § Sliders below for the active threshold value.

## Required: vertical wall host

The canvas builds the base via `XZ Plane` → `Rectangle` → `Param_Surface`.
**Must be a vertical plane** — XZ (facing +Y) or YZ (facing +X). The
scribble explicitly says "Must be a XZ or YZ Plane (Vertical)". A
horizontal XY base will produce kinetic panels facing UP instead of
outward.

For production builds replacing the demo Rectangle with a real Rhino
host face, ensure the host's outward normal is horizontal (parallel
to the world XY plane). See `../geometry-ingest.md` § Outward-normal
verification.

## Components list (exact kinds)

| Stage | Component | Kind | Notes |
|---|---|---|---|
| 01 | `XZ Plane` | `Component_XZPlane` | World XZ plane at origin — vertical wall facing +Y |
| 01 | `Rectangle` | `Component_Rectangle` | Demo base. Inputs: Plane, X Size, Y Size. **For production**, swap for `Param_Brep` referenced to a Rhino host face |
| 01 | `Surface` (param ×2) | `Param_Surface` | One forwards the rectangle into TriB; a second forwards it into the per-cell ListItem chain for the kinetic mechanism. The two-param idiom is a canvas-clarity move; both reference the same upstream rectangle |
| 02 | `Triangle Panels B` *(LunchBox)* | `TriB` | Triangular panelization on the vertical surface |
| 02 | `Area` | `Component_AreaProperties` | Area per panel — feeds SmallerThan as the FIRST input |
| 02 | `Smaller Than` | `Component_SmallerThan` | Boolean filter: `First < Second` → is panel area below threshold? |
| 02 | `Dispatch` | `Component_Dispatch` | Splits panel list into A (matches pattern = TRUE → kinetic branch) and B (FALSE → unchanged background panels) |
| 03 | `Number Slider [Protrusion Amount]` | `GH_NumberSlider` | How far the tetrahedron apex projects out of the wall |
| 03 | `Number Slider [Opening Angle]` | `GH_NumberSlider` | Hinge angle of each lateral face. **The kinetic state driver** — sweep from 0 (closed) → max (fully open) |
| 03 | `Number Slider [Thickness]` | `GH_NumberSlider` | Per-face thickness along the rotated face's normal |
| 04 | `List Item` (variable, ×5) | `Component_ListItemVariable` | Picks specific items from the panel/face/edge lists at each decomposition step |
| 04 | `Deconstruct Brep` (×5) | `Component_DeconstructBrep` | Pulls Faces / Edges / Vertices from each Brep stage so RotateAxis can target a specific edge |
| 04 | `Area` | `Component_AreaProperties` | **Second use** — centroid of the deconstructed face, used as the source point for the Move (the apex base) |
| 04 | `Evaluate Surface` (×1, at stage 4) | `Component_EvaluateSurface` | Samples the base face at the centroid → returns the surface normal vector that the apex protrudes along |
| 04 | `Multiplication` (variable) | `Component_VariableMultiplication` | `Protrusion Amount × normal vector` → motion vector |
| 04 | `Move` | `Component_Move` | Translates the centroid by `motion` → apex point of the tetrahedron |
| 04 | `Extrude To Point` | `Component_ExtrudeToPoint` | Extrudes the base triangle face to the apex point → tetrahedron |
| 04 | `Rotate Axis` (×3) | `Component_RotateAxis` | Hinges each of the 3 lateral faces of the tetrahedron around its base edge by `Opening Angle` |
| 05 | `Evaluate Surface` (×3) | `Component_EvaluateSurface` | Per-rotated-face: samples the face at a deconstructed vertex to get its current normal |
| 05 | `Amplitude` (×3) | `Component_VectorAmplitude` | `normal × Thickness` → per-face extrude direction |
| 05 | `Extrude` (×3, legacy) | `Component_Extrude` | Thickens each rotated face along its own normal. **Not** `Extrude Linear` |

## Wiring (single host, walking the chain)

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 01 | `XZ Plane` | → | `Rectangle.Plane` | Vertical wall plane |
| 02 | `Number Slider [94a8a788, val=10, 0–100 int]` | → | `Rectangle.X Size` | Wall width |
| 03 | `Number Slider [6f403025, val=4, 0–10 int]` | → | `Rectangle.Y Size` | Wall height |
| 04 | `Rectangle.Rectangle` | → | `Param_Surface [5da2e785]` | Implicit Curve→Surface for the panelize branch |
| 04a | `Rectangle.Rectangle` | → | `Param_Surface [8fd07a09]` | Second Param_Surface — feeds the per-cell ListItem chain for stage 4 |
| 05 | `Param_Surface [5da2e785]` | → | `TriB.Surface` | |
| 05a | (TriB.U/V Divisions — unwired, defaults to internal) | | | |
| 06 | `TriB.Panels` | → | `Area [02394b8e].Geometry` | Per-panel area |
| 07 | `Area.Area` | → | `Smaller Than.First Number` | The test value |
| 08 | `Number Slider [70e50a98, val=0.24, 0–10 float]` | → | `Smaller Than.Second Number` | **The "Must be #<Area" threshold** — must be less than typical panel area or Dispatch sends everything to the FALSE branch |
| 09 | `Smaller Than.First < Second` | → | `Dispatch.Dispatch pattern` | TRUE if panel is small enough → goes to kinetic branch |
| 10 | `TriB.Panels` | → | `Dispatch.List` | Same panel list, now split by the boolean |
| 11 | `Param_Surface [8fd07a09]` | → | `List Item [96f49ab9].List` | Per-cell ingest into the kinetic branch (this is the path that becomes a tetrahedron) |
| 12 | `List Item [96f49ab9].Item` | → | `Deconstruct Brep [3da87f5b].Brep` | First decomposition: pull Faces/Edges/Vertices of the triangle face |
| 13 | `Deconstruct Brep [3da87f5b]` | → | `Area [acea690f].Geometry` | Centroid of the base face |
| 13a | `Deconstruct Brep [3da87f5b]` | → | `Evaluate Surface [f80057a5].Surface` | Base face — sampled for its normal |
| 14 | `Area.Centroid` | → | `Evaluate Surface [f80057a5].Point` | Sample location = centroid |
| 14a | `Area.Centroid` | → | `Move [ee486233].Geometry` | The point being translated to become the apex |
| 15 | `Evaluate Surface [f80057a5]` | → | `Multiplication.A` | Base-face normal vector |
| 16 | `Number Slider [Protrusion Amount, val=0.47, 0–21 float]` | → | `Multiplication.B` | Scales the normal |
| 17 | `Multiplication.Result` | → | `Move.Motion` | Apex = centroid + (Protrusion × normal) |
| 18 | `Deconstruct Brep [3da87f5b]` | → | `Extrude To Point.Base` | Base face = the triangle |
| 19 | `Move.Geometry` (the moved centroid) | → | `Extrude To Point.Point` | Apex of the pyramid/tetrahedron |
| 20 | `Extrude To Point` | → | `Deconstruct Brep [da6e40b3].Brep` | Decompose the tetrahedron → Faces / Edges / Vertices |
| 21 | `Deconstruct Brep [da6e40b3]` (Faces output) | → | `List Item [a7db4ad4].List` | List of the tet's faces |
| 22 | `List Item [a7db4ad4].Item` | → | `Deconstruct Brep [3bd9797f].Brep` | Lateral face 1 |
| 22a | `List Item [a7db4ad4].Item` | → | `Deconstruct Brep [2fafb2e8].Brep` | Lateral face 2 |
| 22b | `List Item [a7db4ad4].Item` | → | `Deconstruct Brep [6c8869a1].Brep` | Lateral face 3 — three branches operating on the same item list (tree-aware) |
| 23 | `Deconstruct Brep [3bd9797f]` (Edges) | → | `List Item [ee063340].List` | Edges of lateral face 1 → pick the hinge edge |
| 23a | `Deconstruct Brep [2fafb2e8]` (Edges) | → | `List Item [d2778b12].List` | Edges of lateral face 2 |
| 23b | `Deconstruct Brep [6c8869a1]` (Edges) | → | `List Item [b08ae7df].List` | Edges of lateral face 3 |
| 24 | `Number Slider [34dcc807, val=1, 0–1 int]` | → | three ListItem.Index inputs | Picks edge 1 of each lateral face as the hinge axis (per branch) |
| 24a | `Number Slider [3a59336b, val=2, 0–10 int]` | → | one ListItem.Index input | Selects which item from a list elsewhere in the decomposition chain |
| 25 | `Deconstruct Brep [3bd9797f]` | → | `Rotate Axis [f8659320].Geometry` | The face being rotated |
| 25a | `Deconstruct Brep [2fafb2e8]` | → | `Rotate Axis [d3eac1b5].Geometry` | |
| 25b | `Deconstruct Brep [6c8869a1]` | → | `Rotate Axis [c7c7b725].Geometry` | |
| 26 | `Number Slider [Opening Angle, val=12, 0–100 int]` | → | all 3 `Rotate Axis.Angle` inputs | **The kinetic state driver** — same angle for all three petals (synchronous opening) |
| 27 | `List Item [ee063340].Item` | → | `Rotate Axis [f8659320].Axis` | Hinge edge for face 1 |
| 27a | `List Item [d2778b12].Item` | → | `Rotate Axis [d3eac1b5].Axis` | Face 2 |
| 27b | `List Item [b08ae7df].Item` | → | `Rotate Axis [c7c7b725].Axis` | Face 3 |
| 28 | `Rotate Axis [f8659320]` | → | `Evaluate Surface [b6684373].Surface` | **Stage 5 starts** — re-sample the rotated face for its current normal |
| 28a | `Rotate Axis [d3eac1b5]` | → | `Evaluate Surface [7320f97a].Surface` | |
| 28b | `Rotate Axis [c7c7b725]` | → | `Evaluate Surface [54fe8289].Surface` | |
| 29 | `Deconstruct Brep [3bd9797f]` (a vertex) | → | `Evaluate Surface [b6684373].Point` | Sample point on the rotated face |
| 29a | `Deconstruct Brep [2fafb2e8]` | → | `Evaluate Surface [7320f97a].Point` | |
| 29b | `Deconstruct Brep [6c8869a1]` | → | `Evaluate Surface [54fe8289].Point` | |
| 30 | `Evaluate Surface [b6684373]` (normal vector) | → | `Amplitude [205d275d].Vector` | |
| 30a | `Evaluate Surface [7320f97a]` | → | `Amplitude [33a1aacd].Vector` | |
| 30b | `Evaluate Surface [54fe8289]` | → | `Amplitude [f9736f97].Vector` | |
| 31 | `Number Slider [Thickness, val=0.06, 0–0.1 float]` | → | all 3 `Amplitude.Amplitude` inputs | Per-face thickness magnitude |
| 32 | `Rotate Axis [f8659320]` | → | `Extrude [7af99bda].Base` | Final thicken |
| 32a | `Rotate Axis [d3eac1b5]` | → | `Extrude [3d4f8631].Base` | |
| 32b | `Rotate Axis [c7c7b725]` | → | `Extrude [9802ea66].Base` | |
| 33 | `Amplitude [205d275d]` | → | `Extrude [7af99bda].Direction` | normal × thickness |
| 33a | `Amplitude [33a1aacd]` | → | `Extrude [3d4f8631].Direction` | |
| 33b | `Amplitude [f9736f97]` | → | `Extrude [9802ea66].Direction` | |

## Sliders / defaults (as authored)

| Slider | Value | Min | Max | Type | Drives | Notes |
|---|---|---|---|---|---|---|
| `Protrusion Amount` | 0.47 | 0.0 | 21.0 | float | `Multiplication.B` (normal × this) | Apex height of the tetrahedron above the wall |
| `Opening Angle` | 12 | 0 | 100 | int | All 3 `Rotate Axis.Angle` (degrees) | **The kinetic state driver** — 0 = petals flat against wall (closed), max = petals fully splayed open |
| `Thickness` | 0.06 | 0.0 | 0.1 | float | All 3 `Amplitude.Amplitude` | Per-face material thickness |
| `Area threshold` *(unnamed, 70e50a98)* | 0.24 | 0.0 | 10.0 | float | `Smaller Than.Second Number` | **Must be < typical panel area** or Dispatch sends every triangle to the unchanged branch (no kinetic effect) |
| `width` *(unnamed, 94a8a788)* | 10 | 0 | 100 | int | `Rectangle.X Size` | Wall width along plane X |
| `height` *(unnamed, 6f403025)* | 4 | 0 | 10 | int | `Rectangle.Y Size` | Wall height along plane Y |
| `edge index` *(unnamed, 34dcc807)* | 1 | 0 | 1 | int | 3× `List Item.Index` (hinge edge per face) | Picks which edge of each lateral face acts as the hinge axis |
| `aux index` *(unnamed, 3a59336b)* | 2 | 0 | 10 | int | 1× `List Item.Index` | Selects an item in one of the decomposition lists |

For production builds, the four unnamed sliders should be relabelled
with `gh_add_slider(name="[kinetic] <role>", ...)` so they read as the
user's dials, not "Number Slider".

## The kinetic motion — sweeping `Opening Angle`

The defining feature of this typology is the parametric closed↔open
sweep. To preview the motion:

```
gh_set_slider(opening_angle_guid, 0)    → all petals flat (closed)
gh_set_slider(opening_angle_guid, 45)   → mid-opening
gh_set_slider(opening_angle_guid, 90)   → fully open
gh_recompute  +  rhino_capture_viewport at each
```

The synchronous opening (one slider, three RotateAxis components fed
the same value) is a deliberate authoring choice — produces uniform
"breathing" of all panels in unison. For asymmetric / sequential
animation, split into three separate `Opening Angle [edge N]` sliders
and tune each independently.

## Tuning the Dispatch threshold

The Dispatch + SmallerThan + Area chain is what selects which panels
become kinetic and which stay flat. The threshold slider (currently
0.24) is compared against each panel's area:

- `threshold > max panel area` → ALL panels go to the FALSE branch → **no kinetic effect** at all
- `threshold < min panel area` → ALL panels go to the TRUE branch → every panel becomes a tetrahedron
- somewhere in between → mixed result, with smaller panels (e.g. corner/edge fragments) opening and larger panels staying flat

To tune for a desired ratio:
1. Recompute, read the actual area range with `gh_get_objects(area_guid)`
2. Set the threshold slider via `gh_set_slider` to the area value that splits the population the desired way

## Anti-patterns

| Anti-pattern | Why wrong | Use instead |
|---|---|---|
| Horizontal XY base plane | Tetrahedra apex point UP (out of the floor) instead of outward from a facade | XZ or YZ plane (vertical) — see scribble "Must be a XZ or YZ Plane (Vertical)" |
| Hard-coding the extrude direction with `Unit Z` in stage 5 | Each rotated face has its OWN normal — using world Z would thicken every petal in the same direction regardless of its orientation | Keep the per-face `Evaluate Surface → Amplitude → Extrude` chain (per-face normal × Thickness) — this is Pattern C from [`../extrude-direction.md`](../extrude-direction.md) applied per rotated face |
| Setting the area threshold above all panel areas | Dispatch sends every panel to the FALSE branch — no kinetic effect; the canvas computes silently with zero visible change | Read the actual panel area range and set the slider below typical area (the scribble warns: "Must be < #") |
| Single `Rotate Axis` on the whole tetrahedron | Rotates the entire tetrahedron in space — doesn't produce the "petal opening" effect (which requires each lateral face to hinge independently around its base edge) | Three separate `Rotate Axis` components, one per lateral face, each with a different `Axis` (the lateral face's base edge) |
| `Extrude Linear` for the per-face thickening | Wrong port type — Linear expects a `Line Axis`, not a `Vector Direction` | Legacy `Extrude` (`Component_Extrude`, nickname `"Extr"`) — takes Vector on `Direction` directly. See [`../bridge-quirks.md`](../bridge-quirks.md) |
| `Scale` instead of `Extrude To Point` for the tetrahedron | Scale uniformly shrinks the cell — doesn't create an apex point at controlled protrusion distance | `Extrude To Point` is the correct primitive: Base = triangle face, Point = (centroid + Protrusion × normal) |

## Multi-surface batching

Same principle as perforated and panelized — build the chain on a
**list** of host surfaces, not per-surface. The Dispatch /
ExtrudeToPoint / RotateAxis chain is tree-aware: feed a list of
vertical hosts and each gets one branch through the entire kinetic
graph.

Caveat: for hosts with **different orientations** (e.g. all four
facades of a building, each facing a different cardinal direction),
do NOT use the canvas's `XZ Plane` shortcut for stage 1. Instead,
replace stages 01 (XZ Plane + Rectangle → Param_Surface) with a
`Param_Brep` referenced to the list of Rhino host faces, and let
`Evaluate Surface` in stages 4 + 5 derive the correct per-host normal
automatically. See [`../extrude-direction.md`](../extrude-direction.md)
§ Pattern C and [`../geometry-ingest.md`](../geometry-ingest.md) §
Outward-normal verification.

## Variant: kinetic from a different base typology

The user's `Kinetic.gh` is a complete topology in itself — not a thin
wrapper. But "kinetic" as a *concept* (state-driven opening) can be
applied to other recipes by adding a `state` slider that multiplies a
key driver:

- **Kinetic louver** — multiply `Opening Angle` slider × `state` into
  `Rotate Axis.Angle` in the louver chain
- **Kinetic perforated** — multiply `hole_max` × `state` into the
  perforated-attractor `Construct Domain.A` input
- **Kinetic panelized** — multiply `inset` × `state` into the
  panelized joint-reveal `Scale.Factor`

These are simpler topologies (slider-only modifications of other
recipes) and are catalogued in
[`../recipe-modification-patterns.md`](../recipe-modification-patterns.md)
§ Compound recipes. The canvas in this folder is a richer,
purpose-built kinetic facade — favor cloning it directly when the
prompt is "kinetic facade" or "operable panels" without further
typology specification.

## Cross-references

- Source canvas: [`Kinetic.gh`](./Kinetic.gh)
- Reference renders: [`Kinetic (Arctic).png`](./Kinetic%20%28Arctic%29.png), [`Kinetic (Ghosted).png`](./Kinetic%20%28Ghosted%29.png), [`Kinetic (Close Up).png`](./Kinetic%20%28Close%20Up%29.png)
- Extrude direction (per-face normal Pattern C in stage 5): [`../extrude-direction.md`](../extrude-direction.md)
- Host ingest for production builds: [`../geometry-ingest.md`](../geometry-ingest.md)
- Panelizer interchangeability (TriB is one of many — TriC / QuadA / HexCells / Diamond / QuadStag all swap in): [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Canonical patterns
- Compound "kinetic wrapper" variants on other recipes: [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Compound recipes
- Bridge quirks (Extrude vs Extrude Linear): [`../bridge-quirks.md`](../bridge-quirks.md) § Component name traps

## Verification

- `gh_canvas_summary` → 0 errors / 0 warnings (canvas was clean at inspection: 55 objects, no errors, no warnings)
- `gh_get_runtime_messages(extrude_guids)` → no red/orange messages on any of the three final Extrude components
- **Pre-flight check**: the area threshold slider (currently 0.24, range 0–10) must be set **below typical panel area** for Dispatch to actually split the list. Symptom of misconfiguration: `gh_canvas_summary` reports 0 errors but the bake shows a flat triangulated wall with no kinetic protrusions. Fix: read actual panel areas with `gh_get_objects(area_guid)`, then `gh_set_slider` the threshold below the median.
- Visual: capture perspective + close-up; confirm three-petal openings on the triangles below the area threshold; compare against `Kinetic (Close Up).png` for petal geometry and `Kinetic (Arctic).png` for the overall facade rhythm.
- Sweep test: `gh_set_slider(opening_angle, 0)` → recompute → capture → should show flat wall. `gh_set_slider(opening_angle, max)` → recompute → capture → should show fully splayed petals. If the geometry doesn't visibly change between extremes, the wiring of `Opening Angle` → `Rotate Axis.Angle` is broken; re-trace.

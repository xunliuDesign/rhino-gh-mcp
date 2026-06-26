# Folded / Faceted / Origami — Recipe

> **Source canvases:** `Folded.gh` + `Origami Faceted.gh` (this folder).
> **Status:** validated (transcribed by structural inspection 2026-06-22).
>
> Two distinct folded-family recipes live in this folder. They share the
> "rectangle host → fold geometry → extrude" idea but use completely
> different mechanisms — do not conflate them. Pick by what the user
> describes:
>
> - **Variant A — Folded** = a corrugated / ribbed / pleated wall
>   (long parallel ridges, like a tin roof or accordion). Built by
>   *rotating + mirroring a single rectangle* into a V-section and
>   tiling that V in 2D. No panelizer, no attractor.
> - **Variant B — Origami Faceted** = a field of *pyramidal pinnacles*
>   on a quad grid (each cell = a small pyramid), with apex height
>   driven by attractor proximity. Uses LunchBox `Quad Panels A`,
>   `Pull Point` against `Populate Geometry` points, and
>   `Extrude To Point` per cell.
>
> Both canvases author a placeholder base (Rectangle from sliders).
> Production builds replace the Rectangle stage with a host
> Brep/Surface — see `../geometry-ingest.md`.

---

## Variant A — Folded (corrugated / ribbed pleat)

### A. What it builds

A planar host divided into long parallel **pleats** (the "corrugated tin
roof" or "accordion") then extruded to give panel thickness. The
construction trick is geometric, not panelizer-based:

1. Start from a single rectangle (the **base pleat unit** — one half of a
   V-section, sized by `X Size` × `Y Size`).
2. Rotate it about one of its long edges by `−Rotation°` so it tilts up
   on one side.
3. Mirror the tilted copy across a YZ plane at the opposite vertex →
   produces a **V-section pair** (one tilted up, one tilted down,
   meeting at the ridge).
4. Cross-reference the rotated + mirrored cells, then **array in Y**
   (along the pleat) by `Y Count` and **array in X** (next pleat over)
   by `X Count`.
5. Pass the resulting curve tree through a `Param_Surface` waypoint
   (implicit curve→surface conversion) and extrude along `Unit Z` by
   the thickness slider.

Reference render: [`Folded (Arctic).png`](./Folded%20%28Arctic%29.png)
shows ~10 parallel ridges with thickness; [`Folded
(Ghosted).png`](./Folded%20%28Ghosted%29.png) shows the wireframe with
per-rib Y subdivisions.

### A. Wiring — TRANSCRIBE THIS (the build)

*(Moved to top — this is the recipe's core. Read top-to-bottom and place +
wire components in this exact order. The Components list below disambiguates
ambiguous kinds; Stages overview at the bottom is context only.)*

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 1 | `X Size` slider (val=2, range 0–10) | → | `Rectangle.X Size` | Width of base pleat unit |
| 2 | `Y Size` slider (val=10, range 0–10) | → | `Rectangle.Y Size` | Length of base pleat unit (also drives Series Y step — see wire 12) |
| 3 | (Rectangle.Plane unwired) | → | (defaults to World XY) | Base lies flat on XY |
| 4 | `Rectangle.Rectangle` | → | `Explode (#1).Curve` | Used for axis pick |
| 4a | `Rectangle.Rectangle` | → | `Rotate Axis.Geometry` | The geometry that gets tilted |
| 5 | `Explode (#1).Segments` | → | `List Item.List` (segments) | Edge stream |
| 6 | `Axis of Rotation` slider (val=1, range 0–100, int) | → | `List Item.Index` | Picks edge index 1 |
| 7 | `List Item.Item` (segments) | → | `Rotate Axis.Axis` | The rotation axis line |
| 8 | `Rotation` slider (val=15, range 0–100) | → | `Negative.Value` | Degrees, negated |
| 9 | `Negative.Result` | → | `Radians.Degrees` | Convert to radians |
| 10 | `Radians.Radians` | → | `Rotate Axis.Angle` | Final fold angle (−15° → +15° via mirror trick) |
| 11 | `Explode (#1).Vertices` | → | `List Item.List` (vertices) | Vertex stream |
| 12 | `Point for Plane` slider (val=1, range 0–100, int) | → | `List Item.Index` | Picks vertex index 1 — the mirror-plane origin |
| 13 | `List Item.Item` (vertex) | → | `YZ Plane.Origin` | Mirror plane origin |
| 14 | `Rotate Axis.Geometry` | → | `Mirror.Geometry` | Tilted rectangle |
| 15 | `YZ Plane.Plane` | → | `Mirror.Plane` | YZ plane at chosen vertex |
| 16 | `Rotate Axis.Geometry` | → | `Cross Reference.List (A)` | Half 1 of V-section |
| 17 | `Mirror.Geometry` | → | `Cross Reference.List (B)` | Half 2 of V-section |
| 18 | `Cross Reference.List (A)` | → | `Move (Y).Geometry` | Multi-source — both Cross Ref outputs feed Move (Y) |
| 18a | `Cross Reference.List (B)` | → | `Move (Y).Geometry` | (same input port, second source) |
| 19 | `Y Size` slider (= wire 2's slider) | → | `Series (Y).Step` | Y array step = pleat length |
| 20 | `Y Count` slider (val=4, range 0–100, int) | → | `Series (Y).Count` | |
| 21 | `Series (Y).Series` | → | `Unit Y.Factor` | |
| 22 | `Unit Y.Unit vector` | → | `Move (Y).Motion` | |
| 23 | `Move (Y).Geometry` | → | `Move (X).Geometry` | Array result fed into X array |
| 24 | `Rotate Axis.Geometry` | → | `Explode (#2).Curve` | Re-explode for X-step calc |
| 24a | `Mirror.Geometry` | → | `Explode (#3).Curve` | Re-explode the mirrored copy |
| 25 | `Explode (#2).Vertices` | → | `List Item.List` | Vertices of rotated rect |
| 25a | `Point 1` slider (val=0, range 0–10, int) | → | `List Item.Index` | Pick vertex of rotated rect |
| 26 | `List Item.Item` | → | `Distance.Point A` | |
| 27 | `Explode (#3).Vertices` | → | `List Item.List` | Vertices of mirrored rect |
| 27a | `Point 1` slider (val=0, range 0–10, int) | → | `List Item.Index` | Pick vertex of mirrored rect (second `Point 1` slider — same nickname, different instance) |
| 28 | `List Item.Item` | → | `Distance.Point B` | |
| 29 | `Distance.Distance` | → | `Series (X).Step` | X step = pleat width (one full V) |
| 30 | `X Count` slider (val=10, range 0–10, int) | → | `Series (X).Count` | |
| 31 | `Series (X).Series` | → | `Unit X.Factor` | |
| 32 | `Unit X.Unit vector` | → | `Move (X).Motion` | |
| 33 | `Move (X).Geometry` | → | `Surface` (Param_Surface, waypoint) | Curve→Surface implicit conversion |
| 34 | `Surface` | → | `Extrude.Base` | |
| 35 | thickness slider (val=0.2, range 0–1) | → | `Unit Z.Factor` | |
| 36 | `Unit Z.Unit vector` | → | `Extrude.Direction` | Direction = (0, 0, thickness) — **only correct because the host lies on XY**, see "Extrude direction" |

There are three orphan sliders on the canvas (two unnamed `Number
Slider`s at value 1.0 and 2.0, plus the `Run` toggle and `Scenario`
value-list which gate writes, not geometry). These are leftover from
authoring and play no role in the chain.

### A. Components list (exact kinds)

| Stage | Component | Kind | Notes |
|---|---|---|---|
| 01 | `Rectangle` | `Component_Rectangle` | Base pleat unit. Inputs: Plane (unwired → World XY), X Size, Y Size, Radius (unwired). Output: Rectangle (curve). For production builds, replace with `Param_Brep` / `Param_Surface` referenced to a Rhino GUID — see `../geometry-ingest.md` |
| 02 | `Explode` (Curve) | `Component_ExplodeCurve` | Used twice: (i) explode the base Rectangle to extract one of its 4 edges as the rotation axis; (ii) explode the rotated + mirrored copies later to read their vertices for the X-array step |
| 02 | `List Item` | `Component_ListItemVariable` | Pick edge index from `Explode.Segments` via the `Axis of Rotation` slider — that segment becomes the rotation axis |
| 02 | `Rotation` slider → `Negative` → `Radians` | `OperatorSign` + `FuncToRadians` | Converts the `Rotation` slider's degrees into `−angle` in radians (negative so the fold tilts *down* before mirror flips it back up) |
| 02 | `Rotate Axis` | `Component_RotateAxis` | Inputs: Geometry = Rectangle, Angle = `Radians`, Axis = `List Item.Item` (the picked edge). Output: rotated copy of the rectangle, tilted up |
| 03 | `List Item` (vertices) | `Component_ListItemVariable` | Pick vertex index from `Explode.Vertices` via the `Point for Plane` slider — that vertex becomes the mirror-plane origin |
| 03 | `YZ Plane` | `Component_YZPlane` | Builds a YZ plane at the picked vertex (mirror plane runs in YZ) |
| 03 | `Mirror` | `Component_Mirror` | Inputs: Geometry = `Rotate Axis.Geometry`, Plane = `YZ Plane.Plane`. Output: mirrored copy → second half of the V-pair |
| 04 | `Cross Reference` | `Component_MatchListCrossReference` | Inputs: List (A) = `Rotate Axis.Geometry`, List (B) = `Mirror.Geometry`. Output: paired streams — both halves of the V-section together. The two outputs `List (A)` and `List (B)` both flow into the same `Move.Geometry` input (multi-source) |
| 04 | `Series` (for Y array) | `Component_Series` | Inputs: Start (unwired → 0), Step = the **Y Size** slider (same slider as `Rectangle.Y Size`), Count = `Y Count` slider. Output: series 0, Y, 2Y, … |
| 04 | `Unit Y` | `Component_UnitVectorY` | Factor = `Series.Series` → vector list (0,0,0), (0,Y,0), (0,2Y,0), … |
| 04 | `Move` (Y) | `Component_Move` | Inputs: Geometry = both Cross Reference outputs, Motion = `Unit Y.Unit vector`. Outputs the V-pair arrayed in Y |
| 04 | `Explode` (vertices A) + `List Item` + `Explode` (vertices B) + `List Item` + `Distance` | `Component_ExplodeCurve` + `Component_ListItemVariable` + `Component_Distance` | The two `Point 1` sliders pick a vertex from the rotated rectangle and a vertex from the mirrored rectangle. `Distance` between them = the **pleat width** in X (one full V-section). This distance feeds the X-array `Series.Step` |
| 04 | `Series` (for X array) | `Component_Series` | Inputs: Start (unwired → 0), Step = `Distance.Distance` (pleat width), Count = `X Count` slider |
| 04 | `Unit X` | `Component_UnitVectorX` | Factor = `Series.Series` |
| 04 | `Move` (X) | `Component_Move` | Inputs: Geometry = previous `Move` (Y) output, Motion = `Unit X.Unit vector`. Outputs the full 2D pleat grid as curves |
| 05 | `Surface` | `Param_Surface` | Waypoint — receives the moved curve tree and implicitly converts curves → surfaces for the extrude |
| 05 | `Extrude` *(legacy)* | `Component_Extrude` | Inputs: Base = `Surface` (param), Direction = `Unit Z.Unit vector`. **Not** `Extrude Linear` (which expects a Line `Axis` and has an Orientation gotcha — see `../bridge-quirks.md` § Component name traps) |
| 05 | `Unit Z` | `Component_UnitVectorZ` | Factor = the **thickness** slider (range 0–1). Note: this is `Unit Z` because the demo Rectangle lies on World XY — see "Extrude direction" below |

### A. Sliders / defaults (as authored)

| Slider | Value | Min | Max | Type | Drives | Notes |
|---|---|---|---|---|---|---|
| `X Size` (unnamed) | 2 | 0 | 10 | float | `Rectangle.X Size` | Pleat-unit width |
| `Y Size` (unnamed) | 10 | 0 | 10 | float | `Rectangle.Y Size` + `Series (Y).Step` | Pleat-unit length (also Y array step — keeps tiles flush) |
| `Axis of Rotation` | 1 | 0 | 100 | int | `List Item.Index` (segments) | Which of the 4 edges becomes the fold hinge |
| `Rotation` | 15 | 0 | 100 | float | `Negative` → `Radians` → `Rotate Axis.Angle` | Fold angle in **degrees** (negated before radian conversion) |
| `Point for Plane` | 1 | 0 | 100 | int | `List Item.Index` (vertices) | Which vertex of base rectangle becomes the mirror-plane origin |
| `Y Count` | 4 | 0 | 100 | int | `Series (Y).Count` | Number of pleat rows along Y |
| `X Count` | 10 | 0 | 10 | int | `Series (X).Count` | Number of pleats across X |
| `Point 1` (×2) | 0, 0 | 0 | 10 | int | `List Item.Index` (rotated/mirrored vertex picks for Distance) | Define the two vertices whose distance = X-array step |
| `thickness` (unnamed) | 0.2 | 0 | 1 | float | `Unit Z.Factor` | Extrusion depth |

### A. Anisotropic / unit-aware sliders (production retune)

The canvas uses unit-agnostic sliders. For doc-aware tunes:

| Driver | mm default | mm range | m default | m range | ft default | ft range | Type |
|---|---|---|---|---|---|---|---|
| `pleat_width` (X Size) | 400 | 100–1500 | 0.4 | 0.1–1.5 | 1.3 | 0.3–5 | float |
| `pleat_length` (Y Size, host-height per row) | 3000 | 1000–6000 | 3.0 | 1–6 | 10 | 3–20 | float |
| `fold_angle_deg` (Rotation) | 20 | 5–60 | same | | same | | float |
| `pleat_count` (X Count) | 10 | 4–40 | same | | same | | int |
| `row_count` (Y Count) | 4 | 1–20 | same | | same | | int |
| `thickness` (Unit Z.Factor) | 30 | 5–200 | 0.030 | 0.005–0.2 | 0.1 | 0.02–0.7 | float |

`Axis of Rotation`, `Point for Plane`, and the two `Point 1` sliders are
**topology pickers**, not dimensions — they select which edge/vertex of
the base rectangle is the hinge / mirror origin / X-step source. Their
correct values depend on the rectangle's CCW vertex order and shouldn't
be retuned blindly; if the construction reverses, increment by 1.

### A. Extrude direction — host-normal generalization

The canvas uses `Unit Z` because the base Rectangle lies on World XY
(its normal IS +Z). This is **Pattern A** (axis-aligned host) from
`../extrude-direction.md`.

When transcribing this recipe for a non-XY host, **read what's wired to
`Extrude.Direction`** — do not assume Unit Z (per the authoring callout
at the top of `../extrude-direction.md`). For host orientations:

| Host orientation | Replace `Unit Z` with | Pattern |
|---|---|---|
| Flat on XY (this canvas) | `Unit Z(Factor=thickness)` | A |
| Vertical wall facing +X | `Unit X(Factor=thickness)` | A |
| Oblique single host | `Evaluate Surface → Frame → Deconstruct Plane → Z-Axis → Amplitude(thickness)` | B |
| Multi-host / curved host | `Brep Closest Point → Normal → Amplitude(thickness)` (per cell) | C |

The fold *axis* (the edge picked by `Axis of Rotation`) and the mirror
*plane* (YZ via `YZ Plane`) are also XY-host assumptions. For a vertical
wall host facing +X, replace `YZ Plane` with `XZ Plane` (or build the
mirror plane from the host frame). The fold-axis pick still works
because it reads edges from the rectangle, but the resulting fold
direction now lies in the wall plane, not the world plane.

### A. Host-ingest variant (production replacement for stage 01)

The canvas authors the host from sliders. For production (apply pleats
to an existing Rhino building face), replace stage 01:

```
[Rhino host Brep] (GUID)
        ↓
Param_Brep (persistent data = GUID via gh_set_component_parameter)
        ↓
Deconstruct Brep → Faces → List Item(0)  (extract the face you want)
        ↓
(replaces `Rectangle` — feed into Explode + Rotate Axis as if it were the base rect)
```

See `../geometry-ingest.md` § GUID handoff for the param-mode and
script-mode variants of the host reference. For non-rectangular host
faces, the `Explode` → edge-pick stage still works (gives you the host's
edge curves), but the fold geometry will be skewed if the host isn't a
true rectangle.

### A. Stages overview (context — read AFTER the Wiring table)

```
01. Base Rectangle ──→ 02. Rotate About One Edge ──→ 03. Mirror Across YZ Plane
                                                              ↓
                       05. Param_Surface + Extrude   ←──  04. Cross-Reference + 2D Series Array
                           (along Unit Z × thickness)
```

---

## Variant B — Origami Faceted (pyramidal-pinnacle field, attractor-driven)

### B. What it builds

A planar host **quad-panelized** into N×N cells, with each cell raised
into a small **pyramid** whose apex height varies with proximity to a
field of random attractor points sprinkled on the host. The result is a
faceted "pinnacled" field — see [`Origami Faceted (Perspective
Arctic).png`](./Origami%20Faceted%20%28Perspective%20Arctic%29.png) and
the [Top render](./Origami%20Faceted%20%28Top%20Arctic%29.png) which
shows the diagonal "X" pattern (4 triangular facets per cell sharing the
apex).

This variant **does** use a panelizer (`Quad Panels A`) and **does** use
an attractor field (`Populate Geometry` + `Pull Point`) — making it
closer to perforated-attractor in spirit than to Variant A.

### B. Wiring — TRANSCRIBE THIS (the build)

*(Moved to top — this is the recipe's core. Read top-to-bottom and place +
wire components in this exact order. The Components list below disambiguates
ambiguous kinds; Stages overview at the bottom is context only.)*

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 1 | `X Size` slider (val=10, range 0–10) | → | `Rectangle.X Size` | |
| 2 | `Y Size` slider (val=10, range 0–10) | → | `Rectangle.Y Size` | |
| 3 | `Rectangle.Rectangle` | → | `Surface` (Param_Surface waypoint) | Implicit curve→surface |
| 4 | `Surface` | → | `Quad Panels.Surface` | Host into panelizer |
| 4a | `Surface` | → | `Populate Geometry.Geometry` | Host into attractor cloud |
| 5 | `UV` slider (val=14, range 0–100, int) | → | `Quad Panels.U Divisions` | Joint UV — same slider |
| 5a | `UV` slider | → | `Quad Panels.V Divisions` | (same slider as wire 5) |
| 6 | `Quad Panels.Panels` | → | `Deconstruct Brep.Brep` | Per-cell quad brep |
| 7 | `Deconstruct Brep.Edges` | → | `Point On Curve.Curve` | (Point On Curve.Parameter unwired → t=0, so it reads each edge's start point) |
| 7a | `Deconstruct Brep.Edges` | → | `End Points.Curve` | Edge endpoints for the per-edge fan lines |
| 8 | `Point On Curve.Point` | → | `PolyLine.Vertices` | Build per-cell closed polyline |
| 9 | `PolyLine.Polyline` | → | `Area(#1).Geometry` | Cell centroid |
| 9a | `PolyLine.Polyline` | → | `Scale.Geometry` | Cell to scale |
| 10 | `Amount of Attractor Points` slider (val=5, range 0–10, int) | → | `Populate Geometry.Count` | |
| 11 | `Populate Geometry.Population` | → | `Pull Point.Geometry` | Attractor cloud (multi-point list — Pull Point takes min distance) |
| 12 | `Area(#1).Centroid` | → | `Pull Point.Point` | Per-cell sample point |
| 12a | `Area(#1).Centroid` | → | `Scale.Center` | Same centroid → each cell scales about itself |
| 13 | `Pull Point.Distance` | → | `Sort List.Keys` | Sort distances ascending |
| 13a | `Pull Point.Distance` | → | `Minimum.A` | The actual per-cell distance to clamp |
| 14 | `Sort List.Keys` | → | `Reverse List.List` | Reverse → first item = max |
| 15 | `Reverse List.List` | → | `List Item.List` | (index unwired → 0) → max distance |
| 16 | `List Item.Item` | → | `Division.A` | |
| 17 | `cap_divisor` slider (val=2, range 0–10) | → | `Division.B` | cap = max_distance / cap_divisor |
| 18 | `Division.Result` | → | `Minimum.B` | The cap |
| 19 | `Minimum.Result` | → | `Bounds.Numbers` | |
| 19a | `Minimum.Result` | → | `Remap Numbers.Value` | Clamped per-cell distance |
| 20 | `Bounds.Domain` | → | `Remap Numbers.Source` | |
| 21 | `scale_min` slider (val=0.1, range 0–0.1) | → | `Construct Domain.Domain start` | Smaller-of-scale-factor end (= cells **closest** to attractor) |
| 22 | `scale_max` slider (val=0.9, range 0–1) | → | `Construct Domain.Domain end` | Larger-of-scale-factor end (= cells **farthest** from attractor) |
| 23 | `Construct Domain.Domain` | → | `Remap Numbers.Target` | Target = (0.1, 0.9). Forward order — closer to attractor → smaller scale → smaller inset cell |
| 24 | `Remap Numbers.Mapped` | → | `Scale.Factor` | Per-cell scale (0.1–0.9) |
| 24a | `Remap Numbers.Mapped` | → | `Multiplication.A` | Same value also drives apex height |
| 25 | `Scale.Geometry` | → | `Discontinuity.Curve` | Corner points of scaled inner polyline |
| 25a | `Scale.Geometry` | → | `Offset Surface.Surface` | Used for the normal-vector trick |
| 25b | `Scale.Geometry` | → | `Area(#3).Geometry` | Inset centroid (apex move source) |
| 26 | `End Points.End` | → | `Line(#1).Start Point` | |
| 26a | `Discontinuity.Points` | → | `Line(#1).End Point` | |
| 27 | `End Points.Start` | → | `Line(#2).Start Point` | |
| 27a | `Discontinuity.Points` | → | `Line(#2).End Point` | |
| 28 | `Line(#1).Line` | → | `Join Curves.Curves` | |
| 28a | `Line(#2).Line` | → | `Join Curves.Curves` | (multi-source on same port) |
| 29 | `Join Curves.Curves` | → | `Boundary Surfaces.Edges` | Per-cell base face(s) |
| 30 | `offset_distance` slider (val=0.2, range 0–1) | → | `Offset Surface.Distance` | |
| 31 | `Offset Surface.Surface` | → | `Area(#2).Geometry` | Centroid above host plane |
| 32 | `Area(#2).Centroid` | → | `Vector 2Pt.Point A` | |
| 32a | `Area(#3).Centroid` | → | `Vector 2Pt.Point B` | |
| 33 | `Vector 2Pt.Vector` | → | `Amplitude.Vector` | Vector = host normal (length = offset_distance) |
| 34 | `Height of Peaks` slider (val=0.5, range 0–10) | → | `Multiplication.B` | |
| 35 | `Multiplication.Result` | → | `Amplitude.Amplitude` | Per-cell apex height |
| 36 | `Amplitude.Vector` | → | `Move.Motion` | |
| 37 | `Area(#3).Centroid` | → | `Move.Geometry` | Apex base = inset centroid |
| 38 | `Move.Geometry` | → | `Extrude Point.Point` | Per-cell apex |
| 39 | `Boundary Surfaces.Surfaces` | → | `Extrude Point.Base` | Per-cell base face |

There are two orphan sliders on the canvas (unnamed `Number Slider`s at
1.0 and 2.0), plus the `Run` toggle and `Scenario` value-list which gate
writes, not geometry.

### B. Components list (exact kinds)

| Stage | Component | Kind | Notes |
|---|---|---|---|
| 01 | `Rectangle` | `Component_Rectangle` | Base host. X Size, Y Size = two sliders. For production, replace with `Param_Brep` → `Deconstruct Brep` → face — see `../geometry-ingest.md` |
| 01 | `Surface` | `Param_Surface` | Implicit Curve→Surface waypoint — feeds the host into Quad Panels and Populate Geometry |
| 02 | `Quad Panels` *(LunchBox)* | `QuadA` | Inputs: Surface, U Divisions, V Divisions (both wired to the **same** slider — joint UV). Output: Panels (tree of quad cells, one branch per cell). Per skill author's note 5, swap for any other LunchBox panel component without touching downstream (see "Panelizer interchangeability" below) |
| 03 | `Deconstruct Brep` | `Component_DeconstructBrep` | Extracts Edges from each quad cell |
| 03 | `Point On Curve` | `Component_CurvePointAt` | Parameter unwired (defaults t=0) — extracts the start point of each cell edge. Together these reconstruct the cell's 4 corner points |
| 03 | `PolyLine` | `Component_Polyline` | Inputs: Vertices = the per-cell corner points. Output: one **closed polyline per cell** — the stable curve we'll scale, centroid, and base the pyramid on |
| 03 | `Area` (×3) | `Component_AreaProperties` | (a) on the cell polyline → centroid as `Scale.Center` and `Pull Point.Point`; (b) on the offset surface → centroid for the normal-vector trick; (c) on the scaled polyline → centroid as the apex-move base |
| 04 | `Populate Geometry` | `Component_PopulateGeometry` | Inputs: Geometry = host `Surface`, Count = `Amount of Attractor Points` slider. Output: N random points on the host = the **attractor cloud** |
| 04 | `Pull Point` | `Component_PullPoint` | Inputs: Point = cell centroid, Geometry = attractor cloud (`access: list` — natively handles the multi-point case). Output: Distance from each cell centroid to the nearest attractor point |
| 04 | `Sort List` → `Reverse List` → `List Item`(index=0) → `Division` | `Component_SortList`, `Component_ReverseList`, `Component_ListItemVariable`, `Operator_Division` | Builds the dynamic clamp cap: sort distances ascending → reverse → first item = max distance → divide by a slider (val=2) → that's the cap fed into `Minimum.B`. Effectively `cap = max_distance / cap_divisor` |
| 04 | `Minimum` | `Operator_Minimum` | Clamp: `min(per-cell distance, cap)` — keeps far-away cells from squashing the gradient. **Like perforated-attractor's `Minimum` clamp**, but here the cap is derived from the data rather than a free slider |
| 04 | `Bounds` | `Component_NumericBounds` | Reads the clamped-distance range to feed `Remap Numbers.Source` |
| 04 | `Construct Domain` | `Component_ConstructDomain` | Inputs: Domain start = scale-min slider (val=0.1), Domain end = scale-max slider (val=0.9). Note: **forward** order here (start < end) — closer to attractor → smaller distance → smaller scale factor → smaller inset cell. This is the opposite of perforated-attractor's reversed-domain convention |
| 04 | `Remap Numbers` | `Component_RemapNumbers` | Maps clamped per-cell distance into the (0.1, 0.9) scale-factor domain |
| 04 | `Scale` | `Component_Scale` | **Uniform scale**, NOT `Scale NU`. Inputs: Geometry = cell polyline, Center = `Area.Centroid` (same cell), Factor = `Remap Numbers.Mapped`. Output: per-cell **inset polyline** sized by attractor proximity |
| 05 | `Discontinuity` | `Component_CurveDiscontinuity` | Extracts corner points of the scaled inner polyline |
| 05 | `End Points` | `Component_EndPoints` | On the original cell edges → start + end of each edge |
| 05 | `Line` (×2) | `Component_Line` | Builds the per-edge fan: Line A from `End Points.End` → `Discontinuity.Points`; Line B from `End Points.Start` → `Discontinuity.Points`. These connect outer cell vertices to inner scaled corners |
| 05 | `Join Curves` | `Component_JoinCurves` | Joins the two Line streams into the per-cell facet skeleton |
| 05 | `Boundary Surfaces` | `Component_BoundarySurfaces` | Builds planar surfaces from the joined curve skeleton — produces the **cell base face(s)** to be extruded to the apex |
| 05 | `Offset Surface` | `Component_OffsetSurface` | Inputs: Surface = `Scale.Geometry` (the scaled polyline, implicitly converted to a surface), Distance = a slider (val=0.2). Used **only** to obtain a point displaced perpendicular to the host plane — the offset distance becomes the source vector for the per-cell normal |
| 05 | `Vector 2Pt` | `Component_Vector2Pt` | Inputs: Point A = `Area(Offset Surface).Centroid`, Point B = `Area(Scale).Centroid`. Output: vector B − A = the host normal vector, of length = `Offset Distance`. This is a **homemade host-normal extractor** — same role as `Evaluate Surface → Frame → Z-Axis` in `../extrude-direction.md` Pattern B |
| 05 | `Multiplication` | `Component_VariableMultiplication` | Inputs: A = `Remap Numbers.Mapped` (the per-cell scale factor, reused as a per-cell height multiplier!), B = `Height of Peaks` slider. Output: per-cell apex height |
| 05 | `Amplitude` | `Component_VectorAmplitude` | Inputs: Vector = `Vector 2Pt.Vector` (normal), Amplitude = `Multiplication.Result`. Output: per-cell apex offset vector |
| 05 | `Move` | `Component_Move` | Inputs: Geometry = `Area(Scale).Centroid` (the cell's inset centroid), Motion = `Amplitude.Vector`. Output: per-cell **apex point** — the centroid pushed up by `Remap × HeightOfPeaks` |
| 05 | `Extrude Point` *(Extrude To Point)* | `Component_ExtrudeToPoint` | Inputs: Base = `Boundary Surfaces.Surfaces` (cell base face(s)), Point = `Move.Geometry` (apex). Output: per-cell **pyramid**. This is **not** the legacy `Extrude` taking a vector — it's the "extrude to a single 3D point" variant that produces the pyramidal apex collapse |

### B. Sliders / defaults (as authored)

| Slider | Value | Min | Max | Type | Drives | Notes |
|---|---|---|---|---|---|---|
| `X Size` (unnamed) | 10 | 0 | 10 | float | `Rectangle.X Size` | |
| `Y Size` (unnamed) | 10 | 0 | 10 | float | `Rectangle.Y Size` | |
| `UV` divisions (unnamed) | 14 | 0 | 100 | int | `Quad Panels.U Divisions` + `V Divisions` | Joint slider — 14×14 grid |
| `Amount of Attractor Points` | 5 | 0 | 10 | int | `Populate Geometry.Count` | Random points on host |
| `cap_divisor` (unnamed) | 2 | 0 | 10 | float | `Division.B` | `cap = max_distance / cap_divisor` — flattens far-cell gradient |
| `scale_min` (unnamed) | 0.1 | 0 | 0.1 | float | `Construct Domain.Domain start` | Inset scale near attractor |
| `scale_max` (unnamed) | 0.9 | 0 | 1 | float | `Construct Domain.Domain end` | Inset scale far from attractor |
| `offset_distance` (unnamed) | 0.2 | 0 | 1 | float | `Offset Surface.Distance` | Magnitude of the per-cell normal vector (sets the **direction** of the apex push; height is `Remap × Height of Peaks`) |
| `Height of Peaks` | 0.5 | 0 | 10 | float | `Multiplication.B` | Apex height scaler |

### B. Anisotropic / unit-aware sliders (production retune)

| Driver | mm default | mm range | m default | m range | ft default | ft range | Type |
|---|---|---|---|---|---|---|---|
| `width` | 12000 | 1000–50000 | 12 | 1–50 | 40 | 3–160 | float |
| `height` | 6000 | 1000–30000 | 6 | 1–30 | 20 | 3–100 | float |
| `u_count` | 14 | 4–40 | same | | same | | int |
| `v_count` | 8 | 4–30 | same | | same | | int (split joint UV) |
| `attractor_count` | 5 | 1–20 | same | | same | | int |
| `scale_min` | 0.1 | 0.01–0.5 | same | | same | | float |
| `scale_max` | 0.9 | 0.5–0.99 | same | | same | | float |
| `peak_height` | 300 | 50–1500 | 0.3 | 0.05–1.5 | 1.0 | 0.15–5 | float — Height of Peaks |

The joint UV slider on the canvas should split into `u_count` /
`v_count` for any non-square host — per the panelized recipe's
anisotropic retune (`../panelized/recipe.md` § Anisotropic / unit-aware
sliders).

### B. Extrude direction — homemade per-cell normal

Variant B does **not** wire `Extrude To Point` to a world axis — it
builds a per-cell normal vector via the `Offset Surface → Area → Vector
2Pt` trick. This is functionally equivalent to **Pattern B** in
`../extrude-direction.md` (Evaluate Surface → Frame → Z-Axis), just
implemented with a different component chain. The result is a normal
vector whose **direction** is perpendicular to the host plane and whose
**magnitude** = `offset_distance` slider value — then `Amplitude`
rescales it to the per-cell peak height.

For a non-XY host, the chain works correctly **as-is** because Offset
Surface offsets along the host's local normal, not Unit Z. This is one
of the few demo canvases in this skill that's host-orientation-agnostic
out of the box.

For multi-host or curved hosts, the chain still works per cell (each
cell gets its own normal via its own Offset Surface). For very large
cell counts on curved hosts, switching to the more efficient `Brep
Closest Point → Normal → Amplitude` (Pattern C in
`../extrude-direction.md`) reduces compute cost.

### B. Variant: regular pinnacles (uniform pyramids, no attractor)

Per the same idea as perforated-attractor's "regular perforated"
sub-variant: to build pinnacles **without** the attractor field
(uniform pyramids on a quad grid), delete the entire attractor branch
and feed `Scale.Factor` from a single constant slider, and feed
`Multiplication.A` (or skip Multiplication and feed Amplitude.Amplitude
directly) from `Height of Peaks` alone.

**Delete:**
- `Populate Geometry`
- `Pull Point`
- `Sort List` + `Reverse List` + `List Item` + `Division`
- `Minimum`
- `Bounds`
- `Construct Domain`
- `Remap Numbers`

**Replace with:** one `inset_scale` slider (e.g. 0.5) → `Scale.Factor`,
and route `Height of Peaks` directly into `Amplitude.Amplitude` (skip
`Multiplication` since there's no per-cell remap to multiply by).

| Driver | Default | Range | Notes |
|---|---|---|---|
| `inset_scale` | 0.5 | 0.1–0.9 | Single uniform scale factor for all cells |
| `peak_height` | 0.5 | 0.05–2 | Single uniform apex height |

### B. Host-ingest variant (production replacement for stage 01)

Same as Variant A — replace `Rectangle` + `Param_Surface` with a host
Brep ref + `Deconstruct Brep` + `List Item` to extract a face. See
`../geometry-ingest.md` § GUID handoff.

### B. Multi-surface batching

Use the same `Merge Multiple` / `Geometry Pipeline` patterns as
perforated-attractor — Quad Panels takes a list of hosts and emits a
tree (one branch per host); the downstream chain (Deconstruct →
Polyline → Area → Scale → Extrude To Point) is tree-aware.

See `../perforated-attractor/recipe.md` § Multi-surface batching for
the full pattern.

### B. Stages overview (context — read AFTER the Wiring table)

```
01. Base Rectangle ──→ 02. Quad Panelize ──→ 03. Per-Cell Polyline + Centroid
                                                          ↓
                                              04. Attractor Field → Remap → per-cell scale + per-cell height
                                                          ↓
                                              05. Per-cell pyramid (Boundary Surfaces + Extrude To Point)
```

---

## Panelizer interchangeability (Variant B only)

Per skill author's note 5 and the panelizer-interchangeability pattern
in `../recipe-modification-patterns.md` § Canonical patterns, the
LunchBox panel components are drop-in interchangeable in Variant B:

| Variant | Component (LunchBox) | Pinnacle shape | Notes |
|---|---|---|---|
| **Quad (this canvas)** | `Quad Panels A` (`QuadA`) | 4-sided pyramid | Default |
| Triangular A | `Triangle Panels A` (`TriA`) | 3-sided pyramid (tetrahedron) | Same downstream chain |
| Triangular B | `Triangle Panels B` (`TriB`) | 3-sided pyramid, alternating | |
| Diamond | `Diamond` | Diamond-base pyramid | |
| Hex | `Hexagon Cells` (`HexCells`) | 6-sided pyramid | |
| Staggered | `Staggered Quads` (`QuadStag`) | Brick-pattern pyramids | |
| Quad native fallback | `Divide Domain² + Isotrim` | Quad-base pyramid | If LunchBox absent |

Variant A does **not** use a panelizer — its pleat geometry comes from
rotate + mirror of a single Rectangle, so panelizer swaps don't apply.

## Composition — folded variants of compound recipes

| User asks for | Compose | Order |
|---|---|---|
| "Folded perforated screen" | Variant A + perforated-attractor's per-cell hole | Build folded base (each pleat trapezoid becomes a "cell"); apply `Scale` + `Boundary Surfaces` per pleat for the hole |
| "Kinetic origami pyramids" | Variant B + kinetic wrapper | Drive `Height of Peaks` from `state × max_height` |
| "Origami pyramids on a curved host" | Variant B + Pattern C extrude direction | Replace the Offset Surface normal-trick with `Brep Closest Point → Normal → Amplitude` per cell |

See `../recipe-modification-patterns.md` § Compound recipes for the
composition framework.

---

## Anti-patterns (across both variants)

| Anti-pattern | Why wrong | Use instead |
|---|---|---|
| Hard-coded `Unit Z` extrude for non-XY hosts (Variant A) | Extrudes pleats straight up instead of outward through the wall | Read what's wired to `Extrude.Direction` first; for non-XY hosts use Pattern A/B/C in `../extrude-direction.md` |
| Building folds by per-vertex `Rotate` (Variant A) | Loses pleat continuity; folds don't meet at ridges | Use the canvas's rotate-then-mirror-then-array idiom — one rectangle, one Rotate Axis, one Mirror, two Series |
| Using `Surface Split` to create pleat geometry (Variant A) | Wrong operation — pleats are about rotation + mirror + array, not subtraction | Stick to the rotate-mirror-array chain |
| `Polygon` as the pinnacle base shape (Variant B) | Pinnacle base isn't derived from the cell — visually inconsistent across panelizer swaps | Always derive the inset from the cell polyline via `Area` → `Scale` (the cell-offset-from-panel pattern from `../recipe-modification-patterns.md`) |
| `Extrude Linear` taking a vector | Wrong port type — `Extrude Linear.Axis` expects a `Line`, not a `Vector` | Variant A: use legacy `Extrude` (`Component_Extrude`). Variant B: use `Extrude To Point` (`Component_ExtrudeToPoint`) as the canvas does — it pyramidalizes to a 3D point in one step |
| `Boundary Surfaces` for Variant A's flat pleat surface | Redundant — `Param_Surface` already does the implicit Curve→Surface conversion for the pleat curves | Wire the moved-curve tree directly into `Param_Surface`, then into `Extrude.Base` |
| Reusing one rectangle vertex by **index** without checking CCW order (Variant A) | The `Axis of Rotation`, `Point for Plane`, `Point 1` sliders pick by integer index — if the rectangle is recreated with a different vertex order, the fold geometry inverts | When transcribing for a new host, verify the picked edge/vertex visually; increment slider by 1 if the construction reverses |
| Driving Variant B's `Construct Domain` in reversed order (start > end) | Forward order is intentional here: smaller distance → smaller scale → smaller inset cell near attractor. Reversing would invert the spatial gradient | Match the canvas: `scale_min` → Domain start, `scale_max` → Domain end (forward, unlike perforated-attractor's reversed convention) |

## Multi-surface batching

Same as perforated/panelized: `Merge Multiple` of hosts (Pattern 2) or
`Geometry Pipeline` by layer (Pattern 3). See
[`../perforated-attractor/recipe.md`](../perforated-attractor/recipe.md)
§ Multi-surface batching for the full pattern.

Variant A's `Rotate Axis` + `Mirror` chain is tree-aware (will fold each
host's rectangle independently). Variant B's `Quad Panels` + downstream
is tree-aware in the standard panelize-then-attractor sense.

## Cross-references

- Source canvases: [`Folded.gh`](./Folded.gh) + [`Origami Faceted.gh`](./Origami%20Faceted.gh)
- Reference renders (Folded): [`Folded (Ghosted).png`](./Folded%20%28Ghosted%29.png), [`Folded (Arctic).png`](./Folded%20%28Arctic%29.png)
- Reference renders (Origami Faceted): [`Origami Faceted (Perspective Ghosted).png`](./Origami%20Faceted%20%28Perspective%20Ghosted%29.png), [`Origami Faceted (Perspective Arctic).png`](./Origami%20Faceted%20%28Perspective%20Arctic%29.png), [`Origami Faceted (Top Ghosted).png`](./Origami%20Faceted%20%28Top%20Ghosted%29.png), [`Origami Faceted (Top Arctic).png`](./Origami%20Faceted%20%28Top%20Arctic%29.png) — the top view is where the diagonal "X" per cell reads most clearly
- Extrude direction (for the thickness/normal vector): [`../extrude-direction.md`](../extrude-direction.md)
- Bridge quirks (Extrude vs Extrude Linear traps): [`../bridge-quirks.md`](../bridge-quirks.md)
- Cell-offset-from-panel pattern (Variant B): [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Canonical patterns
- Panelizer interchangeability (Variant B): [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Canonical patterns
- Foundation recipe both variants extend: [`../panelized/recipe.md`](../panelized/recipe.md)
- Attractor-driven sibling (perforated): [`../perforated-attractor/recipe.md`](../perforated-attractor/recipe.md) — Variant B borrows the Pull Point + Minimum + Bounds + Remap idiom
- Kinetic version (folded that opens/closes with `state`): [`../kinetic/recipe.md`](../kinetic/recipe.md)

## Verification

### Variant A — Folded
- `gh_canvas_summary` → 0 errors / 0 warnings (canvas was clean at inspection)
- `gh_get_runtime_messages(extrude_guid)` → no red/orange messages
- Visual: capture from a low side angle in Perspective; confirm parallel
  ridges running in Y with V-shaped profile in X; compare against
  [`Folded (Arctic).png`](./Folded%20%28Arctic%29.png) and [`Folded
  (Ghosted).png`](./Folded%20%28Ghosted%29.png)
- Scale sanity: pleat width (X step) ≈ host width / `X Count`; if pleats
  look ~host-sized, suspect `Distance` picking wrong vertices (check the
  two `Point 1` indices)

### Variant B — Origami Faceted
- `gh_canvas_summary` → 0 errors / 0 warnings (canvas was clean at inspection)
- `gh_get_runtime_messages(extrude_point_guid)` → no red/orange messages
- Visual: capture from **two angles**, because the geometry reads
  differently in each:
  - **Perspective view**: confirm field of pyramidal pinnacles with
    apex-height varying across the surface. Compare against
    [`Origami Faceted (Perspective Arctic).png`](./Origami%20Faceted%20%28Perspective%20Arctic%29.png)
    and [`Origami Faceted (Perspective Ghosted).png`](./Origami%20Faceted%20%28Perspective%20Ghosted%29.png)
  - **Top view**: confirm the diagonal "X" pattern per cell (4
    triangular facets sharing the apex above the centroid) — the fold
    geometry reads more clearly from above than in perspective. Compare
    against [`Origami Faceted (Top Arctic).png`](./Origami%20Faceted%20%28Top%20Arctic%29.png)
    and [`Origami Faceted (Top Ghosted).png`](./Origami%20Faceted%20%28Top%20Ghosted%29.png)
- Scale sanity: apex height ≤ ~1/3 of cell width for visually believable
  pinnacles. If apexes spike very tall, suspect `Height of Peaks` set
  too high relative to `Quad Panels` UV count
- Attractor sanity: with `Amount of Attractor Points` ≥ 2, you should
  see clear "tall zones" near attractor locations and "flat zones" far
  from them. If the field looks uniform, the `Minimum` clamp may be
  squashing the gradient (drop `cap_divisor` toward 1)

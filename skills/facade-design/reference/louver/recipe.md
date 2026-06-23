# Louvers / Brise-Soleil / Fins — Recipe

> **Source canvas:** `Basic Louver.gh` + `Wavy Louver Example.gh` (this folder).
> **Status:** validated (transcribed by structural inspection 2026-06-22).
>
> Two reference canvases live here, capturing two distinct ways of
> generating a louver array. **Variant A — Basic** is a flat host
> subdivided into strips, each strip rotated about its own centerline
> axis and extruded by host-normal × thickness. **Variant B — Wavy** is
> a procedurally generated wavy base lofted from two columns of
> jittered control points, then contoured and lofted-to-flat to give
> each louver a smooth wavy-to-planar transition. They share the
> *louver array* idea but compose almost no components in common — pick
> the variant whose generator matches the user's intent.

## What it builds

An ordered array of louver blades — either (A) flat rectangular fins
tilted to a uniform angle, or (B) wavy fins whose curvature fades from
one edge to the other. Both end at the same place: a row of thickened
blades, repeated along one axis of the host.

A louver array is *not* a panelized surface — there is no per-cell
hole or subdivision in U×V. The host is split into strips in **one**
direction only (U=N, V=1, or U=1, V=N), and each strip becomes one
blade.

## The two variants

| | Variant A — Basic | Variant B — Wavy |
|---|---|---|
| Host source | Authored rectangle (slider-driven) | Procedural wavy surface (two interpolated curves, lofted) |
| Strip subdivision | `Divide Domain² + Isotrim` (U=N, V=1) | `Contour` cuts the wavy Loft into N curves |
| Per-blade variation | Each strip rotated about a per-strip centerline axis | Each contour curve lofted to its XZ-projection (wavy→flat blend) |
| Extrude direction | `Evaluate Surface.Normal` of the pre-rotation strip (host-normal aware) | `Unit X × thickness` (matches the wavy base's natural normal) |
| Sliders | 6 (X size, Y size, U count, V count, Line length, Degree of rotation, Thickness) + 1 MD slider | 6 (Series step, Series count, column offset, random seed, contour distance, thickness) |
| Visual end-state | Tilted slab louvers (e.g. brise-soleil rows) | Curved blades with a smooth wave fading to flat profile |

**Both variants honour the authoring rule for extrude direction:** the
extrusion vector wired into `Extrude.Direction` is **normal to the
initial orientation of the shape being extruded** — never a hardcoded
world axis assumed in advance. In A, that's the per-strip
`Evaluate Surface.Normal` (which happens to be +Z because the
Rectangle lies on World XY). In B, the wavy Loft's natural facing is
+X, so `Unit X × thickness` is the correct host-normal. When
transcribing for a different host orientation, re-derive the normal —
see `../extrude-direction.md`.

---

## Variant A — Basic

### 5 stages

```
01. Base Surface (Rectangle → Param_Surface)
        ↓
02. Strip Subdivision (Divide Domain² → Isotrim, U=N, V=1)
        ↓
03. Per-Strip Rotation Axis (Evaluate Surface + Unit Y + Line SDL)
        ↓
04. Per-Strip Rotation (Degree → Radians → Rotate Axis)
        ↓
05. Thickness Extrude (Amplitude(normal, thickness) → Extrude)
```

### Components list (exact kinds)

| Stage | Component | Kind | Why this one |
|---|---|---|---|
| 01 | `Rectangle` | `Component_Rectangle` | Generates the demo base curve on a plane. Inputs: Plane (unwired → WorldXY), X Size, Y Size, Radius. Output: Rectangle (curve), Length. For production, replace with a Rhino-host `Param_Brep` / `Param_Surface` ref — see `../geometry-ingest.md` |
| 01 | `Surface` (param) | `Param_Surface` | Implicit Curve→Surface converter — accepts the Rectangle curve and emits a planar Surface for the panelizer. Replaces `Boundary Surfaces` |
| 02 | `Divide Domain²` | `Component_Divide2DInterval` | Subdivides the surface's 2D parameter domain into segments. Inputs: Domain (from Param_Surface), U Count, V Count. Output: Segments (list of 2D sub-domains) |
| 02 | `Isotrim` | `Component_IsoTrim` | Extracts each sub-domain as its own sub-surface. Inputs: Surface (here nicknamed "Feed OG surface" — the original Param_Surface), Domain (list from Divide Domain²). Output: list of strip surfaces |
| 03 | `Evaluate Surface` | `Component_EvaluateSurface` | Samples a point + frame + normal on each strip at a UV location. Inputs: Surface (Isotrim output), Point (MD Slider UV). Outputs used: Point (axis start), Normal (extrude direction source) |
| 03 | `MD Slider` | `GH_MultiDimensionalSlider` | 2D slider authored over [0,1]² — picks the UV location at which Evaluate Surface samples each strip. In the canvas it sits near (0.5, 0.5) — the strip's parametric midpoint |
| 03 | `Unit Y` | `Component_UnitVectorY` | World Y unit vector — the direction the rotation-axis line runs. Factor unwired (=1.0). Strips run along Y on this WorldXY host, so the axis is each strip's long-axis |
| 03 | `Line SDL` | `Component_LineSDL` | Start-Direction-Length line. Inputs: Start (Evaluate Surface.Point), Direction (Unit Y vector), Length (slider). Output: Line — the per-strip rotation axis |
| 04 | `Radians` | `FuncToRadians` | Converts the Degree slider to radians (Rotate Axis.Angle expects radians) |
| 04 | `Rotate Axis` | `Component_RotateAxis` | Rotates each strip surface about its own per-strip line axis. Inputs: Geometry (Isotrim.Surface), Angle (radians), Axis (Line) |
| 05 | `Amplitude` | `Component_VectorAmplitude` | Scales the strip's normal by the Thickness slider. Inputs: Vector (Evaluate Surface.Normal), Amplitude (Thickness slider) |
| 05 | `Extrude` *(legacy)* | `Component_Extrude` | Thickens each rotated strip. Inputs: Base (Rotate Axis.Geometry), Direction (Amplitude.Vector). **Not** `Extrude Linear` — see `../bridge-quirks.md` § Component name traps |

### Wiring (from canvas inspection)

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 1 | `X Size` slider (value=10, range 0–10, int) | → | `Rectangle.X Size` | |
| 2 | `Y Size` slider (value=5, range 0–10, int) | → | `Rectangle.Y Size` | Non-square (rectangle 10×5) |
| 3 | (Rectangle.Plane unwired) | → | (defaults to World XY plane) | Base lies flat on XY |
| 4 | `Rectangle.Rectangle` (curve) | → | `Param_Surface` | Implicit Curve→Surface conversion |
| 5 | `Param_Surface` | → | `Divide Domain².Domain` | Domain of the host's 2D parameter space |
| 5a | `Param_Surface` | → | `Isotrim.Surface` (nick "Feed OG surface") | Same host, fed forward to extract subsets |
| 6 | `U Count` slider (value=19, range 0–31, int) | → | `Divide Domain².U Count` | 19 strips along U |
| 7 | `V Count` slider (value=1, range 0–100, int) | → | `Divide Domain².V Count` | 1 segment along V — strips span full V |
| 8 | `Divide Domain².Segments` (list) | → | `Isotrim.Domain` (item) | Iterates → list of 19 strip surfaces |
| 9 | `Isotrim.Surface` | → | `Evaluate Surface.Surface` | Per-strip sampling target |
| 9a | `Isotrim.Surface` | → | `Rotate Axis.Geometry` | Same strip — geometry to rotate |
| 10 | `MD Slider` (2D point ≈ 0.5, 0.5) | → | `Evaluate Surface.Point` | UV location to sample on each strip |
| 11 | `Evaluate Surface.Point` | → | `Line SDL.Start` | Per-strip line origin |
| 11a | `Evaluate Surface.Normal` | → | `Amplitude.Vector` | Per-strip outward normal (=+Z on WorldXY host) |
| 12 | `Unit Y.Unit vector` (Factor unwired = 1.0) | → | `Line SDL.Direction` | Axis runs along world Y (strip's long axis) |
| 13 | `Length` slider (value=1, range 0–1, int) | → | `Line SDL.Length` | Line length (only magnitude matters; axis is infinite) |
| 14 | `Line SDL.Line` | → | `Rotate Axis.Axis` | Rotation axis = the per-strip Y-line |
| 15 | `Degree of Rotation` slider (value=70, range 0–100, int) | → | `Radians.Degrees` | Degrees-to-radians conversion |
| 16 | `Radians.Radians` | → | `Rotate Axis.Angle` | Rotation angle (uniform across all strips) |
| 17 | `Rotate Axis.Geometry` | → | `Extrude.Base` | The rotated strip — what gets thickened |
| 18 | `Thickness` slider (value=0.115, range 0–1, float 3dp) | → | `Amplitude.Amplitude` | Scales the per-strip normal |
| 19 | `Amplitude.Vector` | → | `Extrude.Direction` | Extrude vector = strip-normal × Thickness |

### Sliders / defaults (as authored)

| Slider | Value | Min | Max | Type | Drives | Notes |
|---|---|---|---|---|---|---|
| `X Size` | 10 | 0 | 10 | int | `Rectangle.X Size` | At max — likely "10 m" of demo facade |
| `Y Size` | 5 | 0 | 10 | int | `Rectangle.Y Size` | Half X — rectangular host, not square |
| `U Count` | 19 | 0 | 31 | int | `Divide Domain².U Count` | 19 louvers along U |
| `V Count` | 1 | 0 | 100 | int | `Divide Domain².V Count` | Single span along V — full-height strips |
| `Length` (Line SDL) | 1 | 0 | 1 | int | `Line SDL.Length` | Axis line length — only the direction matters; range is effectively a 0/1 toggle |
| `Degree of Rotation` | 70 | 0 | 100 | int | `Radians.Degrees` | Uniform tilt angle (degrees). 70° is nearly closed louvers |
| `MD Slider` | (2D point) | 0,0 | 1,1 | float | `Evaluate Surface.Point` | UV sample point — defaults to strip's parametric midpoint |
| `Thickness` | 0.115 | 0 | 1 | float (3dp) | `Amplitude.Amplitude` | Louver thickness — unit-agnostic; 0.115 m if doc units = m |

### Anti-patterns (Variant A)

| Anti-pattern | Why wrong | Use instead |
|---|---|---|
| Hard-coded `Unit Z` extrude | Wrong for non-WorldXY hosts — extrudes louvers straight up instead of outward through the host | Read `Evaluate Surface.Normal` of the unrotated strip (as the canvas does) — the chain is host-normal-aware by design. For oblique/curved hosts, see Pattern B/C in `../extrude-direction.md` |
| Normal taken from the **rotated** strip | The post-rotation normal points along the tilted face; extruding by it makes thickness perpendicular to the blade (a 3D wedge) instead of host-normal | Wire `Evaluate Surface` to the **pre-rotation** Isotrim output (as the canvas does) — keeps thickness aligned with the host axis |
| Setting `V Count` ≥ 2 with `Rotate Axis` along Y | Each U×V sub-strip rotates about a Y-line through its own UV midpoint — adjacent cells overlap | Keep `V Count = 1` for horizontal slats / vertical fins. For per-cell rotated panels, switch to the `kinetic-panel` recipe |
| `Extrude Linear` taking a vector | Wrong port type — `Extrude Linear.Axis` expects a `Line`, not a `Vector` | Use legacy `Extrude` (`Component_Extrude`, nickname `"Extr"`) which takes `Vector` on `Direction` directly. See `../bridge-quirks.md` § Component name traps |

---

## Variant B — Wavy

### 5 stages

```
01. Procedural Wavy Base (two columns of jittered points → Interpolate → Loft)
        ↓
02. Contour into Strips (Unit X × distance)
        ↓
03. Project to Flat Plane (XZ Plane → Project)
        ↓
04. Loft Pairs — Wavy → Flat (Loft #2 over [Projected, Original])
        ↓
05. Thickness Extrude (Unit X × thickness)
```

### Components list (exact kinds)

| Stage | Component | Kind | Why this one |
|---|---|---|---|
| 01 | `Construct Point` | `Component_ConstructPoint` | Origin (X=Y=Z unwired = 0,0,0). The seed for both columns of control points |
| 01 | `Series` | `Component_Series` | Generates a list of evenly-spaced numbers (Start unwired = 0, Step = slider, Count = slider). Feeds Unit Z.Factor → vertical heights |
| 01 | `Unit Z` | `Component_UnitVectorZ` | World Z × Series → list of vertical offset vectors |
| 01 | `Move` #1 | `Component_Move` | Origin point moved by each Z offset → column of `count` points stacked in Z |
| 01 | `Unit X` *(of three on canvas)* | `Component_UnitVectorX` | World X × column-offset slider → vector shifting the second column away from the first along X |
| 01 | `Move` #2 | `Component_Move` | Column #1's points moved by Unit X → column #2 (parallel, shifted in X) |
| 01 | `List Length` | `Component_ListLength` | Reads the per-column point count (multi-source input gives per-branch length = `count`). Drives Random.Number and Split List.Index |
| 01 | `Random` | `Component_Random` | Generates `count` random numbers in [0, 1] (Range unwired) — one Y-jitter value per point in each column. Seed slider for reproducibility |
| 01 | `Unit Y` | `Component_UnitVectorY` | World Y × Random → list of Y-jitter vectors |
| 01 | `Move` #3 | `Component_Move` | (Column #1 + Column #2) points moved by Y-jitter → 2N randomly perturbed points |
| 01 | `Split List` | `Component_SplitList` | Splits the 2N perturbed points at index = `count` → List A (column 1 jittered), List B (column 2 jittered) |
| 01 | `Interpolate (t)` ×2 | `Component_InterpCurveWithTangents` | Two interpolated curves, one through each split list — the wavy edges of the base surface |
| 01 | `Loft` #1 | `Component_LoftSurface` | Lofts the two interp curves → the wavy base Brep |
| 02 | `Unit X` *(second of three)* | `Component_UnitVectorX` | World X × 1.0 (Factor unwired) — the contour-plane normal direction. Stays constant; not a slider |
| 02 | `Contour` | `Component_Contour1` | Slices the wavy Brep with parallel planes perpendicular to Unit X, spaced by `contour_dist` slider. Outputs a tree of curves — one per slice |
| 03 | `XZ Plane` | `Component_XZPlane` | World XZ plane positioned at the origin point — the destination plane for projection |
| 03 | `Project` | `Component_Project` | Projects each contour curve onto the XZ plane (along plane's normal = Y). Produces flat copies of every wavy slice |
| 04 | `Loft` #2 | `Component_LoftSurface` | Curves input takes **both** the projected (flat) curves and the original contour (wavy) curves as a merged list. Lofts each pair → a louver strip that transitions wavy→flat |
| 05 | `Unit X` *(third of three)* | `Component_UnitVectorX` | World X × `thickness` slider — extrusion vector (host-normal, since the lofted strips face X) |
| 05 | `Extrude` *(legacy)* | `Component_Extrude` | Thickens each lofted strip along X. Inputs: Base (Loft #2.Loft), Direction (Unit X × thickness) |

### Wiring (from canvas inspection)

Read in two halves — stage 01 (procedural base) is dense; stages 02–05 are linear.

#### Stage 01 — wavy base generation

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 1 | (Construct Point inputs all unwired) | → | (defaults: 0,0,0) | Origin point |
| 2 | `Step` slider (value=1, range 0–10000, int) | → | `Series.Step` | Vertical spacing between control points |
| 3 | `Count` slider (value=5, range 0–10, int) | → | `Series.Count` | Number of control points per column |
| 4 | `Series.Series` | → | `Unit Z.Factor` | Z-vector lengths (0, 1, 2, 3, 4) |
| 5 | `Construct Point.Point` | → | `Move #1.Geometry` | Origin gets moved by each Z offset |
| 5a | `Construct Point.Point` | → | `Contour.Point` | Same origin used as the contour reference point (stage 02) |
| 5b | `Construct Point.Point` | → | `XZ Plane.Origin` | Same origin used to position the projection plane (stage 03) |
| 6 | `Unit Z.Unit vector` | → | `Move #1.Motion` | Column 1: origin × N stacked Z heights |
| 7 | `Move #1.Geometry` | → | `Move #2.Geometry` | Column 1 → second Move (will be shifted in X) |
| 8 | `Column Offset` slider (value=7, range 0–100, int) | → | `Unit X #3.Factor` | X-distance between the two columns |
| 9 | `Unit X #3.Unit vector` | → | `Move #2.Motion` | Shifts column 1's points by (7, 0, 0) → column 2 |
| 10 | `Move #1.Geometry` | → | `Move #3.Geometry` (append) | Column 1 → into the merge that feeds Move #3 |
| 10a | `Move #2.Geometry` | → | `Move #3.Geometry` (append) | Column 2 → into the same merge; total 2N points |
| 11 | `Move #1.Geometry` | → | `List Length.List` (append) | Per-branch list length read |
| 11a | `Move #2.Geometry` | → | `List Length.List` (append) | Second branch; per-branch list access → output is `count` per branch |
| 12 | `List Length.Length` | → | `Random.Number` | How many random values to generate (= count per column) |
| 12a | `List Length.Length` | → | `Split List.Index` | Split index = `count` |
| 13 | `Seed` slider (value=2, range 0–10, int) | → | `Random.Seed` | Random reproducibility |
| 14 | (Random.Range unwired) | → | (defaults to 0.0 → 1.0) | Y-jitter magnitude domain |
| 15 | `Random.Random` (list) | → | `Unit Y.Factor` (item) | Broadcasts → list of Unit Y vectors with random factors |
| 16 | `Unit Y.Unit vector` | → | `Move #3.Motion` | Y-jitter applied to each point in the merged 2N list |
| 17 | `Move #3.Geometry` | → | `Split List.List` | The jittered point cloud (2N points) |
| 18 | `Split List.List A` | → | `Interpolate (t) #A.Vertices` | First N points → first wavy curve |
| 18a | `Split List.List B` | → | `Interpolate (t) #B.Vertices` | Second N points → second wavy curve |
| 19 | `Interpolate (t) #A.Curve` | → | `Loft #1.Curves` (append) | First edge of base surface |
| 19a | `Interpolate (t) #B.Curve` | → | `Loft #1.Curves` (append) | Second edge — Loft #1 emits the wavy Brep |

#### Stages 02 – 05 — contour, project, loft, extrude

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 20 | `Loft #1.Loft` | → | `Contour.Shape` | The wavy base Brep |
| 21 | `Construct Point.Point` | → | `Contour.Point` | Already wired (see 5a) — reference origin for the contour-plane stack |
| 22 | `Unit X #1.Unit vector` (Factor unwired = 1.0) | → | `Contour.Direction` | Contour planes are perpendicular to +X |
| 23 | `Contour Distance` slider (value=0.5, range 0–0.501, float) | → | `Contour.Distance` | Spacing between contour planes. Adjacent panel note `0.5 to 0.75` flags a recommended range that **extends past the slider's authored max** — bump the slider's max if you want to explore it |
| 24 | `Contour.Contours` (tree of curves) | → | `Project.Geometry` | The wavy slice curves |
| 24a | `Contour.Contours` | → | `Loft #2.Curves` (append) | Same tree — wired in directly as one input of the final loft |
| 25 | `XZ Plane.Plane` | → | `Project.Plane` | XZ plane at origin — target plane for projection |
| 26 | `Project.Geometry` (flat projected curves) | → | `Loft #2.Curves` (append) | Pair partner for each wavy contour curve |
| 27 | `Loft #2.Loft` | → | `Extrude.Base` | The wavy-to-flat louver strips |
| 28 | `Thickness` slider (value=0.1, range 0–1, float) | → | `Unit X #2.Factor` | Extrusion magnitude |
| 29 | `Unit X #2.Unit vector` | → | `Extrude.Direction` | Extrude along +X by `thickness` |

### Sliders / defaults (as authored)

| Slider | Value | Min | Max | Type | Drives | Notes |
|---|---|---|---|---|---|---|
| `Step` (Series) | 1 | 0 | 10000 | int | `Series.Step` | Vertical spacing between control points. Very wide range — typical use ≪ 100 |
| `Count` (Series) | 5 | 0 | 10 | int | `Series.Count` | Number of control points per column → determines wavy curve resolution |
| `Column Offset` (Unit X #3) | 7 | 0 | 100 | int | `Unit X #3.Factor` → `Move #2.Motion` | Horizontal distance between the two control-point columns (= width of the wavy base) |
| `Seed` (Random) | 2 | 0 | 10 | int | `Random.Seed` | Reproducibility seed for the Y-jitter |
| `Contour Distance` | 0.5 | 0 | 0.501 | float | `Contour.Distance` | Spacing between louver slices. **Slider max is 0.501** — the adjacent panel reads `0.5 to 0.75`, so widen the range to explore looser louver spacing |
| `Thickness` (Unit X #2) | 0.1 | 0 | 1 | float | `Unit X #2.Factor` → `Extrude.Direction` | Louver thickness along X |

Random.Range is left unwired → defaults to (0.0, 1.0). The Y-jitter
magnitude is therefore capped at 1 unit; no slider exposes this.
For larger waves, wire a `Construct Domain(0, jitter_max_slider)` into
`Random.Range`.

### Anti-patterns (Variant B)

| Anti-pattern | Why wrong | Use instead |
|---|---|---|
| Treating `Loft #1` as a host you ingest from Rhino | The wavy base here is **procedurally generated** from sliders + random — there is no host Brep to reference. For a real-host wavy facade, swap stage 01 for a `Param_Brep` GUID ref (see `../geometry-ingest.md`) and feed it directly into `Contour.Shape` |
| Hard-coded `Unit Z` extrude | Wrong direction — the wavy base faces +X (its lofted curves run vertically in Y/Z), so thickness goes through it via +X | Read what's wired to `Extrude.Direction` (the canvas wires `Unit X × thickness`). For a wavy host with a different facing, re-derive the host-normal — see `../extrude-direction.md` |
| `Project` plane normal NOT aligned with `Contour.Direction` | The flat copies wouldn't sit in the plane of each contour — the loft pairs would twist | Both must use the same axis: `Contour.Direction = Unit X` AND `XZ Plane` (normal = Y); the projection is *along* Y onto the XZ plane, which preserves each contour's X position and flattens Y → planar copies of the wavy slices |
| Splitting at a hardcoded index instead of `List Length` | Locks the split to a specific count; changing the `Count` slider breaks the chain | Wire `List Length.Length` (per-branch) into `Split List.Index` so the split tracks `Series.Count` automatically |
| Wiring `Random.Random` into `Move.Motion` directly | `Move.Motion` is a `Vector` input; Random outputs `Number` | Route through `Unit Y(Factor=Random)` to convert random scalars into Y-vectors before feeding `Move` |

---

## Cross-cutting

### Extrude direction — authoring rule

Both variants honour the same rule: the extrude vector must be
**normal to the initial orientation of the shape being extruded**.
Never assume `Unit Z`.

- Variant A reads it via `Evaluate Surface.Normal` of the pre-rotation
  strip (Pattern B / host-normal aware — works on any flat host).
- Variant B uses `Unit X × thickness` because the wavy Loft's
  faces naturally orient toward +X (the two interp curves run in YZ,
  so the lofted surface's normal is along ±X).

When transcribing for a host with a different facing, re-derive the
normal:

| Host orientation | What to wire into `Extrude.Direction` | Pattern |
|---|---|---|
| WorldXY host (Variant A's case) | `Evaluate Surface.Normal` of pre-rotation strip, or `Unit Z(Factor=thickness)` | A/B |
| Vertical wall facing +X (Variant B's case) | `Unit X(Factor=thickness)` | A |
| Vertical wall facing +Y | `Unit Y(Factor=thickness)` | A |
| Oblique single host | `Evaluate Surface → Frame → Deconstruct Plane → Z-Axis → Amplitude(thickness)` | B |
| Multi-host / curved host | `Brep Closest Point → Normal → Amplitude(thickness)` (per strip) | C |

See `../extrude-direction.md` for the full diagrams.

### Recipe-modification touch-points

These connect louver to the patterns catalogued in
`../recipe-modification-patterns.md`:

- **Panelizer interchangeability**: Variant A uses
  `Divide Domain² + Isotrim` (the **native fallback panelizer** named
  in panelized's "Components list" table). For LunchBox hosts that
  emit strips, `Quad Panels A` with `V Divisions=1` is the drop-in
  equivalent.
- **Cell-from-panel pattern** does **not** apply to louvers — there's
  no Area-centroid-Scale chain. Louvers transform the *strip itself*,
  not an inset of it.
- **Slider-only sun-responsive variant**: replace the constant
  `Degree of Rotation` slider in Variant A with a per-strip angle
  driven by `LB SunPath` (Ladybug) — same chain, the slider just
  becomes a list of computed values.

### Multi-surface batching

Same rule as everywhere: build the chain **once** on a list of host
surfaces — both `Isotrim` (A) and `Contour` (B) accept a list and emit
a tree (one branch per host). Do not duplicate the chain per host.

For Variant A, replacing stage 01:

```
[List of N host GUIDs] → Param_Brep → Param_Surface → Divide Domain² + Isotrim
                                                              ↓
                                                       (rest of chain, tree-aware)
```

For Variant B, the procedural stage 01 doesn't ingest hosts. To
multi-host the wavy concept, replace `Loft #1` entirely with a
`Param_Brep` referencing the host wavy surfaces from Rhino, then feed
that directly into `Contour.Shape`. See
`../perforated-attractor/recipe.md` § Multi-surface batching for the
Merge-Multiple / Geometry-Pipeline patterns.

### Cross-references

- Source canvases: [`Basic Louver.gh`](./Basic%20Louver.gh) +
  [`Wavy Louver Example.gh`](./Wavy%20Louver%20Example.gh)
- Reference renders:
  [`Basic Louver (Ghosted).png`](./Basic%20Louver%20%28Ghosted%29.png),
  [`Basic Louver (Arctic).png`](./Basic%20Louver%20%28Arctic%29.png),
  [`Wavy Louver (Ghosted).png`](./Wavy%20Louver%20%28Ghosted%29.png),
  [`Wavy Louver (Arctic).png`](./Wavy%20Louver%20%28Arctic%29.png)
- Extrude direction (host-aware): [`../extrude-direction.md`](../extrude-direction.md)
- Host ingest (production stage 01): [`../geometry-ingest.md`](../geometry-ingest.md)
- Recipe-modification patterns: [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md)
- Component name traps (Extrude vs Extrude Linear): [`../bridge-quirks.md`](../bridge-quirks.md) § Component name traps
- Related typologies that share the strip-subdivision idea:
  [`../panelized/recipe.md`](../panelized/recipe.md),
  [`../kinetic/recipe.md`](../kinetic/recipe.md)

### Verification

For either variant after the final `gh_recompute`:

- `gh_canvas_summary` → 0 errors / 0 warnings (both canvases were clean at inspection)
- `gh_get_runtime_messages(extrude_guid)` → no red/orange messages
- Visual:
  - Variant A — capture from a viewpoint that shows fin tilt and
    thickness; compare against `Basic Louver (Ghosted).png` /
    `Basic Louver (Arctic).png`
  - Variant B — capture from a viewpoint that shows the wavy-to-flat
    transition; compare against `Wavy Louver (Ghosted).png` /
    `Wavy Louver (Arctic).png`
- Scale sanity:
  - Variant A — louver count should match `U Count`; tilt should match
    `Degree of Rotation`; thickness should look ~1/host-min-dim × `Thickness`
  - Variant B — louver count should be roughly host-X-extent /
    `Contour Distance`; thickness should be `Thickness` units along X

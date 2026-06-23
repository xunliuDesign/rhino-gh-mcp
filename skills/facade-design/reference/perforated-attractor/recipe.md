# Perforated Attractor — Canonical Recipe

The single highest-fidelity reference for the **perforated-attractor** typology.
Reproduce this chain exactly. Do not improvise alternatives — the SKILL's
principle 6 ("Reproduce canonical recipes; don't invent graphs") is in force.

The recipe was extracted by structural inspection of
`Perforated Attractor.gh` in this folder. The renders
`Perforated Attractor (Arctic).png` and `Perforated Attractor (Ghosted).png`
show the resulting geometry.

Status: re-validated against `Perforated Attractor.gh` on 2026-06-23.

## What it builds

A base surface paneled into cells (triangle / quad / hex / diamond / staggered),
each cell carrying a smaller scaled copy of itself as a hole. Hole size grows
with proximity to one or more attractor curves. The perforated plane is then
thickened along the host's outward normal.

Every driver — `u_count`, `v_count`, `hole_min`, `hole_max`,
`attractor_clamp`, `thickness` — is a labelled slider.

## The 5 stages

The PNG and the .gh both lay this out as five horizontally-grouped stages:

```
01. Base Surface & Brep   →   02. Panelization   →   03. Logic (Setting Attractor)
                                                          ↓
                              05. Detailing      ←   04. Construct
                              (Adding Thickness)     (Attraction of Offsetted Curves
                                                     → Differencing the Geometries)
```

Build in this order. Recompute at the end of stage 2, 3, and 4 to catch tree
mismatches early.

## Components list (exact kinds)

These are the names you pass to `gh_add_component`. Watch the traps — many
have OBSOLETE or "Linear" variants that look right but break the chain.

| Stage | Component (exact name) | Kind | Why this one |
|---|---|---|---|
| 01 | `Brep` or `Surface` (param) | `Param_Brep` / `Param_Surface` | Host input |
| 01 | `Curve` (param) | `Param_Curve` | Attractor input — **set to an existing Rhino curve that is coplanar with and lies on the host surface**. A curve off the surface still computes Pull Point distances mathematically, but the gradient stops reading as a spatial pattern on the panel |
| 02 | `Triangle Panels B` *(LunchBox)* | `TriB` | Triangular panelization. **NOT** `LB Triangle Panels A` (that name fails). Variants: `Hexagon Cells` (`HexCells`), `Quad Panels A` (`QuadA`), `Diamond` (`Diamond`), `Staggered Quads` (`QuadStag`) |
| 03 | `Area` | `Component_AreaProperties` | Centroid per cell. **Not** `Component_AreaProperties_OBSOLETE` — bridge resolves the live kind by default if the canvas scope is `defaults` or `all` |
| 03 | `Pull Point` | `Component_PullPoint` | Distance from centroid to attractor curve(s). **NOT** `Curve Closest Point` — Pull Point's `Geometry` input is `access: list` so it natively handles N attractor curves |
| 03 | `Minimum` *(optional)* | `Operator_Minimum` | Clamp the pull distance to a slider cap, so far-away points don't dominate the `Bounds.Max` and squash the close-range gradient |
| 03 | `Bounds` | `Component_NumericBounds` | Reads the distance range to feed Remap.Source |
| 03 | `Construct Domain` | `Component_ConstructDomain` | Builds `(hole_max, hole_min)` — **reversed** so small distance → large scale |
| 03 | `Remap Numbers` | `Component_RemapNumbers` | Distance → per-cell scale factor |
| 04 | `Scale` | `Component_Scale` | **Uniform scale**, NOT `Scale NU`. Inputs: Geometry / Center / Factor |
| 04 | `Boundary Surfaces` | `Component_BoundarySurfaces` | The **differencing** step — see "The Boundary Surfaces trick" below |
| 05 | `Extrude` | `Component_Extrude` | **Legacy `Extrude` taking a `Vector` directly**, NOT `Extrude Linear` (which takes a Line as Axis and has an Orientation gotcha) |
| 05 | `Unit Z` / `Unit X` / `Unit Y` / or arbitrary vector | `Component_UnitVectorZ` etc. | Extrude direction — see "Extrude direction" below |

**Named Param_Curve waypoints** (place these to make the data flow legible):
- `Cells from Panelization` — fed into `Scale.Geometry`
- `Original Curve (Cells from Panelization)` — fed into `Boundary Surfaces.Edges`
- `Scaled Curves` — output of `Scale`, fed into `Boundary Surfaces.Edges`

These aren't decoration; they make every following edit one click away from
the right wire.

## Wiring (single surface)

Read the table top-to-bottom. Source on the left, target on the right; the
chain is sequential except where one output forks to multiple targets.

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 1 | `Brep` (host) | → | `Triangle Panels B.Surface` | Or HexCells / QuadA / Diamond / QuadStag — same downstream |
| 2 | `u_count` slider | → | `Triangle Panels B.U Divisions` | Default 6, range 3–30 |
| 3 | `v_count` slider | → | `Triangle Panels B.V Divisions` | Default 4, range 3–20 |
| 4 | `Triangle Panels B.Panels` | → | `Area.Geometry` | |
| 4a | `Triangle Panels B.Panels` | → | `Cells from Panelization` (Param_Curve waypoint) | The cells we'll SCALE |
| 4b | `Triangle Panels B.Panels` | → | `Original Curve` (Param_Curve waypoint) | The cells we keep AS-IS for the outer boundary |
| 5 | `Area.Centroid` | → | `Pull Point.Point` | Per-cell sample point |
| 5a | `Area.Centroid` | → | `Scale.Center` | **Same** centroid — each cell scales about itself |
| 6 | `Curve` (attractor) | → | `Pull Point.Geometry` | Set persistent data to an existing Rhino curve coplanar with and lying on the host surface. Single curve OR list of curves (Pull Point handles both — closest of all reported) |
| 7 | `Pull Point.Distance` | → | `Minimum.A` *(optional clamp)* | |
| 7a | `attractor_clamp` slider | → | `Minimum.B` | Distances above this value flatten to the cap |
| 8 | `Minimum.Result` (or `Pull Point.Distance` if no clamp) | → | `Bounds.Numbers` | |
| 8a | (same source as 8) | → | `Remap Numbers.Value` | |
| 9 | `Bounds.Domain` | → | `Remap Numbers.Source` | |
| 10 | `hole_max` slider | → | `Construct Domain.Domain start` (A) | **A = hole_max** (reversed — large value first) |
| 10a | `hole_min` slider | → | `Construct Domain.Domain end` (B) | |
| 11 | `Construct Domain.Domain` | → | `Remap Numbers.Target` | |
| 12 | `Cells from Panelization` | → | `Scale.Geometry` | |
| 13 | `Remap Numbers.Mapped` | → | `Scale.Factor` | Per-cell scale (0–1) |
| 14 | `Scale.Geometry` (scaled cells output) | → | `Scaled Curves` waypoint | |
| 15 | `Scaled Curves` | → | `Boundary Surfaces.Edges` | **First** of two sources |
| 15a | `Original Curve` | → | `Boundary Surfaces.Edges` | **Second** source — `append=True` |
| 16 | `Boundary Surfaces.Surfaces` | → | `Extrude.Base` | Planar Brep with hole — see trick below |
| 17 | `extrude_direction_vector` (see "Extrude direction") | → | `Extrude.Direction` | Vector whose length = thickness |

That's the entire chain. ~12 components + sliders + 3 named waypoints.
No `Surface Split`. No `Sort List`. No `List Item`. No fragment selection.

## The Boundary Surfaces trick (the key insight)

`Boundary Surfaces` accepts a `Edges` input with `access: list` of *closed*
curves. When given multiple closed curves where one contains the others, it
treats the outer as the boundary and the inner ones as **holes**, producing
a single planar Brep with a hole in one component.

This is why **no `Surface Split` is needed**. The "differencing" is implicit
in how `Boundary Surfaces` interprets nested closed curves.

If you find yourself reaching for `Surface Split → Sort by Area → List Item -1`
to pick "the larger fragment after a cut", you've taken the wrong path. The
correct move is `Boundary Surfaces` with both the outer and inner curves as
inputs.

## Extrude direction — host normal × thickness

The demo .gh uses `Unit Z(Factor=thickness)` as the Extrude `Direction`.
**This only works because the demo's base surface lies flat on the world XY
plane** — its outward normal is +Z. Most facades are vertical walls whose
normals are *not* Z, and Unit Z would extrude the perforated panels straight
up instead of outward through the wall.

**The general principle**: `Extrude.Direction = host_outward_normal × thickness`.

Three patterns by host orientation:

### Pattern A — host axis-aligned (cheap)
If the host's normal aligns with a world axis, use the matching unit vector:

| Host orientation | Component | Notes |
|---|---|---|
| Flat / floor / ceiling (XY plane, normal +Z) | `Unit Z(Factor=thickness)` | The demo's case |
| Wall facing +X | `Unit X(Factor=thickness)` | |
| Wall facing −X | `Unit X(Factor=-thickness)` | Negative factor flips |
| Wall facing +Y | `Unit Y(Factor=thickness)` | |
| Wall facing −Y | `Unit Y(Factor=-thickness)` | |

### Pattern B — host on an oblique plane (use Amplitude)
For a tilted but flat host, compute the normal once at the surface midpoint and
amplify by thickness:

```
host → Evaluate Surface(uv=0.5,0.5) → Frame → Deconstruct Plane → Z-Axis
                                                                ↓
thickness slider → Amplitude(Vector=Z-Axis, Amplitude=thickness)
                                                                ↓
                                                       Extrude.Direction
```

This is the pattern shown in the recipe's *alt* extrude (the second Extrude
component in the demo .gh file).

### Pattern C — curved host, per-cell normal
For a curved facade where each panel has a slightly different normal, do the
normal computation **per cell** and broadcast as a tree that matches the
Boundary Surfaces output. Add `Brep Closest Point(Brep=host, Point=cell_centroid)`
to get the surface normal at every cell centroid, then `Amplitude` to scale.

For most facade work, Pattern A or B is enough; reach for C only when
curvature is real.

## Multi-surface batching — ONE graph, N branches

Per the SKILL's *Multiple hosts → ONE graph* rule, do not duplicate this
chain per host. Instead, feed a **list of hosts** through the same single
chain — each downstream component produces one tree branch per host.

Three ways to assemble the host list:

### Option 1 — One geometry-ref param with multiple Rhino GUIDs (preferred, but bridge-dependent)
Place ONE `Brep` ref param. Set its persistent data to the list of Rhino
GUIDs. **Known bridge limitation (v0.2.4)**: the bridge's `gh_set_component_parameter`
falls into "Panel mode" — it creates a Panel widget with the GUID text and
wires it to the Brep ref. A multi-line string resolves only the first GUID in
practice. So this option doesn't currently work on this bridge version.

### Option 2 — N geometry-ref params + Merge Multiple (current best for the bridge)
Place N `Brep` ref params (one per host), set each one's persistent data to a
single GUID, then `Merge Multiple` them into one stream. Wire the Merge
output everywhere the chain currently expects the host.

```
Brep_0 ──→ Merge Multiple.Stream 0
Brep_1 ──→ Merge Multiple.Stream 1
Brep_2 ──→ Merge Multiple.Stream 0  (append=True)
Brep_3 ──→ Merge Multiple.Stream 1  (append=True)
              ↓
       Triangle Panels B.Surface
              ↓
              (rest of chain, tree-aware)
```

### Option 3 — Geometry Pipeline (cleanest if hosts share a layer)
A single `Geometry Pipeline` component pulls every object on a named Rhino
layer (e.g. `Srf4Facade`) as a list. Wire its output directly into Triangle
Panels B. No persistent data needed; updates as you add/remove objects from
the layer.

```
Geometry Pipeline(Layer="Srf4Facade") → Triangle Panels B.Surface
```

### Attractor curves — same pattern
If multiple attractor curves exist, merge them too (Pattern 2) or pull by
layer (Pattern 3 with `Layer="Attractor Curves"`). Pull Point's `Geometry`
input is `access: list` — it will compute the distance to the **closest**
of all curves for each cell centroid. This is the SKILL's
"compute distance to the *set*" interpretation.

### Tree alignment — the freeze trap
The most common failure when batching is a **data-tree cross-product** at
the cut step. With Boundary Surfaces this is largely avoided (no Surface
Split, no per-cell list operations). But verify:
- `Area.Centroid` output should have one branch per host, each branch
  holding N cells (one item per cell).
- `Pull Point.Distance` output should match that tree exactly.
- `Scale.Geometry` output should match.
- `Boundary Surfaces.Edges` receives two sources (Scaled + Original), both
  trees aligned to the same shape.

If `Boundary Surfaces` outputs more items than expected, the input trees
are misaligned — usually one is grafted when it should be flat (or vice
versa). Check with `gh_get_objects` on the offending component's input
ports; the bridge reports source GUIDs but the tree depth must be matched
mentally.

## Slider starting values (scale-factor interpretation)

`hole_min` and `hole_max` are **scale factors** in `[0, 1]`, **not** radii in
document units. They control how much of the cell area each hole takes.

The values below are the canvas settings that produced the rendered output in
`Perforated Attractor (Arctic).png` / `Perforated Attractor (Ghosted).png` —
treat them as a **known-good starting point**, not a fixed default. Slide them
within range to explore other outcomes.

| Driver | Starting value | Range | Type | Meaning |
|---|---|---|---|---|
| `u_count` | 40 | 0–100 | int | Subdivisions in surface U direction |
| `v_count` | 10 | 0–10 | int | Subdivisions in surface V direction |
| `hole_min` | 0.2 | 0.0–1.0 | float | Smallest hole — far from attractor → tiny scaled copy of cell |
| `hole_max` | 0.8 | 0.0–1.0 | float | Largest hole — near attractor → cell mostly cut out, thin frame around |
| `attractor_clamp` (optional) | 7.0 | 0.0–10.0 | float | Distances above this cap out — keeps the gradient meaningful |
| `thickness` | 0.265 | 0.0–1.0 | float | Wall thickness (extrude depth) |

**These are fallback values, not defaults to apply blindly.** If the user
supplies a **reference image** or **describes the perforation pattern they
want** (e.g. "tiny holes spaced far apart", "almost solid except for a few
huge cutouts near the entry", "dense gradient that opens up at the top"),
derive the slider values from their input — match `u_count` / `v_count` to
the observed cell density, set `hole_min` / `hole_max` to bracket the
observed range of hole sizes, and place `attractor_clamp` so the gradient
spans the region they pointed at. Only fall back to the table above when
the user has given no visual or descriptive intent.

If recompute is sluggish while iterating (especially when batching multiple
hosts), drop `u_count` / `v_count` to single digits, get the chain wired
correctly, then push the counts back up for the final render.

## Panelizer variants

The downstream chain (stages 3, 4, 5) is identical regardless of panel
shape. Swap only stage 2:

| Variant | Component | Hole shape | Notes |
|---|---|---|---|
| **Triangular** | `Triangle Panels B` (`TriB`) | Triangle | Default; alternating up/down |
| **Hexagonal** | `Hexagon Cells` (`HexCells`) | Hexagon | Honeycomb |
| **Quad** | `Quad Panels A` (`QuadA`) | Quad | Regular grid |
| **Diamond** | `Diamond` | Diamond | Rotated-square pattern |
| **Staggered** | `Staggered Quads` (`QuadStag`) | Staggered quad | Brick / running-bond layout |

All variants are LunchBox components — require LunchBox installed and
`ComponentScope = defaults` or `all`. If LunchBox is absent, fall back to
native `Divide Domain² + Isotrim` (quad only) — note in report.

## Variant: regular perforated (uniform holes, no attractor)

Per skill author's note 6: "Regular perforated facades follows same
recipe as the perforated attractor just without the attractor point
and pull point — without the grouped attractor point components
highlighted on the canvas."

When the user asks for a **uniform** perforated screen (every cell
identical, no spatial variation), build this variant instead of the
full attractor chain:

**What to keep:** Stages 01–02 (Base + Panelize), and the tail end of
stages 03–05 (`Area` → `Scale` → `Boundary Surfaces` → `Extrude`).

**What to delete from the canonical chain:**
- `Curve` param (the attractor input — nicknamed `Attractor Curve` on canvas)
- `Pull Point`
- `Minimum` (the clamp)
- `attractor_clamp` slider (drove `Minimum.B`)
- `Bounds`
- `Construct Domain`
- `Remap Numbers`

On the reference canvas these are **split across two adjacent purple
groups** (both `rgba(170,135,255,150)`), labelled by the scribbles
`Setting Attractor Curve` and `Attraction of Offsetted Curves`. Each
group also contains components the regular variant still needs — so
**do not drop a whole group; delete surgically by component name**:

- **`Setting Attractor Curve` group** (3 members): `Area`, `Pull Point`,
  `Attractor Curve`. **Delete `Pull Point` and `Attractor Curve`. KEEP
  `Area`** — its `Centroid` output still feeds `Scale.Center`.
- **`Attraction of Offsetted Curves` group** (9 members): `Cells from
  Panelization` waypoint, `Minimum`, `Bounds`, `attractor_clamp` slider,
  `Remap Numbers`, `Construct Domain`, `Scale`, `hole_max` slider,
  `hole_min` slider. **Delete `Minimum`, `Bounds`, `attractor_clamp`,
  `Remap Numbers`, `Construct Domain`. KEEP `Cells from Panelization`
  and `Scale`. Replace `hole_max` + `hole_min` with a single
  `hole_scale` slider** wired directly to `Scale.Factor`.

**What replaces them:** a **single `hole_scale` slider** wired
directly into `Scale.Factor`. That's it.

```
[panel curves] ──→ Area ──→ Centroid ──→ Scale.Center
                            ↓
                          Scale.Geometry ←── [panel curves]
                            ↓
                 hole_scale (slider 0.2–0.95) ──→ Scale.Factor
                            ↓
                          Scale.Geometry (output, scaled curves)
                            ↓
                          Boundary Surfaces ──→ Extrude
```

| Driver | Default | Range | Notes |
|---|---|---|---|
| `hole_scale` | 0.5 | 0.2–0.95 | Single uniform scale factor. Replaces `hole_min`/`hole_max`/attractor branch. Closer to 1 → smaller holes (perforated reads as diagrid — see [`../diagrid/recipe.md`](../diagrid/recipe.md)). Closer to 0 → larger holes |

The `u_count`, `v_count`, `thickness` sliders stay identical to the
attractor variant.

When transcribing this variant, state it plainly in the report:
"Regular perforated — same as perforated-attractor minus the attractor
branch, with a single uniform `hole_scale` slider."

## Anti-patterns (named, do not invent these)

These are the wrong moves the recipe *replaces*:

| Anti-pattern | Why wrong | Use instead |
|---|---|---|
| `Polygon → Surface Split → Sort → List Item -1` | Invents the hole shape (not derived from cell); Surface Split + Sort cross-product freezes the canvas on multi-surface | `Scale` the actual cell → feed both Original & Scaled into `Boundary Surfaces` |
| `Scale NU` with X/Y/Z scales | Over-parameterized for uniform scaling | `Scale` (uniform — one Factor input) |
| `Extrude Linear` with `Line SDL` for axis | Requires Orientation plane handling; profile re-projects if Orientation defaults wrong | `Extrude` (legacy) with `Direction` vector directly |
| `Curve Closest Point` for attractor | Only accepts one curve per item — needs duplication for multi-attractor | `Pull Point` (Geometry input is `access: list`, computes min over all curves natively) |
| `Plane Normal + Brep CP` chain to get a scale center | Centroid for `Scale.Center` is just `Area.Centroid` — no extra components needed | `Area.Centroid` directly into `Scale.Center` |
| 4 separate row-graphs for 4 surfaces | N× component count, freezes mid-build, anti to SKILL "Multiple hosts → ONE graph" rule | One chain over a multi-item input (Merge Multiple or Geometry Pipeline) |
| Tangled wires across the build | Unreadable; impossible to edit / debug | Named `Param_Curve` waypoints (`Cells from Panelization`, `Scaled Curves`, `Original Curve`) |
| `hole_min` / `hole_max` as **radii in document units** | Wrong domain — produces panels whose holes are absolute-size, not proportional | Treat as **scale factors** in `[0, 1]` of the cell itself |
| `Unit Z × Thickness` for vertical wall facades | Extrudes straight up, not outward through the wall | Match `Unit X` / `Unit Y` to the host's outward axis, or compute per-host normal via `Evaluate Surface` |

## Bridge-specific quirks (rhino-gh-mcp v0.2.4)

These bit the LLM during the rebuild — encode them so they don't bite again:

1. **Multi-GUID Brep ref doesn't work via persistent data.** The bridge
   creates a Panel widget with the multi-line string but the Brep ref only
   resolves the first GUID. Use `Merge Multiple` of N single-GUID refs, or
   `Geometry Pipeline` by layer name.
2. **`Divide Domain²` (with unicode ²) hangs the client.** Each call appears
   to time out but actually places the component eventually — retrying creates
   duplicates. If you need the component, send ONE call and accept the timeout,
   then verify via `gh_find_components("Divide Domain")`. The native panelizer
   chain in this recipe avoids it entirely.
3. **`MCPv2 → Scenario` gates writes.** Must be set to `Execute` or `Author`.
   Inspect / Tune / Coach scenarios block component placement and parameter
   sets.
4. **Component name traps**: bridge's name resolution sometimes returns
   OBSOLETE variants. Always check the returned `kind` after placement; if
   it ends in `_OBSOLETE`, swap by adding the live variant (often a slightly
   different name like `Triangle Panels B` vs `LB Triangle Panels A`).
5. **`gh_capture_canvas` is broken in this build.** Use Rhino-side capture
   (`rhino_capture_viewport`) for visual confirmation.
6. **`gh_get_context` can exceed the result-size cap** on busy canvases
   (>~50 components). Use `gh_canvas_summary` for overview, then targeted
   `gh_get_objects([guids])` per component cluster.

## Verifying the build

After the final `gh_recompute`:

1. `gh_canvas_summary` → `components_with_errors` and `components_with_warnings`
   must both be 0.
2. `gh_get_runtime_messages` on the Extrude output — should report no errors.
3. `rhino_set_view(standard="Perspective")` + `rhino_capture_viewport` —
   confirm visually: thick walls with holes graded by attractor proximity.
4. **Geometry sanity check**: the perforated wall thickness should look about
   right relative to the host bbox (rule of thumb: < 1/20 of host's shortest
   bbox dimension). Bigger → suspect unit mismatch.

## What this recipe produces, applied to the session task

For 4 vertical Srf4Facade walls + 4 attractor curves in a Rhino doc:

- **Option 2 host wiring**: 4 `Brep` refs (one per Srf4Facade GUID) →
  `Merge Multiple` → `Triangle Panels B`
- **Attractor**: 4 `Curve` refs → `Merge Multiple` → `Pull Point.Geometry`
  (cells pull to the nearest of all 4 — unified field across surfaces)
- **Extrude direction**: per host axis — if all 4 walls face Y (front/back)
  or X (sides), wire `Unit Y(Factor=thickness)` and `Unit X(Factor=thickness)`
  accordingly. If mixed, use Pattern B (Evaluate Surface → Frame → Z-Axis →
  Amplitude) so each host extrudes along its own normal.
- **Total component count**: ~18 chain components + 6 sliders + 1 Construct
  Domain + 8 input refs + 2 Merge Multiple = ~35 total. Vs the 82 of the
  failed row-based attempt.

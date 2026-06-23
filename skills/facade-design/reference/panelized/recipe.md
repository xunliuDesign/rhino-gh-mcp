# Panelized / Curtain-Wall — Recipe

> **Source canvas:** `Panelized.gh` (this folder).
> **Status:** validated (transcribed by structural inspection 2026-06-22).
>
> **The reference canvas is intentionally minimal.** No thickness
> slider, no per-axis split (joint sliders for size and count), no
> inset chain. Production builds typically add a `thickness` slider
> wired to `Unit Z.Factor` as the first extension, split the joint
> sliders into per-axis (`width`/`height`, `u_count`/`v_count`), and
> may layer in a joint-reveal inset. These are documented as variants
> below — their absence from the canvas is by design (this is the
> bare-bones spine other typologies build on), not omission.

## What it builds

A planar base surface paneled into triangular cells (LunchBox `Triangle
Panels C`), each cell extruded along the host normal by a thickness
factor. The reference canvas is a from-scratch demo — sliders generate a
square base surface in-place — so production builds replace stage 01
with a host Brep/Surface referenced from the Rhino doc.

This is the **foundation chain** for several other typologies:
`perforated-attractor`, `diagrid`, `folded`, and `kinetic-panel` all
start from the same panelize-then-extrude spine and add per-cell logic
between stages 02 and 03.

## Sub-variants (panel shape)

The canvas uses `Triangle Panels C`. Per skill author's note 5, LunchBox
panel components are drop-in interchangeable — same `Surface` + `U` + `V`
inputs, same `Panels` output (tree of cells per host). Swap one for
another without touching anything downstream.

| Variant | Component (LunchBox) | Kind | Notes |
|---|---|---|---|
| **Triangular C (this canvas)** | `Triangle Panels C` | `TriC` | Inputs: Surface, U Divisions, V Divisions. Output: Panels |
| Triangular B | `Triangle Panels B` | `TriB` | Alternating up/down; same I/O contract |
| Quad | `Quad Panels A` | `QuadA` | Regular grid |
| Diamond | `Diamond` | `Diamond` | Rotated-square pattern |
| Hex | `Hexagon Cells` | `HexCells` | Honeycomb |
| Staggered | `Staggered Quads` | `QuadStag` | Brick / running-bond layout |
| Quad (native fallback) | `Divide Domain² + Isotrim` | (two components) | If LunchBox absent — quad only |

The downstream chain (extrude, optional inset) is identical across
variants — only stage 02's panelizer swaps.

## The 3 stages (minimal panelize-and-extrude)

```
01. Base Surface (Rectangle → Param_Surface)
        ↓
02. Panelize (Triangle Panels C)
        ↓
03. Extrude (along Unit Z × Factor)
```

This is the **minimal** chain. For a joint-reveal variant (per-panel
inset producing visible mullion lines), see § "Variant: panelized with
joint reveal" below.

## Components list (exact kinds)

| Stage | Component | Kind | Why this one |
|---|---|---|---|
| 01 | `Rectangle` | `Component_Rectangle` | Generates the demo base curve on a plane. Inputs: Plane, X Size, Y Size, Radius. Output: Rectangle (curve), Length. **For production builds**, replace with `Param_Brep` / `Param_Surface` referenced to a Rhino GUID — see `../geometry-ingest.md` |
| 01 | `Surface` (param) | `Param_Surface` | Acts as **implicit Curve→Surface converter** — accepts the Rectangle curve and emits a planar Surface for the panelizer. Replaces what would otherwise be a `Boundary Surfaces` component |
| 02 | `Triangle Panels C` *(LunchBox)* | `TriC` | Triangular panelization. Inputs: Surface, U Divisions, V Divisions. Output: Panels (panel system). Requires LunchBox installed and `ComponentScope = all` (or LunchBox added to `CategoryFilter`) |
| 03 | `Unit Z` | `Component_UnitVectorZ` | Extrude direction = (0,0,1) × Factor. **Factor input is unwired in this canvas** — defaults to 1.0. See "Extrude direction" below for the host-normal generalization |
| 03 | `Extrude` *(legacy)* | `Component_Extrude` | Inputs: Base (geometry), Direction (vector). **Not** `Extrude Linear` (which takes a Line axis and has an Orientation gotcha — see `../bridge-quirks.md` § Component name traps) |

## Wiring (from canvas inspection)

| # | Source | → | Target | Notes |
|---|---|---|---|---|
| 1 | `size` slider (value=10, range 0–10, int) | → | `Rectangle.X Size` | Joint slider — drives both X and Y |
| 2 | `size` slider | → | `Rectangle.Y Size` | Same slider as wire 1 — square base |
| 3 | (Rectangle.Plane unwired) | → | (defaults to World XY plane) | Base lies flat on XY |
| 4 | `Rectangle.Rectangle` (curve) | → | `Param_Surface` | Implicit Curve→Surface conversion |
| 5 | `Param_Surface` | → | `Triangle Panels C.Surface` | Host input to panelizer |
| 6 | `count` slider (value=8, range 0–100, int) | → | `Triangle Panels C.U Divisions` | Joint slider — drives both U and V |
| 7 | `count` slider | → | `Triangle Panels C.V Divisions` | Same slider as wire 6 — uniform grid |
| 8 | `Triangle Panels C.Panels` | → | `Extrude.Base` | Tree of triangular panel surfaces |
| 9 | `Unit Z.Unit vector` | → | `Extrude.Direction` | Vector × Factor = (0,0,1) × 1.0 |
| 10 | (Unit Z.Factor unwired) | → | (defaults to 1.0) | **No thickness slider in this canvas** |

## Sliders / defaults (as authored)

| Slider | Value | Min | Max | Type | Drives | Notes |
|---|---|---|---|---|---|---|
| `size` (joint X/Y) | 10 | 0 | 10 | int | `Rectangle.X Size` + `Y Size` | Square base; max-range value (slider sits at max) |
| `count` (joint U/V) | 8 | 0 | 100 | int | `TriC.U Divisions` + `V Divisions` | Uniform 8×8 panel grid |

**Note:** the canvas sliders are unit-agnostic integer sliders (no
length units encoded). For doc-aware ranges per `rhino_get_scene_info`,
use the typology defaults table in `../typologies.md`.

## Anisotropic / unit-aware sliders (production retune)

The canvas uses two joint sliders. For production builds that need
non-square hosts or anisotropic panel counts, split into four
separate sliders:

| Driver | mm default | mm range | m default | m range | ft default | ft range | Type |
|---|---|---|---|---|---|---|---|
| `width` | 12000 | 1000–50000 | 12 | 1–50 | 40 | 3–160 | float — Rectangle.X Size |
| `height` | 6000 | 1000–30000 | 6 | 1–30 | 20 | 3–100 | float — Rectangle.Y Size |
| `u_count` | 8 | 2–40 | same | | same | | int — TriC.U Divisions |
| `v_count` | 5 | 2–30 | same | | same | | int — TriC.V Divisions |
| `thickness` | 30 | 5–200 | 0.030 | 0.005–0.2 | 0.1 | 0.02–0.7 | float — wires to Unit Z.Factor |

**The canvas does not have a thickness slider** (Unit Z.Factor unwired
→ 1.0). Adding one is a straightforward edit: `gh_add_slider` →
`gh_connect_components(slider, "Unit Z.Factor")`.

## Extrude direction — host-normal generalization

The canvas uses `Unit Z` because the base Rectangle lies on World XY
(its normal IS +Z). This is **Pattern A** (axis-aligned host) from
`../extrude-direction.md`.

When transcribing this recipe for a non-XY host, **read what's wired to
`Extrude.Direction`** — do not assume Unit Z (per skill author's note 1
and the authoring callout at the top of `../extrude-direction.md`). For
host orientations:

| Host orientation | Replace `Unit Z` with | Pattern |
|---|---|---|
| Flat on XY (this canvas) | `Unit Z(Factor=thickness)` | A |
| Vertical wall facing +X | `Unit X(Factor=thickness)` | A |
| Oblique single host | `Evaluate Surface → Frame → Deconstruct Plane → Z-Axis → Amplitude(thickness)` | B |
| Multi-host / curved host | `Brep Closest Point → Normal → Amplitude(thickness)` (per cell) | C |

See `../extrude-direction.md` for the full diagrams.

## Host-ingest variant (production replacement for stages 01)

The canvas authors a placeholder base from sliders. For production
(panelize an existing Rhino building face), replace stage 01 entirely:

```
[Rhino host Brep] (GUID)
        ↓
Param_Brep (persistent data = GUID via gh_set_component_parameter)
        ↓
Deconstruct Brep → Faces → List Item(0)  (extract the face you want)
        ↓
Triangle Panels C.Surface   (replaces the Rectangle → Param_Surface chain)
```

See `../geometry-ingest.md` § GUID handoff for the param-mode and
script-mode variants of the host reference.

## Variant: panelized with joint reveal (per-panel inset)

The canvas does NOT show this; it's the panelized → perforated-style
inset variant. Add between stages 02 and 03:

```
Triangle Panels C.Panels   (panel curves, tree)
        ├──→ Area → Centroid    (one centroid per cell)
        │      ↓
        │   Scale.Center
        │      ↓
        ├──→ Scale.Geometry (panel curves)
        │      ↓
        │   inset_factor slider (e.g. 0.85) → Scale.Factor
        │      ↓
        │   Scale.Geometry (scaled inner curves)
        ↓
Boundary Surfaces (Edges = original panel curves + scaled inner curves)
        ↓
Extrude.Base   (replaces direct TriC → Extrude)
```

This is the **cell-offset-from-panel** pattern named in
`../recipe-modification-patterns.md` § Canonical patterns. Same idiom
underlies perforated-attractor's hole-from-panel and diagrid's
thin-edge-rails — only the Scale.Factor source differs (constant
slider here, Remap output for the attractor variants).

## Anti-patterns

| Anti-pattern | Why wrong | Use instead |
|---|---|---|
| Hard-coded `Unit Z` extrude for non-XY hosts | Extrudes panels straight up instead of outward through the wall | Read what's wired on the canvas first; for non-XY hosts use Pattern A/B/C in `../extrude-direction.md` |
| `Polygon` for the inset shape (in the joint-reveal variant) | Hole shape isn't derived from panel — visually inconsistent across panel shapes (esp. when panelizer is swapped) | `Area` → `Scale` (the cell-offset-from-panel pattern) — keeps inset geometrically tied to whatever panel the panelizer emits |
| `Extrude Linear` taking a vector | Wrong port type — `Extrude Linear.Axis` expects a `Line`, not a `Vector` | Use legacy `Extrude` (`Component_Extrude`, placed via nickname `"Extr"`) which takes `Vector` on `Direction` directly. See `../bridge-quirks.md` § Component name traps |
| `Boundary Surfaces` between Rectangle and TriC | Redundant — `Param_Surface` already does the implicit Curve→Surface conversion | Wire `Rectangle.Rectangle` directly into a `Param_Surface`, then into `TriC.Surface` |
| Joint X/Y or joint U/V sliders for anisotropic builds | Locks width=height and u_count=v_count, blocking common variants like 16-wide × 4-tall panels | Split into separate `width`/`height` and `u_count`/`v_count` sliders per § Anisotropic retune |

## Multi-surface batching — ONE graph, N branches

For multiple host surfaces, build the recipe **once** on a list of
hosts — `Triangle Panels C` accepts a list and emits a tree (one branch
per host). The Extrude downstream is tree-aware. Do not duplicate the
chain per surface.

Wire pattern (replacing stage 01 host-ingest):

```
[List of N host GUIDs] (one read via rhino_get_objects_with_metadata)
        ↓
Param_Brep (persistent data = list of N GUIDs)
        ↓
Triangle Panels C.Surface   (accepts list, emits tree)
        ↓
Extrude.Base   (tree-aware — one extrusion per cell, one branch per host)
```

For per-host extrude direction (curved or multi-orientation hosts),
swap the global `Unit Z` for the per-cell `Brep Closest Point → Normal
→ Amplitude` chain (Pattern C in `../extrude-direction.md`). Tree
alignment: cell centroids and host list are paired branch-by-branch
via Brep CP's longest-list iteration.

See `../perforated-attractor/recipe.md` § Multi-surface batching for
the full pattern — identical here, just replace the Scale/Boundary
Surfaces stages with a direct TriC → Extrude.

## Cross-references

- Source canvas: [`Panelized.gh`](./Panelized.gh)
- Reference render: [`Panelized (Ghosted).png`](./Panelized%20%28Ghosted%29.png)
- Extrude direction (host-aware): [`../extrude-direction.md`](../extrude-direction.md)
- Host ingest (production stage 01): [`../geometry-ingest.md`](../geometry-ingest.md)
- Panelizer interchangeability: [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Canonical patterns
- Cell-offset-from-panel (joint-reveal variant): [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Canonical patterns
- Recipes that build on this spine: [`../perforated-attractor/recipe.md`](../perforated-attractor/recipe.md), [`../diagrid/recipe.md`](../diagrid/recipe.md), [`../folded/recipe.md`](../folded/recipe.md), [`../kinetic/recipe.md`](../kinetic/recipe.md)
- Component name traps (Extrude vs Extrude Linear): [`../bridge-quirks.md`](../bridge-quirks.md) § Component name traps

## Verification

- `gh_canvas_summary` → 0 errors / 0 warnings (canvas was clean at inspection)
- `gh_get_runtime_messages(extrude_guid)` → no red/orange messages
- Visual: capture front view, confirm regular triangular grid with
  consistent panel thickness; compare against `Panelized (Ghosted).png`
- Scale sanity: panel size should be ~1/u_count of the host width — if
  panels look ~host-sized, suspect U/V counts; if panels look much
  smaller, suspect joint sliders splitting wrong

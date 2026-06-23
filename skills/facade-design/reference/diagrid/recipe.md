# Diagrid — Recipe

> **Status: SLIDER-ONLY VARIANT.** Diagrid is the **same recipe as
> [`../perforated-attractor/recipe.md`](../perforated-attractor/recipe.md)** —
> per the skill author's note 3, the only difference is the slider
> range. Tune `hole_min` / `hole_max` close to 1.0 (e.g. 0.85–0.95) so
> the inner scaled cell is *barely smaller* than the outer panel — the
> difference between them reads as **thin edge rails**, which is the
> diagrid look. Triangular panelizer (`Triangle Panels B`) makes it
> read as a structural triangulated frame.
>
> Do **not** author a separate canvas for diagrid. Clone perforated,
> retune sliders.

## What it builds

A surface paneled into triangles (or other shapes), with each cell's
edges visible as thin rails — the perforated chain at the high-end of
its scale-factor range. Reads as a diagrid structural frame
(commercial high-rise vocabulary). The "members" are the differenced
band between each outer panel and its scaled-down inner copy.

For a **true structural-member** diagrid (mesh edges piped into round
or rectangular sections, no panel faces), that's a different topology
not covered by this skill — `parametric-facade/references/` may have
a structural-frame recipe if you need it.

## How to build (clone-and-tune)

1. **Open the perforated-attractor canvas** and follow
   [`../perforated-attractor/recipe.md`](../perforated-attractor/recipe.md)
   stages 01–05 exactly.
2. **Panelizer**: use `Triangle Panels B` (`TriB`) for the structural
   triangulated read. Quad / hex / diamond also work — each produces a
   different lattice (quad = curtain-wall grid, hex = honeycomb frame).
3. **Slider retune**: set the diagrid-specific ranges below. The key
   move is `hole_min`/`hole_max` clustered near 1.0 — that keeps the
   inner scaled cell almost the same size as the outer panel, so the
   differenced ring reads as a thin edge rail.
4. **Optional attractor**: if the user wants member thickness to vary
   spatially (heavier near a column line, lighter toward the corners),
   keep the full perforated-attractor chain. For uniform diagrid (every
   cell identical), use the "regular perforated" variant — drop the
   attractor branch and feed a single slider into `Scale.Factor`. See
   [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md)
   § Slider-only variants.

## Sliders / defaults (diagrid retune of the perforated sliders)

| Driver | mm default | mm range | m default | m range | ft default | ft range | Notes |
|---|---|---|---|---|---|---|---|
| `u_count` | 12 | 3–30 | 12 | 3–30 | 12 | 3–30 | int — passed to `Triangle Panels B` |
| `v_count` | 8 | 3–24 | 8 | 3–24 | 8 | 3–24 | int |
| `hole_min` | 0.85 | 0.70–0.95 | same | same | same | same | float — **near 1.0**, this is the diagrid difference from perforated |
| `hole_max` | 0.95 | 0.85–0.99 | same | same | same | same | float — set just below 1.0; closer to 1 → thinner rails |
| `thickness` | 80 | 20–300 | 0.08 | 0.02–0.3 | 0.25 | 0.05–1.0 | float — extrude depth |

For perforated, the same sliders sit at `hole_min ≈ 0.2` / `hole_max ≈ 0.8`
(wide aperture). Diagrid just shifts them up.

## Anti-patterns

| Anti-pattern | Why wrong | Use instead |
|---|---|---|
| Authoring a separate "diagrid" canvas with `Mesh Triangulation` + `Mesh Edges` + `Pipe` | That's a structural-member topology, not the facade-skin diagrid this skill produces. Also breaks the slider-only-variant principle | Clone perforated-attractor and tune sliders |
| Hard-coded Unit Z extrude | Wrong for non-horizontal hosts | See [`../extrude-direction.md`](../extrude-direction.md) |
| `hole_min` and `hole_max` both at the same value | Produces uniform rails (no variation); fine for "regular diagrid" but state explicitly that the attractor branch should be dropped (see "regular perforated" variant) | If uniform is wanted, drop attractor and use a single slider into `Scale.Factor` |

## Multi-surface batching

Same as perforated: `Merge Multiple` of hosts. See
[`../perforated-attractor/recipe.md`](../perforated-attractor/recipe.md) §
Multi-surface batching.

## Cross-references

- Source recipe (clone this): [`../perforated-attractor/recipe.md`](../perforated-attractor/recipe.md)
- Slider-only variant catalogue: [`../recipe-modification-patterns.md`](../recipe-modification-patterns.md) § Slider-only variants
- Panelizer interchangeability: [`../panelized/recipe.md`](../panelized/recipe.md)
- Bridge quirks: [`../bridge-quirks.md`](../bridge-quirks.md)

## Verification

- `gh_canvas_summary` → 0 errors / 0 warnings
- Visual: capture perspective; confirm thin triangular rails with
  consistent thickness, no solid panel faces visible.
- Validated 2026-06-23 against the perforated-attractor canvas
  retuned to `hole_min=0.85, hole_max=0.95, u_count=12, v_count=8`.
  See `Diagrid (Arctic).png` and `Diagrid (Ghosted).png` in this
  folder — both show 108 triangular cells with thin attractor-graded
  rails and clear voids.
- **Pre-flight check**: the perforated canvas only produces holes
  when its `Attractor Curve` param resolves to an actual curve. If
  the persistent reference points to a missing Rhino GUID (null
  curve), `Pull Point.Distance` is empty, `Scale.Factor` defaults
  to 1, and Boundary Surfaces emits solid triangles with **zero
  inner loops**. Symptom: `gh_canvas_summary` reports 0 errors but
  the bake has no holes. Fix: internalize a curve into the Attractor
  Curve param, or reference a curve in the live Rhino doc, before
  recomputing.

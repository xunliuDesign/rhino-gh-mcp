# Recipe Modification Patterns

When the prompt is **close to** a canonical recipe but not exactly it —
one or two component swaps away — clone the recipe and substitute.
Don't write a new recipe from scratch for every variant; the canonical
chain is the trunk, variants are branches.

This file catalogues the common single-swap moves so the LLM doesn't
have to derive them.

## How to use

1. Map the user's prompt to a base typology via the synonym map (see
   `typologies.md`).
2. Identify whether it's a STRAIGHT match or a VARIANT.
3. For variants, find your variant in the table below. Each row tells
   you: which recipe to clone, which component to swap, what to swap
   it for.
4. Clone the recipe (`<typology>/canvas.gh` + `recipe.md`) and apply
   the single swap. The rest of the chain stays identical.
5. If your variant needs TWO swaps, compose: do both. If it needs
   THREE+, you're in compositional territory — drop to
   `facade-grammar.md`.

## The four common axes of variation

Most variants change one of four things in the canonical chain:

1. **Panelizer** (step 1) — swap the panel shape
2. **Field source** (step 2) — swap what drives the per-cell variation
3. **Transform** (step 3) — swap what the field controls
4. **Combine step** (step 4) — swap how transformed cells assemble

## Variant catalogue

### Panelizer swaps (step 1)

Source recipe: `perforated-attractor/recipe.md` (or any recipe that
uses `Triangle Panels B`).

| User asks for | Swap `Triangle Panels B` for | Notes |
|---|---|---|
| "Hex perforated screen" / "honeycomb facade" | `Hexagon Cells` (`HexCells`) | Same downstream chain |
| "Diamond facade with attractor holes" | `Diamond` | Same downstream |
| "Quad panel facade with variable holes" | `Quad Panels A` (`QuadA`) | Same downstream |
| "Staggered / brick-bond perforated" | `Staggered Quads` (`QuadStag`) | Same downstream |
| "Square panels, native (no LunchBox)" | `Divide Domain² + Isotrim` chain | Quad only; needs 2 components instead of 1 |

These all use the canonical perforated-attractor chain with one
component name swapped. Everything else (Area / Pull Point / Bounds /
Remap / Scale / Boundary Surfaces / Extrude) is identical.

### Field source swaps (step 2)

Source recipe: `perforated-attractor/recipe.md`.

| User asks for | Swap `Pull Point(Geometry=curve)` for | Notes |
|---|---|---|
| "Holes larger near a point" | `Distance(Point A=centroid, Point B=attractor_point)` | Simpler — no curve needed |
| "Holes larger near multiple points" | `Pull Point(Geometry=list of points)` | Pull Point natively handles a list and returns min distance |
| "Holes larger toward the top / bottom" | `Deconstruct Point(centroid).Z` | Use absolute Z directly as the field. Optionally Bounds + Remap to normalize |
| "Holes biased toward one side" | `Deconstruct Point(centroid).X` (or `.Y`) | Same — use position component as field |
| "Sun-driven hole sizes" | Ladybug `LB SunPath.vectors` → dot product with cell normal → Pull-Point-like distance | More complex; see louver § Sun-responsive variant |

The Bounds + Remap + Construct Domain chain that follows is unchanged
— Remap normalizes any scalar field into the `(hole_max, hole_min)`
output domain.

### Transform swaps (step 3)

Source recipe: `perforated-attractor/recipe.md` (where Scale is the
per-cell transform).

| User asks for | Swap `Scale` for | Notes |
|---|---|---|
| "Panels rotate with attractor" | `Rotate Axis(Geometry=cell, Axis=cell_normal_axis, Angle=Remap_output)` | Each cell rotates about its own normal axis |
| "Panels fold outward with attractor" | `Move(Geometry=cell, Motion=Amplitude(normal, Remap_output))` | Cells push out from host surface by Remap-driven distance |
| "Panels tilt forward with attractor" | `Rotate Axis(Axis=cell_bottom_edge, Angle=Remap_output)` | Like a louver but per-cell, not per-strip |
| "Holes shift off-center with attractor" | `Move` the *inner scaled curve* by Remap-driven offset before Boundary Surfaces | Eccentric perforation |

The Combine step (Boundary Surfaces or downstream) may need adjustment
when the transform produces 3D-displaced output (Move instead of Scale).

### Combine swaps (step 4)

Less common — most variants don't change how cells assemble.

| User asks for | Swap `Boundary Surfaces` for | Notes |
|---|---|---|
| "Cells become 3D pyramids" | `Loft` between cell boundary and centroid-elevated point | Each cell pyramidalizes |
| "Cells become solid blocks" | Skip Boundary Surfaces — extrude the cell directly | Foundation of brick or block facades |
| "Cells become arches / shells" | `Loft` between cell boundary and arc through centroid | Fish-scale, shell shingles |

## Compound recipes (two swaps)

Some prompts compose two recipes. Build the FIRST one, then layer the
SECOND on top:

| User asks for | Compose | Order |
|---|---|---|
| "Perforated diagrid" | `diagrid` base (triangulated mesh) + per-triangle attractor-driven scaled-hole from perforated-attractor | Build diagrid first; treat each triangle face as a cell for the perforated step |
| "Kinetic perforated panel" | `perforated-attractor` + `kinetic` wrapper | Build perforated; wrap `hole_max` with `state × hole_max_full` |
| "Sun-responsive louver" | `louver` (vertical or horizontal) + Ladybug sun chain | Replace constant `fin_angle` with per-strip angle from sun direction |
| "Folded louvers that open with sun" | `folded` + `kinetic` + sun chain | Three-level compose: folded base, kinetic state, sun-driven state |
| "Voronoi screen with hole gradient" | `tessellation` + per-cell hole inset from perforated-attractor | Voronoi cells become the panel cells; apply Scale + Boundary Surfaces per cell |

When composing, state the composition explicitly in the report:
"Composed `<recipe A>` + `<recipe B>` — `<A>` produces the panel
layout, `<B>` adds the per-cell variation."

## When to stop and ask

If you can't find your variant within ONE swap of a canonical recipe,
AND the composition isn't in the compound table above, you're in
genuinely novel territory:

- Drop to `facade-grammar.md` and compose from primitives
- If that doesn't fit either, follow `novel-typology-protocol.md`

Don't string together three or more "modifications" of a single
recipe — at that point you've left the recipe behind and you're
inventing. Be honest about that and switch tracks.

## Canonical patterns worth naming

A few wiring idioms recur across recipes. Name them so the LLM
recognizes them on sight when inspecting a canvas.

### Pattern: cell-offset-from-panel (Area → centroid → Scale)

To produce an *inset cell* (a smaller copy of each panel, used as the
hole-shape source for perforated/diagrid, or as a joint-reveal for
panelized): take the panel curve → `Area` → use the **Centroid** output
as the `Scale.Center` → wire a scale factor (constant slider for
uniform, or Remap output for attractor-driven) into `Scale.Factor`. The
scaled curve is the inset; the original panel curve is the outer.
`Boundary Surfaces` then differences inner from outer.

This pattern shows up in:
- `perforated-attractor` — Factor is Remap output (attractor-driven)
- `panelized` (with joint reveal) — Factor is `(1 − 2·inset/cell_size)` constant
- `diagrid` (per note 3 below) — same as perforated, with a slider range that produces thin edges

Recognize it: any time you see `Area` feeding `Scale.Center`, this is
the cell-offset idiom — not a different recipe.

### Pattern: panelizer interchangeability (LunchBox shape swap)

For any typology with a panelization step, the LunchBox panel
components are **drop-in interchangeable** — `Triangle Panels B`,
`Quad Panels A`, `Hexagon Cells`, `Diamond`, `Staggered Quads` all have
the same input/output contract (Surface in → panel cells out as a tree).
Swap one for another without touching anything downstream.

This applies to: `panelized`, `perforated-attractor`, `diagrid`,
`kinetic-panel`, and the panelization start of `folded`. It is the
single most common variant axis — covered in the Panelizer swaps table
above.

The native fallback (`Divide Domain² + Isotrim`) is also interchangeable
but produces quads only.

## Slider-only variants (same chain, different driver values)

Some "variants" are not component swaps at all — they're the SAME
recipe with different slider ranges. Don't write a separate recipe;
clone the source recipe and tune the sliders.

| User asks for | Source recipe | Slider tuning |
|---|---|---|
| "Diagrid facade" | `perforated-attractor/recipe.md` | `hole_min`/`hole_max` set CLOSE to 1.0 (e.g. 0.85–0.95). The Scale factor barely shrinks the inner cell, leaving only thin **edge rails** between the difference outer/inner curves. Triangular panelizer (`Triangle Panels B`) reads as a structural diagrid. (Per **note 3** from the skill author: "Diagrid facade is the same recipe as perforated facade, sliders just need to be changed to create thinner edges of each cell.") |
| "Regular perforated screen (uniform holes, no attractor)" | `perforated-attractor/recipe.md` minus the attractor branch | Delete the grouped attractor components (`Curve` param + `Pull Point` + `Minimum` + `Bounds` + `Construct Domain` + `Remap Numbers`). Wire a single `hole_scale` slider DIRECTLY into `Scale.Factor`. The rest of the chain (panelize → Area → Scale → Boundary Surfaces → Extrude) stays identical. (Per **note 6** from the skill author: "Regular perforated facades follows same recipe as the perforated attractor just without the attractor point and pull point — without the grouped attractor point components highlighted on the canvas.") |

When a prompt matches a slider-only variant, state it: "Same recipe as
perforated-attractor, just with the attractor branch removed" — sets
the right expectation for the user and the canvas they'll see.

## Cross-references

- The grammar this builds from: `facade-grammar.md`
- The full recipes referenced: `perforated-attractor/`, `louver/`,
  `panelized/`, `diagrid/`, `tessellation/`, `folded/`, `kinetic/`
- For truly novel typologies: `novel-typology-protocol.md`
- Component name traps: `bridge-quirks.md`

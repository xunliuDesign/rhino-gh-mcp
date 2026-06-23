---
name: facade-design
version: "2.0"
description: |
  Generate parametric building facades on a live Grasshopper canvas from a
  loose user prompt — across three intake modes that all converge on the
  same build-and-verify spine: (a) from scratch (no host geometry, build a
  placeholder base surface from dimensions), (b) onto an existing Rhino
  host (a surface, Brep face, mass, polysurface, or boundary curve), or
  (c) tile a premade module / block instance across a host. Covers the
  common typologies — panelized / curtain-wall, louvers / brise-soleil /
  fins / sunshades, perforated / mashrabiya / attractor screens, diagrid
  / triangulated mesh, tessellation (Voronoi / hex / Delaunay), folded /
  faceted, and kinetic / responsive. The headline behavior: a vague
  one-line prompt produces a tunable facade with named sliders and
  sensible ranges, without an interrogation. Use this skill whenever the
  user mentions facade, screen, panel, panelization, louver, fin,
  brise-soleil, sunshade, perforated, mashrabiya, diagrid, tessellation,
  Voronoi, curtain wall, cladding, skinning, or wrapping an envelope —
  even if they don't say the word "facade" — or when they ask to "tile /
  array / repeat a module across a surface," or to "build a facade onto
  this existing building / mass / surface." Do NOT use it for solar /
  daylight / wind analysis (that's the ladybug-environmental skill), for
  terrain shaping (landform), for site-scale building placement
  (urban-massing), or for the deep facade vocabulary reference catalog
  with worked .ghx files (that's the parametric-facade skill — this one
  drives the orchestration; reach for parametric-facade's reference files
  when a typology needs more depth than what's encoded here).
recommended_capabilities:
  allow_parameters: true
  allow_components: true
  allow_scripting: true
recommended_scope: defaults
recommended_category_filter: "Surface, Curve, Vector, Transform, Mesh, Maths, Sets, Params"
prerequisites:
  - "ComponentScope = defaults (1) is enough for the vanilla recipes. Flip the canvas-side MCP Server component's ComponentScope to all (2) only if you want plugin assists (LunchBox / Pufferfish / Weaverbird) or Ladybug-driven sun routing."
  - "allow_scripting is requested for the Voronoi / tessellation typology and any topology that needs loops or RhinoCommon. The graph-only typologies do not require it; if scripting is denied, the skill stays in graph mode and substitutes a non-tessellation default."
  - "Optional: a host surface / Brep / curve in the Rhino doc for the 'onto existing' mode; a block definition or selected unit geometry for 'tile a module'. Neither is required — the from-scratch mode builds a placeholder base from dimension sliders."
references:
  - reference/typologies.md
  - reference/geometry-ingest.md
  - reference/tiling-strategies.md
  - reference/connection-rules.md
  - reference/bridge-quirks.md
  - reference/extrude-direction.md
  - reference/facade-grammar.md
  - reference/recipe-modification-patterns.md
  - reference/novel-typology-protocol.md
  - reference/perforated-attractor/recipe.md
  - reference/louver/recipe.md
  - reference/panelized/recipe.md
  - reference/diagrid/recipe.md
  - reference/voronoi/recipe.md
  - reference/folded/recipe.md
  - reference/kinetic/recipe.md
examples:
  - examples/simple_prompt_screen.md
  - examples/from_scratch_louver.md
  - examples/ingest_massing_facade.md
  - examples/tile_module_on_face.md
  - examples/voronoi_panel.md
bundled_assets: []
---

# Facade Design 2.0 Skill

You are operating on a live Grasshopper canvas through the `rhino-gh-mcp`
server. Your job is to turn a possibly-vague facade prompt into a real,
recomputing, tunable parametric facade — fast — without interrogating the
user. The deliverable is a *family* of facades: a graph with labelled
sliders the user can drag, not a single frozen shape. Geometry is the
byproduct; the legible parametric definition is the product.

This skill is the **orchestration layer** for facade work. The companion
`parametric-facade` skill (in this same repo, under
`../parametric-facade/`) is a deeper vocabulary reference — when a
typology needs more than what's encoded in `reference/typologies.md`,
reach for the matching file in `../parametric-facade/references/`.

## Operating principles

Hold these in mind before every step:

1. **Parametrize first.** Every driver (count, depth, angle, scale, hole
   size, fin angle) becomes a labelled `Number Slider` with a *good range*
   (see `reference/typologies.md`). Bake nothing as a constant the user
   can't reach.
2. **Recognize context before authoring.** Cheap preflight reads before any
   write — bridge status, canvas summary, scene + units. Never derive a
   "scale" or a "host" from assumptions when one tool call answers it.
3. **Graph by default; escalate only when topology demands it.** Prefer
   placing and wiring native (or LunchBox) components — the visible graph is
   the point, since users learn the parametric logic by reading the canvas.
   Reach for `gh_write_script_py3` only when the topology genuinely can't be
   expressed legibly in components (Voronoi / Delaunay / image-driven
   fields, or pulling objects via RhinoCommon). A large surface or panel
   count is **not** an escalation trigger — batch it as one data-tree graph
   (see "Multiple hosts → ONE graph"). When you must script, isolate it as
   one node with typed parameters and keep the rest graph-based.
4. **Verify before reporting.** Recompute, scan runtime messages on the
   nodes you touched, scale-sanity check, capture viewport. Fixing on
   detection is part of the skill — silent green is not enough.
5. **Surface only load-bearing judgment calls.** No question to the user
   unless the answer changes the geometry meaningfully. At most one
   clarifying question per request.
6. **Reproduce canonical recipes; don't invent graphs.** Where a typology
   has a recipe file (e.g. `reference/perforated-attractor/recipe.md`),
   build *that exact component chain* — same components, same ports, same
   tree handling. These recipes are transcribed from known-good practitioner
   definitions; improvising a "plausible" alternative is the single biggest
   cause of wrong, inefficient, non-idiomatic builds. If a recipe step names
   `Scale NU` and `Surface Split`, use those — not `Polygon`, not invented
   substitutes. When in doubt, follow the file over your own instinct.

## Simple-prompt intent resolution — read this first

The single most common failure mode for a facade skill is asking three
clarifying questions for a prompt that should just produce a facade.
Resist it. The rule:

> **Infer typology and intent from the prompt → apply typology defaults for
> everything unstated → build immediately → then show the result and the
> tunable sliders. One clarifying question maximum, and only when a choice
> is genuinely load-bearing.**

A choice is "load-bearing" only when the prompt is geometrically ambiguous
in a way you cannot resolve from `rhino_get_scene_info` or sensible
defaults — e.g. a mass has four facade-facing faces and the prompt doesn't
say which to clad. Picking the wrong typology is also load-bearing;
mistaking 5 cm for 50 cm is not (defaults table fixes it).

### The inference chain

For every prompt, walk this chain in order:

1. **Synonym map → typology.** "Sunshade", "brise-soleil", "fins",
   "blades" → louvers. "Perforated", "screen", "mashrabiya", "punched",
   "speckled", "pattern" → perforated-attractor. "Honeycomb", "cellular",
   "Voronoi", "organic cells" → tessellation. "Grid", "panels", "curtain
   wall", "cladding" → panelized. "Lattice", "web", "triangulated",
   "diagrid" → diagrid. "Folded", "origami", "faceted" → folded.
   "Kinetic", "responsive", "operable" → kinetic. Full map in
   `reference/typologies.md`.
2. **Intake mode.** Is there a host in the Rhino doc? A block instance?
   Neither? → from-scratch, onto-existing, or tile-module. The router
   below decides.
3. **Defaults for everything unstated.** Each typology in
   `reference/typologies.md` has a per-driver default *value* and a
   min/max *range*. If the user didn't say "fins 200 mm deep at 30°", you
   still build with `fin_depth = 250 mm, fin_angle = 25°` — sensible
   architecture the first time. Ranges respect the doc unit system: read
   `length_units` from `rhino_get_scene_info` and pick the row of the
   defaults table that matches (mm vs m vs ft).
4. **Build. Then report.** Do not propose, do not ask "should I…". Build.
   Recompute. Capture. Then surface the sliders the user can drag.

### Concrete worked example

See `examples/simple_prompt_screen.md` — starts from
*"put a modern screen on this building"* and shows the full inference:
host recognition, "screen" → perforated-attractor mapping, applied
defaults, build calls, then the slider summary. **Read it before your
first build of the session** — the inference behavior is easier to copy
than re-derive.

### When you *do* ask

The single permitted clarifying question shape:

> "Your mass has four facade-facing faces — should I clad the south face
> (the largest), or all four?"

Not:
- "What typology would you like?" (infer it)
- "How deep should the fins be?" (default + range it)
- "What's the panel size?" (default + range it)
- "Should I use LunchBox or native?" (detect what's installed)

## Preflight (cheap, every run)

Before authoring anything:

1. `gh_status` — confirm the GH bridge is live. If not: "Please drop the
   `rhino-gh-mcp Server (v1)` component on the canvas and set Run = True."
2. `rhino_status` — confirm the Rhino plugin is reachable. If the workflow
   doesn't touch Rhino geometry (e.g. pure from-scratch with no host
   pick), you can skip the Rhino side but you'll still want it for the
   final `rhino_capture_viewport`.
3. `gh_canvas_summary` — see what's already on the canvas. Prefer this
   over `gh_get_context` unless you're editing an existing graph (the
   former is a one-shot digest; the latter returns the whole document and
   can be large). If there's a prior in-progress facade, follow the
   "iteration / clean-slate" rule below before adding more.
4. `rhino_get_scene_info` — when geometry is involved. This is also where
   you learn `length_units` (mm / m / ft / in), which determines the
   defaults-table row to use for slider ranges. Units mismatch is the
   single most common silent failure later.
5. **`gh_capture_canvas` does not work in this build.** The bridge returns
   `"GH_Canvas.GenerateHiResImage not available in this Grasshopper build."`
   For visual confirmation, use `rhino_set_view` + `rhino_capture_viewport`
   instead. Don't waste a call on the canvas capture.
6. **Component availability gates writes.** Before relying on a named
   component, verify it's placeable in the current scope with
   `gh_find_components(query)` against the live canvas (after a probe
   placement) or `gh_list_available_components(refresh=True)` for the
   proxy listing. `gh_list_available_components` is occasionally
   stale-cached even with `refresh=True`; if a component you expect is
   missing from the listing, a successful `gh_add_component` probe is the
   real check. Remove probes after.

## Router — role assignment

Walk the Rhino doc once and label every relevant object:

- **host** — the surface / mass / boundary curve the facade lives on
- **module** — a premade unit to be instanced across a host

Then branch:

| Found in Rhino | Branch | Converges on |
|---|---|---|
| host present, no module | **A. Onto existing host** | base surface contract |
| module present, no host | Ask one Q: "Do you have a host in the doc, or should I build a placeholder base?" Then branch B or A | base surface + module contract |
| both host and module | **C. Tile module on host** | base surface + module contract |
| neither | **D. From scratch** | placeholder base surface contract |

A "block instance" (from `rhino_list_blocks`) is a strong signal for the
module role — a reusable unit explicitly marked as one. Use it.

**All branches converge** on a single contract before anything downstream
runs: *one base surface with a verified outward normal, plus optionally a
module geometry + base plane.* Everything in the typology recipes consumes
that contract regardless of how it was produced. State this convergence
explicitly when you describe the build to the user.

### Multiple hosts → ONE graph, not one-graph-per-surface

This is the single biggest performance rule in the skill. When the prompt
targets **several surfaces at once** — "a perforated screen on each of
these surfaces", "panelize all the facade faces" — do **not** loop the
build, placing a fresh component cluster per surface. That multiplies every
`gh_add_component` / `gh_connect_components` round-trip by N and is the most
common cause of a build dragging into many minutes.

Grasshopper is built to push a *list* of surfaces through *one* graph. Build
the recipe **once** and feed all hosts in as a single multi-item source;
each downstream component then produces one data-tree branch per surface
automatically — same node count whether it's 1 surface or 20.

1. **Collect all hosts in one read.** A single
   `rhino_get_objects_with_metadata(filters={"layer": "<facade-layer>"})`
   (or one filtered call) returns every target surface + GUID. Don't read
   them one at a time.
2. **Reference them as one multi-item param.** Place a single `Surface`
   (or `Brep`) param and set its persistent data to the *list* of Rhino
   GUIDs — one geometry-reference param holding N objects, not N params.
   (`reference/geometry-ingest.md` shows the multi-GUID variant of the
   handoff.)
3. **Build the recipe once on that list.** One `Divide Domain²`/LunchBox
   paneliser, one attractor-distance chain, one hole-radius remap — all of
   it operating on the N-branch tree. Per-surface results fall out as tree
   branches; you never duplicate nodes.
4. **Attractor curves feed in once too.** If the user has multiple attractor
   curves, merge them into one list and compute distance to the *set* (e.g.
   `Curve Closest Point` against the merged curves, or the minimum across
   them) — one chain, not one per curve.
5. **Watch tree alignment, not node count.** The only real cost of batching
   is matching data-tree paths: the attractor logic must apply per host
   branch. If holes come out uniform across all surfaces (attractor ignored)
   or everything collapses to one branch, the tree is grafted/flattened
   wrong — see `reference/connection-rules.md` § data trees. This is a
   wiring fix, not a reason to fall back to looping.

Only build separate graphs per surface in the rare case the user explicitly
wants a *different typology per surface*. Otherwise: one graph, N branches.

### Branch A — onto existing host

The user has a surface / mass / Brep face / boundary curve / mesh. Read
`reference/geometry-ingest.md`. Summary:

1. **Recognition sequence.** `rhino_get_scene_info` → `rhino_get_layers`
   (layers often carry semantic labels like `building`, `site`, `facade`)
   → `rhino_get_objects_with_metadata(filters={"layer": "<plausible>"})`
   to get types and bboxes → optionally a viewport capture to confirm.
2. **Host taxonomy.** Match the type to a handling rule:
   - Planar Surface / single Brep face → use directly.
   - Closed massing (polysurface / Brep solid) → select the facade
     face(s) by normal orientation. The default policy: the largest
     non-horizontal face. Ambiguous case (4+ vertical faces of similar
     size) → one clarifying question.
   - Open polysurface → per-face, OR pick one face by inferred default.
   - Boundary curve (planar) → Loft / Planar Surface to build the face
     first.
   - Mesh → conversion path (Mesh → Brep, or Mesh's QuadRemesh + Brep).
3. **Outward-normal verification.** This is the recurring failure mode.
   Check the host normal points *away* from the mass centroid (for a
   solid) or *outward* in the obvious sense (for a face). If not, `Flip`
   the surface before downstream consumes it.
4. **GUID handoff.** This is the contract between the `rhino_*` doc
   bridge and the `gh_*` canvas bridge. Two mechanisms, both supported:
   - **Default — GH-side geometry reference param.** Place a `Surface`
     or `Brep` param on the canvas. Set its persistent data to a Rhino
     GUID via `gh_set_component_parameter(param_guid, "",
     "<rhino-guid>")`. This is the lightest path and matches how
     `parametric-facade` does it.
   - **Fallback — Python 3 script pulling the object.** Use
     `gh_write_script_py3` to write a small CPython 3 component that
     accepts a GUID string (or a layer name) and returns the
     RhinoCommon `Brep` / `Surface`. Use this when the user has many
     objects of the same type or the GUID is unstable across edits.
   `reference/geometry-ingest.md` shows the exact wiring of both.

Converge on the contract: a `Surface` (or `Brep` face extracted via
`Deconstruct Brep` + `List Item`) wired to whatever the typology recipe
expects.

### Branch B — module input (asked Q)

The user has a module but no host. Pause once for the host question:
"Build a placeholder base wall, or do you have a host in the doc I'm
missing?" Then proceed as A or D for the host, and continue to tiling.

### Branch C — tile module on host

Read `reference/tiling-strategies.md` in addition to `geometry-ingest.md`.
Both the host and module ingest paths run; tiling then orients the
module to every cell of a subdivided host. The full five-step pipeline
is in the tiling reference.

### Branch D — from scratch

No host, no module. Build a placeholder base surface from sliders. The
canonical path (matches the `parametric-facade` skill's testing — avoid
the OBSOLETE bare `Rectangle`):

1. `gh_add_component("XZ Plane")` for a vertical wall (or `XY Plane` for
   a roof / canopy).
2. `gh_add_component("Construct Point")` × 2 — corners at `(0,0,0)` and
   `(width, 0, height)` for a vertical wall. Use sliders `width` and
   `height` with default 12 m and 6 m respectively (or the unit-system
   equivalent — see `reference/typologies.md` defaults table).
3. `gh_add_component("Rectangle 2Pt")` — wire the Plane + the two corners
   → outputs a Rectangle curve.
4. `gh_add_component("Boundary Surfaces")` — wire the Rectangle → a real
   `Surface` to consume downstream.

Tell the user the base is a placeholder ("12 m × 6 m vertical wall —
swap your real surface into the `Base Surface` param when you have it").

## Routing — which build approach

Before reaching for the typology catalog, walk this decision tree.
The catalog only handles tier 1; the other tiers are how the skill
handles novel or off-catalog facades without inventing components.

```
Prompt arrives
     ↓
1. Synonym map (typologies.md) matches a canonical typology?
     YES → Tier 1: clone the recipe.
            reference/<typology>/recipe.md  +  the folder's .gh canvas(es)
     NO  → step 2

2. Prompt is a variant of a canonical recipe via one or two component
   swaps (different panelizer, different field source, different
   transform, different combine)?
     YES → Tier 2: clone the closest recipe and apply the swap.
            See recipe-modification-patterns.md for catalogued swaps.
     NO  → step 3

3. Prompt is decomposable into the 5-step grammar
   (PANELIZE → SAMPLE → TRANSFORM → COMBINE → THICKEN)?
     YES → Tier 3: compose from primitives.
            See facade-grammar.md — pick one option from each step,
            wire the chain, recompute.
     NO  → step 4

4. Truly novel — doesn't fit the grammar:
     → Tier 4: novel-typology-protocol.md
            Acknowledge gap → map to closest grammar composition →
            prototype on ONE face at low counts → capture viewport →
            confirm direction with user → scale only after yes →
            offer to save as new recipe.
```

**Rules across all tiers:**
- Never invent component kinds. Every component you place must come
  from `facade-grammar.md`, `bridge-quirks.md`, or a canonical recipe.
  If you can't name the kind, ask the user.
- Tiers 2–4 are NOT permission to skip verification. Every build, every
  tier, ends with `gh_canvas_summary` + `gh_get_runtime_messages` +
  `rhino_capture_viewport`.
- Tier transitions are visible to the user. Tell them which tier
  you're operating in — "I have a recipe for this" (tier 1) vs "I'm
  composing from primitives" (tier 3) vs "this is novel; I'll
  prototype on one face first" (tier 4). Sets the right expectation
  for accuracy.
- Don't stack tier-2 swaps. If your variant needs three or more
  modifications, you've crossed into tier 3 or 4 — be honest about
  that and switch tracks.

## Typology catalog

Read `reference/typologies.md` for the per-family detail (driver
parameters, defaults, ranges, canonical component chains or script
skeletons, intent → family mapping, synonym map). The seven covered
families (= Tier 1 targets):

1. **Panelized / curtain-wall** — panel subdivision → per-panel frame.
   **Preferred (if LunchBox present): `LB Surface Panels` or `LB Panel
   Frame`** — one component splits the surface (or list of surfaces) into a
   clean panel grid, far more legible on the canvas than the native chain
   and naturally tree-aware for multi-host batching. **Native fallback
   (default scope): `Divide Domain²` → `Isotrim`.** Drivers: `u_count`,
   `v_count`, `inset` (joint reveal). See "Panelising: LunchBox vs native"
   below for the detect-and-fallback rule.
2. **Louvers / brise-soleil** — strip subdivision + per-strip rotation +
   thickening. Drivers: `fin_count`, `fin_depth`, `fin_angle`,
   `fin_thickness`. Sun-responsive variant uses Ladybug if present,
   otherwise a `sun_vector` slider proxy (azimuth + altitude in degrees).
3. **Perforated / attractor screen** — **follow the canonical recipe in
   `reference/perforated-attractor/recipe.md` exactly; do not improvise an
   alternative.** The core move: panel each surface (LunchBox paneliser) →
   `Area` centroid per cell → `Pull Point` distance to the attractor curve →
   `Remap` that distance into a `hole_min..hole_max` scale-factor domain →
   **`Scale NU` a copy of each cell about its own centroid by that factor** →
   `Boundary Surfaces` → **difference (`Surface Split`) the scaled cell out
   of the panel** → `Extrude` for thickness. The aperture is always a
   *scaled copy of the actual panel cell*, never an invented `Polygon`, and
   the hole size is always attractor-driven (the gradient is the point).
   Drivers: `u_count`, `v_count`, `hole_min`, `hole_max`, `thickness`. For
   multiple surfaces, build the chain **once** over the surface list as a
   data tree (one branch per surface) — see the recipe's "Multi-surface"
   section and the router's "Multiple hosts → ONE graph" rule. Known prior
   failure to avoid: `Polygon → SrfSplit → Sort → List Item → Line` with no
   attractor — that is wrong; the recipe file lists it explicitly as an
   anti-pattern.
4. **Diagrid** — UV point grid → triangulated mesh → `Pipe` or extruded
   profiles. Drivers: `u_count`, `v_count`, `member_radius`.
5. **Tessellation** (Voronoi / hex / Delaunay) — script-escalation path
   via `gh_write_script_py3`. Drivers: `seed_count`, `seed_seed`,
   `cell_inset`, `cell_extrude`.
6. **Folded / faceted** — strip grid + edge fold + Loft (the
   fold-louver topology from
   `parametric-facade/references/louvers-and-fins.md`). Drivers:
   `fold_count`, `fold_angle`, `fold_axis`.
7. **Kinetic / responsive** — louver or panel base + a `state` slider
   (0 = closed, 1 = fully open) wired into the per-cell driver as a
   global multiplier. Drivers: `state`, plus the underlying typology's.

## Panelising: LunchBox vs native (detect, prefer, fall back)

For any typology that splits a surface into panels (panelized, perforated,
and the panel-based start of kinetic), **prefer LunchBox when it's
installed** — `LB Surface Panels` turns the whole subdivision into one
readable node and handles a list of surfaces as a tree out of the box,
which is exactly what the multi-host batch rule wants. But the skill's
default scope is `defaults`, and LunchBox lives under `all` scope — so the
first build must never *depend* on it. Mirror the Ladybug detect-and-fallback
pattern:

1. **Detect.** `gh_list_available_components(refresh=True)` — look for
   components prefixed `LB ` from LunchBox (e.g. `LB Surface Panels`, `LB
   Quad Panels`). Note: the listing is occasionally stale-cached; a single
   `gh_add_component("LB Surface Panels")` probe is the real check.
2. **Present → use it.** Flip `ComponentScope` to `all` (or add LunchBox's
   category to `CategoryFilter`), place `LB Surface Panels`, wire the host
   surface list → `Cells`/`Panels` output → downstream. One node, tree-aware,
   legible. Tell the user you used LunchBox and why (cleaner canvas).
3. **Absent → native fallback, no error.** Use `Divide Domain²` → `Isotrim`
   on the same surface list. This is the default-scope path and always
   works on a stock Rhino 8 install. Note the fallback in the report.
4. **If the user's prompt named LunchBox** but it isn't installed, don't
   silently fall back — say it's not detected and offer `_PackageManager`,
   per the plugin-trigger guardrail.

Both paths converge on the same thing downstream: a tree of panel cells,
one branch per host surface. The attractor / hole logic doesn't care which
paneliser produced them.

## Detecting Ladybug for sun-responsive louvers

Detection mirrors the `ladybug-environmental` skill — don't duplicate
that logic, cross-reference it:

1. `gh_list_available_components(refresh=True)` — look for any component
   starting with `LB ` (e.g. `LB Import EPW`).
2. Present → use the `ladybug-environmental` Workflow A (Sun Path) to
   get sun vectors, then feed `LB SunPath.vectors` into the louver's
   per-strip angle driver. Cross-reference: "Set up the Ladybug Sun Path
   per the ladybug-environmental skill, then connect its `vectors`
   output into the louver chain's per-strip angle input."
3. Absent → fall back to a **sun-vector slider proxy**: two sliders
   `sun_azimuth_deg` (0–360, default 180) and `sun_altitude_deg` (0–90,
   default 30) → `Unit Z` rotated by altitude → rotated about Z by
   azimuth → `Negative` → "sun direction". Wire that into the louver
   angle remap as if it were the Ladybug vector. State explicitly that
   this is a manual proxy and Ladybug would compute it from a real EPW.

## Sensible parametric ranges

Every slider gets a default *value* and a default *min/max range* from
`reference/typologies.md`. The ranges matter as much as the defaults —
they are what stops a dragged slider from producing garbage. A
`fin_depth` slider with range 0–10 000 will spend most of its travel in
the absurd zone; range 50–600 mm (or 0.05–0.6 m) keeps the design space
plausible.

Apply via `gh_set_slider_range(guid, min, max)` after `gh_add_slider`.
Choose the unit-aware row from the defaults table based on
`rhino_get_scene_info().length_units`.

## Authoring mechanics — quick reference

See `reference/connection-rules.md` for the depth. Highlights:

- **Always read inputs before wiring** with `gh_get_objects(guids=[...])`.
  Input/output names vary between versions; don't memorize, verify.
- **`gh_connect_components` `append`.** Default replaces existing wires.
  Set `append=True` when you need a multi-source merge into the same
  input (e.g. feeding a list of target planes into `Orient` from two
  separate source branches, or a `Merge` from multiple branches). Closes
  catalog bug 8 (already landed in the installed .gha).
- **`gh_add_component` is gated by `ComponentScope`.** Native components
  resolve under `defaults` scope (1). Plugin components (LunchBox,
  Pufferfish, Weaverbird, Ladybug) need `all` scope (2) OR the user
  adding the relevant category to `CategoryFilter`. `gh_add_any_component`
  bypasses `CategoryFilter` but still requires `scope=all`.
- **Avoid the OBSOLETE `Rectangle`.** Use `Rectangle 2Pt` per the
  from-scratch recipe. Anything in `gh_find_components` whose `kind`
  ends in `_OBSOLETE` is a trap.
- **Avoid native scalar `Multiplication` in sine-wave-driven louvers.**
  The bridge's name lookup falls back to OBSOLETE colour-multiply. Use
  `Remap Numbers` with a constructed domain, or `Amplitude` with a unit
  vector. Documented under the louver recipe in
  `reference/typologies.md`.
- **Script escalation triggers — narrow on purpose.** This skill favours
  visible component placement so users learn from the canvas, so promote to
  `gh_write_script_py3` **only** when the topology genuinely can't be done
  legibly in components: (i) Voronoi / Delaunay / image-field tessellation;
  (ii) you need RhinoCommon directly to pull objects by GUID/layer. Do
  **not** escalate perforated, panelized, louvers, diagrid, or tiling to a
  script just because there are many surfaces or panels — batch them as one
  data-tree graph instead (see the router's "Multiple hosts → ONE graph"
  rule). A high panel count is a tree, not a reason to write Python. Stick
  to IronPython 2 (`gh_write_script_py2`, `gh_execute_code`) only for very
  small scripts that don't need numpy — and note that the bridge currently
  treats `gh_execute_code` as a stub (returns `"execute_code is not
  supported in C# MCP server."`) and `gh_update_script` /
  `gh_write_script_py2` may not apply to Rhino 8's `ZuiPythonComponent` —
  prefer `gh_write_script_py3` and verify with `gh_get_runtime_messages`
  after recompute.

## Iteration / clean-slate handling

On a follow-up like *"now make it diagrid instead"* or *"swap to
mashrabiya"*, do **not** stack a new facade on top of the prior one.

1. **Tag your nodes when you build.** Use a nickname prefix `[facade]`
   on every facade-related component and slider you place (sliders
   accept a nickname directly via `gh_add_slider(name="[facade]
   fin_depth", ...)`). This makes prior-facade nodes identifiable.
2. **On a typology swap**, locate the prior facade with
   `gh_find_components("[facade]")` → get the list of GUIDs → call
   `gh_remove_node(guid)` on each. Then rebuild fresh.
3. The base surface and any user-set host reference param stay —
   they're inputs, not part of the facade. Tag them `[base]` to keep
   them clear.

If the user explicitly asks to *compare* typologies, leave the old
facade in place and build the new one at a canvas offset
(`position_x += 1500`) — this is the rare exception.

## Geometry sanity / scale check

After recompute, before reporting, do a cheap sanity pass:

1. `gh_get_objects([base_surface_guid])` → read the base surface's
   bounding-box size.
2. Compare the facade element scale (panel size, fin depth, hole radius)
   to that bbox. Rules of thumb:
   - A single panel larger than ~1/3 of the host bbox → suspect units.
   - A fin depth larger than ~1/10 of the host bbox → suspect units.
   - A hole radius larger than the panel cell → impossible by definition.
3. If any rule trips, flag in the report: "Scale looks off — your doc is
   in `<units>` and the panel size defaults to `<value>`. Most likely the
   doc is in `<other-units>` — want me to swap to the metric/imperial
   default?"

This catches the most common silent failure (mm/m mismatch) in one
arithmetic check.

## Failure recovery playbook

When `gh_get_runtime_messages(guid)` returns problems on a touched node,
act — don't just report:

| Symptom | Likely cause | Action |
|---|---|---|
| Null geometry / "no data" downstream | host reference param didn't resolve to a real object, or the normal is flipped | Re-read host with `rhino_get_objects_with_metadata` and verify GUID; check normal and `Flip` if needed |
| Empty `Orient` output | target-plane list is grafted wrong, or source base plane is invalid | `gh_get_objects` Orient and verify tree depth on `Target` matches one item per cell; check `Plane` source is a valid plane param |
| Type mismatch on a wire (red/orange wire) | output/input name ambiguity; the input expected `Curve` but got `Brep` | Re-read both endpoints with `gh_get_objects` and pick a known-matching port; insert a converter (`Brep Edges`, `Brep Wireframe`) if needed |
| `gh_add_component` returns "not found" | scope is too tight or plugin not installed | Re-check `gh_list_available_components(refresh=True)`; if a plugin, flip ComponentScope to `all` or substitute the native recipe |
| `gh_connect_components` "input not found" | port name moved between versions, or you addressed by display label instead of true name | `gh_get_objects` to read the live input list and retry with the right name |
| Recompute clean but no visible thickness on blades | `Extrude Linear` `Orientation (P)` left unwired (defaults to WorldXY, profile re-projects to zero) | Wire `YZ Plane` (vertical fins) or `XZ Plane` (horizontal slats) into `Orientation (P)`. See `parametric-facade/references/louvers-and-fins.md` for the full diagnosis. |

The skill **acts on detected failures**, doesn't just enumerate them in
the closing report.

## Don't-do guardrails

- Don't bake constants where a slider belongs. If a value is
  interesting to vary, it's a slider.
- Don't place gated components without checking availability — and
  don't silently fall back to native when a plugin word was in the
  user's prompt; tell them the plugin would be better and offer to
  install (`_PackageManager`). The skill-plugin trigger principle is
  applied throughout the `parametric-facade` skill.
- Don't use IronPython 2 for real math (no numpy). Use
  `gh_write_script_py3`.
- Don't leave the canvas with unreported errors — fix or flag every
  component carrying a red runtime message before reporting.
- Don't fill a host's window / door openings with facade panels — when
  the host has `Brep` holes, cull instances whose center falls inside
  an opening (see `reference/tiling-strategies.md` § Edges & openings).
- Don't ask multiple clarifying questions when defaults would suffice.
- Don't stack a new facade on top of the old one without clearing it
  first (see *Iteration / clean-slate*).
- Don't use `gh_capture_canvas` — it errors in this build. Use
  `rhino_capture_viewport` for visual confirmation.

## Closing sequence

After the build, every time:

1. `gh_recompute` — evaluate the whole graph.
2. `gh_canvas_summary` — read `components_with_errors` and
   `components_with_warnings`. Non-zero → drill in.
3. `gh_get_runtime_messages(guid)` on each touched node that's in the
   error / warning list. Apply the recovery playbook. Do not skip a red
   node.
4. **Geometry sanity check** per the rules above. Fix or flag.
5. `rhino_set_view(standard="Perspective")` then
   `rhino_capture_viewport(max_size=1000)`. For flat facades, a second
   capture from `Front` reads the rhythm better — include both if it
   adds signal.
6. **Report.** Use this shape:
   - *Host:* what was used or created (mode + face / dimensions).
   - *Typology:* the family chosen + which prompt words triggered it.
   - *Sliders:* labelled list with current value and range — the user's
     dials.
   - *Judgment calls:* the one or two non-obvious decisions made (e.g.
     "I picked the south face because it was the largest
     non-horizontal face — say so if you want a different one"). Skip
     if there were none.

## Robustness rules

- **Read inputs before wiring.** `gh_get_objects` returns the real port
  names — they vary between versions and plugin releases. Don't
  memorize port names; resolve them at runtime.
- **Detect, don't assume, plugins.** If a typology recipe prefers a
  plugin component and the listing doesn't show it, probe with a single
  `gh_add_component` call — the listing is occasionally stale-cached.
  If the probe fails too, fall back to the native recipe in the same
  file and note the fallback in the report.
- **Build incrementally and recompute often.** Place base + subdivision
  first, recompute, confirm the grid reads, *then* add cell geometry,
  then variation. A facade graph that's wrong is far easier to debug at
  5 components than at 30.
- **Keep Rhino and Grasshopper conceptually separate.** You don't need
  the whole Rhino scene to build a facade — just the host the user
  picks in and the bbox / scene units. Avoid dumping
  `rhino_get_objects_with_metadata` with no filter.
- **Read all hosts in one call, not N.** When several surfaces are
  targeted, fetch them with a single filtered
  `rhino_get_objects_with_metadata` (by layer or selection), hold the GUID
  list, and reference them as one multi-item param. Re-reading per surface,
  or re-pulling `gh_get_context` between steps, is pure round-trip waste —
  the canvas digest (`gh_canvas_summary`) plus targeted `gh_get_objects` on
  the few nodes you placed is enough. This pairs with the router's
  "Multiple hosts → ONE graph" rule.
- **Treat `gh_capture_canvas` as broken in this build.** Use
  `rhino_capture_viewport` for visual confirmation.

---
name: rhino-gh-bridge-basics
version: "1.0"
description: |
  Baseline knowledge for any session that drives a Rhino + Grasshopper
  canvas via the `rhino-gh-mcp` bridge. Covers the five fundamentals of
  Grasshopper modeling: (1) the geometry hierarchy — bottom-up from
  Points and Vectors → Curves and Surfaces → B-Reps and Meshes; (2) the
  data-flow model — Params hold data, Components transform data, Wires
  move data left-to-right, plus the type-casting rules that decide
  which wires actually work; (3) modifiers and transformations — Math
  /Sets, Transform components, List operations; (4) data trees and
  branching — {path} addressing, list matching (Shortest / Longest /
  Cross Reference), the multiplier-effect failure mode, Match Tree /
  Simplify Tree; (5) execution state — node colors (orange = warning,
  red = error), recompute discipline, Null-result troubleshooting,
  diagnostic components (Param Viewer / Panel / Point List), and the
  when-and-when-NOT-to-bake rule. Plus the bridge-specific layer:
  name-resolution traps, persistent-data limits, MCPv2 Scenario
  gating, host ingest patterns. This skill is NOT workflow-specific —
  pair it with a task skill (facade-design, landform, etc.) that
  brings the per-typology recipes. Load this skill at the start of
  every rhino-gh-mcp session; load the task skill on top when the
  prompt matches a known typology. Use it whenever you place
  components, set parameters, run scripts, or read host geometry on
  this bridge.
recommended_capabilities:
  allow_parameters: true
  allow_components: true
  allow_scripting: true
recommended_scope: defaults
references:
  - reference/bridge-quirks.md
  - reference/host-ingest.md
---

# Rhino-GH Bridge Basics

You are operating on a live Grasshopper canvas through the
`rhino-gh-mcp` server. Grasshopper is a **bottom-up, left-to-right,
data-flow modeling environment**. Most "why isn't this working" bites
trace back to one of the five fundamentals below.

**This is prerequisite knowledge, not a workflow.** Pair with the
matching task skill (facade-design, landform, etc.) for the actual
build.

## The first move on every session

Before any `gh_add_component`, `gh_set_component_parameter`, or
`gh_write_script_py3`, run four read-only preflight calls:

1. **`gh_status`** — confirm the bridge is alive and check Scenario.
   Writes only land when `Scenario = Execute` or `Author` (Author
   required for scripting).
2. **`gh_canvas_summary`** — see what's already on the canvas plus
   any pre-existing `components_with_errors`.
3. **`rhino_get_scene_info`** — read the doc's `length_units` (mm / m
   / ft) and any selected objects.
4. **`rhino_get_objects_with_metadata`** — only if the prompt suggests
   existing host geometry. Returns GUIDs + layer names for ingest via
   `Param_Brep` / `Geometry Pipeline`.

---

## 1. The geometry hierarchy

Grasshopper builds geometry **bottom-up, in this order**:

```
Points + Vectors  →  Curves + Surfaces  →  B-Reps + Meshes
   (primitives)      (1D and 2D shapes)    (volumes / discretized)
```

Each tier is *built from* the tier above. You can't extrude a volume
without a planar surface first; can't loft a surface without two or
more curves; can't draw a curve without control points.

**Tier 1 — Primitives.** The seeds.

| Type | What it is | Common producers |
|---|---|---|
| `Point` (Point3d) | A **location** in space, X/Y/Z | `Construct Point`, `Populate 3D`, `End Points`, `Area.Centroid`, `Evaluate Surface.Point` |
| `Vector` (Vector3d) | A **direction** with magnitude — NOT a location | `Unit X` / `Unit Y` / `Unit Z`, `Vector 2Pt`, `Amplitude`, `Evaluate Surface.Normal` |
| `Plane` (Plane) | A frame: origin point + X-axis + Y-axis + Z-axis | `XY Plane`, `XZ Plane`, `YZ Plane`, `Construct Plane`, `Frame` from `Evaluate Surface` |

**Tier 2 — Curves and Surfaces.** Built from primitives.

| Type | What it is | Common producers |
|---|---|---|
| `Line` (Line3d) | A straight segment with explicit Start and End | `Line` (two points), `Line SDL`, `Brep Edges` |
| `Polyline` / `Curve` | Connected curve (open or closed) | `PolyLine`, `Interpolate (t)`, `Rectangle`, `Circle`, `Arc`, `Join Curves` |
| `Surface` | A single-face 2D shape, possibly trimmed | `Param_Surface` (implicit Curve→Surface), `Loft`, `Sweep`, `Boundary Surfaces` |

**Tier 3 — B-Reps and Meshes.** Built from surfaces.

| Type | What it is | Common producers |
|---|---|---|
| `Brep` (Boundary Representation) | A volume of joined surfaces — can have holes, multiple faces, internal voids | `Extrude`, `Extrude To Point`, `Solid Union` / `Difference`, `Cap Holes` |
| `Mesh` | Discretized surface or volume — triangles or quads | `Mesh from Brep`, Kangaroo simulations |

**Operationally:** the geometry tier of the output has to match what
the next component accepts. `Extrude` takes a `Surface` (or any
Geometry that can act as a Brep face) for its Base — you can't feed
it a Curve directly without `Param_Surface` for the implicit
conversion, OR `Boundary Surfaces` to close a curve into a planar
Brep first.

**The implicit Curve→Surface conversion.** `Param_Surface` accepts a
closed planar curve and silently emits the planar surface bounded by
it. The "Rectangle → Param_Surface" idiom in every from-scratch demo
relies on this — `Param_Surface` is NOT a no-op, it's a
tier-transition component.

---

## 2. Data flow — Params, Components, Wires

The canvas is a factory pipeline. Three element types:

| Element | Role | Examples |
|---|---|---|
| **Params** (Parameters) | **Hold** data. Empty containers that store geometry, numbers, or references. | `Param_Brep`, `Param_Curve`, `Param_Surface`, `Param_Number`, `Param_Point`. Standalone "ref" components for waypoints or Rhino-GUID ingest. |
| **Components** | **Transform** data. Active operations: inputs → process → outputs. | `Loft`, `Extrude`, `Move`, `Rotate Axis`, `Boundary Surfaces`, `Triangle Panels B`. |
| **Wires** | **Move** data. From output → input, strictly **left-to-right**. | Visible connections; created via `gh_connect_components(source_guid, source_port, target_guid, target_port)`. |

**Param waypoints in recipes.** A named `Param_Curve` mid-chain (e.g.
`Cells from Panelization`, `Scaled Curves`) is a pass-through — input
flows through unchanged — but it makes the data flow legible on the
canvas. Not for computation; for clarity.

**Reading direction is left-to-right and acyclic.** No loops back to
upstream components. Trace a build from leftmost inputs (sliders,
host refs) forward through transformations to the final output (usually
an Extrude or other thickening step).

**Multi-source inputs.** One input port can accept multiple wires from
different sources — streams concatenate per branch. The bridge
supports this via `append=True` on the second and subsequent
`gh_connect_components` calls to the same port. Common pattern:
`Boundary Surfaces.Edges` takes outer cell curve + inner offset curve
as two appended wires.

### Data type compatibility (casting)

Grasshopper auto-converts some types between ports. Others fail
silently or hard.

**Auto-casts (work without intervention):**

| From | To | How |
|---|---|---|
| `Line` | `Vector` | Uses Line direction (End − Start) |
| `Line` | `Curve` | Lines are a Curve subtype |
| `Closed planar Curve` | `Surface` | The `Param_Surface` implicit conversion |
| `Number` | `Integer` | Floor cast; loss-of-precision usually fine for count/index |
| `Integer` | `Number` | Lossless widening |
| `Surface` | `Brep` | Single-face Brep wrapping |
| `Brep` | `Mesh` | NOT auto — requires `Mesh from Brep` |

**Strict types (will fail or behave unexpectedly):**

| Port | Accepts | Fails on | Workaround |
|---|---|---|---|
| `Vector` input (e.g. `Extrude.Direction`, `Move.Motion`) | Vector, or Line via auto-cast | Point (Point is a location, not a direction) | Use `Vector 2Pt(Point A, Point B)` to get a vector between two points |
| `Line` input (e.g. `Rotate Axis.Axis`, `Extrude Linear.Axis`) | Line | Vector (no auto-cast in this direction) | Use `Line SDL` or `Line` from two points to convert |
| `Surface` input (e.g. `Isotrim.Surface`, `Evaluate Surface.Surface`) | Surface, or closed-planar Curve via auto-cast | Non-planar Curve, Polysurface | Use `Boundary Surfaces` for nested curves; `Deconstruct Brep.Faces` to extract a face from a Polysurface |
| `Brep` input (e.g. `Extrude.Base`, `Contour.Shape`) | Brep, Surface (auto-wrapped) | Curve | Convert curve → surface first via `Param_Surface` or `Boundary Surfaces` |
| `Domain` input (e.g. `Remap Numbers.Source`, `Isotrim.Domain`) | Domain (Interval) | Number, Vector | Wrap with `Construct Domain(start, end)` |

**Point vs Vector — the most-confused pair.**

- **Point** = absolute location. `(3, 5, 2)` means "the spot 3 units
  along X, 5 along Y, 2 along Z from world origin." Used for: input
  to `Move.Geometry`, `Line.Start`, `Construct Point.X/Y/Z`.
- **Vector** = direction with magnitude. `(3, 5, 2)` here means "go 3
  in X, 5 in Y, 2 in Z from wherever you currently are." Used for:
  input to `Move.Motion`, `Extrude.Direction`, `Rotate Axis.Axis`
  derivation.

Mixing them silently produces wrong offsets — if you wire a point's
XYZ into `Move.Motion`, the geometry translates by the point's
coordinates instead of by an intended direction. When in doubt: a
Vector points at *where to go*; a Point names *a place*.

---

## 3. Modifiers and transformations

Components that edit data without creating geometry from scratch.

### Math and Sets — controlling numeric inputs

| Component | What it does |
|---|---|
| `Series` | List of evenly-spaced numbers (Start, Step, Count). Drives Unit-vector factors for arrays |
| `Range` | N numbers between Domain start and end (uniform spacing) |
| `Random` | N pseudorandom numbers in a domain. Seed slider for reproducibility |
| `Construct Domain` | Builds an interval (Start, End) — feeds `Remap Numbers.Source/Target` |
| `Remap Numbers` | Maps Value from one Domain into another. Driver of every attractor-graded chain |
| `Bounds` | Reads min/max from a list → emits Domain. Pairs with Remap Numbers |
| `Expression` | Evaluates a formula like `x*0.5 + y` over inputs. Use `VariantParameter` inputs (see `reference/bridge-quirks.md`) |
| `Sine` / `Cosine` / `Multiplication` / `Division` / `Minimum` | Per-item math. Some have `_OBSOLETE` variants — functionally identical, leave them |

### Transformations — moving geometry without recreating it

Preserve type (Brep in → Brep out) but change position / orientation / size:

| Component | What it does | Inputs |
|---|---|---|
| `Move` | Translate by a Vector | Geometry, Motion (Vector) |
| `Rotate Axis` | Rotate around a Line by an angle | Geometry, Angle (**radians!**), Axis (Line) |
| `Rotate 3D` | Rotate around a Plane (more general but rarely needed) | Geometry, Plane, Angle |
| `Scale` (uniform) | Scale around a center point by one Factor | Geometry, Center (Point), Factor (Number) |
| `Scale NU` (non-uniform) | Scale separately on X/Y/Z. Avoid when uniform Scale works | Geometry, Plane, X/Y/Z factors |
| `Mirror` | Reflect across a Plane | Geometry, Plane |

**Rotation uses radians.** `Rotate Axis.Angle` expects radians. If
your slider is in degrees, route through `Radians` (`FuncToRadians`)
first. The unit error is silent — tiny rotations instead of intended.

### List operations — managing multiple items

| Component | What it does |
|---|---|
| `List Item` | Pick one item by Index. Bread-and-butter "extract item N" |
| `List Length` | Per-branch count. Use to set Random.Number and Split List.Index dynamically |
| `Shift List` | Cyclic rotation — useful for offset patterns |
| `Cull Pattern` / `Cull Nth` / `Dispatch` | Filter by boolean / index / pattern. `Dispatch` is the common "split into kept / discarded" gate |
| `Sort List` | Sort by Keys; reorder Values A/B/C accordingly |
| `Split List` | Split at Index → List A and List B |
| `Reverse List` | Flip order |

---

## 4. Data trees and branching — the most-bitten concept

Single biggest source of "why does my graph emit ghosts." Master this
or every multi-host recipe fails.

### What a tree is

Grasshopper's data structure is a **tree of branches**. Each branch
has a **path** like `{0}`, `{0;1}`, `{0;1;2}` and contains a list of
items.

```
Flat list (1 branch):     {0}  [a, b, c, d]
Tree with 2 branches:     {0}  [a, b, c]
                          {1}  [d, e, f]
Nested tree (2 levels):   {0;0}  [a, b]
                          {0;1}  [c, d]
                          {1;0}  [e, f]
```

**When trees appear.** A panelizer emits a tree where each branch is
one host surface. Per-cell operations downstream (Area, Scale,
Boundary Surfaces) preserve branch structure: one cell → one item per
branch.

### List matching modes

When a component receives unequal data on different inputs:

| Mode | Behavior | When to use |
|---|---|---|
| **Shortest list** | Truncates at the shorter input's length | Rare |
| **Longest list** (default for most) | Repeats the shorter input to match the longer | The norm — broadcasts sliders and shared values across all items |
| **Cross Reference** | Pairs every item of A with every item of B (Cartesian product) | The `Cross Reference` component explicitly does this when you need all combinations |

### The multiplier effect — silent geometry explosion

Mismatched data structures don't always produce Null. More commonly
they produce **silent, exponential geometry duplication**. Watch the
wires:

- **Single vs list grafting.** Grafting one input while leaving the
  other flat forces Grasshopper to run the operation against *every
  single item individually*, often producing N² geometry where you
  wanted N. If you don't know why your graph has 400 panels when you
  asked for 20, check whether something upstream is grafting.
- **Empty branches.** If a tree contains an empty path (e.g. `{0;1}`
  has 0 items), any component downstream interacting with that branch
  may output Null silently — and the data preview still shows "tree
  with 3 branches," masking the problem.
- **Match Tree / Simplify Tree before combining two complex streams.**
  When two upstream trees have different path depths or shapes,
  `Match Tree` aligns them, and `Simplify Tree` strips redundant
  path levels. Use them BEFORE feeding the combined data into the
  next component, not after wondering why downstream blew up.

### Tree-handling rules of thumb

- **Build per-host operations on a list of N hosts, not by duplicating
  the chain N times.** Panelizers accept lists, emit trees — one
  branch per host.
- **Use `Merge Multiple`** to combine N separate `Param_Brep` refs
  into one stream.
- **Use `append=True`** for multi-source wiring on the same port.
- **When alignment looks wrong**, `gh_get_objects(component_guid)`
  shows the per-input data preview — read it.
- **Use diagnostic components** to see tree shape directly — see §
  Debugging toolkit in Section 5.

---

## 5. Execution state and baking

### Node states — the visual diagnostic

Every component has a color that tells you whether it's healthy:

| Color | Meaning | What to do |
|---|---|---|
| **Default** (no overlay) | Component computed cleanly | Nothing |
| **Orange** | Warning — usually missing/empty input, or tree-alignment mismatch that emitted nothing | `gh_get_runtime_messages(guid)` to read warning text |
| **Red** | Error — exception thrown | `gh_get_runtime_messages(guid)` for error text; investigate inputs |
| **Grey** (preview off) | Geometry preview disabled but computing fine | Cosmetic only |

**Recompute discipline.** Don't build the entire chain then recompute
once at the end. Recompute at every structural milestone:

1. After stage 01 wired → `gh_recompute` → check leftmost components
   for orange
2. After stage 02 → recompute → check the panelizer for tree shape
3. After each subsequent stage

Per-stage recompute catches per-stage problems; chained-up recompute
produces uninterpretable noise.

### Common causes of Null results — the 4-item checklist

When a component outputs `Null` (visible in a Panel or via
`gh_get_objects`), walk these in order:

1. **Invalid intersections.** Components like `Curve | Curve` (CCX,
   `Component_CurveIntersection`) or `Surface | Line` (SLX) return
   Null if the geometries don't physically touch in 3D space. Confirm
   geometric overlap before debugging the wiring.
2. **Open vs closed loops.** `Boundary Surfaces`, `Cap Holes`,
   `Planar Voronoi.Boundary` return Null if input curves aren't
   perfectly closed (start point ≠ end point, or not planar). Run
   `Join Curves` first to close polyline edges; check `Curve.Closed`
   property before feeding.
3. **Incorrect Domains.** `Isotrim` (Sub Surface) returns Null if the
   input U or V Domain falls outside the original surface's parametric
   range. Re-parameterize the surface to (0, 1) first (right-click
   the Surface input → "Reparameterize"), OR wire `Bounds` of the
   surface's actual domain into the Domain input.
4. **Negative or zero values.** Components requiring positive
   dimensions (`Offset Curve.Distance`, `Extrude.Direction` magnitude,
   `Circle.Radius`, `Series.Step`) break or produce empty geometry on
   `0` or negative. Check slider min and the actual value reaching
   the input.

### The MCP debugging toolkit — components that let you "see" the data

When a build looks wrong but no component is orange/red, drop these
in mid-chain to inspect:

| Component | What it shows | When to use |
|---|---|---|
| `Param Viewer` | Tree structure — branch count, path notation `{0;0}`, item count per branch | Suspected tree-alignment problem. Wire ANY output into Param Viewer's input — it diagrams the tree shape |
| `Panel` (the `//` text panel) | Literal text output of any data — including `Null` and `<Empty>` placeholders | Suspected Null. Panel will show "Null" explicitly on dead branches that look fine in the viewport |
| `Point List` | Item indices drawn at each point's location in the Rhino viewport | Suspected list-order problem. Confirms whether item 0 of one list aligns with item 0 of another (e.g., when wiring two `Split List` outputs into `Loft.Curves` and the loft looks twisted) |

Drop these in temporarily; remove after the build is validated. They
don't change the data flow — they just expose it.

### Baking — when (and when NOT) to bake

**Baking** converts Grasshopper preview geometry into permanent Rhino
document objects.

**Default: do NOT bake.** For parametric facade / massing / pattern
work, the deliverable is the *graph* — a tunable definition with
named sliders the user can drag. Baking commits one frozen state and
loses parametric editability.

**When to bake (explicit ask only):**
- User asks for "final geometry in Rhino" / "bake the result" /
  "send to fabrication"
- User names a target Rhino layer
- Downstream workflow is non-parametric (rendering, export, CAM)

If you bake without explicit instruction, you've removed the user's
ability to retune — which contradicts the whole point of the
parametric build. **When in doubt, leave it live and report the
sliders the user can drag.**

If you must bake, target a named layer for organization (e.g.
"BakedFacade") so the result is easy to find and clean up.

### "0 errors but nothing visible" — the recovery walk

You recomputed, canvas summary reports 0 errors / 0 warnings, but the
viewport is empty or wrong:

1. **Wrong Scenario?** `gh_status` — anything other than Execute or
   Author silently swallows writes.
2. **Host geometry via Python script?** Scripts return empty silently
   when GUID lookup fails. Use `Param_Brep` or `Geometry Pipeline`
   instead — see `reference/host-ingest.md`.
3. **Multi-GUID persistent data?** Only the first GUID resolves; the
   rest are silently dropped. See `reference/bridge-quirks.md`.
4. **Vector wired into a Line input** (or vice versa)? No runtime
   error — the wire just doesn't propagate. The `Extrude` vs
   `Extrude Linear` trap is the most common case.
5. **Tree alignment broken?** Drop a `Param Viewer` on the suspected
   component's output to see actual branch structure.
6. **Null on the wire?** Drop a `Panel` to read the literal output —
   it'll print "Null" if the data is dead.

---

## Probe-then-verify — the defensive default

Every component placement that might trap on naming:

```
1. gh_add_component(intended_name)
2. gh_find_components(intended_name)    →  get GUID
3. gh_get_objects([guid])               →  confirm kind, inputs, outputs
4. If wrong kind:  gh_remove_node(guid), try alternate name (e.g. nickname)
5. If right:       proceed to wire
```

Cheaper than building a 20-component chain on the wrong variant and
discovering at recompute. See `reference/bridge-quirks.md` for the
full component-name trap list.

---

## Reference files in this skill

- [`reference/bridge-quirks.md`](reference/bridge-quirks.md) — the
  full list of bridge-version-specific traps: component name
  resolution, persistent-data Panel-mode fallback, MCPv2 Scenario
  gating, ComponentScope, broken/stubbed tools, result-size cap.
  Re-validate on bridge version bumps.
- [`reference/host-ingest.md`](reference/host-ingest.md) — the
  canonical ways to bring existing Rhino geometry into the GH chain
  (`Param_Brep` "Set one Brep", `Geometry Pipeline` by layer, N-refs
  + `Merge Multiple`). The authoring rule: **use these patterns,
  never a Python script** for host ingest.

## How this skill composes with task skills

Task skills (facade-design, landform, etc.) bring **per-typology
recipes** — full wiring tables transcribed from validated `.gh`
files. The build is always:

1. **bridge-basics** (this skill) handles the *how* of working on the
   canvas: tier-aware wiring, casting rules, tree alignment, recompute
   discipline, Null troubleshooting, bridge quirk avoidance.
2. **task skill** handles the *what* of the typology: which
   components in what order, which sliders with what defaults,
   typology-specific anti-patterns.

When a recipe says "place `Component_Extrude` (NOT `Extrude
Linear`)", that's a pointer back to bridge-basics. When a recipe's
stage 01 shows `Rectangle → Param_Surface` as a *from-scratch*
placeholder, the substitution to `Param_Brep` for an existing-host
case lives in this skill's `host-ingest.md`.

Load both together; let bridge-basics catch the canvas-mechanics
mistakes so the task skill can focus on its recipe.

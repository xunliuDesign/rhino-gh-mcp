# Tiling Strategies — Module → Host

When the user has a premade module (a unit panel, a window, a
mashrabiya cell, a small assembly) and a host (a wall, a face, a
curved surface), this is the pipeline that orients the module to every
cell of the host.

The host has already been ingested per `geometry-ingest.md`; the
contract here is *base surface + module geometry + module base plane*.

## The five-step pipeline

```
1. Prepare module base plane + footprint
2. Subdivide host into oriented target frames
3. Orient: module base plane → each target plane
4. Reconcile fit (one of four policies)
5. Handle edges & openings
```

Build in this order. Recompute after step 2, after step 3, and after
step 5 — the three checkpoints where things go visibly wrong.

## Step 1 — module base plane + footprint

The module needs a *base plane*: an origin and a coordinate frame that
will be re-anchored to each target on the host. The module also has a
*footprint*: a 2D outline of how much surface area one instance takes
up.

### How to find the module base plane

If the module is a block instance:
- The block has a definition with its own world origin. `rhino_list_blocks`
  reports the block's `name` and `id`. Treat the block's world origin as
  the module's base plane origin, with `XY Plane` as the frame.

If the module is loose geometry the user selected:
- Take the geometry's bounding box and pick the **bottom face center**
  as the origin, with `XY Plane` (or whichever face is the "mount" face).
- One clarifying Q permitted: "Which face of your module mounts to the
  wall — the bottom, or the back? I'll use the bottom by default."
  *Skip the Q if there's an obvious axis (e.g. the module is much flatter
  on one side).*

### How to compute the footprint

Take the bbox at the chosen mount face's plane. `footprint_width` and
`footprint_height` come from the bbox dimensions in that plane.

Cache as: a `Plane` param `[module] base_plane` and a panel
`[module] footprint` reporting "W × H".

## Step 2 — subdivide the host into oriented target frames

Each target frame is an origin point on the host + an orientation
(surface normal + a consistent "up" reference).

### Native chain

```
host surface  ->  Divide Domain²(U=u_count, V=v_count)  ->  sub-domains
host surface + sub-domains  ->  Isotrim  ->  sub-surface list
sub-surfaces  ->  Area  ->  centroid points (one per cell)
sub-surfaces  ->  Evaluate Surface(u=0.5, v=0.5)  ->  centroid normals
                                                     (more accurate
                                                     than Area-based)
```

Build target planes:

```
centroid points + centroid normals  ->  Plane Normal  ->  target planes
                                                          (one per cell)
```

Consistent "up": `Plane Normal` outputs a plane with X aligned to the
world X-axis projected onto the normal's plane (Grasshopper's default).
This is the right answer for most vertical walls (you get "up" = world
+Z within each cell's plane). For a roof or canopy, the default may
flip oddly — override by building the plane via `Construct Plane(P,
X, Y)` with an explicit X axis computed as `Cross(world_up, normal)`.

### Counts

How many cells? Depends on the fit policy (next step). For the default
(fixed-size + grid-from-module):

```
u_count = round(host_bbox_width / footprint_width)
v_count = round(host_bbox_height / footprint_height)
```

Expose these as `[facade] u_count` and `[facade] v_count` sliders. Use
the defaults table values as the *initial* count but compute the
range from the host bbox so the user can sweep across realistic counts.

## Step 3 — Orient module → target planes

The `Orient` component does the work:

```
gh_add_component("Orient")
inputs:
  G (Geometry):  module geometry list (instances will inherit module
                  geometry per-target)
  A (Source plane):  [module] base_plane (one)
  B (Target plane):  list of target planes (one per cell — N planes)
output:
  G (Geometry):  N copies of the module, each oriented to one target
```

The data tree gotcha that breaks this every time:

- `A` should be **one** plane.
- `B` should be a **list** of N planes — *all in one branch*, not one
  branch per plane. If B is grafted (one branch per plane), Orient does
  one copy total instead of N.
- `G` should be **one** geometry (or one branch of geometries that get
  grouped together as the module).

Verify with `gh_get_objects(orient_guid)` after wiring; if N is wrong,
check the `B` source's tree depth with `gh_get_objects` on its
predecessor.

**`append=True` use case:** if you have target planes coming from two
sources (e.g. one set on the main facade, another on a window
surround), wire both into Orient's `B` with `append=True` on the
second call, so Orient receives the merged list. Without `append`, the
second wire replaces the first.

## Step 4 — fit policies (four)

The module rarely fits the host exactly. Pick a policy.

### 4.1 Scale-to-fit (rare default)

Scale the module to fill each cell exactly. Use when modules are
visual / decorative and exact dimensional matching matters less.

Wiring: add a `Scale NU` after `Orient` with non-uniform factors
`cell_width / footprint_width` and `cell_height / footprint_height`.

Result: modules deform per cell. Acceptable for screens / cladding;
**not** acceptable for windows, structural panels, or anything with
real dimensions.

### 4.2 Fixed-size + grid-from-module (DEFAULT)

Compute the grid count from the host / module size ratio. Modules stay
their native size. Leftover space at the edges is handled in step 5.

This is the default unless the prompt says otherwise. It honors the
module's real dimensions and produces a regular grid.

### 4.3 Fixed-size + trim

Keep the module size, place on a regular grid, **trim** instances whose
center falls outside the host. Edges get partial coverage.

Use when the host is irregular or has cutouts. Couple with step 5's
center-test culling.

### 4.4 Adaptive (if module exposes parameters)

If the module is itself parametric (it has sliders for `width` /
`height`), recompute the module per cell with cell-matched dimensions.

Requires the module to be a *parametric definition*, not a static
block. Detect by checking whether `rhino_list_blocks` returned a block
with an `update_type` of "linked" or "embedded" (linked → parametric
host, possibly adaptive). For a static block, this policy is
unavailable — fall back to 4.2.

### Switch table

| Prompt phrase | Policy |
|---|---|
| (default — nothing specific) | 4.2 fixed-size + grid-from-module |
| "stretch the module to fit", "fit each cell exactly" | 4.1 scale-to-fit |
| "trim at the edges", "irregular host", "around the windows" | 4.3 fixed-size + trim |
| "scale the module with the cell size", "adaptive", "parametric module" | 4.4 adaptive (verify module is parametric first) |

## Step 5 — edges & openings

Two failure modes to address:

### 5.1 Modules at the edge of the host

For policies 4.2 and 4.3, the rightmost / topmost cells may not have a
full module's worth of space. Two responses:

- **Cull**: drop instances whose footprint extends past the host
  boundary. Use `Point in Curve` against the host's outer boundary on
  each target-plane origin; cull `false` results.
- **Trim**: clip the module geometry to the host. Use `Surface Split`
  on the module's projected outline against the host outline; keep
  the inside half.

Default is cull — simpler and gives a clean, regular grid with a small
border of bare host.

### 5.2 Modules in window / door openings

If the host has openings (the user has `Brep` holes in the facade, or
separate `Curve`s on a `windows` layer), modules must not fill them.

Pull the opening curves into the canvas (same GUID handoff as the host
— a `Curve` param wired by GUID, or a Python 3 script reading by
layer). Build a flat region for each opening (`Boundary Surfaces` if
planar). For each target-plane origin, run `Point in Curve` against
each opening curve; if inside any, cull.

Wire diagram:

```
target_origins  ->  Point in Curve(opening_curves)  ->  inside? (bool)
                ->  Negative (Logical Not)          ->  outside? (bool)
                ->  Dispatch (target_planes)        ->  kept_planes,
                                                       culled_planes
kept_planes  ->  Orient input B  ->  modules only on the wall, not in
                                     windows
```

Report partial coverage in the closing summary: "Tiled 124 modules;
culled 8 that overlapped openings; 4 partial cells at the right edge
left bare." The user shouldn't have to discover this by counting.

## Drivers exposed to the user

Per the brief: tiling drivers (density, spacing, optional attractor)
all become sliders.

| Driver | Default | Range | Type | Notes |
|---|---|---|---|---|
| `u_count` | computed from host/module ratio | 1 – 3× the computed value | int | The user's primary density dial |
| `v_count` | computed from host/module ratio | 1 – 3× the computed value | int | |
| `u_offset` | 0 | -1 – 1 | float | Per-row shift, for staggered / running-bond layouts |
| `v_offset` | 0 | -1 – 1 | float | |
| `attractor_strength` (optional) | 0 | 0 – 2 | float | When non-zero, drives per-cell scale or rotation toward an attractor |
| `attractor_pt_x/y/z` (optional) | host center | bbox bounds | float | Position of the attractor in world coords |

When attractor is engaged: wire `Distance(attractor_pt, target_origin)`
→ `Bounds` → `Remap Numbers` into `(0, attractor_strength)` → multiply
the module's per-cell scale / rotation. This is the same attractor
pattern as the panel-grid recipes; share the subgraph.

## Common failure modes (tiling-specific)

| Symptom | Cause | Fix |
|---|---|---|
| Only one module appears at the origin | Orient B is grafted, not a flat list | Re-graft / flatten Orient B input; verify tree with `gh_get_objects` |
| Modules face wrong way (into the wall) | Module base plane Z axis points opposite the target plane Z axis | Reverse the module base plane's normal before wiring |
| Modules rotate inconsistently | Target planes' X axes flip across the host (Plane Normal default behavior) | Override target plane construction with explicit `Construct Plane(P, X, Y)` and computed cross-product X axis |
| Modules in openings | Step 5.2 skipped or opening curves not on the right layer | Re-check opening source; verify `Point in Curve` is using the correct curves |
| Edge cells distorted (policy 4.1) | Scale-to-fit applied but module has non-isotropic features (e.g. window with mullions) | Switch to policy 4.2 (fixed-size + grid-from-module) — or 4.4 if module is parametric |
| Last column / row missing | Floor instead of round in `u_count` calc | Use `round` not `floor`; or expose `u_count` as a slider and let user adjust |

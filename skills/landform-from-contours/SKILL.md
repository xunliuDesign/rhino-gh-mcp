---
name: landform-from-contours
description: |
  Converts a layer of imported contour curves in Rhino into a watertight
  terrain mesh, with derived slope/aspect analysis surfaces, using a curated
  set of Grasshopper user-objects. Use this when the user provides contour
  data (DEM, survey, GIS export) and asks for terrain modeling.
policy: curated
curated_group: ["MCP", "Landform"]
version: 0.1.0
---

# Landform from contours

A reference workflow that takes a layer of contour curves and produces:

1. A patched / lofted terrain surface and mesh.
2. A "draped" plan (projection of any plan-view geometry onto the terrain).
3. Slope and aspect maps suitable for site-suitability analysis.

This is the canonical curated-mode example for rhino-gh-mcp — the user-objects
referenced below constrain what the LLM is allowed to place on the canvas. See
[../README.md](../README.md) for the broader Skills conventions.

## Inputs you need from the user

- A Rhino layer name (default: `Contours`) containing the contour curves.
- Approximate XY bounds of the site, or a closed boundary curve on a known layer.
- Target mesh resolution (coarse / medium / fine).

If the user hasn't supplied these, ask once. Don't try to infer from geometry
before asking.

## Engagement

Start the server with `--policy curated --curated-group MCP,Landform`. The
curated allow-list keeps the model focused on this domain — it can still call
`rhino_*` read tools and `gh_*` slider/panel tools freely.

## Step-by-step strategy

### 1. Orient yourself (cheap reads first)

- `gh_status` — confirm the GH bridge is up.
- `rhino_status` — confirm the Rhino bridge is up.
- `rhino_get_layers` — find the contour layer (case-insensitive match for
  "contour"). If multiple candidates, ask the user.
- `rhino_capture_viewport` with `show_annotations=False` — get a visual
  reference of the site.

### 2. Read the existing canvas

- `gh_get_context(simplified=True)` — see what's already on the canvas.
- If a landform definition is already partially built (user-objects present
  with nicknames like `Terrain.*`), continue from there. Otherwise build fresh.

### 3. Build from user-objects

The curated group exposes these user-objects (place via `gh_add_component`):

- `Terrain.ContoursIn` — reads contour curves by layer name
- `Terrain.PatchSurface` — patches a Brep from contours
- `Terrain.MeshFromSurface` — meshes the surface at a target resolution
- `Terrain.SlopeMap` — colored mesh by slope
- `Terrain.AspectMap` — colored mesh by aspect (compass direction of normal)

Place them, then connect with `gh_connect_components`. Add `gh_add_slider` for
the resolution input.

### 4. Set parameters

- Use `gh_set_component_parameter` to write the layer name into `Terrain.ContoursIn`.
- Use `gh_set_slider` for resolution.
- `gh_recompute` to evaluate.

### 5. Verify

- `gh_get_runtime_messages` on each user-object — catch errors before the user
  notices them.
- `rhino_capture_viewport` to confirm the terrain looks reasonable.
- If the mesh is degenerate (no faces, NaN bounds), debug by stepping back
  through `gh_get_objects` on the failing component.

### 6. Hand off

Report: which layer was used, the resolution chosen, the GUID of the slope-
map output, and any warnings. Don't bake to Rhino unless the user asks — the
GH definition is the source of truth.

## What NOT to do

- Don't escalate to `gh_add_any_component` or script injection — that's L3
  territory. If the user objects's behavior is wrong, ask them to update the
  user-object definition, then re-engage.
- Don't run `rhino_execute_code` — not available in this policy.
- Don't bake without asking.

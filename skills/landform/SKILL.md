---
name: landform
description: |
  Build a parametric landform definition on a Grasshopper canvas using the
  bundled MCP landscape user-objects. Drives the LLM through the canonical
  BaseMesh -> PrimitiveGeometry -> GeometryPlacer -> Modifier -> Visualizer
  chain. Best used when the user wants to design or iterate on a terrain
  surface, urban ground plane, garden topography, or any large mesh that
  needs to be sculpted by point/curve influence.
recommended_capabilities:
  allow_parameters: true
  allow_components: true
  allow_scripting: false
recommended_scope: curated
recommended_category_filter: "MCP"
bundled_assets:
  - "MCP_UserObjects_Landscape example/Create Landform.ghuser"
  - "MCP_UserObjects_Landscape example/Grid Geometry Placer.ghuser"
  - "MCP_UserObjects_Landscape example/Random Geometry Placer.ghuser"
  - "MCP_UserObjects_Landscape example/Absolute Modifier.ghuser"
  - "MCP_UserObjects_Landscape example/Relative Modifier.ghuser"
  - "MCP_UserObjects_Landscape example/Noise Modifier.ghuser"
  - "MCP_UserObjects_Landscape example/Landform Render.ghuser"
  - "MCP_UserObjects_Landscape example/Vis Contour.ghuser"
---

# Parametric Landform Skill

You are operating on a live Grasshopper canvas through the `rhino-gh-mcp`
server. Your job is to build a working parametric landform definition by
placing, wiring, and configuring components from the **MCP** category.

The MCP-category components are user-objects from this skill's bundle. Make
sure they are installed on the user's machine before starting (see
`MCP_UserObjects_Landscape example/` in this skill folder; user-objects
go in `%APPDATA%\Grasshopper\UserObjects\` on Windows or
`~/Library/Application Support/McNeel/Rhinoceros/8.0/scripts/UserObjects/`
on Mac, then restart Grasshopper).

## Before you start

1. Call `gh_status` to confirm the bridge is live.
2. Call `gh_canvas_summary` to see what's already on the canvas.
3. Call `gh_list_available_components` (or `get_all_component_proxies`
   if the client uses the older name) to confirm the MCP user-objects are
   discoverable. Look for these subcategories: `BaseMesh`,
   `Primitive Geometries`, `Geometry Placer`, `Modifiers`, `Visualizers`.
4. If any are missing, stop and ask the user to install the bundled
   user-objects before continuing.

## The required chain

Every landform definition has this dataflow shape:

```
BaseMesh -> GeometryPlacer (driven by a PrimitiveGeometry) -> Modifier(s) -> Visualizer
```

The BaseMesh output must ALSO be wired into every Modifier's inputMesh, not
just the first one in the chain. The Modifier's outputMesh feeds the next
Modifier (if any) and ultimately the Visualizer.

## Step-by-step workflow

### 1. BaseMesh

Add a `Create Landform` component. This produces the initial flat mesh to
be sculpted.

- Defaults: `size = 400`, `resolution = 100`. Keep these unless the user
  asks otherwise.
- Add a slider for `size` (range 100-1000) and a slider for `resolution`
  (range 20-200, integer).

### 2. PrimitiveGeometry

Add a primitive that will drive the sculpting. The most useful options:

- `Circle` (from MCP) — radial influence
- `Rectangle` (from MCP) — orthogonal influence
- `Curve` / `Point` — direct user-drawn shapes if they want hand control

For a default starting point, use a Circle with radius slider (range 10-200).

### 3. GeometryPlacer

Add either:

- `Grid Geometry Placer` — repeats the primitive on a regular grid across
  the base mesh
- `Random Geometry Placer` — scatters the primitive randomly

Wire:

- the PrimitiveGeometry into the placer's `inputGeometry` input
- BaseMesh's `outputMesh` into the placer's `inputMesh` input

For Grid placer, add sliders for grid resolution (X, Y), and for Random
placer, add a seed slider + count slider.

### 4. Modifier(s)

Add at least one Modifier. Options:

- `Absolute Modifier` — pulls the mesh up/down by a fixed height
- `Relative Modifier` — multiplies the existing displacement
- `Noise Modifier` — adds procedural noise (use after another modifier to
  layer "natural" variation on top of designed forms)

Wire each Modifier:

- the previous stage's geometries output into this Modifier's `geometries`
  input
- **CRITICAL**: BaseMesh's `outputMesh` into THIS Modifier's `inputMesh`
  (every modifier in the chain needs the base reference, not just the
  first one)

You can stack modifiers — multiple instances of the same type are fine.
Add a height/strength slider for each.

### 5. Visualizer

Always add `Landform Render`. Optionally also add `Vis Contour`.

Wire:

- the last Modifier's `outputMesh` into the Visualizer's `inputMesh`

For Vis Contour, add a slider for contour interval (range 1-20).

### 6. Finalize

1. Call `gh_recompute` to evaluate the whole graph.
2. Call `gh_get_runtime_messages` on each placed component if anything
   shows red — diagnose and fix wiring/parameter issues.
3. Call `rhino_set_view(standard="Perspective")` then
   `rhino_capture_viewport` so the user can see the result.

## Robustness rules

- **Always** call `gh_get_objects` (or the older `expire_and_get_info`)
  for a component before wiring it — input/output names matter and
  occasionally vary between user-object versions.
- If a `gh_connect_components` call fails with "input not found", re-read
  the component's inputs via `gh_get_objects` and retry with the actual
  name.
- If `gh_add_component` returns "not found", check that the MCP
  user-objects are installed (see "Before you start").
- Treat Rhino and Grasshopper as separate environments. Don't read Rhino
  scene info while building a landform — it's not needed and adds noise.

## Worked example

User: "Make me a landform with a circular hill in the middle."

1. `gh_canvas_summary` — confirm empty canvas
2. `gh_add_component("Create Landform", 100, 100)` -> base_guid
3. `gh_add_slider("size", 100, 1000, 400, 100, 200)` -> size_guid;
   `gh_connect_components(size_guid, "", base_guid, "size")`
4. `gh_add_slider("resolution", 20, 200, 100, 100, 250, integer=True)`
   -> res_guid; connect to base "resolution"
5. `gh_add_component("Circle", 300, 100)` -> circle_guid
6. `gh_add_slider("radius", 10, 200, 60, 300, 200)` -> r_guid;
   connect to circle "Radius"
7. `gh_add_component("Grid Geometry Placer", 500, 100)` -> placer_guid;
   connect circle "C" -> placer "inputGeometry";
   connect base "outputMesh" -> placer "inputMesh"
8. `gh_add_component("Absolute Modifier", 700, 100)` -> mod_guid;
   connect placer "geometries" -> mod "geometries";
   connect base "outputMesh" -> mod "inputMesh"  (do not skip this!)
9. `gh_add_slider("height", 0, 100, 30, 700, 200)` -> h_guid;
   connect h_guid -> mod "height"
10. `gh_add_component("Landform Render", 900, 100)` -> vis_guid;
    connect mod "outputMesh" -> vis "inputMesh"
11. `gh_recompute`
12. `rhino_set_view(standard="Perspective")` +
    `rhino_capture_viewport`

Report the captured viewport back to the user.

## Variations to suggest

After the default chain is built and recomputed, common useful tweaks:

- "Add a noise modifier on top" -> chain a Noise Modifier between the
  current modifier and the Landform Render
- "Use a Random Placer instead" -> remove the Grid Placer, add Random
  Geometry Placer with the same connections
- "Make the hill taller" -> set the absolute modifier's height slider
- "Show me contour lines" -> add Vis Contour wired to the same final
  mesh as Landform Render

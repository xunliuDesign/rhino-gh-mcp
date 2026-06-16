---
name: ladybug-environmental
version: 0.2.0
description: |
  Drive basic environmental analysis on a Rhino site model using the
  Ladybug Tools Grasshopper plugin. Three canonical workflows: sun path
  visualization, sun-hours analysis on geometry, and wind rose. All start
  from an EPW weather file. Use this skill when the user asks about
  daylight, solar access, wind direction, or general climate analysis
  for a site.
modes: [coach, execute]
required_plugins: [ladybug]
required_capabilities:
  allow_parameters: true
  allow_components: true
  allow_scripting: false
allowed_categories: [Ladybug, Params, Sets, Curve, Surface, Vector]
recommended_scope: defaults
recommended_category_filter: "Ladybug, Params, Sets"
prerequisites:
  - "Ladybug Tools must be installed in the user's Rhino (food4rhino / package manager)."
  - "An EPW weather file. The user can download one from epwmap.com or another EPW source."
  - "ComponentScope on the canvas must be set to 'defaults' (1) or 'all' (2), so Ladybug components are placeable."
# Execute-mode allowlist — the raw tools the three canonical workflows need.
allow_tools:
  - gh_add_component
  - gh_add_any_component
  - gh_add_slider
  - gh_connect_components
  - gh_set_slider
  - gh_set_slider_range
  - gh_set_component_parameter
  - gh_recompute
  - rhino_set_view
references:
  - https://docs.ladybug.tools/ladybug-primer/
  - https://github.com/ladybug-tools/ladybug-grasshopper
---

# Ladybug Tools Environmental Analysis

You are operating on a live Grasshopper canvas through the `rhino-gh-mcp`
server. Your job is to set up a Ladybug Tools environmental analysis
based on what the user asks. This skill covers three canonical workflows;
pick the one that matches the user's intent.

> **DOCUMENTATION-ONLY SKILL**: this skill was authored from the public
> Ladybug Primer without live testing. Component input/output names
> below match the v1.x Ladybug plugin (the modern "LB" prefix); if the
> user has the legacy plugin (single-name components like "Ladybug_SunPath"),
> ask them to clarify which version is installed.

## Before you start

1. Call `gh_status` to confirm the bridge is live.
2. Call `gh_canvas_summary` to see what's already on the canvas.
3. Call `gh_get_capabilities` (or inspect the canvas component's inputs)
   to confirm `ComponentScope` is `defaults` (1) or `all` (2). Ladybug
   components live under the `Ladybug` category and won't be placeable
   from `curated` scope unless the user explicitly adds `Ladybug` to
   their `CategoryFilter`.
4. Call `gh_list_available_components(refresh=True)` and check the result
   for at least one component starting with `LB ` (e.g. `LB Import EPW`).
   If nothing matches, stop and tell the user: *"I don't see Ladybug
   components on this Grasshopper install. Please install the Ladybug
   Tools plugin via the food4rhino package manager or the LBT
   installer, then restart Grasshopper and try again."*

## Common foundation: load an EPW

Every workflow needs weather data. The user provides a path to a `.epw`
file (a weather data file, typically downloaded from
https://epwmap.com or similar).

1. Place an `LB Import EPW` component. The `_epw_file` input takes a
   string path to the .epw on disk. Either:
   - Ask the user for the path, then pass it via `gh_set_component_parameter`
     to the `_epw_file` input.
   - Or add a `File Path` parameter (stock GH component) connected to
     the input.

`LB Import EPW` outputs include:
- `location` — site location object (lat/lon/timezone), used by SunPath
- `dry_bulb_temperature`, `relative_humidity`, `wind_speed`,
  `wind_direction`, `direct_normal_rad`, `diffuse_horizontal_rad`,
  `global_horizontal_rad`, and many more — hourly data collections
- `years`, `dew_point_temperature`, `sky_cover`, etc.

## Workflow A — Sun Path

Useful when the user wants: *"show me how the sun moves across this
site over the year"* or to visualise solar access.

```
.epw -> LB Import EPW -> LB SunPath
                    \
                     +--> [optional analemma / sun vectors -> shading studies]
```

Steps:

1. Place `LB Import EPW`, wire in the .epw path as above.
2. Place `LB SunPath`. Connect:
   - `LB Import EPW.location` -> `LB SunPath._location`
3. Optional but commonly useful inputs on `LB SunPath`:
   - `_hoys` (Hour Of Year list) — to highlight specific times; leave
     empty to draw the full annual analemma
   - `north_` — angle in degrees (default 0 = North up)
   - `center_pt_` — Point3d (default origin)
   - `_scale_` — number (default 1.0)
   - `dl_saving_` — Boolean (whether to account for daylight saving)
4. Useful outputs to wire to `LB Preview VisualizationSet` (for
   rich Rhino-side display) OR just leave on the canvas for the
   default preview:
   - `analemma` — the figure-8 sun path curves
   - `compass` — N/E/S/W reference geometry
   - `sun_pts` — sun positions
   - `vectors` — sun direction vectors (input for shading analysis)
5. Call `gh_recompute`, then `rhino_set_view(standard="Perspective")`
   and `rhino_capture_viewport` so the user sees the result.

## Workflow B — Sun Hours / Solar Access Analysis

Useful when the user wants: *"how many hours of direct sun does this
patio (or solar panel, or facade) get?"* — produces a colored mesh
overlay on the input geometry.

```
.epw -> LB Import EPW -> LB SunPath (vectors)
                                      \
geometry from Rhino doc -----> LB Direct Sun Hours -> colored mesh
```

Steps:

1. Place `LB Import EPW` + `LB SunPath` per Workflow A. Note the
   `vectors` output of `LB SunPath`.
2. The geometry to analyse comes from the Rhino doc. Ways to feed it in:
   - User has already selected geometry: ask them to right-click a
     `Brep` or `Mesh` param on the canvas and "Set One Brep / Set Multiple
     Breps", picking from the viewport.
   - Or use `gh_get_objects_with_metadata` from the Rhino side to find
     specific objects by layer name, then ask the user to manually wire
     them in (we don't currently have a `bake_from_rhino_to_gh` tool).
3. Place `LB Direct Sun Hours` (or `LB Incident Radiation` if you want
   energy rather than hours):
   - `_geometry` <- the Brep/Mesh param holding the user's geometry
   - `_vectors` <- `LB SunPath.vectors`
   - Optional `_grid_size_`: default 1.0 (units of the Rhino doc).
     Smaller = finer analysis but slower.
   - Optional `context_`: shading geometry that blocks sun but
     isn't being analysed (other buildings, trees).
4. Outputs to surface:
   - `results` — hours per analysis face, useful as a panel
   - `mesh` — colored result mesh, preview directly
   - `total` — sum across all faces
5. Recompute, capture viewport.

## Workflow C — Wind Rose

Useful when the user asks: *"what's the prevailing wind direction at
this site?"* or *"show me the wind frequency by direction."*

```
.epw -> LB Import EPW -> wind_speed + wind_direction
                                          \
                                           LB Wind Rose
```

Steps:

1. Place `LB Import EPW`. Note the outputs `wind_speed` and
   `wind_direction` — both are hourly data collections.
2. Place `LB Wind Rose`. Connect:
   - `LB Import EPW.wind_speed` -> `LB Wind Rose._data`
     (data is whatever you want binned by direction — wind speed is the
     canonical choice; you could also pass dry-bulb temperature or any
     other hourly series for a "wind-binned temperature rose")
   - `LB Import EPW.wind_direction` -> `LB Wind Rose._wind_direction`
3. Useful optional inputs on `LB Wind Rose`:
   - `north_` — north angle
   - `_dir_count_` — number of direction segments (default 16)
   - `_center_pt_` — placement in the Rhino scene
   - `legend_par_` — for customising the legend
   - `statement_` — filter expression like `"a >= 3"` to show only days
     with wind speed >= 3 m/s
4. Outputs to surface to the user:
   - `mesh` — the colored rose
   - `prevailing` — predominant wind direction in degrees clockwise from N
   - `calm_hours` — number of hours below the calm threshold
   - `histogram_data` — raw frequency data for further analysis
5. Recompute, capture viewport.

## Robustness rules

- The first time you place an `LB ` component on a fresh canvas,
  Ladybug may pop a one-time license / acceptance dialog on the Rhino
  side. The user has to dismiss this manually. If you see a long delay
  or runtime error after the first placement, ask the user to check
  Rhino's Notification area.
- If `gh_add_component("LB SunPath")` returns "not found in Grasshopper-default
  scope", the canvas's `ComponentScope` is too restrictive. Either:
  - Ask the user to flip `ComponentScope` to `defaults` (1) or `all` (2),
    OR
  - Ask them to add `Ladybug` to the `CategoryFilter` text input.
- `LB Direct Sun Hours` can be slow on dense meshes. Start with
  `_grid_size_` = 1.0 (or larger) and refine after the user confirms
  the geometry is correct.
- The `_run` input on the analysis-heavy components (Sun Hours, Incident
  Radiation, etc) is a Boolean — typically connected to a Button or a
  Boolean Toggle. Set it via `gh_add_component` + `gh_set_toggle`.

## Variations to suggest after the default workflow

- *"Show me only the summer months"* — add an `LB Analysis Period`
  component, connect its `period` output to the `period_` input on
  SunPath / Wind Rose.
- *"Visualise it in 3D over the model"* — use `LB Preview
  VisualizationSet` rather than the default preview.
- *"What about radiation rather than just hours?"* — swap `LB Direct
  Sun Hours` for `LB Incident Radiation`. Same inputs, returns kWh/m²
  instead of hours.

## Stretch — Honeybee / Dragonfly

If the user starts asking about energy modelling, daylighting with
glazing, indoor comfort, or whole-building energy simulation, those
are **Honeybee** (single-building) or **Dragonfly** (urban-scale)
workflows — separate plugins in the Ladybug Tools suite. This skill
doesn't cover them; ask the user to clarify their goal and consider
authoring a sibling skill if the workflow is well-defined.

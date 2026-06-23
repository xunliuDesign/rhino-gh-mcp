# Example — Tile a Premade Module onto a Host Face

Branch C of the router. The user has both a host face and a module
(block instance) in the Rhino doc.

## Prompt

> *"Tile the 'window_unit' block across the front of this building."*

Synonym map: no typology word directly — but "tile" + "block" + "across"
is unambiguously tile-module. Default underlying typology is
panelized (one module per cell).

## Preflight

```
gh_status()             → ok
rhino_status()          → ok
rhino_get_scene_info()  → {"length_units": "Meters", ...}
rhino_list_blocks()     → [{"name": "window_unit",
                            "id": "<block-guid>",
                            "object_count": 7,
                            "update_type": "static"}]
```

A static block named `window_unit`. Its bbox (from the block's
`get_scene_info` sampling) is 1.5 × 1.2 × 0.3 m — a window-sized
unit. This is the **module**.

Find the host:

```
rhino_get_objects_with_metadata(
    filters={"layer": "building"},
    metadata_fields=["short_id", "type", "bbox", "name"],
)
→ [
    {"short_id": "b1", "type": "Brep",
     "bbox": {"min":[0,0,0], "max":[30, 12, 24]},
     "name": "office"}
  ]
```

A 30 × 12 × 24 m Brep. "Front" is unspecified but in conjunction with
"this building" + a single object, default policy picks the largest
non-horizontal face. Largest is the 30 × 24 (=720 m²) faces; tied
between the two long sides. Pick one (say, +Y) and report the choice.

(One clarifying Q permitted under the load-bearing rule. For this
example, autonomous mode = skip the Q, pick +Y, flag in report.)

## Host ingest (Python 3 face picker + outward-normal check)

```
brep_g = gh_add_component("Brep", 100, 100)
gh_set_component_parameter(brep_g, "", "<rhino-guid-of-b1>")

face_script_g = gh_add_component("Python 3 Script", 300, 100)
gh_write_script_py3(
    face_script_g,
    code="""
import Rhino.Geometry as rg

# inputs: mass (Brep), bias (str — "+Y","-Y","+X","-X")
# outputs: facade_face (Brep)

bias_axis = bias[1] if bias else "Y"
bias_sign = 1 if (not bias or bias[0] == "+") else -1
target_axis = rg.Vector3d.YAxis if bias_axis == "Y" else rg.Vector3d.XAxis
target_axis *= bias_sign

best_face = None
best_score = -1
for face in mass.Faces:
    u_mid = face.Domain(0).Mid
    v_mid = face.Domain(1).Mid
    n = face.NormalAt(u_mid, v_mid)
    if abs(n.Z) > 0.8:
        continue
    score = rg.Vector3d(n) * target_axis
    if score > best_score:
        area = rg.AreaMassProperties.Compute(face).Area
        if best_face is None or area >= 0.9 * best_face_area:
            best_score = score
            best_face = face
            best_face_area = area

face_brep = best_face.DuplicateFace(True)
p = best_face.PointAt(best_face.Domain(0).Mid,
                      best_face.Domain(1).Mid)
n = best_face.NormalAt(best_face.Domain(0).Mid,
                       best_face.Domain(1).Mid)
center = mass.GetBoundingBox(True).Center
if rg.Vector3d(p - center) * n < 0:
    face_brep.Flip()
facade_face = face_brep
""",
    param_definitions=[
        {"type":"input", "name":"mass", "type_hint":"Brep"},
        {"type":"input", "name":"bias", "type_hint":"str"},
        {"type":"output","name":"facade_face","type_hint":"Brep"},
    ],
)
gh_connect_components(brep_g, "Brep", face_script_g, "mass")

bias_panel_g = gh_add_component("Panel", 300, 250)
gh_set_component_parameter(bias_panel_g, "", "+Y")
gh_connect_components(bias_panel_g, "", face_script_g, "bias")

face_param_g = gh_add_component("Surface", 500, 100)
gh_connect_components(face_script_g, "facade_face",
                     face_param_g, "")
gh_set_component_parameter(face_param_g, "NickName",
                           "[base] facade_face")
```

## Module ingest

The block instance can be referenced into the canvas via its block
definition GUID, but the cleaner path is to bake one instance into a
known position, link it via a `Geometry` param, and use its bbox to
seed the module base plane.

For the autonomous trace, write a Python 3 script that grabs the block
definition's geometry directly:

```
module_g = gh_add_component("Python 3 Script", 100, 400)
gh_write_script_py3(
    module_g,
    code="""
import Rhino
import Rhino.Geometry as rg
import scriptcontext as sc

# inputs: block_name (str)
# outputs: geom (Brep — combined), base_plane (Plane),
#          footprint (Rectangle3d)

doc = sc.doc
idef = None
for d in doc.InstanceDefinitions:
    if d is None: continue
    if d.Name == block_name:
        idef = d
        break

if idef is None:
    geom = None; base_plane = None; footprint = None
else:
    # Get the definition's geometry
    geoms = []
    for o in idef.GetObjects():
        g = o.Geometry
        if isinstance(g, rg.Extrusion):
            g = g.ToBrep()
        if isinstance(g, (rg.Brep, rg.Surface, rg.Mesh)):
            geoms.append(g)

    if not geoms:
        geom = None; base_plane = None; footprint = None
    else:
        # Combined bbox
        bbox = rg.BoundingBox.Empty
        for g in geoms:
            bbox.Union(g.GetBoundingBox(True))
        # Base plane: bottom face center, XY-aligned
        bottom_center = rg.Point3d(
            (bbox.Min.X + bbox.Max.X) * 0.5,
            (bbox.Min.Y + bbox.Max.Y) * 0.5,
            bbox.Min.Z,
        )
        base_plane = rg.Plane(bottom_center,
                              rg.Vector3d.XAxis,
                              rg.Vector3d.YAxis)
        footprint = rg.Rectangle3d(
            base_plane,
            bbox.Max.X - bbox.Min.X,
            bbox.Max.Y - bbox.Min.Y,
        )
        # Group all geometry
        geom = geoms  # let the script output a list
""",
    param_definitions=[
        {"type":"input", "name":"block_name", "type_hint":"str"},
        {"type":"output","name":"geom",       "type_hint":"Brep"},
        {"type":"output","name":"base_plane", "type_hint":"Plane"},
        {"type":"output","name":"footprint",  "type_hint":"Rectangle3d"},
    ],
)
name_panel_g = gh_add_component("Panel", 100, 480)
gh_set_component_parameter(name_panel_g, "", "window_unit")
gh_connect_components(name_panel_g, "", module_g, "block_name")
```

Tag the outputs as `[module] geom`, `[module] base_plane`,
`[module] footprint`:

```
geom_param_g = gh_add_component("Geometry", 300, 400)
gh_connect_components(module_g, "geom", geom_param_g, "")
gh_set_component_parameter(geom_param_g, "NickName",
                           "[module] geom")

plane_param_g = gh_add_component("Plane", 300, 450)
gh_connect_components(module_g, "base_plane", plane_param_g, "")
gh_set_component_parameter(plane_param_g, "NickName",
                           "[module] base_plane")
```

## Tiling subgraph (per tiling-strategies.md)

Default policy: fixed-size + grid-from-module. Compute `u_count` and
`v_count` from host_bbox / footprint.

Host face bbox: 30 (X) × 24 (Z). Module footprint: 1.5 × 1.2 → for a
+Y-facing wall, "width" maps to X (1.5 m) and "height" maps to Z
(1.2 m). u_count = round(30 / 1.5) = 20; v_count = round(24 / 1.2) = 20.

Slider for user override:

```
u_count_g = gh_add_slider("[facade] u_count", 5, 60, 20,
                          700, 50, integer=True)
v_count_g = gh_add_slider("[facade] v_count", 5, 60, 20,
                          700, 90, integer=True)
```

Subdivide and build target planes:

```
dd_g = gh_add_component("Divide Domain²", 900, 100)
gh_connect_components(face_param_g, "", dd_g, "Domain")
gh_connect_components(u_count_g, "", dd_g, "U Count")
gh_connect_components(v_count_g, "", dd_g, "V Count")

iso_g = gh_add_component("Isotrim", 1100, 100)
gh_connect_components(face_param_g, "", iso_g, "Surface")
gh_connect_components(dd_g, "Segments", iso_g, "Domain")

area_g = gh_add_component("Area", 1300, 100)
gh_connect_components(iso_g, "Surface", area_g, "Geometry")

eval_g = gh_add_component("Evaluate Surface", 1300, 200)
gh_connect_components(iso_g, "Surface", eval_g, "Surface")
# Use uv = (0.5, 0.5) — Construct Point not needed because Evaluate Surface
# accepts a single UV input. Set persistent data:
gh_set_component_parameter(eval_g, "Point", "{0.5, 0.5}")

# Combine into a target plane per cell
plane_norm_g = gh_add_component("Plane Normal", 1500, 100)
gh_connect_components(eval_g, "Point", plane_norm_g, "Origin")
gh_connect_components(eval_g, "Normal", plane_norm_g, "Z-Axis")
```

Orient module to every target plane:

```
orient_g = gh_add_component("Orient", 1700, 100)
gh_connect_components(geom_param_g, "", orient_g, "Geometry")
gh_connect_components(plane_param_g, "", orient_g, "Source Plane")
gh_connect_components(plane_norm_g, "Plane", orient_g, "Target Plane")
```

Read live ports right after placement — `gh_get_objects([orient_g])`
to confirm the input names ("G", "A", "B" vs "Geometry", "Source plane",
"Target plane"). The names may differ between versions; use what the
live read says.

## Closing

```
gh_recompute()
gh_canvas_summary()
  → 0 errors expected; warnings on Orient if tree shapes mismatch

# Verify Orient output count
orient_info = gh_get_objects([orient_g])
# Expect output Geometry to be a list of 20×20 = 400 module copies

rhino_set_view(standard="Perspective")
rhino_capture_viewport(max_size=1000)
```

Sanity: 400 module copies × 1.5 × 1.2 m = matches 30 × 24 face. OK.

## Report

> Tiled `window_unit` across the +Y face of your office building
> (30 × 24 m).
>
> Typology: tile-module (over panelized base). "Tile" + "block" +
> "across" mapped to tile-module.
>
> Sliders:
> - `[facade] u_count` = 20 (5 – 60)
> - `[facade] v_count` = 20 (5 – 60)
>
> Judgment calls:
> - Picked the +Y face because both long sides tied for largest;
>   say "−Y" if you want the back face.
> - Used the default fit policy (fixed-size, grid-from-module — your
>   module stays its native 1.5 × 1.2 m and the grid count is
>   computed from the host size).
>
> If you want the modules to vary per cell — e.g. some windows open,
> some closed — say "add an attractor" and I'll wire the per-cell
> attractor driver into the module's scale or rotation.

# Example — From-Scratch Louver Facade

Branch D of the router. No host in the Rhino doc, no module — build the
placeholder base, then a louver system.

## Prompt

> *"Give me a vertical fin facade — clean modern look."*

Synonym map: "vertical fin" → `louvers` (vertical sub-variant).
Intake: nothing in the Rhino doc → from-scratch.

## Preflight

```
gh_status()           → ok
rhino_status()        → ok
gh_canvas_summary()   → empty canvas
rhino_get_scene_info()
                      → {"length_units": "Meters",
                         "layers": ["Default"],
                         "object_count": 0}
```

Empty doc + meters. Use the m column of the louver defaults.

## Build the placeholder base

```
# Plane
plane_g = gh_add_component("XZ Plane", 100, 100)
# Plane params don't need a corner — XZ Plane outputs world XZ.

# Dimension sliders for the placeholder wall
width_g = gh_add_slider("[base] width",  3, 40, 12, 100, 200)
height_g = gh_add_slider("[base] height", 2, 20, 6,  100, 250)

# Corner points
p1_g = gh_add_component("Construct Point", 300, 100)
# Leave X=Y=Z=0 (defaults wired to nothing)

p2_g = gh_add_component("Construct Point", 300, 200)
gh_connect_components(width_g, "", p2_g, "X")
gh_connect_components(height_g, "", p2_g, "Z")

# Rectangle 2Pt (NOT bare Rectangle — that's OBSOLETE)
rect_g = gh_add_component("Rectangle 2Pt", 500, 150)
gh_connect_components(plane_g, "Plane", rect_g, "Plane")
gh_connect_components(p1_g, "Point", rect_g, "Point A")
gh_connect_components(p2_g, "Point", rect_g, "Point B")

# Boundary Surfaces → a real Surface
bsurf_g = gh_add_component("Boundary Surfaces", 700, 150)
gh_connect_components(rect_g, "Rectangle", bsurf_g, "Edges")

# Tag the surface output as [base] facade_face for downstream
face_param_g = gh_add_component("Surface", 900, 150)
gh_connect_components(bsurf_g, "Surfaces", face_param_g, "")
gh_set_component_parameter(face_param_g, "NickName",
                            "[base] facade_face")
```

The base is a 12 × 6 m vertical wall on XZ plane.

## Louver sliders

Per `typologies.md` § 2 (louvers — vertical fins variant), m column:

```
fin_count_g = gh_add_slider("[facade] fin_count",
                             4, 80, 24, 1100, 50, integer=True)
fin_depth_g = gh_add_slider("[facade] fin_depth",
                             0.05, 0.6, 0.25, 1100, 90)
fin_angle_g = gh_add_slider("[facade] fin_angle_deg",
                             -45, 60, 25, 1100, 130)
fin_thickness_g = gh_add_slider("[facade] fin_thickness",
                                 0.003, 0.06, 0.015, 1100, 170)
```

## Strip subdivision (U only — vertical strips)

```
# Need a fixed V=1 input. Use a Panel.
v1_g = gh_add_component("Number", 1100, 220)
gh_set_component_parameter(v1_g, "", "1")

dd_g = gh_add_component("Divide Domain²", 1300, 100)
gh_connect_components(face_param_g, "", dd_g, "Domain")
gh_connect_components(fin_count_g, "", dd_g, "U Count")
gh_connect_components(v1_g, "Number", dd_g, "V Count")

iso_g = gh_add_component("Isotrim", 1500, 100)
gh_connect_components(face_param_g, "", iso_g, "Surface")
gh_connect_components(dd_g, "Segments", iso_g, "Domain")
```

## Per-strip rotation axis and centerline

```
# Strip area centroid (anchor)
strip_area_g = gh_add_component("Area", 1700, 100)
gh_connect_components(iso_g, "Surface", strip_area_g, "Geometry")

# Vertical axis at strip center — Unit Z anchored at the centroid
unitz_g = gh_add_component("Unit Z", 1700, 200)
# Factor stays at 1

# Build the axis as a Line from centroid up
line_axis_g = gh_add_component("Line SDL", 1900, 100)
gh_connect_components(strip_area_g, "Centroid", line_axis_g, "Start")
gh_connect_components(unitz_g, "Vector", line_axis_g, "Direction")
# Length doesn't matter for rotation axis — wire height
gh_connect_components(height_g, "", line_axis_g, "Length")
```

## Rotate per strip

```
# Angle in radians
rad_g = gh_add_component("Radians", 1900, 250)
gh_connect_components(fin_angle_g, "", rad_g, "Angle")

rot_g = gh_add_component("Rotate Axis", 2100, 100)
gh_connect_components(iso_g, "Surface", rot_g, "Geometry")
gh_connect_components(rad_g, "Radians", rot_g, "Angle")
gh_connect_components(line_axis_g, "Line", rot_g, "Axis")
```

## Extrude outward by fin_depth (vertical fins → Y-axis)

For a vertical wall on XZ plane, "outward" is +Y.

```
unity_g = gh_add_component("Unit Y", 2100, 250)
gh_connect_components(fin_depth_g, "", unity_g, "Factor")

extrude_g = gh_add_component("Extrude", 2300, 100)
gh_connect_components(rot_g, "Geometry", extrude_g, "Base")
gh_connect_components(unity_g, "Vector", extrude_g, "Direction")
```

## Thicken (so each fin is solid, not a single surface)

For the vertical-fin variant, the fin already has thickness through
the Extrude. If a separate `fin_thickness` slider is requested, swap
the Extrude for a `Box from Domain` or use `Offset Surface` on the
rotated strip — depends on whether the user wants a "plate" or a
"slab". Default: the Extrude above is the plate.

For solid blades with explicit thickness, do this instead of the
plain Extrude above:

```
# Build a Line SDL along the wall normal as the extrusion axis
line_ext_g = gh_add_component("Line SDL", 2300, 100)
gh_connect_components(strip_area_g, "Centroid", line_ext_g, "Start")
gh_connect_components(unity_g, "Vector", line_ext_g, "Direction")
gh_connect_components(fin_depth_g, "", line_ext_g, "Length")

# Plane the profile lives in — for vertical fins, it's YZ
yz_plane_g = gh_add_component("YZ Plane", 2300, 250)

# Extrude Linear with explicit Orientation (critical — see
# parametric-facade/references/louvers-and-fins.md § "no visible
# thickness" gotcha)
extrude_g = gh_add_component("Extrude Linear", 2500, 100)
gh_connect_components(rot_g, "Geometry", extrude_g, "Profile")
gh_connect_components(yz_plane_g, "Plane", extrude_g, "Orientation (P)")
gh_connect_components(line_ext_g, "Line", extrude_g, "Axis")
```

## Closing

```
gh_recompute()
gh_canvas_summary()
  → 0 errors, 0 warnings, 1 Brep output Geometry on the Extrude

# scale sanity
gh_get_objects([face_param_g])  → bbox 12 × 6 m
# fin_depth = 0.25 m = 1/48 of the long side → well within sanity

rhino_set_view(standard="Perspective")
rhino_capture_viewport(max_size=1000)
rhino_set_view(standard="Front")
rhino_capture_viewport(max_size=1000)
```

## Report

> Built a vertical-fin louver facade on a 12 × 6 m placeholder wall
> (swap your real surface into `[base] facade_face` when ready).
>
> Typology: louvers (vertical fins) — "vertical fin" mapped directly.
>
> Sliders:
> - `[base] width` = 12 m (3 – 40)
> - `[base] height` = 6 m (2 – 20)
> - `[facade] fin_count` = 24 (4 – 80)
> - `[facade] fin_depth` = 0.25 m (0.05 – 0.6)
> - `[facade] fin_angle_deg` = 25° (−45 – 60)
> - `[facade] fin_thickness` = 0.015 m (0.003 – 0.06)
>
> Judgment calls:
> - "Clean modern look" is decorative — I used the default angle 25°
>   (away from world North) so the fins read at oblique view rather
>   than aligning flush. Drag `fin_angle_deg` to 0 for flush blades.
>
> To make these sun-responsive: install Ladybug (`_PackageManager` →
> `ladybug-tools`) and ask "drive the fin angle by the sun"; I'll
> swap the constant angle for the Ladybug `LB SunPath.vectors` path.

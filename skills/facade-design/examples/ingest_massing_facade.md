# Example — Ingest an Existing Massing, Build a Diagrid

Branch A of the router. The user has a closed Brep mass; the prompt
asks for a structural typology.

## Prompt

> *"Wrap the office tower on the 'building' layer with a diagrid."*

Synonym map: "diagrid" → `diagrid`.
Intake: existing host on a named layer → ingest.
"Wrap" = signal that this is facade work; reinforces, doesn't change
typology.

## Preflight

```
gh_status()           → ok
rhino_status()        → ok
gh_canvas_summary()   → empty canvas
rhino_get_scene_info()
  → {"length_units": "Meters",
     "layers": ["Default", "building", "site", "context"]}

rhino_get_layers()    → confirms "building" exists, full path
                        "::building"
```

## Ingest the host

```
rhino_get_objects_with_metadata(
    filters={"layer": "building"},
    metadata_fields=["short_id", "type", "bbox", "name"],
)
→ [
    {"short_id": "t1", "type": "Brep",
     "bbox": {"min":[0,0,0], "max":[30, 30, 120]},
     "name": "office_tower"}
  ]
```

One Brep, 30 × 30 × 120 m. Closed massing → multi-face host.

Place a Brep param and link by GUID:

```
brep_g = gh_add_component("Brep", 100, 100)
gh_set_component_parameter(brep_g, "", "<rhino-guid-of-t1>")
gh_set_component_parameter(brep_g, "NickName", "[base] tower")
```

For a diagrid that **wraps** the tower (not just one face), we need
all four vertical faces of the box. Pull them with a small Python 3
script that filters for non-horizontal faces:

```
faces_g = gh_add_component("Python 3 Script", 300, 100)
gh_write_script_py3(
    faces_g,
    code="""
import Rhino.Geometry as rg

# inputs: mass (Brep)
# outputs: facade_faces (Brep — multiple)

result = []
center = mass.GetBoundingBox(True).Center
for face in mass.Faces:
    u_mid = face.Domain(0).Mid
    v_mid = face.Domain(1).Mid
    n = face.NormalAt(u_mid, v_mid)
    if abs(n.Z) > 0.8:
        continue  # near-horizontal — top / bottom
    # verify outward normal
    p = face.PointAt(u_mid, v_mid)
    if rg.Vector3d(p - center) * n < 0:
        face_brep = face.DuplicateFace(True)
        face_brep.Flip()
    else:
        face_brep = face.DuplicateFace(True)
    result.append(face_brep)
facade_faces = result
""",
    param_definitions=[
        {"type":"input", "name":"mass", "type_hint":"Brep"},
        {"type":"output","name":"facade_faces","type_hint":"Brep"},
    ],
)
gh_connect_components(brep_g, "Brep", faces_g, "mass")

# Cache as [base] facade_faces (list)
faces_param_g = gh_add_component("Brep", 500, 100)
gh_connect_components(faces_g, "facade_faces", faces_param_g, "")
gh_set_component_parameter(faces_param_g, "NickName",
                           "[base] facade_faces")
```

Four vertical faces, each oriented outward, available as a list.

## Diagrid drivers (per typologies.md § 4, m units)

```
u_count_g = gh_add_slider("[facade] u_count", 3, 30, 12,
                          700, 50, integer=True)
v_count_g = gh_add_slider("[facade] v_count", 3, 24, 16,
                          700, 90, integer=True)
member_r_g = gh_add_slider("[facade] member_radius",
                            0.01, 0.2, 0.06, 700, 130)
```

Note: 16 V on a 120 m tall face = 7.5 m / cell vertically, vs 30/12 =
2.5 m horizontally. That's a 1:3 aspect — visible as elongated
triangles. The skill should either flag this or auto-balance v_count
to match aspect ratio. For now flag in the report and let the user
drag it.

## Diagrid graph (per face)

`Divide Surface` produces a UV point grid per face. To get the
diagonal lines, we walk pairs of neighbour points. `Mesh Triangulation`
on the grid does this in one shot. But it needs the points as a flat
list per face; `Divide Surface` outputs a tree (one branch per V
row).

```
ds_g = gh_add_component("Divide Surface", 900, 100)
gh_connect_components(faces_param_g, "Brep", ds_g, "Surface")
gh_connect_components(u_count_g, "", ds_g, "U Count")
gh_connect_components(v_count_g, "", ds_g, "V Count")

# Divide Surface returns Points (tree) + Normals (tree).
# For diagrid, build a mesh from those points by Delaunay or Mesh
# Triangulation. The native `Mesh Triangulation` is the right
# component here.

mt_g = gh_add_component("Delaunay Mesh", 1100, 100)
gh_connect_components(ds_g, "Points", mt_g, "Points")

# Mesh Edges → list of edge lines
me_g = gh_add_component("Mesh Edges", 1300, 100)
gh_connect_components(mt_g, "Mesh", me_g, "Mesh")

# Pipe to give the members real radius
pipe_g = gh_add_component("Pipe", 1500, 100)
gh_connect_components(me_g, "Naked Edges", pipe_g, "Curve",
                     append=False)
gh_connect_components(me_g, "Interior Edges", pipe_g, "Curve",
                     append=True)
gh_connect_components(member_r_g, "", pipe_g, "Radius")
```

The two-source wire into Pipe.Curve uses `append=True` for the second
— that's the multi-source merge pattern. After the first wire,
Pipe.Curve carries Naked Edges; after the second (with append),
it carries both Naked + Interior Edges. (The duplicate-pair guard
prevents re-adding the same source if you re-run this snippet.)

## Closing

```
gh_recompute()

gh_canvas_summary()
  → check errors / warnings
gh_get_runtime_messages(pipe_g)
  → expect "1. There was a problem creating the pipes" if member_r
    is too large for the segment length. Defaults at 0.06 m on 30 m
    cells = no issue.

# Sanity check — cell size vs member radius
# Cell ~2.5 × 7.5 m, member 0.06 m → member is < 1% of cell. OK.

rhino_set_view(standard="Perspective")
rhino_capture_viewport(max_size=1000)
```

## Report

> Built a diagrid on all 4 vertical faces of your `office_tower`
> (30 × 30 × 120 m).
>
> Typology: diagrid — directly named.
>
> Sliders:
> - `[facade] u_count` = 12 (3 – 30) — horizontal divisions per face
> - `[facade] v_count` = 16 (3 – 24) — vertical divisions
> - `[facade] member_radius` = 0.06 m (0.01 – 0.2)
>
> Judgment calls:
> - Defaulted to "wrap all 4 vertical faces" because "wrap" was in
>   the prompt. Say so if you want just one face.
> - Cell aspect is roughly 1:3 (2.5 × 7.5 m) — diagonals will read
>   as elongated. Drag `v_count` to ~32 for square cells, or
>   `u_count` to 4 for vertical-emphasis.
>
> If you want corner members where the diagrid wraps each Brep edge
> (rather than four independent face grids), say so — the current
> setup is one Delaunay per face, so corner connections are visual,
> not structural.

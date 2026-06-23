# Example — Vague Prompt → Concrete Facade

The headline behavior of this skill. Read this before your first build
of the session — the inference chain is easier to copy than re-derive.

## The prompt

> **User:** *"put a modern screen on this building"*

That's it. No typology stated, no parameters, no face picked. Three
ambiguous words: "modern", "screen", "this building".

## What the skill does NOT do

- Ask "What kind of screen?" (the word maps cleanly via the synonym map)
- Ask "How big should the holes be?" (defaults table covers it)
- Ask "Which face?" (default policy is the largest non-horizontal face)
- Propose three options and wait for the user to pick (do, then iterate)

## What the skill DOES do — the inference chain

### Step 1 — preflight

```
gh_status()          → "Server is available"
rhino_status()       → "Server is available"
gh_canvas_summary()  → {"total_objects": 0, ...}
                       (clean canvas)
rhino_get_scene_info()
                     → {
                         "length_units": "Meters",
                         "layers": ["Default", "building", "site"],
                         "object_count": 3,
                         ...
                       }
```

Units = meters → use the m column of the defaults table. Layers
include `building` → strong signal the user's "this building" is on
that layer.

### Step 2 — recognize the host

```
rhino_get_objects_with_metadata(
    filters={"layer": "building"},
    metadata_fields=["short_id", "type", "bbox", "name"],
)
→ [
    {
      "short_id": "b1",
      "type": "Brep",
      "bbox": {"min": [0,0,0], "max": [20, 8, 30]},
      "name": "tower",
    }
  ]
```

One Brep on the `building` layer, name "tower", 20×8×30 m. Closed
massing — host taxonomy: "closed massing", default policy says largest
non-horizontal face.

Place a `Brep` param on the canvas, set persistent data:

```
brep_guid = gh_add_component("Brep", 100, 100)
gh_set_component_parameter(brep_guid, "", "<rhino-guid-of-tower>")
gh_set_component_parameter(brep_guid, "NickName", "[base] tower")
```

### Step 3 — extract the facade face

The user said "modern screen on this building" — they want a screen on
the most visible face. Default: largest non-horizontal face. With a
20 × 8 × 30 m box-like tower, the two largest faces are 20 × 30 = 600
m² (the two long sides). Tie — but the orientation is unspecified, so
pick one (say, the +Y face, which is "front" in plan). Optionally flag
in the report.

Place a small Python 3 script that picks the face by largest area
among non-horizontal faces:

```
script_guid = gh_add_component("Python 3 Script", 300, 100)
gh_write_script_py3(
    script_guid,
    code="""
import Rhino.Geometry as rg

# inputs: mass (Brep)
# outputs: facade_face (Surface)

best_face = None
best_area = 0.0
for face in mass.Faces:
    n = face.NormalAt(face.Domain(0).Mid, face.Domain(1).Mid)
    if abs(n.Z) > 0.8:
        continue  # skip near-horizontal
    area = rg.AreaMassProperties.Compute(face).Area
    if area > best_area:
        best_area = area
        best_face = face.ToBrep()
facade_face = best_face
""",
    param_definitions=[
        {"type":"input", "name":"mass", "type_hint":"Brep"},
        {"type":"output","name":"facade_face","type_hint":"Brep"},
    ],
)
gh_connect_components(brep_guid, "Brep", script_guid, "mass")
```

(In practice the parametric-facade skill's testing showed that
right-clicking the Brep param and picking the face manually is also
viable — the script path is the one-shot autonomous version.)

### Step 4 — verify outward normal

Add the 6-component subgraph from `reference/geometry-ingest.md` §
Outward-normal verification, OR fold the check into the same Python 3
script:

```
# Extended script body:
center = mass.GetBoundingBox(True).Center
u_mid = best_face.Faces[0].Domain(0).Mid
v_mid = best_face.Faces[0].Domain(1).Mid
p = best_face.Faces[0].PointAt(u_mid, v_mid)
n = best_face.Faces[0].NormalAt(u_mid, v_mid)
if rg.Vector3d(p - center) * n < 0:
    best_face.Flip()
facade_face = best_face
```

Cache as `[base] facade_face`:

```
# The script's output already carries the corrected face. Add an
# explicit Surface param downstream so iterations can grab it.
face_param_guid = gh_add_component("Surface", 500, 100)
gh_connect_components(script_guid, "facade_face",
                       face_param_guid, "")
gh_set_component_parameter(face_param_guid, "NickName",
                           "[base] facade_face")
```

### Step 5 — map "screen" → typology

Synonym map: "screen" → `perforated-attractor`. Defaults table (m
units):

| Driver | Default | Range |
|---|---|---|
| `u_count` | 20 | 4 – 60 |
| `v_count` | 12 | 4 – 40 |
| `hole_min` | 0.02 m | 0.005 – 0.2 |
| `hole_max` | 0.20 m | 0.02 – 0.5 |
| `attractor_strength` | 1.0 | 0.1 – 5.0 |
| `attractor_pt_x` | 10 (half host W) | 0 – 20 |
| `attractor_pt_z` | 15 (half host H) | 0 – 30 |

For the +Y-facing 20 × 30 m face: cell size is 20/20 × 30/12 = 1.0 × 2.5 m.
`hole_max` defaults to 0.20 m which is well inside the cell — no clamp
needed.

### Step 6 — build (placements + wires)

In rough order, with x-positions stepping by 200 per stage:

```
# Sliders first — they anchor the design family
u_count_g = gh_add_slider("[facade] u_count", 4, 60, 20, 700, 50,
                           integer=True)
v_count_g = gh_add_slider("[facade] v_count", 4, 40, 12, 700, 90,
                           integer=True)
hole_min_g = gh_add_slider("[facade] hole_min", 0.005, 0.2, 0.02,
                            700, 130)
hole_max_g = gh_add_slider("[facade] hole_max", 0.02, 0.5, 0.20,
                            700, 170)
attr_strength_g = gh_add_slider("[facade] attractor_strength",
                                 0.1, 5.0, 1.0, 700, 210)
attr_x_g = gh_add_slider("[facade] attractor_pt_x", 0, 20, 10,
                          700, 250)
attr_z_g = gh_add_slider("[facade] attractor_pt_z", 0, 30, 15,
                          700, 290)

# Build the attractor point
attr_pt_g = gh_add_component("Construct Point", 900, 250)
gh_connect_components(attr_x_g, "", attr_pt_g, "X")
gh_connect_components(attr_z_g, "", attr_pt_g, "Z")
# Y left at 0 — host is at Y=0 in this coordinate system

# Subdivide
dd_g = gh_add_component("Divide Domain²", 900, 100)
gh_connect_components(face_param_guid, "", dd_g, "Domain")
gh_connect_components(u_count_g, "", dd_g, "U Count")
gh_connect_components(v_count_g, "", dd_g, "V Count")

iso_g = gh_add_component("Isotrim", 1100, 100)
gh_connect_components(face_param_guid, "", iso_g, "Surface")
gh_connect_components(dd_g, "Segments", iso_g, "Domain")

# Per-panel centroid
area_g = gh_add_component("Area", 1300, 100)
gh_connect_components(iso_g, "Surface", area_g, "Geometry")

# Distance to attractor → per-panel scalar
dist_g = gh_add_component("Distance", 1500, 100)
gh_connect_components(area_g, "Centroid", dist_g, "Point A")
gh_connect_components(attr_pt_g, "Point", dist_g, "Point B")

# Bounds + Remap into (hole_min, hole_max), respecting attractor_strength
bnd_g = gh_add_component("Bounds", 1700, 100)
gh_connect_components(dist_g, "Distance", bnd_g, "Numbers")

# Construct the target domain (hole_min, hole_max)
dom_g = gh_add_component("Construct Domain", 1700, 200)
gh_connect_components(hole_min_g, "", dom_g, "Domain Start")
gh_connect_components(hole_max_g, "", dom_g, "Domain End")

remap_g = gh_add_component("Remap Numbers", 1900, 100)
gh_connect_components(dist_g, "Distance", remap_g, "Value")
gh_connect_components(bnd_g, "Domain", remap_g, "Source")
gh_connect_components(dom_g, "Domain", remap_g, "Target")

# (Optional — for attractor_strength to bite, multiply remap output by
# strength then re-clamp. Skipping for brevity in this trace.)

# Per-panel hole circle on the panel centroid, on the surface's plane
circle_g = gh_add_component("Circle", 2100, 100)
gh_connect_components(area_g, "Centroid", circle_g, "Plane")
gh_connect_components(remap_g, "Mapped", circle_g, "Radius")

# Cut from the panel — Surface Split, then keep the outer half
split_g = gh_add_component("Surface Split", 2300, 100)
gh_connect_components(iso_g, "Surface", split_g, "Surface")
gh_connect_components(circle_g, "Circle", split_g, "Curves")

# split.Fragments returns two pieces per cell. Pick the one whose
# area is LARGER (the panel-with-hole rather than the disc).
sort_g = gh_add_component("Sort List", 2500, 100)
fa_g = gh_add_component("Area", 2500, 200)
gh_connect_components(split_g, "Fragments", fa_g, "Geometry")
gh_connect_components(fa_g, "Area", sort_g, "Keys")
gh_connect_components(split_g, "Fragments", sort_g, "Values A")

last_g = gh_add_component("List Item", 2700, 100)
gh_connect_components(sort_g, "Values A", last_g, "List")
# Pick the largest (last after ascending sort)
neg1_g = gh_add_slider("__internal_idx_last", -1, -1, -1, 2700, 200,
                       integer=True)
gh_connect_components(neg1_g, "", last_g, "Index")
gh_set_component_parameter(last_g, "Wrap", "true")
```

(The `Sort + List Item` pattern is one way; alternatively use `Cull
Pattern` with a known interleave. Implementation details
depend on the live Surface Split output tree — verify with
`gh_get_objects` after the placement and adjust.)

### Step 7 — closing sequence

```
gh_recompute()
gh_canvas_summary()
  → {"components_with_errors": 0, "components_with_warnings": 0, ...}

rhino_set_view(standard="Perspective")
rhino_capture_viewport(max_size=1000)
rhino_set_view(standard="Front")
rhino_capture_viewport(max_size=1000)
```

### Step 8 — report

```
Built a perforated screen on your tower's largest non-horizontal face
(20 × 30 m, facing +Y).

Typology: perforated-attractor — picked because "screen" mapped to the
perforated pattern in the synonym map.

Sliders to drag:
  [facade] u_count           = 20 (range 4 – 60)
  [facade] v_count           = 12 (range 4 – 40)
  [facade] hole_min          = 0.02 m  (range 0.005 – 0.2)
  [facade] hole_max          = 0.20 m  (range 0.02 – 0.5)
  [facade] attractor_strength = 1.0    (range 0.1 – 5.0)
  [facade] attractor_pt_x    = 10 m   (range 0 – 20)
  [facade] attractor_pt_z    = 15 m   (range 0 – 30)

Judgment calls:
  - Picked the +Y face because both 20×30 long faces tied for largest;
    say so if you want the −Y or one of the short faces instead.
  - The hole_max default of 20 cm is well inside the 1 m × 2.5 m cell
    size, so no clamping was needed.

To change typology — "make it louvers" or "swap to diagrid" — say so;
I'll clear the [facade]-tagged nodes and rebuild fresh.
```

## What made this work

- **No clarifying questions.** "modern" = no constraint, "screen" =
  typology, "this building" = host on the obvious layer. Three
  inferences, one build.
- **Defaults that produce real architecture.** 20 × 12 grid with hole
  range 2–20 cm is something an architect would actually draw. Range
  0–100% would have produced garbage at slider sweep time.
- **Tagged everything `[facade]` and `[base]`.** Follow-up prompts
  ("make it diagrid") can find and remove the prior facade in one
  call.
- **Reported the judgment call.** The face pick was a tie; the user
  needs to know which way it went so they can redirect cheaply.

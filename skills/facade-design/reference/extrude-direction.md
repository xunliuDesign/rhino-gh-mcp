# Extrude Direction — Host-Aware Thickness

Cross-cutting pattern used by any typology that thickens a planar
region into a 3D wall: perforated, panelized, folded, kinetic, parts of
diagrid.

> **Authoring rule (when transcribing a recipe from a canvas):** the
> extrude direction must be **normal to the original host surface**, not
> a hard-coded world axis. When you inspect the canvas, read what is
> actually wired into the `Extrude.Direction` (or `Extrude Linear.Axis`)
> port — do not assume `Unit Z`. The canvas's wiring is the source of
> truth: it tells you which of the three patterns below the author chose
> and *why*. Transcribe that vector chain (Unit X/Y/Z, Evaluate Surface
> Z-axis, or Brep CP Normal) into the recipe verbatim.

The wrong approach (which is what most demo files default to): hard-code
`Unit Z × Thickness`. This only works when the host surface lies flat on
the world XY plane (its normal is +Z). Most facades are vertical walls
or oblique surfaces whose normals are NOT Z — and Unit Z extrudes the
panels straight up instead of outward through the wall.

The general principle: **extrude direction = host outward normal ×
thickness**. Three patterns, picked by host orientation:

## Pattern A — host axis-aligned (cheapest)

If the host's normal aligns with a world axis, use the matching unit
vector. The `Factor` input multiplies the unit, so a single component
handles direction-and-magnitude in one shot.

| Host orientation | Component | Notes |
|---|---|---|
| Flat / floor / ceiling (XY plane, normal +Z) | `Unit Z(Factor=thickness)` | The demo .gh case — only matches because demo surface is on XY |
| Wall facing +X | `Unit X(Factor=thickness)` | |
| Wall facing −X | `Unit X(Factor=-thickness)` | Negative factor flips |
| Wall facing +Y | `Unit Y(Factor=thickness)` | |
| Wall facing −Y | `Unit Y(Factor=-thickness)` | |

Then wire that component's output (a `Vector`) → `Extrude.Direction`
(for legacy Extrude — takes `Vector` directly).

For `Extrude Linear` (which takes a Line `Axis` not a Vector), build
the line: `Line SDL(Start=(0,0,0), Direction=Unit X.Vector,
Length=1.0)`. The Vector's length encodes the thickness, so Length=1
is fine.

**When to use**: single host or all hosts share the same world-aligned
outward direction.

## Pattern B — single host on an oblique plane (Evaluate Surface frame)

For a tilted but flat host (e.g. a sloped roof, an angled wall),
compute the normal once at the surface midpoint:

```
Brep → Brep Face / Surface  → Evaluate Surface(uv=(0.5, 0.5)) → Frame (Plane)
                                                                  ↓
                                                          Deconstruct Plane
                                                                  ↓
                                                                Z-Axis (Vector)
                                                                  ↓
                                              Amplitude(Vector=Z-Axis, Amplitude=thickness)
                                                                  ↓
                                                          Extrude.Direction
```

If the input is a Brep (not a Surface), first extract a Surface via
`Deconstruct Brep → Faces → List Item(0)`, then Evaluate Surface.

**When to use**: single oblique host where one normal at the midpoint
is a good approximation for the whole surface (flat or near-flat
surfaces).

## Pattern C — multi-host or curved host (per-cell Brep CP normal)

For (a) multiple hosts with different outward directions, or (b) a
curved host where each panel has its own normal — compute the normal
**per cell** using the cell centroid as a sample point:

```
[hosts] (Brep, tree) ──→ Brep Closest Point.Brep
                         ↑
[cell centroids] (Point, tree) ──→ Brep Closest Point.Point
                                              ↓
                                           Normal (Vector — per cell)
                                              ↓
                                  Amplitude(Vector=Normal, Amplitude=thickness)
                                              ↓
                                      Extrude.Direction (per cell vector)
```

Brep CP returns the surface normal at the point on the Brep closest to
the input Point. For cells derived from `Triangle Panels B / Quad
Panels A / etc.`, the centroid is ON the host surface, so the closest
point IS the centroid and the normal returned is the host's local
normal at that location.

**Tree alignment** — critical for multi-host:
- `cell centroids` arrives as a tree with one branch per host, N
  centroids per branch
- `hosts` arrives as a list of N hosts (item-access input)
- Brep CP processes per-branch: each branch's centroids paired with
  the matching host by branch index (longest-list iteration)
- Output `Normal` matches centroid tree shape — one normal per cell

This is the most robust pattern. Use it as the default for any
multi-host build. It also handles oblique/curved single hosts (Pattern
B is just a 1-cell special case).

## Choosing between patterns

| Situation | Pattern |
|---|---|
| Single host, flat on XY/XZ/YZ plane | A (axis-aligned) |
| Single host, flat but tilted | B (Evaluate Surface) |
| Multiple hosts, all same orientation | A applied uniformly |
| Multiple hosts, different orientations | **C (Brep CP per cell)** |
| Single curved host (panel-by-panel normal varies) | **C (Brep CP per cell)** |

For a skill or recipe that doesn't know in advance, **default to
Pattern C** — it's robust to every case and the extra `Brep CP +
Amplitude` cost is two components, paid once.

## Component name gotcha

`Extrude` (the bridge's name resolution) returns `Extrude Linear`,
which takes a Line `Axis` not a Vector `Direction`. For the cleanest
implementation of Pattern A/B/C above, use the **legacy `Extrude`**
(`Component_Extrude`) — placed via name `"Extr"` (its nickname).

If you must use `Extrude Linear`:
- Build the axis Line via `Line SDL(Start=any point, Direction=normal
  vector, Length=thickness)`
- Or `Line(Start=any point, End=Start + Amplitude(normal, thickness))`
- Wire that Line into `Extrude Linear.Axis`
- Leave `Orientation (P)` unwired or set to the host plane

See `bridge-quirks.md` § Component name traps for the full Extrude
variant table.

# Facade-Specific Bridge Notes

The **full** rhino-gh-mcp bridge quirks list — component name traps,
persistent-data Panel-mode fallback, MCPv2 Scenario gating,
ComponentScope, broken/stubbed tools, result-size cap — lives in the
[`rhino-gh-bridge-basics`](../../../rhino-gh-bridge-basics/reference/bridge-quirks.md)
skill. **Read that first.** Anything in this file is *additional*
context that's specific to facade recipes.

If `rhino-gh-bridge-basics` is loaded for this session (it should be —
it's the prerequisite for every rhino-gh-mcp build), you have all the
general traps already.

## Facade-specific points

The general quirks list already covers everything that happens here.
The handful of items below are repeated for emphasis because they
trigger in **every** facade recipe and are the most-encountered facade
failure modes:

### Extrude is the terminator — get the right variant

Every recipe in this skill ends in a thickening step. **Use legacy
`Extrude` (`Component_Extrude`), NOT `Extrude Linear`
(`Component_ExtrudeLinear`).**

- Legacy `Extrude.Direction` takes a `Vector` (Unit Z × thickness, or
  Amplitude(normal, thickness)).
- `Extrude Linear.Axis` takes a `Line` and has an Orientation gotcha
  that reprojects the profile.

Bridge name resolution prefers `Extrude Linear` when you ask for
`Extrude`. Defensive move: ask for nickname `Extr` to force the
legacy variant, then verify the returned `kind` is `Component_Extrude`
(NOT `Component_ExtrudeLinear`).

The one exception is **folded Variant B (Origami Faceted)**, which
uses `Extrude To Point` (`Component_ExtrudeToPoint`) to produce
pyramidal apexes. That's a different Extrude variant — recipe says so
explicitly.

### Boundary Surfaces is the differencing component

Used in `perforated-attractor`, `voronoi`, and `folded Variant B`.
`Boundary Surfaces.Edges` accepts a list of closed nested curves
(outer + inner) and emits a single planar Brep with a hole — no
`Surface Split`, no `Sort by Area`, no `List Item -1` needed.

The trick: feed the outer cell curve as the first input, append the
inner offset/scaled curve as the second (via `append=True`). The
result is one Brep with the inner curve as a hole, ready for
Extrude. See the perforated-attractor recipe's "Boundary Surfaces
trick" section for the wiring detail.

### Host ingest for vertical walls — see host-ingest.md

Kinetic and louver recipes assume a vertical wall host (XZ or YZ
plane). When substituting the from-scratch Rectangle with an
existing Rhino host, ensure the host's outward normal is horizontal
(parallel to the world XY plane), or the kinetic petals point in the
wrong direction.

For the substitution patterns themselves (`Param_Brep` / `Geometry
Pipeline` / N-refs + Merge), see the basics skill's
[`host-ingest.md`](../../../rhino-gh-bridge-basics/reference/host-ingest.md).
The general patterns work unchanged for facades — the skill-specific
note is just the vertical-normal constraint above.

## When the bridge updates

Re-validate the general quirks in `rhino-gh-bridge-basics/reference/
bridge-quirks.md` on bridge version bumps. Any facade-specific
shifts (e.g. a new `Extrude` variant the bridge starts preferring)
get added here.

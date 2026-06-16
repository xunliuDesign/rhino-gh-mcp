"""Scenario → Capabilities mapping.

The v0.2 redesign moves the canvas surface from "four orthogonal flags" to
"one Scenario dropdown". The five scenarios collapse the meaningful
combinations of the underlying capability flags into named modes:

    Inspect : read-only
    Tune    : params only
    Coach   : params + components (curated), no scripting
    Execute : Skill-defined (defaults to Coach until the Skill overrides)
    Author  : full freedom incl. scripting

This module owns the canonical mapping. It is the source of truth that both
the Python server and the C# canvas component must agree on; the canvas
makes its choice locally and surfaces it back via `get_capabilities`, so the
server can derive (and reason about) what the canvas thinks is allowed.
"""

from __future__ import annotations

from enum import Enum

from .capabilities import Capabilities, ComponentScope


class Scenario(str, Enum):
    """The five user-visible scenarios."""

    INSPECT = "inspect"
    TUNE = "tune"
    COACH = "coach"
    EXECUTE = "execute"
    AUTHOR = "author"


# Numeric ids correspond to the V2 component's integer Scenario input.
SCENARIO_BY_INDEX: dict[int, Scenario] = {
    0: Scenario.INSPECT,
    1: Scenario.TUNE,
    2: Scenario.COACH,
    3: Scenario.EXECUTE,
    4: Scenario.AUTHOR,
}


def scenario_from_name(name: str | None) -> Scenario:
    """Parse a scenario string (case-insensitive). Defaults to Coach on unknown."""
    if not name:
        return Scenario.COACH
    try:
        return Scenario(name.strip().lower())
    except ValueError:
        return Scenario.COACH


def capabilities_for(scenario: Scenario, source: str = "scenario") -> Capabilities:
    """Return the default Capabilities for a Scenario.

    Mirrors the table in docs/v0.2-redesign.md. Execute mode defaults to
    Coach behaviour because the gating happens at the per-tool layer once
    the Skill loads; the underlying flags still need to be "writeable enough"
    for the Skill's commands to function.

    Args:
        scenario: which scenario to map.
        source: marker for diagnostics — defaults to "scenario" but callers
            doing canvas-derived mapping should pass "canvas:scenario".
    """
    if scenario is Scenario.INSPECT:
        return Capabilities(
            allow_parameters=False,
            allow_components=False,
            allow_scripting=False,
            component_scope=ComponentScope.CURATED,
            category_filter="",
            source=source,
        )
    if scenario is Scenario.TUNE:
        return Capabilities(
            allow_parameters=True,
            allow_components=False,
            allow_scripting=False,
            component_scope=ComponentScope.CURATED,
            category_filter="",
            source=source,
        )
    if scenario is Scenario.COACH:
        return Capabilities(
            allow_parameters=True,
            allow_components=True,
            allow_scripting=False,
            component_scope=ComponentScope.CURATED,
            category_filter="",
            source=source,
        )
    if scenario is Scenario.EXECUTE:
        # Behaves like Coach by default; per-Skill gating tightens this further
        # in tools/_gate.py when the Skill's `commands:` table is non-empty.
        return Capabilities(
            allow_parameters=True,
            allow_components=True,
            allow_scripting=False,
            component_scope=ComponentScope.CURATED,
            category_filter="",
            source=source,
        )
    # Author
    return Capabilities(
        allow_parameters=True,
        allow_components=True,
        allow_scripting=True,
        component_scope=ComponentScope.DEFAULTS,
        category_filter="",
        source=source,
    )

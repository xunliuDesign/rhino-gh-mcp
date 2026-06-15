"""Tests for the Scenario → Capabilities mapping (v0.2)."""

from __future__ import annotations

import pytest

from rhino_gh_mcp.capabilities import ComponentScope
from rhino_gh_mcp.scenarios import (
    SCENARIO_BY_INDEX,
    Scenario,
    capabilities_for,
    scenario_from_name,
)


@pytest.mark.parametrize(
    "name,expected",
    [
        ("inspect", Scenario.INSPECT),
        ("Tune", Scenario.TUNE),
        (" COACH ", Scenario.COACH),
        ("execute", Scenario.EXECUTE),
        ("Author", Scenario.AUTHOR),
        ("", Scenario.COACH),  # default
        (None, Scenario.COACH),  # default
        ("garbage", Scenario.COACH),  # default
    ],
)
def test_scenario_from_name(name, expected):
    assert scenario_from_name(name) is expected


def test_scenario_by_index_covers_all_five():
    """The integer mapping must agree with the V2 component's Scenario input."""
    assert SCENARIO_BY_INDEX[0] is Scenario.INSPECT
    assert SCENARIO_BY_INDEX[1] is Scenario.TUNE
    assert SCENARIO_BY_INDEX[2] is Scenario.COACH
    assert SCENARIO_BY_INDEX[3] is Scenario.EXECUTE
    assert SCENARIO_BY_INDEX[4] is Scenario.AUTHOR
    assert len(SCENARIO_BY_INDEX) == 5


# Mapping table from docs/v0.2-redesign.md §"Mapping table". Translating:
#   Inspect  : params=R, components=R, scripting=R     -> all False
#   Tune     : params=W, components=R, scripting=R     -> only params True
#   Coach    : params=W, components=W, scripting=R     -> both writes, no script
#   Execute  : Skill-defined → defaults to Coach-equivalent for the flag layer
#   Author   : params=W, components=W, scripting=W     -> everything True
@pytest.mark.parametrize(
    "scenario,allow_params,allow_comp,allow_script,scope",
    [
        (Scenario.INSPECT, False, False, False, ComponentScope.CURATED),
        (Scenario.TUNE, True, False, False, ComponentScope.CURATED),
        (Scenario.COACH, True, True, False, ComponentScope.CURATED),
        (Scenario.EXECUTE, True, True, False, ComponentScope.CURATED),
        (Scenario.AUTHOR, True, True, True, ComponentScope.DEFAULTS),
    ],
)
def test_capabilities_for_each_scenario(scenario, allow_params, allow_comp,
                                        allow_script, scope):
    caps = capabilities_for(scenario)
    assert caps.allow_parameters is allow_params
    assert caps.allow_components is allow_comp
    assert caps.allow_scripting is allow_script
    assert caps.component_scope is scope


def test_capabilities_for_records_source():
    """Source tag carries through so we can trace why a Capabilities was picked."""
    c = capabilities_for(Scenario.COACH, source="canvas:scenario")
    assert c.source == "canvas:scenario"


def test_inspect_blocks_writes_via_allows():
    """Inspect scenario should literally deny every write tool name."""
    caps = capabilities_for(Scenario.INSPECT)
    # Reads still pass…
    assert caps.allows("gh_get_context") is True
    # …and every write category is closed.
    assert caps.allows("gh_set_slider") is False
    assert caps.allows("gh_add_component") is False
    assert caps.allows("rhino_execute_code") is False


def test_tune_allows_parameter_writes_only():
    caps = capabilities_for(Scenario.TUNE)
    assert caps.allows("gh_set_slider") is True
    assert caps.allows("gh_set_toggle") is True
    assert caps.allows("gh_add_component") is False
    assert caps.allows("gh_write_script_py3") is False


def test_coach_allows_components_but_not_scripting():
    caps = capabilities_for(Scenario.COACH)
    assert caps.allows("gh_add_component") is True
    assert caps.allows("gh_connect_components") is True
    assert caps.allows("gh_set_slider") is True
    assert caps.allows("rhino_execute_code") is False
    assert caps.allows("gh_write_script_py3") is False


def test_author_allows_everything_including_scripting():
    caps = capabilities_for(Scenario.AUTHOR)
    assert caps.allows("rhino_execute_code") is True
    assert caps.allows("gh_write_script_py3") is True
    assert caps.allows("gh_add_component") is True

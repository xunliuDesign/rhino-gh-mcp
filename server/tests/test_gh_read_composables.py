"""Tests for composable read tools that synthesize from get_context.

These tools (gh_list_toggles, gh_list_value_lists, gh_canvas_summary,
gh_find_components) wrap a single get_context call and project a useful view
out of it. We test them with a stub bridge so they don't need a running
Grasshopper.
"""

from __future__ import annotations

import json
from typing import Any

import pytest
from mcp.server.fastmcp import FastMCP

from rhino_gh_mcp.capabilities import CapabilitiesProvider, preset_for
from rhino_gh_mcp.config import Policy as PolicyEnum
from rhino_gh_mcp.tools import gh_read


class StubBridge:
    """Minimal stand-in for GrasshopperBridge.send/ping."""

    base_url = "http://127.0.0.1:9999"

    def __init__(self, context: dict[str, dict[str, Any]]):
        self._context = context

    def ping(self) -> bool:
        return True

    def send(self, command: str, /, **params: Any) -> dict[str, Any]:
        if command == "get_context":
            return {"status": "success", "result": self._context}
        raise AssertionError(f"unexpected command: {command}")


def _registered(app: FastMCP, name: str):
    """Pull a registered tool's underlying function out of FastMCP for direct call."""
    tools = getattr(app._tool_manager, "_tools", {})
    entry = tools.get(name)
    assert entry is not None, f"tool {name!r} not registered"
    # FastMCP wraps the user function in a Tool dataclass; .fn is the callable.
    return entry.fn


SAMPLE_CONTEXT: dict[str, dict[str, Any]] = {
    "guid-slider-1": {
        "name": "Number Slider",
        "nickName": "radius",
        "kind": "GH_NumberSlider",
        "value": 4.2,
        "min": 0.0,
        "max": 10.0,
    },
    "guid-panel-1": {
        "name": "Panel",
        "nickName": "notes",
        "kind": "GH_Panel",
        "userText": "hello",
    },
    "guid-toggle-1": {
        "name": "Boolean Toggle",
        "nickName": "enable_baking",
        "kind": "GH_BooleanToggle",
        "value": True,
    },
    "guid-toggle-2": {
        "name": "Boolean Toggle",
        "nickName": "wireframe",
        "kind": "GH_BooleanToggle",
        "value": False,
    },
    "guid-vl-1": {
        "name": "Value List",
        "nickName": "facade_type",
        "kind": "GH_ValueList",
        "items": [
            {"name": "glass", "expression": "0"},
            {"name": "panel", "expression": "1"},
        ],
        "selectedItems": ["glass"],
    },
    "guid-comp-warn": {
        "name": "Loft",
        "nickName": "Loft",
        "kind": "Loft",
        "runtimeMessages": [{"level": "Warning", "text": "no curves"}],
    },
    "guid-comp-err": {
        "name": "Brep",
        "nickName": "Brep",
        "kind": "Param_Brep",
        "runtimeMessages": [{"level": "Error", "text": "no input"}],
    },
}


@pytest.fixture
def app_and_bridge():
    app = FastMCP(name="test")
    bridge = StubBridge(SAMPLE_CONTEXT)
    caps = CapabilitiesProvider(default=preset_for(PolicyEnum.PARAMETER))
    gh_read.register(app, bridge, caps)
    return app, bridge


def test_list_toggles_returns_both_toggles(app_and_bridge):
    app, _ = app_and_bridge
    out = json.loads(_registered(app, "gh_list_toggles")())
    nicknames = sorted(t["nickname"] for t in out)
    assert nicknames == ["enable_baking", "wireframe"]
    values = {t["nickname"]: t["value"] for t in out}
    assert values["enable_baking"] is True
    assert values["wireframe"] is False


def test_list_value_lists_returns_items_and_selection(app_and_bridge):
    app, _ = app_and_bridge
    out = json.loads(_registered(app, "gh_list_value_lists")())
    assert len(out) == 1
    entry = out[0]
    assert entry["nickname"] == "facade_type"
    assert [i["name"] for i in entry["items"]] == ["glass", "panel"]
    assert entry["selected"] == ["glass"]


def test_canvas_summary_counts_and_widgets(app_and_bridge):
    app, _ = app_and_bridge
    out = json.loads(_registered(app, "gh_canvas_summary")())
    assert out["total_objects"] == len(SAMPLE_CONTEXT)
    assert out["components_with_errors"] == 1
    assert out["components_with_warnings"] == 1
    widget_kinds = sorted(w["kind"] for w in out["widgets"])
    assert widget_kinds == ["panel", "slider", "toggle", "toggle", "value_list"]


def test_canvas_summary_kind_histogram_is_sorted_desc(app_and_bridge):
    app, _ = app_and_bridge
    out = json.loads(_registered(app, "gh_canvas_summary")())
    counts = list(out["components_by_kind"].values())
    assert counts == sorted(counts, reverse=True)
    assert out["components_by_kind"]["GH_BooleanToggle"] == 2


def test_find_components_matches_by_nickname(app_and_bridge):
    app, _ = app_and_bridge
    out = json.loads(_registered(app, "gh_find_components")(query="rad"))
    assert len(out) == 1
    assert out[0]["nickname"] == "radius"


def test_find_components_matches_by_kind(app_and_bridge):
    app, _ = app_and_bridge
    out = json.loads(_registered(app, "gh_find_components")(query="toggle"))
    assert {m["nickname"] for m in out} == {"enable_baking", "wireframe"}


def test_find_components_empty_query_returns_empty(app_and_bridge):
    app, _ = app_and_bridge
    out = json.loads(_registered(app, "gh_find_components")(query=""))
    assert out == []


def test_find_components_limit_is_honored(app_and_bridge):
    app, _ = app_and_bridge
    out = json.loads(_registered(app, "gh_find_components")(query=" ", limit=2))
    # space matches everything joined with " " — verifies limit cap
    assert len(out) <= 2


def test_list_toggles_handles_bridge_error():
    class ErrorBridge(StubBridge):
        def send(self, command: str, /, **params: Any) -> dict[str, Any]:
            return {"status": "error", "result": "boom"}

    app = FastMCP(name="test")
    caps = CapabilitiesProvider(default=preset_for(PolicyEnum.PARAMETER))
    gh_read.register(app, ErrorBridge({}), caps)
    out = _registered(app, "gh_list_toggles")()
    assert out.startswith("Error:")

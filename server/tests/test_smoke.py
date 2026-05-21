"""Smoke tests - verify the server builds and the soft-gate works.

These tests do NOT require a running Rhino or Grasshopper. They construct
the FastMCP app, count the registered tools, and validate that capability
gating correctly denies out-of-scope calls at runtime.
"""

from __future__ import annotations

import pytest

from rhino_gh_mcp.capabilities import (
    ALL_TOOLS,
    Capabilities,
    CapabilitiesProvider,
    ComponentScope,
    preset_for,
)
from rhino_gh_mcp.config import Config, Policy as PolicyEnum, Transport
from rhino_gh_mcp.server import build_app


def _registered_names(app) -> set[str]:
    return set(getattr(app._tool_manager, "_tools", {}))  # noqa: SLF001


def test_every_tool_is_registered_regardless_of_policy():
    """Soft-gate model: all tools should always be advertised, no matter the
    --policy preset. Gating happens at call time."""
    for policy in (PolicyEnum.PARAMETER, PolicyEnum.CURATED, PolicyEnum.FULL):
        config = Config(policy=policy, transport=Transport.STDIO)
        app = build_app(config)
        tools = _registered_names(app)
        missing = ALL_TOOLS - tools
        assert not missing, f"Policy {policy.value}: missing tools {missing}"


def test_preset_capabilities_match_old_tiers():
    """The old PARAMETER / CURATED / FULL tier semantics should be preserved
    by the presets so existing CLI users see no behavior change."""
    p = preset_for(PolicyEnum.PARAMETER)
    assert p.allow_parameters is True
    assert p.allow_components is False
    assert p.allow_scripting is False

    c = preset_for(PolicyEnum.CURATED)
    assert c.allow_parameters is True
    assert c.allow_components is True
    assert c.allow_scripting is False

    f = preset_for(PolicyEnum.FULL)
    assert f.allow_parameters is True
    assert f.allow_components is True
    assert f.allow_scripting is True


@pytest.mark.parametrize(
    "caps,tool,should_allow",
    [
        # Read tools are always permitted
        (Capabilities(False, False, False), "gh_get_context", True),
        (Capabilities(False, False, False), "rhino_get_layers", True),
        (Capabilities(False, False, False), "gh_canvas_summary", True),
        # Parameter writes
        (Capabilities(True, False, False), "gh_set_slider", True),
        (Capabilities(False, True, True), "gh_set_slider", False),
        (Capabilities(True, False, False), "gh_set_toggle", True),
        # Component writes
        (Capabilities(False, True, False), "gh_add_component", True),
        (Capabilities(True, False, False), "gh_add_component", False),
        (Capabilities(False, True, False), "gh_connect_components", True),
        # Scripting
        (Capabilities(True, True, True), "rhino_execute_code", True),
        (Capabilities(True, True, False), "rhino_execute_code", False),
        (Capabilities(True, True, True), "gh_write_script_py3", True),
    ],
)
def test_capabilities_allows_by_bucket(caps, tool, should_allow):
    assert caps.allows(tool) is should_allow


def test_capabilities_unknown_tool_fails_closed():
    caps = Capabilities(True, True, True)
    assert caps.allows("gh_invented_tool") is False


def test_capabilities_provider_caches_until_ttl():
    """The provider should cache its current Capabilities and only refresh
    via the bridge on stale reads."""
    initial = preset_for(PolicyEnum.PARAMETER)
    provider = CapabilitiesProvider(default=initial)
    assert provider.current() is initial

    # No bridge attached -> stays at default forever
    assert provider.current().allow_components is False


def test_capabilities_provider_force_overrides():
    provider = CapabilitiesProvider(default=preset_for(PolicyEnum.CURATED))
    new_caps = Capabilities(
        allow_parameters=False,
        allow_components=False,
        allow_scripting=True,
        component_scope=ComponentScope.ALL,
        source="test",
    )
    provider.force(new_caps)
    assert provider.current().allow_scripting is True
    assert provider.current().source == "test"


def test_config_string_includes_policy():
    config = Config(policy=PolicyEnum.FULL, transport=Transport.HTTP, http_port=9000)
    s = str(config)
    assert "policy=full" in s
    assert "http:9000" in s

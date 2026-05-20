"""Smoke tests — verify the server builds and the policy filter works.

These tests do NOT require a running Rhino or Grasshopper. They construct the
FastMCP app, count the registered tools per policy, and validate that the
policy registry is self-consistent.
"""

from __future__ import annotations

import pytest

from rhino_gh_mcp.config import Config, Policy as PolicyEnum, Transport
from rhino_gh_mcp.policies import policy_for
from rhino_gh_mcp.policies.base import (
    CURATED_TOOLS,
    FULL_TOOLS,
    PARAMETER_TOOLS,
)
from rhino_gh_mcp.server import build_app


def test_policy_registry_is_nested():
    """Each tier must be a strict superset of the lower one."""
    assert PARAMETER_TOOLS.issubset(CURATED_TOOLS)
    assert CURATED_TOOLS.issubset(FULL_TOOLS)
    assert PARAMETER_TOOLS != CURATED_TOOLS
    assert CURATED_TOOLS != FULL_TOOLS


def test_policy_for_returns_distinct_policies():
    p = policy_for(PolicyEnum.PARAMETER)
    c = policy_for(PolicyEnum.CURATED)
    f = policy_for(PolicyEnum.FULL)
    assert len(p.tools) < len(c.tools) < len(f.tools)


@pytest.mark.parametrize(
    "policy_enum,expected_min",
    [
        (PolicyEnum.PARAMETER, 10),
        (PolicyEnum.CURATED, 16),
        (PolicyEnum.FULL, 22),
    ],
)
def test_build_app_registers_tools_per_policy(policy_enum, expected_min):
    """Building the app should register at least the expected number of tools.

    We use >= rather than == so adding new tools doesn't break this test —
    the strict policy-set membership test above catches accidental leaks.
    """
    config = Config(policy=policy_enum, transport=Transport.STDIO)
    app = build_app(config)
    tool_manager = getattr(app, "_tool_manager", None)
    assert tool_manager is not None, "FastMCP shape changed — update test"
    tools = getattr(tool_manager, "_tools", {})
    assert len(tools) >= expected_min, f"Got only {len(tools)} tools: {sorted(tools)}"


def test_parameter_policy_does_not_expose_write_tools():
    """L1 must never expose component-placement or script-injection tools."""
    config = Config(policy=PolicyEnum.PARAMETER, transport=Transport.STDIO)
    app = build_app(config)
    tools = set(getattr(app._tool_manager, "_tools", {}))  # noqa: SLF001
    forbidden = {
        "gh_add_component",
        "gh_add_any_component",
        "gh_connect_components",
        "gh_remove_node",
        "gh_write_script_py2",
        "gh_write_script_py3",
        "gh_write_script_cs",
        "gh_execute_code",
        "rhino_execute_code",
    }
    leaked = tools & forbidden
    assert not leaked, f"L1 policy leaked write tools: {leaked}"


def test_curated_policy_does_not_expose_full_only_tools():
    """L2 must not expose L3-only tools like script injection."""
    config = Config(policy=PolicyEnum.CURATED, transport=Transport.STDIO)
    app = build_app(config)
    tools = set(getattr(app._tool_manager, "_tools", {}))  # noqa: SLF001
    forbidden = {
        "gh_add_any_component",
        "gh_write_script_py2",
        "gh_write_script_py3",
        "gh_write_script_cs",
        "gh_execute_code",
        "rhino_execute_code",
    }
    leaked = tools & forbidden
    assert not leaked, f"L2 policy leaked L3 tools: {leaked}"


def test_full_policy_exposes_script_injection():
    """L3 must expose the script-component injection tools — that's its point."""
    config = Config(policy=PolicyEnum.FULL, transport=Transport.STDIO)
    app = build_app(config)
    tools = set(getattr(app._tool_manager, "_tools", {}))  # noqa: SLF001
    expected = {
        "gh_write_script_py2",
        "gh_write_script_py3",
        "gh_write_script_cs",
        "gh_execute_code",
        "rhino_execute_code",
    }
    missing = expected - tools
    assert not missing, f"L3 policy missing script-injection tools: {missing}"


def test_config_string_includes_policy():
    config = Config(policy=PolicyEnum.FULL, transport=Transport.HTTP, http_port=9000)
    s = str(config)
    assert "policy=full" in s
    assert "http:9000" in s

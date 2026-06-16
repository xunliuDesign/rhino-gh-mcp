"""Shared `@gated` decorator — soft capability gate at tool-call time.

Usage at a tool registration site:

    @app.tool(name="gh_xyz")
    @gated(caps, "gh_xyz")
    def gh_xyz(arg: str) -> str:
        '''Docstring becomes the tool description.'''
        ...

The outer @app.tool is FastMCP's registration. The inner @gated wraps the
function so that, at every call, it checks the current Capabilities and
short-circuits with a clean denial string if the capability is off.

@functools.wraps preserves __wrapped__, which inspect.signature follows by
default — so FastMCP still sees the original function's typed parameters
when building the tool's JSON schema.

v0.2 — in addition to the capability check, the gate also enforces
Execute-mode skill restrictions: when the canvas scenario is "execute" and
the active skill declares a `commands:` table (or an `allow_tools:` list),
non-allowed tool calls are rejected with a clean per-skill message.
"""

from __future__ import annotations

import functools
from typing import Callable, TypeVar

from ..capabilities import (
    CapabilitiesProvider,
    denial_message,
    execute_mode_blocks,
    execute_skill_gate_message,
)
from ..skills import get_skill

F = TypeVar("F", bound=Callable[..., str])


def _skill_allowed_tools(skill_id: str) -> frozenset[str] | None:
    """Build the per-skill allow-list from frontmatter.

    Sources, unioned:
      1. `allow_tools:` — explicit list of real MCP tool names the Skill
         needs (e.g. ["gh_add_component", "gh_set_slider"]). This is the
         practical way to make Execute mode functional today.
      2. `commands:` — dict; each key becomes a synthetic tool name
         `gh_skill_command_<verb>`. These don't exist as real tools yet —
         dynamic FastMCP registration is a v0.2.x deliverable. Surface them
         so dynamic-registration code in the future can wire them up
         without changing this gate.

    Returns None if the skill couldn't be found (signals "skill state on the
    canvas is ahead of what we have on disk — fail open").
    """
    skill = get_skill(skill_id)
    if skill is None:
        return None
    allowed: set[str] = set()
    # 1. Real tool allowlist — the workhorse for v0.2.0 Execute mode.
    for tool_name in skill.allow_tools:
        allowed.add(str(tool_name))
    # 2. Synthetic command tools (placeholder for v0.2.x dynamic registration).
    for verb in skill.commands:
        allowed.add(f"gh_skill_command_{verb}")
    return frozenset(allowed)


def gated(caps: CapabilitiesProvider, name: str) -> Callable[[F], F]:
    def decorator(fn: F) -> F:
        @functools.wraps(fn)
        def wrapper(*args, **kwargs):
            c = caps.current()
            if not c.allows(name):
                return denial_message(name, c)
            # v0.2: Execute-mode skill gate. Only applies if scenario is
            # "execute" AND a skill is active. Costs one skill lookup per
            # call — could be cached, but skills are tiny (file IO is the
            # bottleneck) and Execute mode isn't the default.
            if c.scenario == "execute" and c.active_skill:
                allowed = _skill_allowed_tools(c.active_skill)
                if execute_mode_blocks(name, c, allowed):
                    return execute_skill_gate_message(name, c.active_skill)
            return fn(*args, **kwargs)
        return wrapper  # type: ignore[return-value]
    return decorator

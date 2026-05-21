"""Legacy shim — the old hard-tier policy module.

The hard-tier `policy.allows(name)` mechanism has been replaced by the soft
runtime `Capabilities` model in `rhino_gh_mcp.capabilities`. Tools are now
registered unconditionally and gate themselves on capability state at call
time.

This module is preserved so existing imports and tests don't break. The
PARAMETER / CURATED / FULL frozensets are kept in sync with the new
capability presets so anyone reasoning about "what does L1/L2/L3 mean"
still gets a meaningful answer. New code should import from
`rhino_gh_mcp.capabilities` instead.
"""

from __future__ import annotations

from dataclasses import dataclass

from ..capabilities import (
    _COMPONENT_WRITE_TOOLS,
    _PARAMETER_WRITE_TOOLS,
    _READ_TOOLS,
    _SCRIPTING_TOOLS,
)
from ..config import Policy as PolicyEnum

# Same partition as before, derived from the capability buckets.
PARAMETER_TOOLS: frozenset[str] = _READ_TOOLS | _PARAMETER_WRITE_TOOLS
CURATED_TOOLS: frozenset[str] = PARAMETER_TOOLS | _COMPONENT_WRITE_TOOLS - frozenset(
    {"gh_add_any_component"}
)
FULL_TOOLS: frozenset[str] = CURATED_TOOLS | _SCRIPTING_TOOLS | frozenset(
    {"gh_add_any_component"}
)


@dataclass(frozen=True)
class Policy:
    """Backward-compatible name-list view of a tier.

    Used only by legacy tests and any external code that still imports
    `policy_for`. Real gating now lives in `Capabilities.allows`.
    """

    name: str
    tools: frozenset[str]

    def allows(self, tool_name: str) -> bool:
        return tool_name in self.tools

    def allowed_tools(self) -> frozenset[str]:
        return self.tools


_REGISTRY: dict[PolicyEnum, Policy] = {
    PolicyEnum.PARAMETER: Policy("parameter", PARAMETER_TOOLS),
    PolicyEnum.CURATED: Policy("curated", CURATED_TOOLS),
    PolicyEnum.FULL: Policy("full", FULL_TOOLS),
}


def policy_for(p: PolicyEnum) -> Policy:
    return _REGISTRY[p]

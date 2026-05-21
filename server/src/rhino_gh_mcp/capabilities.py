"""Runtime capability state — the soft-gate replacement for the old hard-tier policy.

All tools are now registered with FastMCP at startup. Whether a tool actually
executes is decided at call time by checking the current Capabilities. This
gives us:

  - One tool surface visible to the LLM (no startup-time mystery)
  - A dynamically adjustable set of what the LLM may do (canvas can flip flags
    mid-conversation without restarting the server)
  - Clean error messages back to the LLM when a capability is off

Three source-of-truth precedences (highest wins):

  1. Live state from the Grasshopper canvas component (queried via
     `get_capabilities` bridge command, cached briefly).
  2. The CLI `--policy` flag, mapped to a preset capability set.
  3. The conservative built-in default (CURATED preset).

The CLI preset is the FALLBACK when the canvas isn't reachable. When the
canvas is reachable, its capability state wins — that's the whole point of
the redesign.
"""

from __future__ import annotations

import logging
import time
from dataclasses import dataclass, replace
from enum import Enum
from typing import Any

from .config import Policy

log = logging.getLogger(__name__)


class ComponentScope(str, Enum):
    """When `allow_components` is True, which components may be placed."""

    CURATED = "curated"   # only categories listed in category_filter
    DEFAULTS = "defaults"  # stock Grasshopper components (Assembly = Grasshopper)
    ALL = "all"           # any installed component including third-party plug-ins


@dataclass(frozen=True)
class Capabilities:
    """What the LLM is currently allowed to do.

    Two orthogonal axes:
      - WHAT the LLM can do (`allow_parameters`, `allow_components`, `allow_scripting`)
      - WHICH components are in scope when placement is allowed (`component_scope`)

    A request like "place a Cylinder" needs BOTH `allow_components=True` AND
    `Cylinder`'s category to be in the active scope.
    """

    allow_parameters: bool = True
    allow_components: bool = True
    allow_scripting: bool = False
    component_scope: ComponentScope = ComponentScope.CURATED
    # Free-text category filter, used when component_scope == CURATED.
    # Comma-separated. Matched against IGH_Component.Category (case-insensitive).
    category_filter: str = "MCP"
    # Where this Capabilities came from — for logging / diagnostics.
    source: str = "default"

    def allows(self, name: str) -> bool:
        """Check whether a tool name is permitted under the current capabilities.

        This is a coarse name-based check (the heritage of the old policy
        module). Tools that further depend on component scope (place_component)
        do an additional check on the canvas side.
        """
        if name in _READ_TOOLS:
            return True
        if name in _PARAMETER_WRITE_TOOLS:
            return self.allow_parameters
        if name in _COMPONENT_WRITE_TOOLS:
            return self.allow_components
        if name in _SCRIPTING_TOOLS:
            return self.allow_scripting
        # Unknown tool — fail closed.
        log.warning("Capabilities.allows: unknown tool %r — denying", name)
        return False


# --- Tool-name partition -----------------------------------------------------
# Same names that used to be in policies/base.py. The partition is now by
# WHICH CAPABILITY a tool needs, not by tier.

_READ_TOOLS: frozenset[str] = frozenset(
    {
        # Grasshopper inspection
        "gh_status",
        "gh_get_context",
        "gh_get_objects",
        "gh_get_selected",
        "gh_list_sliders",
        "gh_list_panels",
        "gh_list_toggles",
        "gh_list_value_lists",
        "gh_canvas_summary",
        "gh_find_components",
        "gh_get_panel_content",
        "gh_get_runtime_messages",
        "gh_list_available_components",  # listing what's available is reading
        "gh_recompute",  # idempotent recompute is effectively read-side
        # Multimodal — viewport capture is read-only
        "gh_capture_canvas",
        "rhino_capture_viewport",
        # Rhino inspection
        "rhino_status",
        "rhino_get_scene_info",
        "rhino_get_layers",
        "rhino_get_objects_with_metadata",
        "rhino_list_blocks",
        # View driving — touches viewport, not document objects
        "rhino_set_view",
    }
)

_PARAMETER_WRITE_TOOLS: frozenset[str] = frozenset(
    {
        "gh_set_slider",
        "gh_set_toggle",
        "gh_select_value_list",
        "gh_set_component_parameter",
        "gh_set_slider_range",
    }
)

_COMPONENT_WRITE_TOOLS: frozenset[str] = frozenset(
    {
        "gh_add_component",
        "gh_add_slider",
        "gh_add_any_component",
        "gh_connect_components",
        "gh_remove_node",
    }
)

_SCRIPTING_TOOLS: frozenset[str] = frozenset(
    {
        "gh_write_script_py3",
        "gh_write_script_cs",
        "gh_write_script_py2",
        "gh_update_script",
        "gh_execute_code",
        "rhino_execute_code",
        "rhino_run_named_command",
    }
)

ALL_TOOLS: frozenset[str] = (
    _READ_TOOLS | _PARAMETER_WRITE_TOOLS | _COMPONENT_WRITE_TOOLS | _SCRIPTING_TOOLS
)


# --- Presets — the old CLI policies, expressed as capability sets ------------

_PRESETS: dict[Policy, Capabilities] = {
    Policy.PARAMETER: Capabilities(
        allow_parameters=True,
        allow_components=False,
        allow_scripting=False,
        component_scope=ComponentScope.CURATED,
        source="cli:parameter",
    ),
    Policy.CURATED: Capabilities(
        allow_parameters=True,
        allow_components=True,
        allow_scripting=False,
        component_scope=ComponentScope.CURATED,
        source="cli:curated",
    ),
    Policy.FULL: Capabilities(
        allow_parameters=True,
        allow_components=True,
        allow_scripting=True,
        component_scope=ComponentScope.ALL,
        source="cli:full",
    ),
}


def preset_for(policy: Policy) -> Capabilities:
    return _PRESETS[policy]


def from_canvas_reply(reply: dict[str, Any]) -> Capabilities | None:
    """Build a Capabilities from the .gha's `get_capabilities` reply.

    Returns None if the reply was an error or didn't include capability keys
    (e.g. talking to a pre-v0.1.5 .gha that hasn't been updated yet).
    """
    if reply.get("status") != "success":
        return None
    result = reply.get("result") or {}
    if "allow_parameters" not in result:
        return None
    try:
        scope_str = result.get("component_scope") or "curated"
        scope = ComponentScope(scope_str)
    except ValueError:
        scope = ComponentScope.CURATED
    return Capabilities(
        allow_parameters=bool(result.get("allow_parameters", True)),
        allow_components=bool(result.get("allow_components", True)),
        allow_scripting=bool(result.get("allow_scripting", False)),
        component_scope=scope,
        category_filter=str(result.get("category_filter") or ""),
        source="canvas",
    )


# --- Cache: the live capability state shared across tool calls ---------------
# Tools read this on every call. The bridge layer (or a background poll) can
# update it with a fresh canvas reading; until then the CLI preset stays in
# force. Cache TTL is short so that a canvas-side flip propagates quickly.

_CACHE_TTL_SECONDS = 3.0


class CapabilitiesProvider:
    """Lookups + cache for current capabilities.

    Lifecycle: one instance per server process, owned by the FastMCP app.
    Tools call .current() at the top of every invocation; that method
    consults the cache and refreshes from the canvas at most every
    `_CACHE_TTL_SECONDS`.
    """

    def __init__(self, default: Capabilities, *, gh_bridge: Any = None):
        self._default = default
        self._current = default
        self._gh_bridge = gh_bridge
        self._last_refresh = 0.0

    @property
    def default(self) -> Capabilities:
        return self._default

    def set_bridge(self, gh_bridge: Any) -> None:
        self._gh_bridge = gh_bridge

    def current(self) -> Capabilities:
        """Return the active Capabilities, refreshing from canvas if stale."""
        now = time.monotonic()
        if now - self._last_refresh < _CACHE_TTL_SECONDS:
            return self._current
        self._last_refresh = now
        if self._gh_bridge is None:
            return self._current

        # Best-effort canvas query. If the bridge or component isn't up, we
        # silently fall back to the cached value — usually the CLI default.
        try:
            reply = self._gh_bridge.send("get_capabilities")
        except Exception as exc:
            log.debug("get_capabilities probe failed: %s", exc)
            return self._current
        canvas_caps = from_canvas_reply(reply)
        if canvas_caps is not None:
            self._current = canvas_caps
        return self._current

    def force(self, caps: Capabilities) -> None:
        """Override the current capabilities (used by tests)."""
        self._current = replace(caps)
        self._last_refresh = time.monotonic()


def denial_message(tool_name: str, caps: Capabilities) -> str:
    """Format a clean message returned to the LLM when a tool is denied.

    The message tells the LLM exactly which canvas-side flag to flip — so a
    well-prompted model can ask the user to enable it.
    """
    if tool_name in _PARAMETER_WRITE_TOOLS:
        knob = "AllowParameters"
    elif tool_name in _COMPONENT_WRITE_TOOLS:
        knob = "AllowComponents"
    elif tool_name in _SCRIPTING_TOOLS:
        knob = "AllowScripting"
    else:
        knob = "(unknown)"
    return (
        f"Tool {tool_name!r} is currently disabled (capability denied). "
        f"To enable it, set the `{knob}` input on the rhino-gh-mcp Server "
        f"component to True. Active capabilities source: {caps.source}."
    )

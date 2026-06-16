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
    # v0.2: scenario + active skill, reported by the V2 canvas component.
    # V1 canvas leaves these empty/default, so the server falls back to
    # gating purely on (allow_parameters, allow_components, allow_scripting).
    scenario: str = ""  # "inspect" | "tune" | "coach" | "execute" | "author" | ""
    active_skill: str = ""  # skill id, or "" for none

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
        # Server meta (skill discovery)
        "list_skills",
        "load_skill",
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
        # v0.2: Skills v2 + turn tracking
        "gh_list_skills",            # newer alias for list_skills (Skills v2)
        "gh_introspect_definition",  # parse a .gh archive on disk
        "gh_begin_turn",
        "gh_end_turn",
        "gh_dismiss_highlights",
        # v0.2.3: read-side productivity tool — peek at a component output.
        "gh_get_component_output",
        # v0.2.4: fast canvas outline tools — wire-graph clusters + drill-down.
        "gh_canvas_outline",
        "gh_file_outline",
        "gh_cluster_flow",
    }
)

_PARAMETER_WRITE_TOOLS: frozenset[str] = frozenset(
    {
        "gh_set_slider",
        "gh_set_toggle",
        "gh_select_value_list",
        "gh_set_component_parameter",
        "gh_set_slider_range",
        "gh_set_expression_formula",
        # v0.2.3: parameter-write-shaped layout + content tools.
        "gh_set_panel_content",
        "gh_move_component",
        "gh_organize_components",
    }
)

_COMPONENT_WRITE_TOOLS: frozenset[str] = frozenset(
    {
        "gh_add_component",
        "gh_add_slider",
        "gh_add_any_component",
        "gh_connect_components",
        "gh_remove_node",
        # v0.2: loading a Skill's reference .gh definition merges components
        # into the current canvas — same risk profile as gh_add_component.
        "gh_load_skill_reference",
        # v0.2.2: arbitrary file path version of the above. Same risk profile.
        "gh_merge_definition",
        # v0.2.3: productivity tools that ADD a new object to the canvas
        # (Panel, Group, referencing Param, or a baked Rhino object).
        "gh_bake_to_rhino",
        "gh_reference_rhino_object",
        "gh_add_panel",
        "gh_group_components",
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
        # v0.2 — only the V2 canvas component populates these. V1 returns
        # missing keys, which collapse to "" via the default.
        scenario=str(result.get("scenario") or ""),
        active_skill=str(result.get("active_skill") or ""),
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


# --- v0.2: Execute-mode skill gating ----------------------------------------
# When the canvas is in Execute scenario and a Skill is active, the AI is
# restricted to the Skill's command grammar. Read tools always remain
# available — the AI still needs to inspect the canvas to know what's there.
# Per design doc §"Execute mode infrastructure": "this Skill doesn't support
# that — change Scenario to Coach or Author for full freedom".

# Tools that stay available in Execute mode regardless of skill — pure-read.
_EXECUTE_ALWAYS_AVAILABLE: frozenset[str] = frozenset(_READ_TOOLS) | frozenset({
    # Coach/Execute infrastructure
    "gh_begin_turn",
    "gh_end_turn",
    "gh_dismiss_highlights",
    # Skills introspection (always allowed so AI can see what's offered)
    "gh_list_skills",
    "gh_introspect_definition",
})


def execute_skill_gate_message(tool_name: str, skill_name: str) -> str:
    """Wording for the "Skill doesn't support that" denial per design doc."""
    return (
        f"Skill {skill_name!r} doesn't support {tool_name!r} — change Scenario "
        f"to Coach or Author for full freedom."
    )


def execute_mode_blocks(tool_name: str, caps: Capabilities,
                        skill_allowed_tools: frozenset[str] | None) -> bool:
    """Whether Execute-mode skill gating blocks a tool call.

    Returns False (not blocked) if:
      - we're not in Execute mode, OR
      - no Skill is active (skill_allowed_tools is None / empty), OR
      - the tool is in the always-available set, OR
      - the tool is explicitly whitelisted by the skill.
    """
    if caps.scenario != "execute":
        return False
    if not caps.active_skill:
        return False
    if skill_allowed_tools is None:
        # No skill loaded server-side even though canvas says one's active —
        # be permissive (fail open) so a typo in the canvas input doesn't
        # brick the session.
        return False
    if tool_name in _EXECUTE_ALWAYS_AVAILABLE:
        return False
    if tool_name in skill_allowed_tools:
        return False
    return True

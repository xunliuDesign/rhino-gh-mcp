"""FastMCP app construction and tool registration."""

from __future__ import annotations

import logging
from contextlib import asynccontextmanager
from pathlib import Path
from typing import AsyncIterator

from mcp.server.fastmcp import FastMCP

from .bridges.grasshopper import GrasshopperBridge
from .bridges.rhino import RhinoBridge
from .capabilities import CapabilitiesProvider, preset_for
from .config import Config
from .tools import gh_read, gh_script, gh_write, meta, multimodal, rhino_tools, v2_tools
from .turns import TurnTracker

log = logging.getLogger(__name__)

# Path to the shared Coach mode system prompt that the server injects into
# the FastMCP `instructions` when scenario=coach + a skill is active.
_COACH_PROMPT_PATH = (
    Path(__file__).resolve().parents[3] / "skills" / "_shared" / "coach-system-prompt.md"
)


def build_app(config: Config) -> FastMCP:
    """Construct the FastMCP application for the given configuration.

    Every tool is registered unconditionally. Whether a tool actually runs is
    decided at call time by the CapabilitiesProvider, which:

      - starts with the preset capabilities derived from --policy
      - overrides those with whatever the .gha canvas reports via the
        `get_capabilities` bridge command (cached, refreshed at most every
        few seconds).

    The bridges are created lazily — the server starts even if Rhino /
    Grasshopper aren't running yet.
    """
    gh_bridge = GrasshopperBridge(port=config.gh_port)
    rhino_bridge = RhinoBridge(port=config.rhino_port)

    default_caps = preset_for(config.policy)
    caps = CapabilitiesProvider(default=default_caps, gh_bridge=gh_bridge)
    # v0.2: Coach mode turn tracker. Stateful, lives for the process lifetime.
    turns = TurnTracker()

    @asynccontextmanager
    async def lifespan(_: FastMCP) -> AsyncIterator[dict]:
        log.info(
            "rhino-gh-mcp ready (default caps=%s, gh=%d, rhino=%d)",
            default_caps.source,
            config.gh_port,
            config.rhino_port,
        )
        try:
            yield {
                "gh": gh_bridge,
                "rhino": rhino_bridge,
                "config": config,
                "caps": caps,
                "turns": turns,
            }
        finally:
            log.info("rhino-gh-mcp shutting down")
            gh_bridge.close()
            rhino_bridge.close()

    app = FastMCP(
        name="rhino-gh",
        instructions=_server_instructions(config, default_caps),
        lifespan=lifespan,
    )

    meta.register(app, caps)
    gh_read.register(app, gh_bridge, caps)
    gh_write.register(app, gh_bridge, caps, config)
    gh_script.register(app, gh_bridge, caps)
    rhino_tools.register(app, rhino_bridge, caps)
    multimodal.register(app, gh_bridge, rhino_bridge, caps)
    # v0.2 surface: Skills v2 + turn tracking + .gh introspection.
    v2_tools.register(app, gh_bridge, caps, turns)

    log.info(
        "Registered %d tools (default caps: params=%s components=%s scripting=%s scope=%s)",
        _count_tools(app),
        default_caps.allow_parameters,
        default_caps.allow_components,
        default_caps.allow_scripting,
        default_caps.component_scope.value,
    )
    return app


def _count_tools(app: FastMCP) -> int:
    """Best-effort tool count for log output."""
    try:
        tool_manager = getattr(app, "_tool_manager", None)
        if tool_manager is not None:
            return len(getattr(tool_manager, "_tools", {}))
    except Exception:
        pass
    return -1


def _server_instructions(config, default_caps) -> str:
    """High-level guidance shown to the model on connect.

    Skills (loaded by the client) carry workflow-specific prose; this is the
    minimum the model needs to navigate the tool surface and understand the
    soft-gate model.

    v0.2 — also includes scenario guidance and (if available on disk) the
    shared Coach mode system prompt. The prompt is injected unconditionally
    so it's available to any MCP client; the actual gating of Coach behavior
    happens in `_gate.py` based on the live canvas reading.
    """
    common = (
        "You control a Rhino 3D + Grasshopper session through this server. "
        "Two bridges sit behind these tools: 'gh_*' tools talk to a Grasshopper "
        "canvas via the MCP Server component; 'rhino_*' tools talk to the Rhino "
        "document via the Rhino plugin. Before writing, read - call "
        "gh_get_context or rhino_get_scene_info to ground yourself. Work in "
        "small steps and recompute / capture the viewport to verify. "
        "BEFORE tackling any non-trivial task, call `list_skills` (legacy) or "
        "`gh_list_skills` (v2, structured) to see if a workflow recipe exists; "
        "if a skill matches the user's request, call `load_skill(<id>)` and "
        "follow it rather than re-deriving the workflow."
    )
    soft_gate = (
        " All tools are advertised, but each tool is gated at call time by "
        "the canvas's current capability flags. In v0.1 these were exposed as "
        "three booleans (AllowParameters, AllowComponents, AllowScripting); "
        "in v0.2 they're derived from a single Scenario dropdown on the v2 "
        "canvas component (Inspect / Tune / Coach / Execute / Author). "
        "If a call returns 'capability denied', ask the user to change the "
        "Scenario (v2) or flip the matching toggle (v1). Defaults at server "
        f"startup: params={default_caps.allow_parameters}, "
        f"components={default_caps.allow_components}, "
        f"scripting={default_caps.allow_scripting}, "
        f"component_scope={default_caps.component_scope.value}."
    )
    # Coach prompt is included unconditionally because MCP `instructions` is
    # set once at session-start — we can't re-issue it when the canvas flips
    # to Coach mid-session. The prompt itself is self-gated ("apply only when
    # the canvas reports scenario: coach"), so it's a no-op in other modes.
    coach = ""
    try:
        if _COACH_PROMPT_PATH.is_file():
            coach = (
                "\n\n--- Coach-mode addendum (apply only when scenario=coach) ---\n"
                + _COACH_PROMPT_PATH.read_text(encoding="utf-8")
            )
    except OSError:
        coach = ""
    return common + soft_gate + coach

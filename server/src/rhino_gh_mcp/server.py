"""FastMCP app construction and tool registration."""

from __future__ import annotations

import logging
from contextlib import asynccontextmanager
from typing import AsyncIterator

from mcp.server.fastmcp import FastMCP

from .bridges.grasshopper import GrasshopperBridge
from .bridges.rhino import RhinoBridge
from .config import Config, Policy
from .policies import policy_for
from .tools import gh_read, gh_script, gh_write, multimodal, rhino_tools

log = logging.getLogger(__name__)


def build_app(config: Config) -> FastMCP:
    """Construct the FastMCP application for the given configuration.

    Tools are registered conditionally based on the active policy. The bridge
    objects are created lazily — the server starts even if Rhino/Grasshopper
    aren't running yet.
    """
    gh_bridge = GrasshopperBridge(port=config.gh_port)
    rhino_bridge = RhinoBridge(port=config.rhino_port)
    policy = policy_for(config.policy)

    @asynccontextmanager
    async def lifespan(_: FastMCP) -> AsyncIterator[dict]:
        log.info(
            "rhino-gh-mcp ready (policy=%s, gh=%d, rhino=%d)",
            config.policy.value,
            config.gh_port,
            config.rhino_port,
        )
        try:
            yield {"gh": gh_bridge, "rhino": rhino_bridge, "config": config}
        finally:
            log.info("rhino-gh-mcp shutting down")
            gh_bridge.close()
            rhino_bridge.close()

    app = FastMCP(
        name="rhino-gh",
        instructions=_server_instructions(config),
        lifespan=lifespan,
    )

    gh_read.register(app, gh_bridge, policy)
    gh_write.register(app, gh_bridge, policy, config)
    gh_script.register(app, gh_bridge, policy)
    rhino_tools.register(app, rhino_bridge, policy)
    multimodal.register(app, gh_bridge, rhino_bridge, policy)

    log.info(
        "Registered %d tools under policy '%s'",
        len(policy.allowed_tools()) if policy.allowed_tools() is not None else _count_tools(app),
        config.policy.value,
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


def _server_instructions(config: Config) -> str:
    """High-level guidance shown to the model on connect.

    Skills (loaded by the client) carry workflow-specific prose; this is the
    minimum the model needs to navigate the tool surface.
    """
    common = (
        "You control a Rhino 3D + Grasshopper session through this server. "
        "Two bridges sit behind these tools: 'gh.*' tools talk to a Grasshopper "
        "canvas via the MCP Server component; 'rhino.*' tools talk to the Rhino "
        "document via the Rhino plugin. Before writing, read — call "
        "gh.get_context or rhino.get_scene_info to ground yourself. Work in "
        "small steps and recompute / capture the viewport to verify."
    )
    if config.policy is Policy.PARAMETER:
        return (
            common
            + " You are operating in PARAMETER mode: you may only read state and "
            "adjust sliders, panels, toggles, value-lists, and trigger recomputes. "
            "You cannot add, remove, or wire components."
        )
    if config.policy is Policy.CURATED:
        groups = ", ".join(config.curated_group) if config.curated_group else "(default user-objects)"
        return (
            common
            + f" You are operating in CURATED mode. You may place components only "
            f"from these category groups: {groups}. Use gh.list_available_components "
            "to see what's allowed."
        )
    return (
        common
        + " You are operating in FULL mode: you may place any component, wire "
        "freely, and write Python 3 / IronPython 2 / C# script components. With "
        "great power etc. — prefer small reversible steps and verify with viewport "
        "captures."
    )

"""Grasshopper read-only tools: canvas inspection."""

from __future__ import annotations

import json
import logging
from typing import Any

from mcp.server.fastmcp import FastMCP

from ..bridges.grasshopper import BridgeError, GrasshopperBridge
from ..policies import Policy

log = logging.getLogger(__name__)


def register(app: FastMCP, gh: GrasshopperBridge, policy: Policy) -> None:
    """Register read-only Grasshopper tools that the active policy allows."""

    def _gate(name: str) -> bool:
        if not policy.allows(name):
            log.debug("policy '%s' excludes %s", policy.name, name)
            return False
        return True

    def _result(reply: dict[str, Any]) -> str:
        """Normalize a bridge reply into a string suitable as a tool return."""
        if reply.get("status") == "error":
            return f"Error: {reply.get('result') or reply.get('message') or 'unknown'}"
        return json.dumps(reply.get("result", reply), indent=2, default=str)

    if _gate("gh_status"):

        @app.tool(name="gh_status")
        def gh_status() -> str:
            """Return the live status of the Grasshopper bridge.

            Use this first to confirm the MCP Server component is running on the
            canvas before issuing any other gh_* commands.
            """
            try:
                if not gh.ping():
                    return "Grasshopper bridge is not responding at " + gh.base_url
                reply = gh.send("is_server_available")
                return _result(reply)
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_get_context"):

        @app.tool(name="gh_get_context")
        def gh_get_context(simplified: bool = False) -> str:
            """Return the full canvas state: components, params, wires, sorted by execution order.

            Args:
                simplified: When True, drop detailed parameter properties (smaller payload).
            """
            try:
                return _result(gh.send("get_context", simplified=simplified))
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_get_objects"):

        @app.tool(name="gh_get_objects")
        def gh_get_objects(
            instance_guids: list[str],
            simplified: bool = False,
            context_depth: int = 0,
        ) -> str:
            """Return information about specific components by GUID.

            Args:
                instance_guids: GUIDs to retrieve.
                simplified: When True, minimal component info only.
                context_depth: 0-3 — how many wire hops of neighbors to include.
            """
            try:
                return _result(
                    gh.send(
                        "get_objects",
                        instance_guids=instance_guids,
                        simplified=simplified,
                        context_depth=context_depth,
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_get_selected"):

        @app.tool(name="gh_get_selected")
        def gh_get_selected(simplified: bool = False, context_depth: int = 0) -> str:
            """Return information about currently selected canvas components.

            Args:
                simplified: When True, minimal component info only.
                context_depth: 0-3.
            """
            try:
                return _result(
                    gh.send(
                        "get_selected",
                        simplified=simplified,
                        context_depth=context_depth,
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_list_sliders"):

        @app.tool(name="gh_list_sliders")
        def gh_list_sliders() -> str:
            """Return all number sliders on the canvas with current value and range.

            This is the primary read tool for PARAMETER-mode work — it filters
            the canvas down to just the controls the LLM is allowed to touch.
            """
            try:
                reply = gh.send("get_context", simplified=True)
                if reply.get("status") == "error":
                    return _result(reply)
                ctx = reply.get("result", {}) or {}
                sliders: list[dict[str, Any]] = []
                for guid, info in ctx.items():
                    kind = (info.get("kind") or "").lower()
                    if "slider" in kind or info.get("name") == "Number Slider":
                        sliders.append(
                            {
                                "instance_guid": guid,
                                "nickname": info.get("nickName") or info.get("name"),
                                "value": info.get("value"),
                                "min": info.get("min"),
                                "max": info.get("max"),
                            }
                        )
                return json.dumps(sliders, indent=2)
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_list_panels"):

        @app.tool(name="gh_list_panels")
        def gh_list_panels() -> str:
            """Return all Panel components on the canvas with their GUIDs and nicknames."""
            try:
                reply = gh.send("get_context", simplified=True)
                if reply.get("status") == "error":
                    return _result(reply)
                ctx = reply.get("result", {}) or {}
                panels = [
                    {
                        "instance_guid": guid,
                        "nickname": info.get("nickName") or info.get("name"),
                    }
                    for guid, info in ctx.items()
                    if "panel" in (info.get("kind") or "").lower()
                ]
                return json.dumps(panels, indent=2)
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_get_panel_content"):

        @app.tool(name="gh_get_panel_content")
        def gh_get_panel_content(instance_guid: str) -> str:
            """Return the full content of a Panel component (text + runtime data).

            Args:
                instance_guid: GUID of the Panel.
            """
            try:
                return _result(gh.send("get_panel_content", instance_guid=instance_guid))
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_get_runtime_messages"):

        @app.tool(name="gh_get_runtime_messages")
        def gh_get_runtime_messages(instance_guid: str) -> str:
            """Return runtime messages (warnings, errors) for a single component.

            Use this after recompute to diagnose failures.

            Args:
                instance_guid: GUID of the component to inspect.
            """
            try:
                reply = gh.send(
                    "get_objects",
                    instance_guids=[instance_guid],
                    context_depth=0,
                )
                if reply.get("status") == "error":
                    return _result(reply)
                info = (reply.get("result") or {}).get(instance_guid) or {}
                messages = info.get("runtimeMessages") or info.get("messages") or []
                return json.dumps(
                    {"instance_guid": instance_guid, "runtime_messages": messages},
                    indent=2,
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_list_available_components"):

        @app.tool(name="gh_list_available_components")
        def gh_list_available_components(
            limit: int = 500,
            refresh: bool = False,
        ) -> str:
            """List Grasshopper components available for placement.

            In CURATED policy this is filtered by the .gha component's
            CategoryFilter input. Use this before gh_add_component to know what
            names are valid.

            Args:
                limit: Maximum number of proxies to return (default 500).
                refresh: When True, ask the bridge to rebuild its proxy cache.
            """
            try:
                return _result(
                    gh.send("get_all_component_proxies", limit=limit, refresh=refresh)
                )
            except BridgeError as exc:
                return f"Error: {exc}"

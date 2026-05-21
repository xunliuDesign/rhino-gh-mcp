"""Grasshopper read-only tools: canvas inspection.

All tools register unconditionally. The @gated decorator checks current
capabilities at call time and returns a clean denial string if denied.
Read tools always pass the capability check (Capabilities.allows treats
all read tools as permitted).
"""

from __future__ import annotations

import json
import logging
from typing import Any

from mcp.server.fastmcp import FastMCP

from ..bridges.grasshopper import BridgeError, GrasshopperBridge
from ..capabilities import CapabilitiesProvider
from ._gate import gated

log = logging.getLogger(__name__)


def register(app: FastMCP, gh: GrasshopperBridge, caps: CapabilitiesProvider) -> None:
    """Register read-only Grasshopper tools."""

    def _result(reply: dict[str, Any]) -> str:
        """Normalize a bridge reply into a string suitable as a tool return."""
        if reply.get("status") == "error":
            return f"Error: {reply.get('result') or reply.get('message') or 'unknown'}"
        return json.dumps(reply.get("result", reply), indent=2, default=str)

    @app.tool(name="gh_status")
    @gated(caps, "gh_status")
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

    @app.tool(name="gh_get_context")
    @gated(caps, "gh_get_context")
    def gh_get_context(simplified: bool = False) -> str:
        """Return the full canvas state: components, params, wires, sorted by execution order.

        Args:
            simplified: When True, drop detailed parameter properties (smaller payload).
        """
        try:
            return _result(gh.send("get_context", simplified=simplified))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_get_objects")
    @gated(caps, "gh_get_objects")
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

    @app.tool(name="gh_get_selected")
    @gated(caps, "gh_get_selected")
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

    @app.tool(name="gh_list_sliders")
    @gated(caps, "gh_list_sliders")
    def gh_list_sliders() -> str:
        """Return all number sliders on the canvas with current value and range.

        This is the primary read tool for parameter-mode work — it filters
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

    @app.tool(name="gh_list_panels")
    @gated(caps, "gh_list_panels")
    def gh_list_panels() -> str:
        """Return all Panel components on the canvas with their GUIDs and nicknames.

        Panel text content is NOT included in this list — use
        gh_get_panel_content(instance_guid) for that.
        """
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

    @app.tool(name="gh_list_toggles")
    @gated(caps, "gh_list_toggles")
    def gh_list_toggles() -> str:
        """Return all Boolean Toggle widgets on the canvas with GUID, nickname, value.

        `value` reflects the current toggle state (requires .gha v0.1.3+).
        """
        try:
            reply = gh.send("get_context", simplified=True)
            if reply.get("status") == "error":
                return _result(reply)
            ctx = reply.get("result", {}) or {}
            toggles = [
                {
                    "instance_guid": guid,
                    "nickname": info.get("nickName") or info.get("name"),
                    "value": info.get("value"),
                }
                for guid, info in ctx.items()
                if "booleantoggle" in (info.get("kind") or "").lower().replace("_", "")
                or info.get("name") == "Boolean Toggle"
            ]
            return json.dumps(toggles, indent=2)
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_list_value_lists")
    @gated(caps, "gh_list_value_lists")
    def gh_list_value_lists() -> str:
        """Return all Value List widgets on the canvas with items + current selection.

        Each entry: {instance_guid, nickname, items: [{name, expression}, ...],
        selected: [names]}.
        """
        try:
            reply = gh.send("get_context", simplified=True)
            if reply.get("status") == "error":
                return _result(reply)
            ctx = reply.get("result", {}) or {}
            lists = [
                {
                    "instance_guid": guid,
                    "nickname": info.get("nickName") or info.get("name"),
                    "items": info.get("items") or [],
                    "selected": info.get("selectedItems") or [],
                }
                for guid, info in ctx.items()
                if "valuelist" in (info.get("kind") or "").lower().replace("_", "")
                or info.get("name") == "Value List"
            ]
            return json.dumps(lists, indent=2)
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_canvas_summary")
    @gated(caps, "gh_canvas_summary")
    def gh_canvas_summary() -> str:
        """Return a high-level digest of the canvas — cheaper than gh_get_context.

        Use this first when joining an existing session: it gives counts by
        component kind, a flat list of parameter widgets (sliders/toggles/
        value-lists/panels with GUID + nickname), and counts of components
        carrying runtime errors or warnings.
        """
        try:
            reply = gh.send("get_context", simplified=True)
            if reply.get("status") == "error":
                return _result(reply)
            ctx = reply.get("result", {}) or {}
            kinds: dict[str, int] = {}
            widgets: list[dict[str, Any]] = []
            errors = 0
            warnings = 0
            for guid, info in ctx.items():
                kind = info.get("kind") or info.get("name") or "Unknown"
                kinds[kind] = kinds.get(kind, 0) + 1
                msgs = info.get("runtimeMessages") or info.get("messages") or []
                for m in msgs:
                    level = (m.get("level") if isinstance(m, dict) else str(m)) or ""
                    if "error" in level.lower():
                        errors += 1
                        break
                    if "warning" in level.lower():
                        warnings += 1
                        break
                nick = info.get("nickName") or info.get("name")
                kind_lc = kind.lower().replace("_", "")
                if "slider" in kind_lc:
                    widgets.append({"guid": guid, "kind": "slider", "nickname": nick})
                elif "booleantoggle" in kind_lc:
                    widgets.append({"guid": guid, "kind": "toggle", "nickname": nick})
                elif "valuelist" in kind_lc:
                    widgets.append({"guid": guid, "kind": "value_list", "nickname": nick})
                elif "panel" in kind_lc:
                    widgets.append({"guid": guid, "kind": "panel", "nickname": nick})
            return json.dumps(
                {
                    "total_objects": len(ctx),
                    "components_by_kind": dict(
                        sorted(kinds.items(), key=lambda kv: (-kv[1], kv[0]))
                    ),
                    "widgets": widgets,
                    "components_with_errors": errors,
                    "components_with_warnings": warnings,
                },
                indent=2,
            )
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_find_components")
    @gated(caps, "gh_find_components")
    def gh_find_components(query: str, limit: int = 50) -> str:
        """Search canvas components by partial name/nickname (case-insensitive).

        Use this to locate components without dumping the full canvas. Matches
        against `name`, `nickName`, and `kind`.

        Args:
            query: Substring to match (case-insensitive).
            limit: Maximum number of matches to return (default 50).
        """
        try:
            reply = gh.send("get_context", simplified=True)
            if reply.get("status") == "error":
                return _result(reply)
            ctx = reply.get("result", {}) or {}
            q = (query or "").lower().strip()
            if not q:
                return json.dumps([], indent=2)
            matches: list[dict[str, Any]] = []
            for guid, info in ctx.items():
                haystack = " ".join(
                    str(info.get(k) or "") for k in ("name", "nickName", "kind")
                ).lower()
                if q in haystack:
                    matches.append(
                        {
                            "instance_guid": guid,
                            "name": info.get("name"),
                            "nickname": info.get("nickName"),
                            "kind": info.get("kind"),
                        }
                    )
                    if len(matches) >= limit:
                        break
            return json.dumps(matches, indent=2)
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_get_panel_content")
    @gated(caps, "gh_get_panel_content")
    def gh_get_panel_content(instance_guid: str) -> str:
        """Return the full content of a Panel component (text + runtime data).

        Args:
            instance_guid: GUID of the Panel.
        """
        try:
            return _result(gh.send("get_panel_content", instance_guid=instance_guid))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_get_runtime_messages")
    @gated(caps, "gh_get_runtime_messages")
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

    @app.tool(name="gh_list_available_components")
    @gated(caps, "gh_list_available_components")
    def gh_list_available_components(
        limit: int = 500,
        refresh: bool = False,
    ) -> str:
        """List Grasshopper components available for placement.

        Filtered by the .gha's current ComponentScope + CategoryFilter inputs.
        Use this before gh_add_component to know what names are valid.

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

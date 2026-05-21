"""Rhino-side tools: scene inspection, geometry, code execution.

All tools register unconditionally; @gated gates them at call time.
"""

from __future__ import annotations

import json
import logging
from typing import Any

from mcp.server.fastmcp import FastMCP

from ..bridges.rhino import BridgeError, RhinoBridge
from ..capabilities import CapabilitiesProvider
from ._gate import gated

log = logging.getLogger(__name__)


def register(app: FastMCP, rhino: RhinoBridge, caps: CapabilitiesProvider) -> None:
    def _result(reply: dict[str, Any]) -> str:
        if reply.get("status") == "error":
            msg = reply.get("message") or reply.get("result") or "unknown"
            return f"Error: {msg}"
        payload = reply.get("result", reply)
        if isinstance(payload, str):
            return payload
        return json.dumps(payload, indent=2, default=str)

    @app.tool(name="rhino_status")
    @gated(caps, "rhino_status")
    def rhino_status() -> str:
        """Check whether the Rhino plugin's MCP service is reachable.

        Use this first to confirm Rhino is running with the plugin loaded.
        """
        if not rhino.ping():
            return f"Rhino bridge is not responding at {rhino.host}:{rhino.port}"
        try:
            return _result(rhino.send("is_server_available"))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="rhino_get_scene_info")
    @gated(caps, "rhino_get_scene_info")
    def rhino_get_scene_info() -> str:
        """Return a lightweight overview of the Rhino document.

        Includes layer list and a small sample of objects per layer.
        For full detail use rhino_get_objects_with_metadata.
        """
        try:
            return _result(rhino.send("get_scene_info"))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="rhino_get_layers")
    @gated(caps, "rhino_get_layers")
    def rhino_get_layers() -> str:
        """List all layers in the Rhino document."""
        try:
            return _result(rhino.send("get_layers"))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="rhino_get_objects_with_metadata")
    @gated(caps, "rhino_get_objects_with_metadata")
    def rhino_get_objects_with_metadata(
        filters: dict[str, Any] | None = None,
        metadata_fields: list[str] | None = None,
    ) -> str:
        """Return objects with their MCP metadata, filtered as requested.

        Args:
            filters: Dict supporting keys: layer (wildcards OK), name
                     (wildcards OK), short_id (exact match).
            metadata_fields: Optional projection — only these keys are returned.
        """
        try:
            return _result(
                rhino.send(
                    "get_objects_with_metadata",
                    filters=filters or {},
                    metadata_fields=metadata_fields,
                )
            )
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="rhino_list_blocks")
    @gated(caps, "rhino_list_blocks")
    def rhino_list_blocks() -> str:
        """List all block (InstanceDefinition) definitions in the Rhino document.

        Returns each block's name, id, object count, and update type. Useful
        before placing block instances or generating a parametric assembly.
        """
        try:
            return _result(rhino.send("list_blocks"))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="rhino_set_view")
    @gated(caps, "rhino_set_view")
    def rhino_set_view(name: str | None = None, standard: str | None = None) -> str:
        """Switch the active viewport to a named view or a standard projection.

        Use exactly one of `name` or `standard`. Standard projections:
        Top, Front, Right, Left, Back, Bottom, Perspective. The viewport is
        zoom-extents-ed after a standard projection change.

        Args:
            name: Named view (saved in the document) to restore.
            standard: One of Top|Front|Right|Left|Back|Bottom|Perspective.
        """
        if (name is None) == (standard is None):
            return "Error: pass exactly one of `name` or `standard`."
        payload: dict[str, Any] = {}
        if name is not None:
            payload["name"] = name
        if standard is not None:
            payload["standard"] = standard
        try:
            return _result(rhino.send("set_view", **payload))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="rhino_execute_code")
    @gated(caps, "rhino_execute_code")
    def rhino_execute_code(code: str) -> str:
        """Execute Python in Rhino (CPython 3 in Rhino 8).

        A helper `add_object_metadata(obj_id, name, description)` is injected
        into the namespace — call it after creating any object so it shows
        up in later rhino_get_objects_with_metadata queries.

        Args:
            code: Python source.
        """
        try:
            return _result(rhino.send("execute_code", code=code))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="rhino_run_named_command")
    @gated(caps, "rhino_run_named_command")
    def rhino_run_named_command(command: str, echo: bool = False) -> str:
        """Run a Rhino command by name (e.g. '_SelAll', '_BooleanUnion').

        This is the catch-all for native Rhino operations the plugin doesn't
        wrap with a typed tool.

        Args:
            command: Rhino command name including the leading underscore.
            echo: When True, echo the command to the command history.
        """
        try:
            return _result(rhino.send("run_named_command", command=command, echo=echo))
        except BridgeError as exc:
            return f"Error: {exc}"

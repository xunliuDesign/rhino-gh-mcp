"""Grasshopper script-component injection: Python 3, IronPython 2, C#.

The differentiating capability of this project. All four tools gate on the
`allow_scripting` capability — disabled by default since it can run
arbitrary code in Rhino's process.
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
    def _result(reply: dict[str, Any]) -> str:
        if reply.get("status") == "error":
            return f"Error: {reply.get('result') or reply.get('message') or 'unknown'}"
        return json.dumps(reply.get("result", reply), indent=2, default=str)

    @app.tool(name="gh_update_script")
    @gated(caps, "gh_update_script")
    def gh_update_script(
        instance_guid: str,
        code: str = "",
        description: str = "",
        message_to_user: str = "",
        param_definitions: list[dict[str, Any]] | None = None,
    ) -> str:
        """Update an existing Script component's code, description, and/or parameters.

        When param_definitions is provided, ALL parameters are redefined —
        include every input/output you want the component to have, even ones
        you're keeping. Existing wires may detach if names change.

        Args:
            instance_guid: GUID of the Script component.
            code: New source (IronPython 2 by default; use gh_write_script_py3
                  or gh_write_script_cs for the Rhino 8 Script component variants).
            description: New description string.
            message_to_user: Optional note surfaced as a runtime message.
            param_definitions: Optional full replacement of input/output params.
                Each entry is {"type": "input"|"output", "name": str, ...}.
        """
        try:
            return _result(
                gh.send(
                    "update_script",
                    instance_guid=instance_guid,
                    code=code,
                    description=description,
                    message_to_user=message_to_user,
                    param_definitions=param_definitions or [],
                )
            )
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_write_script_py2")
    @gated(caps, "gh_write_script_py2")
    def gh_write_script_py2(
        instance_guid: str,
        code: str,
        description: str = "",
        param_definitions: list[dict[str, Any]] | None = None,
    ) -> str:
        """Write IronPython 2.7 code into an existing GHPython component.

        IronPython 2 limitations: no f-strings, no walrus operator, no
        type hints, no PEP 604 unions. Use `result = value` to set outputs.

        Args:
            instance_guid: GUID of the GHPython component.
            code: IronPython 2.7 source.
            description: Optional component description.
            param_definitions: Optional full replacement of input/output params.
        """
        try:
            return _result(
                gh.send(
                    "update_script",
                    instance_guid=instance_guid,
                    code=code,
                    description=description,
                    message_to_user="",
                    param_definitions=param_definitions or [],
                    script_language="ironpython2",
                )
            )
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_write_script_py3")
    @gated(caps, "gh_write_script_py3")
    def gh_write_script_py3(
        instance_guid: str,
        code: str,
        description: str = "",
        param_definitions: list[dict[str, Any]] | None = None,
    ) -> str:
        """Write CPython 3 code into a Rhino 8 Script component.

        Rhino 8's Script component runs real CPython 3.9 — f-strings, type
        hints, modern syntax all OK. Use `result = value` to set outputs.

        NOTE: Bridge support for the Rhino 8 Script component lands in
        plugin v0.2.

        Args:
            instance_guid: GUID of the Script component.
            code: Python 3 source.
            description: Optional component description.
            param_definitions: Optional full replacement of input/output params.
        """
        try:
            return _result(
                gh.send(
                    "update_script",
                    instance_guid=instance_guid,
                    code=code,
                    description=description,
                    message_to_user="",
                    param_definitions=param_definitions or [],
                    script_language="python3",
                )
            )
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_write_script_cs")
    @gated(caps, "gh_write_script_cs")
    def gh_write_script_cs(
        instance_guid: str,
        code: str,
        description: str = "",
        param_definitions: list[dict[str, Any]] | None = None,
    ) -> str:
        """Write C# code into a Rhino 8 Script component.

        Use this for performance-critical or RhinoCommon-heavy work that
        doesn't fit comfortably in Python. Reference RhinoCommon as you would
        in a normal Script-component C# fragment.

        NOTE: Bridge support lands in plugin v0.2.

        Args:
            instance_guid: GUID of the Script component.
            code: C# source.
            description: Optional component description.
            param_definitions: Optional full replacement of input/output params.
        """
        try:
            return _result(
                gh.send(
                    "update_script",
                    instance_guid=instance_guid,
                    code=code,
                    description=description,
                    message_to_user="",
                    param_definitions=param_definitions or [],
                    script_language="csharp",
                )
            )
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_execute_code")
    @gated(caps, "gh_execute_code")
    def gh_execute_code(code: str) -> str:
        """Execute arbitrary IronPython 2.7 inside the running .gha context.

        Powerful: can add components, mutate the document, read evaluation
        state. Use sparingly — prefer high-level tools when one exists.

        Args:
            code: IronPython 2.7 source. Set `result = ...` to return data.
        """
        try:
            return _result(gh.send("execute_code", code=code))
        except BridgeError as exc:
            return f"Error: {exc}"

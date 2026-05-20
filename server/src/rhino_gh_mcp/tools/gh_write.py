"""Grasshopper write tools: place, wire, set, remove, recompute."""

from __future__ import annotations

import json
import logging
from typing import Any

from mcp.server.fastmcp import FastMCP

from ..bridges.grasshopper import BridgeError, GrasshopperBridge
from ..config import Config
from ..policies import Policy

log = logging.getLogger(__name__)


def register(app: FastMCP, gh: GrasshopperBridge, policy: Policy, config: Config) -> None:
    """Register Grasshopper write tools that the active policy allows."""

    def _gate(name: str) -> bool:
        if not policy.allows(name):
            log.debug("policy '%s' excludes %s", policy.name, name)
            return False
        return True

    def _result(reply: dict[str, Any]) -> str:
        if reply.get("status") == "error":
            return f"Error: {reply.get('result') or reply.get('message') or 'unknown'}"
        return json.dumps(reply.get("result", reply), indent=2, default=str)

    # --- Parameter writes (allowed at L1+) ---------------------------------

    if _gate("gh_set_slider"):

        @app.tool(name="gh_set_slider")
        def gh_set_slider(instance_guid: str, value: float) -> str:
            """Set the current value of a Number Slider.

            Args:
                instance_guid: GUID of the slider (from gh_list_sliders).
                value: New value. Will be clamped server-side to the slider's range.
            """
            try:
                return _result(
                    gh.send(
                        "set_component_parameter",
                        instance_guid=instance_guid,
                        param_name="",  # the slider itself
                        value=str(value),
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_set_component_parameter"):

        @app.tool(name="gh_set_component_parameter")
        def gh_set_component_parameter(
            instance_guid: str,
            param_name: str,
            value: str,
        ) -> str:
            """Set any input parameter on a component (panel, toggle, swatch, slider, etc.).

            The bridge picks the right widget mode based on the parameter type.

            Args:
                instance_guid: GUID of the component owning the parameter.
                param_name: Name or nickname of the parameter.
                value: New value as a string. Numeric/boolean strings are parsed
                       server-side.
            """
            try:
                return _result(
                    gh.send(
                        "set_component_parameter",
                        instance_guid=instance_guid,
                        param_name=param_name,
                        value=value,
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_recompute"):

        @app.tool(name="gh_recompute")
        def gh_recompute() -> str:
            """Trigger a full canvas recompute. Call after a batch of slider changes."""
            try:
                return _result(gh.send("recompute_all"))
            except BridgeError as exc:
                return f"Error: {exc}"

    # --- Topology writes (L2+) ---------------------------------------------

    if _gate("gh_add_component"):

        @app.tool(name="gh_add_component")
        def gh_add_component(
            component_name: str,
            position_x: int = 100,
            position_y: int = 100,
        ) -> str:
            """Add a component to the canvas by name or nickname.

            In CURATED policy, the .gha enforces the configured category
            allow-list — components outside the allow-list are rejected.

            Args:
                component_name: Exact name (e.g. 'Number Slider') or nickname.
                position_x: Canvas X coordinate.
                position_y: Canvas Y coordinate.
            """
            try:
                return _result(
                    gh.send(
                        "add_component_to_canvas",
                        component_name=component_name,
                        position_x=position_x,
                        position_y=position_y,
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_add_slider"):

        @app.tool(name="gh_add_slider")
        def gh_add_slider(
            name: str,
            min_value: float,
            max_value: float,
            value: float,
            position_x: int = 100,
            position_y: int = 100,
            integer: bool = False,
        ) -> str:
            """Add a Number Slider to the canvas.

            Args:
                name: Nickname for the slider.
                min_value: Slider minimum.
                max_value: Slider maximum.
                value: Initial value.
                position_x: Canvas X.
                position_y: Canvas Y.
                integer: When True, slider snaps to integers.
            """
            try:
                return _result(
                    gh.send(
                        "add_slider_to_canvas",
                        name=name,
                        min_value=min_value,
                        max_value=max_value,
                        value=value,
                        position_x=position_x,
                        position_y=position_y,
                        integer=integer,
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_set_slider_range"):

        @app.tool(name="gh_set_slider_range")
        def gh_set_slider_range(
            instance_guid: str,
            min_value: float,
            max_value: float,
        ) -> str:
            """Adjust the min/max range of an existing slider.

            Args:
                instance_guid: GUID of the slider.
                min_value: New minimum.
                max_value: New maximum.
            """
            try:
                return _result(
                    gh.send(
                        "set_slider_range",
                        instance_guid=instance_guid,
                        min_value=min_value,
                        max_value=max_value,
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_connect_components"):

        @app.tool(name="gh_connect_components")
        def gh_connect_components(
            source_guid: str,
            source_output: str,
            target_guid: str,
            target_input: str,
        ) -> str:
            """Connect a source output to a target input. Replaces any existing
            wire on the target input.

            Args:
                source_guid: GUID of the source component or slider.
                source_output: Output param name/nickname (use "" for sliders).
                target_guid: GUID of the target component.
                target_input: Input param name/nickname.
            """
            try:
                return _result(
                    gh.send(
                        "connect_components",
                        source_guid=source_guid,
                        source_output=source_output,
                        target_guid=target_guid,
                        target_input=target_input,
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

    if _gate("gh_remove_node"):

        @app.tool(name="gh_remove_node")
        def gh_remove_node(instance_guid: str) -> str:
            """Remove a component or floating param from the canvas.

            Args:
                instance_guid: GUID of the node to remove.
            """
            try:
                return _result(gh.send("remove_node", instance_guid=instance_guid))
            except BridgeError as exc:
                return f"Error: {exc}"

    # --- Full mode (L3) ----------------------------------------------------

    if _gate("gh_add_any_component"):

        @app.tool(name="gh_add_any_component")
        def gh_add_any_component(
            component_name: str,
            position_x: int = 100,
            position_y: int = 100,
        ) -> str:
            """L3 only: place any component, ignoring CategoryFilter.

            Functionally identical to gh_add_component but the name makes the
            intent (escape the curated allow-list) explicit in the trace.

            Args:
                component_name: Exact name or nickname.
                position_x: Canvas X.
                position_y: Canvas Y.
            """
            try:
                return _result(
                    gh.send(
                        "add_component_to_canvas",
                        component_name=component_name,
                        position_x=position_x,
                        position_y=position_y,
                        bypass_filter=True,
                    )
                )
            except BridgeError as exc:
                return f"Error: {exc}"

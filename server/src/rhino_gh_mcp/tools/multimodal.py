"""Multimodal tools: viewport capture, canvas screenshot, image utilities."""

from __future__ import annotations

import base64
import io
import logging
from typing import Any

from mcp.server.fastmcp import FastMCP, Image
from PIL import Image as PILImage

from ..bridges.grasshopper import BridgeError as GHBridgeError
from ..bridges.grasshopper import GrasshopperBridge
from ..bridges.rhino import BridgeError as RhinoBridgeError
from ..bridges.rhino import RhinoBridge
from ..capabilities import CapabilitiesProvider, denial_message
from ._gate import gated

log = logging.getLogger(__name__)


def register(
    app: FastMCP,
    gh: GrasshopperBridge,
    rhino: RhinoBridge,
    caps: CapabilitiesProvider,
) -> None:
    # Note: @gated returns a string on denial. The capture tools return
    # mcp.Image, so we can't directly use the same decorator. We do an
    # explicit capability check at the top, raising on denial (which MCP
    # surfaces as an error). The check is functionally equivalent.

    @app.tool(name="rhino_capture_viewport")
    def rhino_capture_viewport(
        layer: str | None = None,
        show_annotations: bool = True,
        max_size: int = 800,
    ) -> Image:
        """Capture the current Rhino viewport as a PNG.

        Args:
            layer: Optional layer name - only annotations on this layer are shown.
            show_annotations: When True, draw object short_ids over the image.
            max_size: Max width or height in pixels.
        """
        c = caps.current()
        if not c.allows("rhino_capture_viewport"):
            raise RuntimeError(denial_message("rhino_capture_viewport", c))
        try:
            reply = rhino.send(
                "capture_viewport",
                layer=layer,
                show_annotations=show_annotations,
                max_size=max_size,
            )
        except RhinoBridgeError as exc:
            raise RuntimeError(f"Rhino bridge error: {exc}") from exc

        return _normalize_image_reply(reply, "viewport")

    @app.tool(name="gh_capture_canvas")
    def gh_capture_canvas(max_size: int = 1200) -> Image:
        """Capture the current Grasshopper canvas as a PNG.

        Useful for the LLM to "see" the topology it just edited.

        Args:
            max_size: Max width or height in pixels.
        """
        c = caps.current()
        if not c.allows("gh_capture_canvas"):
            raise RuntimeError(denial_message("gh_capture_canvas", c))
        try:
            reply = gh.send("capture_canvas", max_size=max_size)
        except GHBridgeError as exc:
            raise RuntimeError(f"Grasshopper bridge error: {exc}") from exc

        return _normalize_image_reply(reply, "canvas")

    # Silence unused-import lint if needed.
    _ = gated


def _normalize_image_reply(reply: dict[str, Any], kind: str) -> Image:
    """Decode various server reply shapes into an mcp.Image.

    Supports two shapes:
    1. {"type": "image", "source": {"data": "<base64>"}}  (legacy Rhino bridge)
    2. {"status": "success", "result": {"data": "<base64>", "format": "png"}}
    """
    if reply.get("status") == "error":
        raise RuntimeError(f"{kind} capture error: {reply.get('result') or reply.get('message')}")

    b64: str | None = None
    fmt = "png"

    if reply.get("type") == "image":
        b64 = (reply.get("source") or {}).get("data")
    else:
        result = reply.get("result") or {}
        if isinstance(result, dict):
            b64 = result.get("data") or result.get("base64")
            fmt = result.get("format", "png")
        elif isinstance(result, str):
            b64 = result

    if not b64:
        raise RuntimeError(f"{kind} capture returned no image data: {reply!r}")

    raw = base64.b64decode(b64)
    img = PILImage.open(io.BytesIO(raw))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return Image(data=buf.getvalue(), format="png")

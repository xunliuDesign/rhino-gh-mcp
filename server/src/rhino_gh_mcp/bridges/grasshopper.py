"""HTTP bridge to the Grasshopper MCP Server component (.gha).

Wire protocol (kept compatible with the existing rhino_gh_mcpComponent.cs in
_archive project so the v0 .gha keeps working during the v1 rollout):

    POST http://127.0.0.1:<port>/
    Content-Type: application/json
    Body: {"type": "<command>", ...flat params}

Response (200 OK):
    {"status": "success" | "error", "result": <any>}
"""

from __future__ import annotations

import logging
from typing import Any

import httpx

log = logging.getLogger(__name__)

DEFAULT_TIMEOUT = 60.0


class BridgeError(RuntimeError):
    """Raised when the bridge can't reach Grasshopper or got an error response."""


class GrasshopperBridge:
    """Singleton-style HTTP client. Lazy-connects, recovers from failures."""

    def __init__(self, host: str = "127.0.0.1", port: int = 9999, timeout: float = DEFAULT_TIMEOUT):
        self.host = host
        self.port = port
        self.base_url = f"http://{host}:{port}"
        self._client: httpx.Client | None = None
        self._timeout = timeout

    def _client_or_new(self) -> httpx.Client:
        if self._client is None:
            self._client = httpx.Client(timeout=self._timeout)
        return self._client

    def ping(self) -> bool:
        """Quick health check. True if the component responded."""
        try:
            r = self._client_or_new().post(
                self.base_url, json={"type": "is_server_available"}, timeout=2.0
            )
            return r.status_code == 200
        except Exception as exc:
            log.debug("GH ping failed: %s", exc)
            return False

    def send(self, command: str, /, **params: Any) -> dict[str, Any]:
        """Send a command. Returns the parsed JSON response dict.

        Raises BridgeError on transport failure. Server-side errors are returned
        in the dict as {"status": "error", ...} — callers decide how to surface
        them to the LLM.
        """
        payload: dict[str, Any] = {"type": command, **params}
        log.debug("GH→ %s %s", command, list(params.keys()))
        try:
            r = self._client_or_new().post(self.base_url, json=payload)
            r.raise_for_status()
            data = r.json()
        except httpx.HTTPError as exc:
            self.close()
            raise BridgeError(
                f"Grasshopper bridge unreachable at {self.base_url}. "
                f"Start the MCP Server component in Grasshopper (Run=True). "
                f"Underlying error: {exc}"
            ) from exc
        except ValueError as exc:
            raise BridgeError(f"Non-JSON response from Grasshopper: {exc}") from exc

        if not isinstance(data, dict):
            raise BridgeError(f"Unexpected response shape from Grasshopper: {data!r}")
        return data

    def close(self) -> None:
        if self._client is not None:
            try:
                self._client.close()
            except Exception:
                pass
            self._client = None

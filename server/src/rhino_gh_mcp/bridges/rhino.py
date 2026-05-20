"""TCP-JSON bridge to the Rhino plugin (.rhp).

Wire protocol (kept compatible with the rhino_script_client/MCPService.cs
pattern in _archive project so the v0 plugin keeps working):

    Client → Server: JSON object terminated by EOF on the same connection.
    {"type": "<command>", "params": {...}}

    Server → Client: JSON object, may be large (viewport captures).
    {"status": "success" | "error", "result": <any>, "message": "..."}

The bridge auto-reconnects per request — Rhino's socket lifecycle is fragile
across long-lived connections, so a fresh socket per command is more reliable.
"""

from __future__ import annotations

import json
import logging
import socket
import time
from typing import Any

log = logging.getLogger(__name__)

DEFAULT_TIMEOUT = 30.0
RECV_BUFFER = 1024 * 1024  # 1 MB chunks


class BridgeError(RuntimeError):
    pass


class RhinoBridge:
    def __init__(self, host: str = "127.0.0.1", port: int = 9876, timeout: float = DEFAULT_TIMEOUT):
        self.host = host
        self.port = port
        self.timeout = timeout

    def ping(self) -> bool:
        """Quick TCP-level health check."""
        try:
            with socket.create_connection((self.host, self.port), timeout=2.0):
                return True
        except OSError as exc:
            log.debug("Rhino ping failed: %s", exc)
            return False

    def send(self, command: str, /, **params: Any) -> dict[str, Any]:
        """Send a command and read a single JSON response. New socket each call."""
        payload = {"type": command, "params": params}
        encoded = json.dumps(payload).encode("utf-8")
        log.debug("Rhino→ %s %s", command, list(params.keys()))

        try:
            with socket.create_connection((self.host, self.port), timeout=self.timeout) as sock:
                sock.settimeout(self.timeout)
                sock.sendall(encoded)
                try:
                    sock.shutdown(socket.SHUT_WR)
                except OSError:
                    pass
                return self._recv_json(sock)
        except (OSError, socket.timeout) as exc:
            raise BridgeError(
                f"Rhino bridge unreachable at {self.host}:{self.port}. "
                f"Make sure the Rhino plugin is loaded and the MCP service is on. "
                f"Underlying error: {exc}"
            ) from exc

    def _recv_json(self, sock: socket.socket) -> dict[str, Any]:
        buf = bytearray()
        deadline = time.monotonic() + self.timeout
        while True:
            if time.monotonic() > deadline:
                raise BridgeError(f"Timed out waiting for Rhino response after {self.timeout}s")
            try:
                chunk = sock.recv(RECV_BUFFER)
            except socket.timeout as exc:
                raise BridgeError("Socket timeout receiving Rhino response") from exc
            if not chunk:
                break
            buf.extend(chunk)
            try:
                data = json.loads(buf.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError):
                continue
            if not isinstance(data, dict):
                raise BridgeError(f"Unexpected response shape from Rhino: {data!r}")
            return data
        if not buf:
            raise BridgeError("Empty response from Rhino")
        raise BridgeError(f"Could not parse Rhino response: {buf[:200]!r}")

    def close(self) -> None:
        # No persistent socket to close; provided for symmetry with GrasshopperBridge.
        pass

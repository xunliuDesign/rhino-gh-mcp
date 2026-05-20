"""End-to-end smoke test for the Rhino .rhp bridge.

Prerequisites:
  1. Build + install the Rhino plugin once via plugins/rhino/reinstall.sh
  2. Open Rhino 8
  3. In Rhino's command line, type: _ToggleMcpService
     You should see "rhino-gh-mcp: MCP service running on 127.0.0.1:9876"

Usage:
    cd server
    uv run python ../examples/smoke_test_rhino.py
    uv run python ../examples/smoke_test_rhino.py --policy full   # exercise execute_code + run_named_command

What it does:
    1. is_server_available — reports plugin version + Rhino version
    2. get_layers
    3. get_scene_info (samples)
    4. get_objects_with_metadata (no filter)
    5. capture_viewport (writes PNG to /tmp)
    6. [full] execute_code (creates a Rhino.Geometry.Sphere via PythonScript,
       reads its volume, doesn't add to document — proves Python runs)
    7. [full] run_named_command (_SelAll then _SelNone)

Exits non-zero on any failure.
"""

from __future__ import annotations

import argparse
import base64
import sys
import tempfile
import time
from pathlib import Path

# Make the server package importable when running from repo root
THIS_DIR = Path(__file__).resolve().parent
SERVER_SRC = THIS_DIR.parent / "server" / "src"
if str(SERVER_SRC) not in sys.path:
    sys.path.insert(0, str(SERVER_SRC))

from rhino_gh_mcp.bridges.rhino import BridgeError, RhinoBridge  # noqa: E402

PASS = "\x1b[32mPASS\x1b[0m"
FAIL = "\x1b[31mFAIL\x1b[0m"
SKIP = "\x1b[33mSKIP\x1b[0m"


class Tally:
    def __init__(self) -> None:
        self.passed = 0
        self.failed = 0
        self.skipped = 0
        self.errors: list[str] = []

    def record(self, name: str, ok: bool, detail: str = "") -> None:
        if ok:
            self.passed += 1
            print(f"  {PASS}  {name}  {detail}")
        else:
            self.failed += 1
            self.errors.append(f"{name}: {detail}")
            print(f"  {FAIL}  {name}  {detail}")

    def skip(self, name: str, why: str) -> None:
        self.skipped += 1
        print(f"  {SKIP}  {name}  ({why})")

    def summary(self) -> int:
        total = self.passed + self.failed + self.skipped
        print()
        print(f"  {self.passed}/{total} passed, {self.failed} failed, {self.skipped} skipped")
        if self.errors:
            print()
            print("Failures:")
            for e in self.errors:
                print(f"  - {e}")
        return 0 if self.failed == 0 else 1


def _ok(reply: dict) -> bool:
    return isinstance(reply, dict) and reply.get("status") == "success"


def run(args: argparse.Namespace) -> int:
    bridge = RhinoBridge(port=args.port)
    tally = Tally()

    print(f"\nConnecting to Rhino bridge at {bridge.host}:{bridge.port}\n")

    # 1. Liveness + version
    print("[1] Liveness + version")
    tally.record("ping", bridge.ping(), f"{bridge.host}:{bridge.port}")
    try:
        reply = bridge.send("is_server_available")
        if _ok(reply):
            r = reply.get("result", {}) or {}
            detail = (
                f"plugin={r.get('plugin_name')} v{r.get('plugin_version')} "
                f"rhino={r.get('rhino_version')}"
            )
            tally.record("is_server_available", True, detail)
        else:
            tally.record("is_server_available", False, str(reply.get("message") or reply))
    except BridgeError as e:
        tally.record("is_server_available", False, str(e))
        print("\nBridge unreachable. Make sure:")
        print("  - The Rhino plugin is installed (plugins/rhino/reinstall.sh)")
        print("  - Rhino 8 is running")
        print("  - You ran the command:  _ToggleMcpService")
        return 2

    # 2. Layers
    print("\n[2] Document reads")
    reply = bridge.send("get_layers")
    n = len((reply.get("result") or {}).get("layers", [])) if _ok(reply) else 0
    tally.record("get_layers", _ok(reply), f"{n} layers")

    # 3. Scene info
    reply = bridge.send("get_scene_info")
    n_layers = len((reply.get("result") or {}).get("layers", [])) if _ok(reply) else 0
    tally.record("get_scene_info", _ok(reply), f"{n_layers} layers with samples")

    # 4. Objects with metadata
    reply = bridge.send("get_objects_with_metadata", filters={}, metadata_fields=None)
    n_obj = (reply.get("result") or {}).get("count", 0) if _ok(reply) else 0
    tally.record("get_objects_with_metadata", _ok(reply), f"{n_obj} objects matched")

    # 5. Viewport capture
    print("\n[3] Viewport capture")
    try:
        reply = bridge.send("capture_viewport", max_size=900)
        if _ok(reply):
            data = (reply.get("result") or {}).get("data")
            if data:
                tmp_path = Path(tempfile.gettempdir()) / f"rhino_gh_mcp_viewport_{int(time.time())}.png"
                tmp_path.write_bytes(base64.b64decode(data))
                tally.record("capture_viewport", True, f"saved to {tmp_path}")
            else:
                tally.record("capture_viewport", False, "no data in result")
        else:
            tally.record("capture_viewport", False, str(reply.get("message") or reply))
    except BridgeError as e:
        tally.record("capture_viewport", False, str(e))

    # 6/7. L3 extras
    if args.policy == "full":
        print("\n[4] L3 (full) extras")
        # execute_code: ensure Python runtime is alive
        reply = bridge.send(
            "execute_code",
            code=(
                "import Rhino.Geometry as rg\n"
                "s = rg.Sphere(rg.Point3d(0,0,0), 5.0)\n"
                "result = 'sphere r=%s volume=%.2f' % (s.Radius, (4.0/3.0) * 3.14159 * s.Radius**3)\n"
            ),
        )
        ok = _ok(reply)
        detail = str((reply.get("result") or {}).get("result", reply))[:120]
        tally.record("execute_code", ok, detail)

        # run_named_command
        reply = bridge.send("run_named_command", command="_SelAll", echo=False)
        tally.record("run_named_command _SelAll", _ok(reply), str(reply.get("result"))[:80])
        reply = bridge.send("run_named_command", command="_SelNone", echo=False)
        tally.record("run_named_command _SelNone", _ok(reply), str(reply.get("result"))[:80])

    return tally.summary()


def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--port", type=int, default=9876, help="Rhino bridge port (default 9876)")
    p.add_argument(
        "--policy",
        choices=["curated", "full"],
        default="curated",
        help="When 'full', exercises L3 commands (execute_code, run_named_command)",
    )
    return p.parse_args()


if __name__ == "__main__":
    sys.exit(run(_parse_args()))

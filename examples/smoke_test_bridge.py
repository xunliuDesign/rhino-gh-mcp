"""End-to-end smoke test: hits every supported bridge command against a live
Grasshopper canvas. Run this with the MCP Server (v1) component on the canvas
and Run = True. Does NOT need Claude Desktop, doesn't need the MCP server
process either — just the .gha and the Python bridge module.

Usage:
    cd server
    uv run python ../examples/smoke_test_bridge.py
    uv run python ../examples/smoke_test_bridge.py --policy full  # try L3 stuff too

What it does:
    1. ping + is_server_available
    2. get_context (counts components)
    3. add_slider_to_canvas, add_component_to_canvas (Circle)
    4. connect_components (slider -> Circle.R)
    5. set_component_parameter (set slider value)
    6. set_slider_range
    7. recompute_all + get_runtime_messages
    8. capture_canvas (writes PNG to /tmp)
    9. remove_node (cleanup)
   10. [full] add_component_to_canvas with bypass_filter
   11. [full] execute_code

Prints a one-line PASS/FAIL per step. Exits non-zero on any failure.

If a step fails, the script keeps going (so you see the full picture) but the
final exit code reflects all failures.
"""

from __future__ import annotations

import argparse
import base64
import json
import sys
import tempfile
import time
import uuid
from pathlib import Path

# Make the server package importable when running from repo root
THIS_DIR = Path(__file__).resolve().parent
SERVER_SRC = THIS_DIR.parent / "server" / "src"
if str(SERVER_SRC) not in sys.path:
    sys.path.insert(0, str(SERVER_SRC))

from rhino_gh_mcp.bridges.grasshopper import BridgeError, GrasshopperBridge  # noqa: E402

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
    bridge = GrasshopperBridge(port=args.port)
    tally = Tally()
    created_guids: list[str] = []

    print(f"\nConnecting to Grasshopper bridge at {bridge.base_url}\n")

    # 1. ping
    print("[1] Liveness")
    tally.record("ping", bridge.ping(), bridge.base_url)
    try:
        reply = bridge.send("is_server_available")
        tally.record("is_server_available", _ok(reply), str(reply.get("result")))
    except BridgeError as e:
        tally.record("is_server_available", False, str(e))
        print("\nBridge unreachable. Make sure:")
        print(f"  - Grasshopper has the MCP Server (v1) component on the canvas")
        print(f"  - The component's Run toggle is True")
        print(f"  - Status output reads 'Listening on 127.0.0.1:{args.port}'")
        return 2

    # 2. context
    print("\n[2] Read canvas")
    reply = bridge.send("get_context", simplified=True)
    n = len((reply.get("result") or {}))
    tally.record("get_context", _ok(reply), f"{n} canvas objects")

    # 3. add slider + component
    print("\n[3] Place + wire")
    slider_nick = f"smoke_{uuid.uuid4().hex[:6]}"
    reply = bridge.send(
        "add_slider_to_canvas",
        name=slider_nick,
        min_value=1.0,
        max_value=50.0,
        value=5.0,
        position_x=100,
        position_y=100,
        integer=False,
    )
    tally.record("add_slider_to_canvas", _ok(reply), str(reply.get("result")))

    # Find the slider GUID via get_context (since add_slider doesn't return it)
    ctx = bridge.send("get_context", simplified=True).get("result") or {}
    slider_guid = next(
        (g for g, info in ctx.items() if info.get("nickName") == slider_nick),
        None,
    )
    if slider_guid:
        created_guids.append(slider_guid)
        tally.record("locate_added_slider", True, slider_guid)
    else:
        tally.record("locate_added_slider", False, "could not find by nickname")

    # Try to add a Circle component. CategoryFilter must include "Curve" or
    # bypass_filter must be allowed. We try without bypass first; if that
    # fails we know the filter is restrictive — fall back to bypass.
    reply = bridge.send(
        "add_component_to_canvas",
        component_name="Circle",
        position_x=350,
        position_y=100,
    )
    if not _ok(reply):
        reply = bridge.send(
            "add_component_to_canvas",
            component_name="Circle",
            position_x=350,
            position_y=100,
            bypass_filter=True,
        )
        tally.record("add_component_to_canvas (bypass_filter)", _ok(reply), str(reply.get("result")))
    else:
        tally.record("add_component_to_canvas", _ok(reply), str(reply.get("result")))

    # Locate the Circle GUID
    ctx = bridge.send("get_context", simplified=True).get("result") or {}
    circle_guid = None
    for g, info in ctx.items():
        if info.get("name") == "Circle" and g not in created_guids and g != slider_guid:
            circle_guid = g
            break
    if circle_guid:
        created_guids.append(circle_guid)
        tally.record("locate_added_circle", True, circle_guid)
    else:
        tally.skip("locate_added_circle", "Circle not placed — wiring tests will skip")

    # 4. connect
    if slider_guid and circle_guid:
        reply = bridge.send(
            "connect_components",
            source_guid=slider_guid,
            source_output="",  # slider itself
            target_guid=circle_guid,
            target_input="R",
        )
        tally.record("connect_components", _ok(reply), str(reply.get("result")))
    else:
        tally.skip("connect_components", "missing slider or circle")

    # 5. set component parameter (value on the slider)
    if slider_guid:
        reply = bridge.send(
            "set_component_parameter",
            instance_guid=slider_guid,
            param_name="",
            value="27.5",
        )
        tally.record("set_component_parameter (slider value)", _ok(reply), str(reply.get("result"))[:80])

    # 6. set slider range  (v0.1.1 NEW)
    if slider_guid:
        reply = bridge.send(
            "set_slider_range",
            instance_guid=slider_guid,
            min_value=0.0,
            max_value=100.0,
        )
        tally.record("set_slider_range", _ok(reply), str(reply.get("result"))[:80])

    # 7. recompute + runtime messages
    print("\n[4] Recompute + introspection")
    reply = bridge.send("recompute_all")
    tally.record("recompute_all", _ok(reply), str(reply.get("result")))

    if circle_guid:
        reply = bridge.send("get_runtime_messages", instance_guid=circle_guid)
        ok = _ok(reply)
        msgs = (reply.get("result") or {}).get("runtime_messages", []) if ok else []
        tally.record("get_runtime_messages", ok, f"{len(msgs)} message(s)")

    # 8. capture canvas
    print("\n[5] Canvas capture")
    try:
        reply = bridge.send("capture_canvas", max_size=900)
        if _ok(reply):
            data = (reply.get("result") or {}).get("data")
            if data:
                tmp_path = Path(tempfile.gettempdir()) / f"rhino_gh_mcp_canvas_{int(time.time())}.png"
                tmp_path.write_bytes(base64.b64decode(data))
                tally.record("capture_canvas", True, f"saved to {tmp_path}")
            else:
                tally.record("capture_canvas", False, "no data in result")
        else:
            tally.record("capture_canvas", False, str(reply.get("result")))
    except BridgeError as e:
        tally.record("capture_canvas", False, str(e))

    # 9. policy / full-mode extras
    if args.policy == "full":
        print("\n[6] L3 (full) extras")
        # execute_code: light touch — just print canvas object count from IronPython
        reply = bridge.send(
            "execute_code",
            code=(
                "doc = ghenv.Component.OnPingDocument()\n"
                "result = 'canvas has %d objects' % len(list(doc.Objects))"
            ),
        )
        tally.record("execute_code", _ok(reply), str(reply.get("result"))[:80])

    # 10. cleanup — remove what we created
    print("\n[7] Cleanup")
    for g in created_guids:
        reply = bridge.send("remove_node", instance_guid=g)
        tally.record(f"remove_node {g[:8]}", _ok(reply), str(reply.get("result"))[:60])

    return tally.summary()


def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--port", type=int, default=9999, help="GH bridge port (default 9999)")
    p.add_argument(
        "--policy",
        choices=["curated", "full"],
        default="curated",
        help="When 'full', exercises L3-only commands like execute_code",
    )
    return p.parse_args()


if __name__ == "__main__":
    sys.exit(run(_parse_args()))

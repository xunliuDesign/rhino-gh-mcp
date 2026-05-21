"""Verify v0.1.5 capability gate: canvas-side flags actually deny calls.

Steps:
  1. Confirm .gha v0.1.5 (get_capabilities should respond).
  2. Read the live capability state from the canvas.
  3. Try a scripting call (gh_execute_code) - should be denied if AllowScripting=False.
  4. Try a read call (gh_status) - should always succeed.
  5. Try a component call (placing a slider) - succeeds if AllowComponents=True.

Run from server/ with both bridges up:
    uv run python ..\\examples\\verify_capability_gate.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "server" / "src"))

from rhino_gh_mcp.bridges.grasshopper import GrasshopperBridge


def main() -> int:
    gh = GrasshopperBridge()
    if not gh.ping():
        print("FAIL: GH bridge not reachable on 9999"); return 1

    print("[1] get_capabilities probe")
    caps_reply = gh.send("get_capabilities")
    if caps_reply.get("status") != "success":
        print(f"  FAIL: {caps_reply}")
        print("  (likely talking to a pre-v0.1.5 .gha - reinstall + restart Rhino)")
        return 2
    caps = caps_reply["result"]
    print(f"  plugin_version    = {caps.get('plugin_version')}")
    print(f"  allow_parameters  = {caps.get('allow_parameters')}")
    print(f"  allow_components  = {caps.get('allow_components')}")
    print(f"  allow_scripting   = {caps.get('allow_scripting')}")
    print(f"  component_scope   = {caps.get('component_scope')}")
    print(f"  category_filter   = {caps.get('category_filter')}")

    print("\n[2] Read tool (gh_status / is_server_available) - always allowed")
    r = gh.send("is_server_available")
    print(f"  -> {r.get('status')}: {(r.get('result') or {}).get('plugin_version')}")

    print("\n[3] Scripting tool (execute_code) - gated by AllowScripting")
    r = gh.send("execute_code", code="result = 1 + 1")
    status = r.get("status")
    print(f"  -> status={status}")
    print(f"     result={r.get('result')}")
    if not caps.get("allow_scripting"):
        if status == "error" and "AllowScripting" in str(r.get("result", "")):
            print("  [PASS] Cleanly denied with knob-name in message")
        else:
            print("  [FAIL] Expected a clean denial naming AllowScripting")
    else:
        if status == "success":
            print("  [PASS] AllowScripting is on, call succeeded")
        else:
            print(f"  [FAIL] AllowScripting is on but call failed: {r}")

    print("\n[4] Component tool (add_slider_to_canvas) - gated by AllowComponents")
    r = gh.send("add_slider_to_canvas", name="cap_probe", min_value=0, max_value=1,
                value=0.5, position_x=900, position_y=400, integer=False)
    status = r.get("status")
    print(f"  -> status={status}")
    if caps.get("allow_components"):
        if status == "success":
            print("  [PASS] AllowComponents on, placed slider")
            # cleanup - find and remove
            ctx = gh.send("get_context", simplified=True).get("result") or {}
            for guid, info in ctx.items():
                if (info.get("nickName") or "") == "cap_probe":
                    gh.send("remove_node", instance_guid=guid)
                    print(f"  cleanup: removed {guid}")
                    break
        else:
            print(f"  [FAIL] {r}")
    else:
        if status == "error" and "AllowComponents" in str(r.get("result", "")):
            print("  [PASS] Cleanly denied with knob-name in message")
        else:
            print(f"  [FAIL] Expected clean denial: {r}")

    print("\nDone.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

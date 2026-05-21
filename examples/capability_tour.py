"""Hit a useful subset of bridge commands against the live Rhino+GH and
print what each returns. A "what can this thing do, right now" demo.

Run from server/ with both bridges up:
    uv run python ..\\examples\\capability_tour.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "server" / "src"))

from rhino_gh_mcp.bridges.grasshopper import GrasshopperBridge
from rhino_gh_mcp.bridges.rhino import RhinoBridge


def section(title: str) -> None:
    print()
    print("=" * 72)
    print(title)
    print("=" * 72)


def show(label: str, reply: dict) -> None:
    payload = reply.get("result", reply)
    if isinstance(payload, (dict, list)):
        s = json.dumps(payload, indent=2, default=str)
        if len(s) > 1200:
            s = s[:1200] + "\n  ... (truncated)"
        print(f"\n>> {label}\n{s}")
    else:
        print(f"\n>> {label}\n{payload}")


def main() -> int:
    gh = GrasshopperBridge()
    rh = RhinoBridge()
    if not gh.ping():
        print("GH bridge not reachable on 9999"); return 1
    if not rh.ping():
        print("Rhino bridge not reachable on 9876"); return 1

    section("A. Status / identity")
    show("Grasshopper bridge identity", gh.send("is_server_available"))
    show("Rhino bridge identity", rh.send("is_server_available"))

    section("B. Read the canvas")
    show("gh_canvas_summary (digest)", _canvas_summary(gh))

    section("C. Read the Rhino scene")
    show("rhino_get_layers", rh.send("get_layers"))
    show("rhino_list_blocks", rh.send("list_blocks"))

    section("D. Drive the viewport")
    show("rhino_set_view standard=Perspective", rh.send("set_view", standard="Perspective"))
    cap = rh.send("capture_viewport", max_size=400)
    if cap.get("status") == "success":
        r = cap["result"]
        print(f"\n>> rhino_capture_viewport -> {r['width']}x{r['height']} png, {len(r['data'])} chars b64")
    else:
        print(f"\n>> rhino_capture_viewport FAILED: {cap}")

    section("E. End-to-end widget workflow on a fresh canvas region")
    cleanup: list[str] = []
    try:
        sl = gh.send("add_slider_to_canvas", name="tour_radius",
                     min_value=1, max_value=20, value=5,
                     position_x=900, position_y=50, integer=False)
        sl_guid = _find(gh, "slider", "tour_radius")
        cleanup.append(sl_guid)
        tg = gh.send("add_component_to_canvas",
                     component_name="Boolean Toggle",
                     position_x=900, position_y=120, bypass_filter=True)
        tg_guid = _find(gh, "booleantoggle")
        cleanup.append(tg_guid)
        vl = gh.send("add_component_to_canvas",
                     component_name="Value List",
                     position_x=900, position_y=200, bypass_filter=True)
        vl_guid = _find(gh, "valuelist")
        cleanup.append(vl_guid)
        print(f"  placed: slider={sl_guid}, toggle={tg_guid}, value_list={vl_guid}")

        show("set toggle TRUE", gh.send("set_toggle_value", instance_guid=tg_guid, value=True))
        show("select value list by name 'Three'",
             gh.send("set_value_list_selection", instance_guid=vl_guid, item="Three"))
        show("list_toggles (synthesized from get_context)", _list_toggles(gh))
        show("list_value_lists", _list_value_lists(gh))
    finally:
        section("F. Cleanup")
        for guid in cleanup:
            if guid:
                r = gh.send("remove_node", instance_guid=guid)
                print(f"  removed {guid}: {r.get('status')}")

    print("\nDone.\n")
    return 0


def _canvas_summary(gh: GrasshopperBridge) -> dict:
    """Mirror of gh_canvas_summary's Python logic - so we can call it without
    routing through FastMCP just for this demo."""
    reply = gh.send("get_context", simplified=True)
    if reply.get("status") == "error":
        return reply
    ctx = reply.get("result") or {}
    kinds: dict[str, int] = {}
    widgets: list[dict] = []
    errors = warnings = 0
    for guid, info in ctx.items():
        kind = info.get("kind") or info.get("name") or "Unknown"
        kinds[kind] = kinds.get(kind, 0) + 1
        for m in (info.get("runtimeMessages") or []):
            lvl = (m.get("level") if isinstance(m, dict) else str(m)) or ""
            if "error" in lvl.lower(): errors += 1; break
            if "warning" in lvl.lower(): warnings += 1; break
        nick = info.get("nickName") or info.get("name")
        kl = (info.get("kind") or "").lower().replace("_", "")
        for tag, key in (("slider","slider"), ("toggle","booleantoggle"),
                         ("value_list","valuelist"), ("panel","panel")):
            if key in kl:
                widgets.append({"guid": guid, "kind": tag, "nickname": nick})
                break
    return {"status": "success", "result": {
        "total_objects": len(ctx),
        "components_by_kind": dict(sorted(kinds.items(), key=lambda kv: (-kv[1], kv[0]))),
        "widgets": widgets,
        "components_with_errors": errors,
        "components_with_warnings": warnings,
    }}


def _list_toggles(gh: GrasshopperBridge) -> dict:
    ctx = gh.send("get_context", simplified=True).get("result") or {}
    out = []
    for guid, info in ctx.items():
        if "booleantoggle" in (info.get("kind") or "").lower().replace("_", ""):
            out.append({"guid": guid, "nickname": info.get("nickName") or info.get("name"),
                        "value": info.get("value")})
    return {"status": "success", "result": out}


def _list_value_lists(gh: GrasshopperBridge) -> dict:
    ctx = gh.send("get_context", simplified=True).get("result") or {}
    out = []
    for guid, info in ctx.items():
        if "valuelist" in (info.get("kind") or "").lower().replace("_", ""):
            out.append({"guid": guid, "nickname": info.get("nickName") or info.get("name"),
                        "items": info.get("items") or [],
                        "selected": info.get("selectedItems") or []})
    return {"status": "success", "result": out}


def _find(gh: GrasshopperBridge, kind_substr: str, nickname: str | None = None):
    ctx = gh.send("get_context", simplified=True).get("result") or {}
    matches = []
    for guid, info in ctx.items():
        k = (info.get("kind") or "").lower().replace("_", "")
        if kind_substr in k:
            if nickname and info.get("nickName") != nickname: continue
            matches.append(guid)
    return matches[-1] if matches else None


if __name__ == "__main__":
    sys.exit(main())

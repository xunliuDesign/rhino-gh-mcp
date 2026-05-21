"""Verify v0.1.3+ .gha extensions end-to-end:

  - v0.1.3: GetParamInfo emits slider value/min/max, toggle value,
    value-list items/selectedItems.
  - v0.1.4: set_toggle_value + set_value_list_selection write paths work.
  - .rhp v0.1.1 (when --rhino is set): list_blocks + set_view both succeed.

Adds widgets, exercises read + write, verifies, cleans up.

Run from the repo's `server/` dir:
    uv run python ..\\examples\\verify_widget_values.py            # GH only
    uv run python ..\\examples\\verify_widget_values.py --rhino    # GH + Rhino
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "server" / "src"))

from rhino_gh_mcp.bridges.grasshopper import GrasshopperBridge
from rhino_gh_mcp.bridges.rhino import RhinoBridge


def main(check_rhino: bool = False) -> int:
    gh = GrasshopperBridge()
    if not gh.ping():
        print("FAIL: GH bridge not reachable on 9999")
        return 1

    added: list[str] = []
    failures = 0
    passes = 0

    def expect(label: str, cond: bool, detail: str = "") -> None:
        nonlocal failures, passes
        status = "PASS" if cond else "FAIL"
        print(f"  [{status}] {label}{(' - ' + detail) if detail else ''}")
        if cond:
            passes += 1
        else:
            failures += 1

    print("[1] Add a slider, check value/min/max fields")
    slider = gh.send(
        "add_slider_to_canvas",
        name="probe_slider",
        min_value=0,
        max_value=10,
        value=4.2,
        position_x=50,
        position_y=50,
        integer=False,
    )
    slider_guid = _find_by_kind(gh, "slider", "probe_slider")
    if slider_guid:
        added.append(slider_guid)
    print(f"    slider guid: {slider_guid}")

    ctx = (gh.send("get_context", simplified=True).get("result") or {}) if slider_guid else {}
    info = ctx.get(slider_guid) or {}
    expect("slider.value populated", info.get("value") is not None, f"value={info.get('value')!r}")
    expect("slider.min populated", info.get("min") is not None, f"min={info.get('min')!r}")
    expect("slider.max populated", info.get("max") is not None, f"max={info.get('max')!r}")

    print("\n[2] Add a Boolean Toggle via add_component_to_canvas, check value field")
    toggle_reply = gh.send(
        "add_component_to_canvas",
        component_name="Boolean Toggle",
        position_x=50,
        position_y=120,
        bypass_filter=True,
    )
    toggle_guid = _find_by_kind(gh, "booleantoggle")
    if toggle_guid:
        added.append(toggle_guid)
    print(f"    toggle guid: {toggle_guid}    add_reply: {toggle_reply.get('status')}")
    ctx = gh.send("get_context", simplified=True).get("result") or {}
    info = ctx.get(toggle_guid) or {}
    expect(
        "toggle.value populated (bool)",
        isinstance(info.get("value"), bool),
        f"value={info.get('value')!r}",
    )

    print("\n[3] Add a Value List, check items + selectedItems")
    vl_reply = gh.send(
        "add_component_to_canvas",
        component_name="Value List",
        position_x=50,
        position_y=200,
        bypass_filter=True,
    )
    vl_guid = _find_by_kind(gh, "valuelist")
    if vl_guid:
        added.append(vl_guid)
    print(f"    value-list guid: {vl_guid}   add_reply: {vl_reply.get('status')}")
    ctx = gh.send("get_context", simplified=True).get("result") or {}
    info = ctx.get(vl_guid) or {}
    expect(
        "value_list.items populated",
        isinstance(info.get("items"), list),
        f"items={info.get('items')!r}",
    )
    expect(
        "value_list.selectedItems populated",
        isinstance(info.get("selectedItems"), list),
        f"selectedItems={info.get('selectedItems')!r}",
    )

    print("\n[4] v0.1.4: write paths (set_toggle_value, set_value_list_selection)")
    if toggle_guid:
        r = gh.send("set_toggle_value", instance_guid=toggle_guid, value=True)
        expect("set_toggle_value -> True", r.get("status") == "success", str(r.get("result")))
        ctx = gh.send("get_context", simplified=True).get("result") or {}
        expect("toggle.value reads back True", (ctx.get(toggle_guid) or {}).get("value") is True)
    if vl_guid:
        r = gh.send("set_value_list_selection", instance_guid=vl_guid, item=1)
        expect("set_value_list_selection by index=1", r.get("status") == "success", str(r.get("result")))
        ctx = gh.send("get_context", simplified=True).get("result") or {}
        selected = (ctx.get(vl_guid) or {}).get("selectedItems") or []
        expect("value_list selection reflects index 1", len(selected) == 1, f"selected={selected}")
        r2 = gh.send("set_value_list_selection", instance_guid=vl_guid, item="Three")
        expect(
            "set_value_list_selection by name 'Three'",
            r2.get("status") == "success",
            str(r2.get("result")),
        )

    if check_rhino:
        print("\n[5] Rhino .rhp v0.1.1: list_blocks + set_view")
        rh = RhinoBridge()
        if not rh.ping():
            print("  [FAIL] Rhino bridge not reachable on 9876")
            failures += 1
        else:
            blocks = rh.send("list_blocks")
            expect(
                "list_blocks returns success",
                blocks.get("status") == "success",
                f"count={(blocks.get('result') or {}).get('count')}",
            )
            for proj in ("Top", "Front", "Perspective"):
                r = rh.send("set_view", standard=proj)
                expect(f"set_view standard={proj}", r.get("status") == "success", str(r.get("result")))
            bad = rh.send("set_view", standard="Bogus")
            expect("set_view rejects unknown standard", bad.get("status") == "error")

    print("\n[6] Cleanup")
    for guid in added:
        r = gh.send("remove_node", instance_guid=guid)
        print(f"    removed {guid}: {r.get('status')}")

    total = passes + failures
    print(f"\n  {passes}/{total} checks passed, {failures} failed")
    return 0 if failures == 0 else 2


def _find_by_kind(gh: GrasshopperBridge, kind_substr: str, nickname: str | None = None) -> str | None:
    """Scan get_context for a top-level widget matching `kind_substr` (substring of `kind`)."""
    ctx = gh.send("get_context", simplified=True).get("result") or {}
    matches = []
    for guid, info in ctx.items():
        k = (info.get("kind") or "").lower().replace("_", "")
        if kind_substr in k:
            if nickname and info.get("nickName") != nickname:
                continue
            matches.append(guid)
    return matches[-1] if matches else None


if __name__ == "__main__":
    sys.exit(main(check_rhino="--rhino" in sys.argv))

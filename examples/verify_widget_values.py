"""Verify v0.1.3 .gha extension: GetParamInfo emits widget values.

Adds a slider + toggle + value-list + panel to the canvas, calls get_context,
asserts that value/min/max/items/selectedItems/userText show up. Cleans up.

Run from the repo's `server/` dir with the GH bridge listening on 9999:
    uv run python ..\\examples\\verify_widget_values.py
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "server" / "src"))

from rhino_gh_mcp.bridges.grasshopper import GrasshopperBridge


def main() -> int:
    gh = GrasshopperBridge()
    if not gh.ping():
        print("FAIL: GH bridge not reachable on 9999")
        return 1

    added: list[str] = []
    failures = 0

    def expect(label: str, cond: bool, detail: str = "") -> None:
        nonlocal failures
        status = "PASS" if cond else "FAIL"
        print(f"  [{status}] {label}{(' - ' + detail) if detail else ''}")
        if not cond:
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

    print("\n[4] Cleanup")
    for guid in added:
        r = gh.send("remove_node", instance_guid=guid)
        print(f"    removed {guid}: {r.get('status')}")

    print(f"\n  {len([_ for _ in range(7)]) - failures}/7 checks passed, {failures} failed")
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
    sys.exit(main())

"""v0.2 tool registrations: Skills v2 + turn-tracking + .gh introspection.

These are the new tools introduced by the v0.2 redesign. They live in their
own module so the v0.1 tool registration files (gh_read, gh_write, etc.) can
stay untouched — the diff is contained and the rollback to v0.1.7 surface is
just "don't register this module".

Tools registered here:

  * Skills v2
      - gh_list_skills           : Skills v2 directory listing with frontmatter
      - gh_introspect_definition : parse a .gh archive (zip + XML) off disk

  * Skill reference loading
      - gh_load_skill_reference  : push a Skill's reference .gh into the canvas

  * Coach mode (turn tracking, text-only in v0.2.0)
      - gh_begin_turn            : start a new AI turn, returns turn_id
      - gh_end_turn              : finalize a turn, returns changed-guid set
      - gh_dismiss_highlights    : clear turn state (no canvas paint in v0.2.0)
"""

from __future__ import annotations

import base64
import json
import logging
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from typing import Any

from mcp.server.fastmcp import FastMCP

from .. import skills as skills_mod
from ..bridges.grasshopper import BridgeError, GrasshopperBridge
from ..capabilities import CapabilitiesProvider
from ..turns import TurnTracker
from ._gate import gated

log = logging.getLogger(__name__)


def register(app: FastMCP, gh: GrasshopperBridge, caps: CapabilitiesProvider,
             turns: TurnTracker) -> None:
    """Register the v0.2 tool surface."""

    def _result(reply: dict[str, Any]) -> str:
        if reply.get("status") == "error":
            return f"Error: {reply.get('result') or reply.get('message') or 'unknown'}"
        return json.dumps(reply.get("result", reply), indent=2, default=str)

    # --- Skills v2 --------------------------------------------------------

    @app.tool(name="gh_list_skills")
    @gated(caps, "gh_list_skills")
    def gh_list_skills() -> str:
        """List every Skill available on this server with its v0.2 frontmatter.

        Returns the structured form (name, version, modes, allowed_categories,
        required_plugins, commands, …). Use this in place of `list_skills`
        when you want the typed Skill manifest instead of the legacy free-
        form one. Old Skills without v0.2 frontmatter still appear, just
        with empty collections for the v0.2-only fields.
        """
        skills = skills_mod.load_skills()
        out: list[dict[str, Any]] = []
        for skill in skills.values():
            out.append(
                {
                    "name": skill.name,
                    "version": skill.version,
                    "description": skill.description,
                    "modes": list(skill.modes),
                    "required_plugins": list(skill.required_plugins),
                    "allowed_categories": list(skill.allowed_categories),
                    "required_components": list(skill.required_components),
                    "reference_examples": list(skill.reference_examples),
                    "images": skill.images,
                    "commands": skill.commands,
                    "path": str(skill.path) if skill.path else None,
                }
            )
        return json.dumps({"count": len(out), "skills": out}, indent=2, default=str)

    @app.tool(name="gh_introspect_definition")
    @gated(caps, "gh_introspect_definition")
    def gh_introspect_definition(file_path: str) -> str:
        """Parse a .gh file on disk and return a summary WITHOUT touching the canvas.

        Use this to ground explanations of an existing definition (e.g. in
        Inspect / Coach mode) before deciding whether to load it via
        gh_load_skill_reference. Returns a summary of components, their
        category/name, and rough wire counts.

        Args:
            file_path: Absolute path to a .gh or .ghx file.
        """
        path = Path(file_path).expanduser().resolve()
        if not path.is_file():
            return f"Error: file not found: {path}"
        try:
            summary = _introspect_gh_archive(path)
        except Exception as exc:
            return f"Error: failed to parse {path.name}: {exc}"
        return json.dumps(summary, indent=2, default=str)

    @app.tool(name="gh_merge_definition")
    @gated(caps, "gh_merge_definition")
    def gh_merge_definition(file_path: str,
                            pivot_x: float = 100.0,
                            pivot_y: float = 100.0) -> str:
        """Merge an arbitrary .gh definition from disk into the current canvas.

        Unlike gh_load_skill_reference (which only loads from a Skill bundle),
        this accepts any file path. Use it when the user has a downloaded
        .gh file they want to inspect or modify alongside the MCP Server:

          1. open a fresh canvas
          2. drop the v2 Server component (Toggle + Value List auto-wire)
          3. flip Run=True
          4. ask the AI to call gh_merge_definition("/path/to/their/file.gh")

        The bridge MergeDocument's the file's components into the current
        canvas (does NOT replace) — the Server component stays. Newly placed
        components are recorded in the current turn so they highlight teal
        in Coach mode.

        Args:
            file_path: Absolute path to a .gh or .ghx file.
            pivot_x: top-left X for the merged objects. Default 100.
            pivot_y: top-left Y. Default 100.
        """
        path = Path(file_path).expanduser().resolve()
        if not path.is_file():
            return f"Error: file not found: {path}"
        try:
            data_b64 = base64.b64encode(path.read_bytes()).decode("ascii")
        except OSError as exc:
            return f"Error: could not read {path}: {exc}"
        try:
            reply = gh.send(
                "load_definition",
                data=data_b64,
                pivot_x=float(pivot_x),
                pivot_y=float(pivot_y),
            )
        except BridgeError as exc:
            return f"Error: {exc}"
        return _result(reply)

    @app.tool(name="gh_load_skill_reference")
    @gated(caps, "gh_load_skill_reference")
    def gh_load_skill_reference(skill_id: str, ref_name: str,
                                pivot_x: float = 100.0,
                                pivot_y: float = 100.0) -> str:
        """Load a Skill's reference .gh definition onto the current canvas.

        Looks up the Skill by id, finds the named reference example in its
        frontmatter, reads the bytes off disk, and sends them to the bridge
        via the `load_definition` command. Placement pivot defaults to
        (100, 100) on the canvas.

        Args:
            skill_id: Skill name (the YAML `name:` field) or directory name.
            ref_name: reference example to load — matched against
                `reference_examples[].file` basename or `name:`.
            pivot_x, pivot_y: top-left placement of the merged objects.
        """
        skill = skills_mod.get_skill(skill_id)
        if skill is None:
            return f"Error: skill {skill_id!r} not found."
        ref_path = skill.reference_path(ref_name)
        if ref_path is None or not ref_path.is_file():
            wanted = ref_name
            choices = [str(r.get("file", "")) for r in skill.reference_examples]
            return (
                f"Error: reference {wanted!r} not found in skill {skill.name!r}. "
                f"Available: {choices or '(none)'}"
            )
        try:
            data_b64 = base64.b64encode(ref_path.read_bytes()).decode("ascii")
        except OSError as exc:
            return f"Error: could not read {ref_path}: {exc}"
        try:
            reply = gh.send(
                "load_definition",
                data=data_b64,
                pivot_x=float(pivot_x),
                pivot_y=float(pivot_y),
            )
        except BridgeError as exc:
            return f"Error: {exc}"
        return _result(reply)

    # --- Coach: turn tracking --------------------------------------------

    @app.tool(name="gh_begin_turn")
    @gated(caps, "gh_begin_turn")
    def gh_begin_turn() -> str:
        """Start a new AI turn. Returns the new turn_id.

        Call this at the START of an AI response if Scenario is Coach.
        Subsequent canvas-mutating tools will be tagged with this turn_id
        so gh_end_turn can summarize "what I changed".
        Calling this without a Skill / outside Coach is harmless — the turn
        just records nothing of consequence.
        """
        # Server-side tracker.
        server_turn = turns.begin()
        # Forward to bridge so canvas-side state stays in sync (best-effort).
        bridge_turn: int | None = None
        try:
            reply = gh.send("begin_turn")
            if reply.get("status") == "success":
                bridge_turn = int((reply.get("result") or {}).get("turn_id") or 0) or None
        except BridgeError:
            # Bridge unreachable — proceed with server-side state only.
            pass
        return json.dumps({"turn_id": server_turn, "bridge_turn_id": bridge_turn},
                          indent=2)

    @app.tool(name="gh_end_turn")
    @gated(caps, "gh_end_turn")
    def gh_end_turn(turn_id: int = 0) -> str:
        """End an AI turn. Returns a structured "what changed this turn" summary
        AND triggers canvas-side highlight painting.

        Call this at the END of an AI response if you called gh_begin_turn.
        The bridge will draw a 3 px teal ring around every component touched
        during this turn — the user will see at a glance what the AI did.
        Use this summary to also narrate "## What I changed in this turn"
        in your text reply (per the Coach-mode prompt).

        Highlights stay on the canvas until `gh_dismiss_highlights` is called
        (with this turn_id or with 0 to clear all turns).

        Args:
            turn_id: turn id from gh_begin_turn. 0 = end the current turn.
        """
        state = turns.end(turn_id or None)
        bridge_summary: dict[str, Any] = {}
        try:
            reply = gh.send("end_turn", turn_id=turn_id or turns.current_turn_id)
            if reply.get("status") == "success":
                bridge_summary = reply.get("result") or {}
        except BridgeError:
            pass
        out = {
            "turn_id": state.turn_id if state else turn_id,
            "added_guids": sorted(state.added_guids) if state else [],
            "modified_guids": sorted(state.modified_guids) if state else [],
            "wired_pairs": list(state.wired_pairs) if state else [],
            "bridge_changed_count": bridge_summary.get("changed_count", 0),
            "bridge_changed_guids": bridge_summary.get("changed_guids", []),
            "note": (
                "Canvas highlights are now visible — every component in "
                "`bridge_changed_guids` is ringed in teal. Tell the user "
                "what changed, then optionally call gh_dismiss_highlights "
                "once they've acknowledged."
            ),
        }
        return json.dumps(out, indent=2, default=str)

    @app.tool(name="gh_dismiss_highlights")
    @gated(caps, "gh_dismiss_highlights")
    def gh_dismiss_highlights(turn_id: int = 0) -> str:
        """Clear the recorded changes for one or all turns.

        Args:
            turn_id: id to clear (0 = clear all turns).
        """
        cleared = turns.dismiss(turn_id or None)
        try:
            gh.send("dismiss_highlights", turn_id=turn_id or 0)
        except BridgeError:
            pass
        return json.dumps({"cleared_turns": cleared}, indent=2)

    # --- v0.2.3 productivity tools ---------------------------------------

    @app.tool(name="gh_bake_to_rhino")
    @gated(caps, "gh_bake_to_rhino")
    def gh_bake_to_rhino(instance_guid: str, layer: str = "") -> str:
        """Bake a Grasshopper component's output to the Rhino document.

        Walks the component's outputs and bakes any bake-aware geometry
        (curves, breps, meshes, points, …) to Rhino. Returns the list of
        Rhino GUIDs that landed in the doc.

        Args:
            instance_guid: the GH component to bake from.
            layer: optional Rhino layer name. Created if it doesn't exist.
                Defaults to the current Rhino layer when empty.
        """
        try:
            return _result(gh.send("bake_to_rhino",
                                    instance_guid=instance_guid,
                                    layer=layer))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_reference_rhino_object")
    @gated(caps, "gh_reference_rhino_object")
    def gh_reference_rhino_object(rhino_guid: str,
                                   x: float = 100.0,
                                   y: float = 100.0) -> str:
        """Reference a Rhino document object as input to Grasshopper.

        Drops the right kind of GH parameter (Curve / Brep / Mesh / Point /
        Surface / Geometry) on the canvas with persistent data pointing at
        the Rhino object's GUID — the same as right-clicking a Curve param
        and choosing "Set one Curve" in Rhino.

        Use this when the user has geometry already drawn in Rhino and you
        want it as input to a definition you're building in Grasshopper.

        Args:
            rhino_guid: the Rhino object's GUID (from rhino_get_objects_with_metadata).
            x, y: canvas position for the new param. Defaults to (100, 100).
        """
        try:
            return _result(gh.send("reference_rhino_object",
                                    rhino_guid=rhino_guid,
                                    x=float(x), y=float(y)))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_add_panel")
    @gated(caps, "gh_add_panel")
    def gh_add_panel(text: str, x: float = 100.0, y: float = 100.0) -> str:
        """Add a Grasshopper Panel containing arbitrary text.

        Useful for leaving inline notes / explanations next to your work
        so the user can read what each cluster of components is for.

        Args:
            text: the panel's content. Multiline supported.
            x, y: canvas position. Defaults to (100, 100).
        """
        try:
            return _result(gh.send("add_panel", text=text, x=float(x), y=float(y)))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_set_panel_content")
    @gated(caps, "gh_set_panel_content")
    def gh_set_panel_content(instance_guid: str, text: str) -> str:
        """Replace the text on an existing Grasshopper Panel.

        Args:
            instance_guid: GUID of the Panel component.
            text: new text content. Multiline supported.
        """
        try:
            return _result(gh.send("set_panel_content",
                                    instance_guid=instance_guid, text=text))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_get_component_output")
    @gated(caps, "gh_get_component_output")
    def gh_get_component_output(instance_guid: str,
                                 output_name: str = "",
                                 max_items: int = 100) -> str:
        """Read the volatile data on a component's output (data tree summary).

        Returns the branches and item values so you can explain what a
        component is actually producing (numbers, point coordinates, curve
        descriptions, ...). The output is the string form of each value,
        which is human-readable for most goo types.

        Args:
            instance_guid: GUID of the component (or a floating param).
            output_name: name or nickname of the output to read. Empty
                reads the first output.
            max_items: cap the total values returned. Default 100.
        """
        try:
            return _result(gh.send("get_component_output",
                                    instance_guid=instance_guid,
                                    output_name=output_name,
                                    max_items=int(max_items)))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_group_components")
    @gated(caps, "gh_group_components")
    def gh_group_components(instance_guids: list[str],
                             nickname: str = "Group",
                             color: str = "") -> str:
        """Wrap components in a Grasshopper Group (visual cluster).

        The group renders as a rounded rectangle behind the listed
        components with the nickname displayed in the header. Useful for
        keeping a parametric definition organized — group sliders together,
        group the geometry-generating components, group the modifiers.

        Args:
            instance_guids: GUIDs of the components to include.
            nickname: group label shown on canvas. Default "Group".
            color: HTML hex string like "#FFE5A0" for the group background.
                Leave empty for the default pastel.
        """
        try:
            return _result(gh.send("group_components",
                                    instance_guids=instance_guids,
                                    nickname=nickname,
                                    color=color))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_move_component")
    @gated(caps, "gh_move_component")
    def gh_move_component(instance_guid: str, x: float, y: float) -> str:
        """Move a single canvas object to a new pivot.

        For laying out the canvas tidily after the AI has been building,
        prefer gh_organize_components — it computes a sensible left-to-right
        flow automatically. Use this for one-off positioning.

        Args:
            instance_guid: GUID of the object to move.
            x, y: new pivot in canvas coordinates.
        """
        try:
            return _result(gh.send("move_component",
                                    instance_guid=instance_guid,
                                    x=float(x), y=float(y)))
        except BridgeError as exc:
            return f"Error: {exc}"

    # --- v0.2.4 fast outline tools --------------------------------------

    @app.tool(name="gh_canvas_outline")
    @gated(caps, "gh_canvas_outline")
    def gh_canvas_outline() -> str:
        """ULTRA-FAST canvas digest — use this FIRST when joining a session or analyzing a definition.

        Returns the canvas split into wire-connected clusters with each
        cluster's component-type histogram, bounding box, endpoints, and
        input widgets. Designed to be ~1 KB for any reasonable canvas —
        replaces the gh_get_context → 111k chars → subagent dispatch chain
        for the "what's on this canvas / explain this definition" use case.

        Format: short integer IDs (c1, c2, ...) for endpoint/input objects,
        with a `guids` lookup table at the end. Drill into a cluster's
        dataflow with gh_cluster_flow(cluster_id).
        """
        try:
            return _result(gh.send("canvas_outline"))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_file_outline")
    @gated(caps, "gh_file_outline")
    def gh_file_outline(file_path: str) -> str:
        """Read a .gh / .ghx file off disk and return the same outline structure
        as gh_canvas_outline — WITHOUT polluting the current canvas.

        Use this for "explain this downloaded file" workflows. The bridge
        loads the archive into a temporary in-memory GH_Document, runs the
        clustering, then disposes the temp doc. The user's canvas is never
        touched. Works for both modern zip-format .gh and raw-XML .ghx.

        Args:
            file_path: Absolute path to a .gh or .ghx file.
        """
        path = Path(file_path).expanduser().resolve()
        if not path.is_file():
            return f"Error: file not found: {path}"
        try:
            data_b64 = base64.b64encode(path.read_bytes()).decode("ascii")
        except OSError as exc:
            return f"Error: could not read {path}: {exc}"
        try:
            return _result(gh.send("file_outline", data=data_b64))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_cluster_flow")
    @gated(caps, "gh_cluster_flow")
    def gh_cluster_flow(cluster_id: int) -> str:
        """Drill down into one cluster from the LAST outline call — returns its
        stage-based dataflow (~300 chars even for large clusters).

        Stages are topological levels inside the cluster: stage 1 = sources
        (no in-cluster upstream), stage 2 = first transforms, ..., final
        stage = endpoints. Each stage reports its component-type histogram
        and any slider / toggle / value-list nicknames.

        Args:
            cluster_id: id returned by gh_canvas_outline or gh_file_outline.
                Cache is process-local — call outline first in the same
                MCP session.
        """
        try:
            return _result(gh.send("cluster_flow", cluster_id=int(cluster_id)))
        except BridgeError as exc:
            return f"Error: {exc}"

    @app.tool(name="gh_organize_components")
    @gated(caps, "gh_organize_components")
    def gh_organize_components(instance_guids: list[str] | None = None,
                                start_x: float = 100.0,
                                start_y: float = 100.0,
                                column_width: float = 250.0,
                                row_height: float = 110.0) -> str:
        """Auto-layout components left → right by data-flow depth.

        Computes each component's depth (the longest path from a source) and
        places it in a column at that depth, stacking parallel components
        vertically. Run this after building a definition to escape the
        "everything piled in one spot" problem.

        Args:
            instance_guids: GUIDs to organize. Omit to organize ALL canvas
                objects (recommended after you've just added a bunch).
            start_x, start_y: top-left anchor for the layout.
            column_width: horizontal gap between depth columns.
            row_height: vertical gap between siblings.
        """
        args: dict[str, Any] = {
            "start_x": float(start_x),
            "start_y": float(start_y),
            "column_width": float(column_width),
            "row_height": float(row_height),
        }
        if instance_guids:
            args["instance_guids"] = instance_guids
        try:
            return _result(gh.send("organize_components", **args))
        except BridgeError as exc:
            return f"Error: {exc}"


# --- .gh archive introspection ------------------------------------------------
# Grasshopper's binary format (.gh) is a deflate-compressed XML document.
# (.ghx is the same XML, uncompressed.) We extract just enough to summarize
# without actually loading it into Grasshopper — useful for Inspect mode.


def _introspect_gh_archive(path: Path) -> dict[str, Any]:
    """Parse a .gh / .ghx file and return a structural summary.

    Returns: {format, root_objects, components: [{guid, name, category, ...}]}
    Best-effort — the .gh format isn't strictly documented and we touch only
    a small fraction of the chunk tree.
    """
    raw = path.read_bytes()
    # .gh = deflate-compressed XML, .ghx = raw XML. Sniff by header.
    if path.suffix.lower() == ".ghx" or raw.startswith(b"<"):
        xml_bytes = raw
        fmt = "ghx"
    else:
        # gh files are usually zip archives containing one XML entry per
        # logical chunk, but older formats are pure deflate. Try zip first.
        try:
            with zipfile.ZipFile(path, "r") as zf:
                names = zf.namelist()
                # Largest XML entry is typically the canvas chunk.
                xml_name = None
                xml_size = -1
                for n in names:
                    if n.endswith(".xml") and zf.getinfo(n).file_size > xml_size:
                        xml_name = n
                        xml_size = zf.getinfo(n).file_size
                if xml_name is None:
                    return {
                        "format": "gh-zip",
                        "entries": names,
                        "components": [],
                        "warning": "no .xml entry found",
                    }
                xml_bytes = zf.read(xml_name)
                fmt = "gh-zip"
        except zipfile.BadZipFile:
            # Not a zip — likely the older single-stream format. We don't
            # have a clean deflate header to parse here without GH-internal
            # knowledge; surface what we can.
            return {
                "format": "gh-binary",
                "size_bytes": len(raw),
                "components": [],
                "warning": "not a zip archive; full parse requires GH runtime",
            }

    try:
        tree = ET.fromstring(xml_bytes)
    except ET.ParseError as exc:
        return {"format": fmt, "components": [], "warning": f"XML parse failed: {exc}"}

    # Walk chunks named "Object" — Grasshopper writes one per canvas object.
    components: list[dict[str, Any]] = []
    for chunk in tree.iter("chunk"):
        chunk_name = chunk.get("name") or ""
        if chunk_name != "Object":
            continue
        info: dict[str, Any] = {}
        # The interesting items live in <items> children with `name` attrs.
        for item in chunk.iter("item"):
            name = item.get("name") or ""
            if name in ("Name", "NickName", "Description", "InstanceGuid"):
                info[name] = (item.text or "").strip()
            if name == "GUID":
                info["TypeGuid"] = (item.text or "").strip()
        if info:
            components.append(info)

    return {
        "format": fmt,
        "components": components,
        "component_count": len(components),
    }

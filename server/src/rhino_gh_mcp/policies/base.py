"""Policy: which tool names are advertised to the LLM at startup.

A Policy is an immutable filter applied at tool-registration time. Tools call
`policy.allows(name)` before binding themselves to the FastMCP app, so the LLM
literally cannot see tools outside its tier.

This is intentionally a coarse-grained allow-list. Finer-grained guards (e.g.
"a placed component's category must be in the curated_group") live in the tool
implementations themselves.
"""

from __future__ import annotations

from dataclasses import dataclass

from ..config import Policy as PolicyEnum

# --- Tool name registries --------------------------------------------------
# Keep these strings in sync with the @app.tool() function names. The test
# suite validates this invariant.

PARAMETER_TOOLS: frozenset[str] = frozenset(
    {
        # Read-only inspection
        "gh_status",
        "gh_get_context",
        "gh_get_objects",
        "gh_get_selected",
        "gh_list_sliders",
        "gh_list_panels",
        "gh_list_toggles",
        "gh_list_value_lists",
        "gh_canvas_summary",
        "gh_find_components",
        "gh_get_panel_content",
        "gh_get_runtime_messages",
        # Pure parameter writes
        "gh_set_slider",
        "gh_set_toggle",
        "gh_select_value_list",
        "gh_set_component_parameter",
        "gh_recompute",
        # Multimodal — read-only viewport
        "gh_capture_canvas",
        "rhino_capture_viewport",
        # Rhino read
        "rhino_status",
        "rhino_get_scene_info",
        "rhino_get_layers",
        "rhino_get_objects_with_metadata",
        "rhino_list_blocks",
        # Rhino view (non-mutating to doc)
        "rhino_set_view",
    }
)

CURATED_TOOLS: frozenset[str] = PARAMETER_TOOLS | frozenset(
    {
        "gh_list_available_components",
        "gh_add_component",
        "gh_add_slider",
        "gh_connect_components",
        "gh_remove_node",
        "gh_set_slider_range",
    }
)

FULL_TOOLS: frozenset[str] = CURATED_TOOLS | frozenset(
    {
        "gh_add_any_component",
        "gh_write_script_py3",
        "gh_write_script_cs",
        "gh_write_script_py2",
        "gh_update_script",
        "gh_execute_code",
        "rhino_execute_code",
        "rhino_run_named_command",
    }
)


@dataclass(frozen=True)
class Policy:
    """An immutable allow-list of tool names."""

    name: str
    tools: frozenset[str]

    def allows(self, tool_name: str) -> bool:
        return tool_name in self.tools

    def allowed_tools(self) -> frozenset[str]:
        return self.tools


_REGISTRY: dict[PolicyEnum, Policy] = {
    PolicyEnum.PARAMETER: Policy("parameter", PARAMETER_TOOLS),
    PolicyEnum.CURATED: Policy("curated", CURATED_TOOLS),
    PolicyEnum.FULL: Policy("full", FULL_TOOLS),
}


def policy_for(p: PolicyEnum) -> Policy:
    return _REGISTRY[p]

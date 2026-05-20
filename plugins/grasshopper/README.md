# Grasshopper plugin (`rhino_gh_mcp.gha`)

A `.gha` that registers an **MCP Server** component on the Grasshopper canvas.
When `Run = True`, the component starts a loopback HTTP listener (default port
9999) that accepts the JSON command protocol the Python MCP server uses.

## v0.1.0 status

The canonical source is `_archive project/rhino_gh_mcp-main/rhino_gh_mcp/`.
We're not rewriting it from scratch — it's well structured and the v1 Python
server speaks its protocol unchanged.

The work scheduled for v0.2.0 of this plugin is:

1. **Rename namespace** from `rhino_gh_mcp` to something cleaner once the
   project name is finalized.
2. **Add `script_language` switch** in `update_script` so the .gha can target
   the Rhino 8 Script component (Python 3 + C#), not just legacy GHPython.
3. **Add `capture_canvas` command** — render the canvas image and return base64.
4. **Add `get_runtime_messages` first-class command** so the Python tool
   doesn't have to fish them out of `get_objects`.
5. **Add `set_slider_range` command** for the new tool of the same name.
6. **Add `bypass_filter` honoring** on `add_component_to_canvas` so the L3
   `gh_add_any_component` works as intended.

Track these in the issue list once the repo is public.

## Component inputs

| Input | Type | Default | Purpose |
|-------|------|---------|---------|
| Run | bool | false | Start/stop the HTTP listener |
| CategoryFilter | string | "MCP" | Comma-separated component categories the LLM may place |
| SetParameterMode | int | 0 | 0 = panel, 1 = interactive widget, 2 = volatile data |
| AutoRecompute | bool | false | Recompute after every successful command |

## Wire protocol

```
POST http://127.0.0.1:<port>/
Content-Type: application/json

{"type": "<command>", ...flat params}
```

Response:

```
HTTP/1.1 200 OK
Content-Type: application/json

{"status": "success" | "error", "result": <any>}
```

Commands handled: `is_server_available`, `add_component_to_canvas`,
`add_slider_to_canvas`, `get_context`, `get_objects`, `get_selected`,
`update_script`, `update_script_with_code_reference`, `connect_components`,
`remove_node`, `recompute_all`, `get_all_component_proxies`,
`get_all_component_library`, `set_component_parameter`, `get_panel_content`,
`execute_code`, `expire_component`, `get_debug_log`.

Planned for v0.2: `capture_canvas`, `set_slider_range`, `get_runtime_messages`,
script-language-aware `update_script`.

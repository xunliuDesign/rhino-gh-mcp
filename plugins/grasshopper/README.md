# Grasshopper plugin — `RhinoGhMcp.gha`

A `.gha` that registers an **MCP Server** component on the Grasshopper canvas.
When `Run = True`, the component starts a loopback HTTP listener (default port
9999) that speaks the JSON command protocol the Python MCP server uses.

## Build

Requires the .NET 7 SDK or newer. Tested on .NET SDK 10 (macOS) and Windows.

```bash
cd plugins/grasshopper
dotnet build -c Release
```

This produces two `.gha` files (the build targets both):

```
bin/Release/net7.0/RhinoGhMcp.gha            # use this on Mac
bin/Release/net7.0-windows/RhinoGhMcp.gha    # use this on Windows
```

## Install

### 0. Remove any v0 `.gha` first

The v0 plugin (`rhino_gh_mcp*.gha`) shipped a component with name
`"rhino_gh_mcp MCP Server"`. v1 ships a different component
(`"rhino-gh-mcp Server (v1)"`) with a different ComponentGuid, so the two can
*coexist* in Grasshopper — but you'll get two MCP components in the ribbon and
you'll have to remember which is which. Cleaner to remove v0 first:

```bash
# Mac — find what's installed
ls "$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper "*"/Libraries/" 2>/dev/null | grep -i mcp

# Mac — remove v0 (adjust the filename to match what `ls` printed)
rm "$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper "*"/Libraries/rhino_gh_mcp_0704.gha"
```

```powershell
# Windows — find and remove
Get-ChildItem "$env:APPDATA\Grasshopper\Libraries\" -Filter "*mcp*.gha"
Remove-Item "$env:APPDATA\Grasshopper\Libraries\rhino_gh_mcp_0704.gha"
```

Restart Rhino after removing.

### 1. Install v1

**Mac (Rhino 8):**
```bash
cp bin/Release/net7.0/RhinoGhMcp.gha \
   "$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper "*"/Libraries/"
```
Then in Finder, right-click the copied `.gha` → **Get Info** → tick **Open
anyway** (Gatekeeper will block first launch otherwise). Restart Rhino.

**Windows (Rhino 8):**
```powershell
Copy-Item bin\Release\net7.0-windows\RhinoGhMcp.gha `
          "$env:APPDATA\Grasshopper\Libraries\"
```
In File Explorer, right-click the copied `.gha` → **Properties** → **Unblock**.
Restart Rhino.

## Use

1. Open Rhino 8, launch Grasshopper.
2. New tab **MCP** is visible. Drag the **rhino-gh-mcp Server (v1)** component
   (nickname `MCPv1`) onto the canvas.
3. Wire a `Boolean Toggle` to its `Run` input and set it to `True`.
4. The `Status` output should read `Listening on 127.0.0.1:9999`.
5. The `Version` output should read `rhino-gh-mcp v0.1.0 (https://github.com/xunliuDesign/rhino-gh-mcp)` —
   this is the canonical way to verify which build of the plugin Rhino loaded.

## Component inputs

| Input | Type | Default | Purpose |
|-------|------|---------|---------|
| `RunServer` | bool | false | Start/stop the HTTP listener |
| `CategoryFilter` | string | `MCP` | Comma-separated component categories the LLM may place via `gh_add_component` |
| `SetParameterMode` | int | 0 | 0 = panel, 1 = interactive widget (slider/toggle/swatch), 2 = volatile data |
| `AutoRecompute` | bool | false | Recompute after every successful command |
| `Port` | int | 9999 | **v1 NEW** — TCP port for the HTTP listener. Change this and the Python server's `--gh-port` together to avoid the v0 collision. |

## Component outputs

| Output | Type | Purpose |
|--------|------|---------|
| `Status` | string | Live server status (`Server Off`, `Listening on 127.0.0.1:9999`, etc.) |
| `Debug` | string | Sticky debug info — last error / connection log |
| `Version` | string | **v1 NEW** — plugin version + repo URL. Use this to confirm which build is loaded. |

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

Commands handled today (v0.1.0):

- `is_server_available`, `get_debug_log`
- `get_context`, `get_objects`, `get_selected`, `get_panel_content`
- `get_all_component_proxies`, `get_all_component_library`
- `add_component_to_canvas`, `add_slider_to_canvas`
- `connect_components`, `remove_node`, `expire_component`, `recompute_all`
- `set_component_parameter`
- `update_script`, `update_script_with_code_reference`, `execute_code`

Planned for v0.2:

- `capture_canvas` — render canvas to PNG for `gh_capture_canvas` tool
- `get_runtime_messages` — first-class warning/error read
- `set_slider_range` — adjust min/max without losing the value
- `update_script` with `script_language` (Python 3 / C#) for Rhino 8 Script component injection
- `bypass_filter` honoring on `add_component_to_canvas` for L3 mode

## Project layout

```
plugins/grasshopper/
├── RhinoGhMcp.csproj      # Multi-target: net7.0 + net7.0-windows
├── RhinoGhMcp.sln
└── src/
    ├── McpServerComponent.cs   # The MCP Server component + all command handlers
    ├── RhinoGhMcpInfo.cs       # Assembly metadata
    └── Resources/
        └── Icon.png            # 24x24 component icon
```

## Why no Rhino 7 / net48 target?

Rhino 7 ships IronPython 2.7 in the GH Python component. We dropped IronPython
2 support from the Python tool surface (the v0 prompt scaffolding for it is in
`_archive project/`) because Rhino 8's CPython 3 path is strictly better and
the duplicate code path was hurting more than helping. If you need Rhino 7
support, the v0 code in the archive still works.

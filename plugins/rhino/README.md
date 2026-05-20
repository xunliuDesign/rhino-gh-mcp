# Rhino plugin — `RhinoGhMcpRhino.rhp`

A Rhino plugin that hosts a TCP-JSON listener on `127.0.0.1:9876`. The
Python MCP server's `rhino_*` tools talk to it; the Grasshopper `.gha`
under [../grasshopper](../grasshopper) is the parallel half on the GH side.

## v0.1.0 status

Working — `is_server_available`, `get_scene_info`, `get_layers`,
`get_objects_with_metadata`, `capture_viewport`, `execute_code`,
`run_named_command` all implemented. UI-thread marshalling via
`RhinoApp.InvokeOnUiThread`.

The v0 source from `_archive project/rhino_script_client/` had the MCP
service mixed with an unrelated StreamDiffusion experiment. The v1 here is
a clean carve-out — MCP only — with the previously-commented-out handlers
re-implemented.

## Build

```bash
cd plugins/rhino
dotnet build -c Release
# Output: bin/Release/net7.0/RhinoGhMcpRhino.rhp
```

Single target — `net7.0`. Rhino 8 loads it on both Mac and Windows; we
don't use WinForms so we don't need the `-windows` TFM split.

## Install

Use the helper script (handles "first time" vs "update" automatically):

```bash
./reinstall.sh
```

### First-time install

Rhino's plugin loader registers a `.rhp` once, then remembers its path.
On first install, the script can't update in place — it'll print the
`.rhp` path and tell you to either:

- **Drag** the `.rhp` from `bin/Release/net7.0/RhinoGhMcpRhino.rhp` onto
  Rhino's main window, or
- Open Rhino, run `_PluginManager`, click `Install...`, browse to the same
  path.

Restart Rhino after Rhino confirms the install.

### Update install

Once Rhino knows about the plugin, subsequent `./reinstall.sh` runs detect
the registered install folder and overwrite the `.rhp` there.

## Start the listener

```
_ToggleMcpService
```

Idempotent — running it again stops the listener. While running you'll see
`rhino-gh-mcp: MCP service running on 127.0.0.1:9876` in Rhino's command
window.

## Wire protocol

```
TCP 127.0.0.1:9876

Client writes: {"type": "<command>", "params": {...}}
Then closes the write half.
Server writes: {"status": "success"|"error", "result": <any>, "message": "..."}
```

One request per connection. The server reads until EOF on the client's
write half, then writes the reply, then closes.

## Commands

| Command | What it does |
|---|---|
| `is_server_available` | Returns plugin_name, plugin_version, assembly_location, rhino_version, host, port |
| `get_scene_info` | Layer list + up to 5 sample objects per layer |
| `get_layers` | Full layer list with visibility, lock, color, full path |
| `get_objects_with_metadata` | Filtered objects with user-text metadata (filters: layer, name, short_id — wildcards supported) |
| `capture_viewport` | Active viewport as base64 PNG with width/height |
| `execute_code` | Run Python via `Rhino.Runtime.PythonScript`. Set `result = ...` to return data. |
| `run_named_command` | Run any Rhino command name (e.g. `_SelAll`, `_-SaveAs`, `_BooleanUnion`) |

Planned for v0.2:

- `list_blocks`, `instance_definitions` reads
- `import_file` / `export_file` typed wrappers
- `set_view` (named-view application)
- `measure_distance`, `area`, `volume`
- `bake_from_gh` — coordinated bake from a wired GH definition

## Why TCP not HTTP?

Different threading model from Grasshopper. Rhino's main-thread reentrancy
under HTTP listeners has been historically fragile on macOS (the v0
StreamDiffusion experiment deadlocked Rhino on long-running streams). A
one-shot TCP connection per request — read, dispatch on UI thread, write,
close — avoids whole classes of hang.

## Project layout

```
plugins/rhino/
├── RhinoGhMcpRhino.csproj
├── RhinoGhMcpRhino.sln
├── reinstall.sh
├── README.md
└── src/
    ├── Plugin.cs                  # Rhino.PlugIns.PlugIn-derived registration
    ├── McpService.cs              # TCP listener + dispatch + all handlers
    ├── ToggleMcpServiceCommand.cs # _ToggleMcpService command
    └── AssemblyInfo.cs            # Stable plugin GUID + metadata
```

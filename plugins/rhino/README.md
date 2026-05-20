# Rhino plugin (`rhino_gh_mcp.rhp`)

A Rhino plugin that hosts a TCP-JSON listener for the `rhino_*` tools.

## v0.1.0 status

Not yet ported into this directory. The v0 source in
`_archive project/rhino_gh_mcp-main/rhino_script_client/` is the starting
point, but it mixes the MCP service with an unrelated StreamDiffusion
experiment we are dropping. The v1 plugin is a clean carve-out of just the
MCP-relevant files:

- `MCPService.cs` — TCP listener and command dispatch
- `rhino_script_clientPlugin.cs` — Rhino plugin registration
- `ToggleMCPServiceCommand.cs` — `_ToggleMCP` Rhino command to start/stop the listener

Will land in P1.

## Wire protocol

```
TCP 127.0.0.1:<port>

Client writes: {"type": "<command>", "params": {...}}
Then closes the write half.
Server writes: {"status": "success"|"error", "result": <any>, "message": "..."}
```

Commands handled (planned for v0.1.0):

- `is_server_available`
- `get_scene_info`
- `get_layers`
- `get_objects_with_metadata`
- `capture_viewport`
- `execute_code`

Commands for v0.2.0:

- `run_named_command`
- `list_blocks`
- `import_file`, `export_file`
- `boolean_*`
- `measure_distance`

## Why TCP not HTTP?

Different from the Grasshopper bridge on purpose. Rhino's command marshalling
on the main UI thread is fragile under HTTP servers (the listener pattern we
tried in v0 deadlocked Rhino on macOS under load). A line-delimited TCP socket
with one request per connection avoids the problem and is dirt-simple to
implement.

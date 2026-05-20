# Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  MCP Client (Claude Desktop / Claude Code / Cursor / web UI)     │
│                                                                  │
│  - Loads Skills (workflows: landform, façade, zoning, ...)       │
│  - Calls tools advertised by the server                          │
└──────────────────────────┬──────────────────────────────────────┘
                           │ MCP (stdio or streamable-http)
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│  rhino-gh-mcp (Python 3.11+, FastMCP)                            │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Tool surface (filtered by Policy at registration time)    │  │
│  │  ├─ gh_read.*       canvas inspection                      │  │
│  │  ├─ gh_write.*      place / wire / set / recompute         │  │
│  │  ├─ gh_script.*     L3 only: Py3 / Py2 / C# injection      │  │
│  │  ├─ rhino_tools.*   scene + execute_code                   │  │
│  │  └─ multimodal.*    viewport + canvas capture              │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  Policy (L1 parameter / L2 curated / L3 full) — picked at start  │
│                                                                  │
│  Bridges (loopback only):                                        │
│  ├─ GrasshopperBridge  HTTP :9999  (httpx)                       │
│  └─ RhinoBridge        TCP  :9876  (socket)                      │
└──────────┬───────────────────────────────────┬──────────────────┘
           │                                   │
           │ HTTP POST /                       │ TCP JSON one-shot
           │                                   │
┌──────────▼───────────────────────┐ ┌─────────▼──────────────────┐
│ rhino_gh_mcp.gha                 │ │ rhino_gh_mcp.rhp           │
│ (Grasshopper assembly, C#)       │ │ (Rhino plugin, C#)         │
│                                  │ │                            │
│ MCP Server component on canvas   │ │ MCPService listener        │
│ - UI-thread marshalling          │ │ - UI-thread marshalling    │
│ - Handles: add/wire/update/...   │ │ - Handles: scene/layers/... │
└─────────────┬────────────────────┘ └─────────────┬──────────────┘
              │                                    │
              └─────── shared GH ↔ Rhino doc ──────┘
```

## Why two plugins?

Grasshopper is a host-managed dataflow with its own UI thread and component
lifecycle. Rhino's document and viewport are separate concerns with separate
threading rules. Splitting the bridge means each side can use the threading
model it's already good at, and the LLM can issue Rhino-only commands without
needing Grasshopper open at all.

## Why HTTP for GH and TCP for Rhino?

Different reliability profiles. Grasshopper's component lifecycle is stable
enough for a long-lived HTTP listener — the .gha runs as a regular component,
restarting it is a one-click toggle. Rhino's listener has historically been
flakier across macOS / Windows; a one-shot TCP request avoids whole classes of
hang and "stuck socket" failure modes. Both share the same JSON envelope so
the bridge code on the Python side is nearly identical.

## Why policy-at-registration not policy-at-call?

If a tool isn't registered, the MCP client never sees it in `tools/list` and
the LLM literally cannot invoke it. That's stronger than runtime checks
(which would still leak the tool's existence and tempt the model to ask the
user to escalate). Per-call guards still exist for fine-grained checks
(e.g. "this component's category is in the allow-list") that can't be
expressed at registration time.

## What gets logged where

- **Python server** logs to stderr (so stdio transport is clean). Set
  `--log-level DEBUG` for wire-protocol traces.
- **Grasshopper .gha** keeps a 1000-line ring buffer via `LogDebug` / `LogError`.
  Read it with the (existing) `get_debug_log` command.
- **Rhino .rhp** will get an equivalent — TBD in P1.

## Concurrency model

- The MCP server is single-process. stdio transport is synchronous by nature;
  streamable-http transport may multiplex requests but each tool call hits one
  bridge call.
- Both bridges serialize requests at the Python level. Inside the C# plugins,
  command handlers marshal back to the Rhino/GH UI thread with
  `RhinoApp.InvokeOnUiThread`.
- No queue, no batching yet. If contention becomes an issue we'll add a
  bounded queue per bridge in P4.

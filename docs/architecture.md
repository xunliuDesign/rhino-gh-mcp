# Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  MCP Client (Claude Desktop / Claude Code / Cursor / web UI)     │
│                                                                  │
│  - Loads Skills (workflows: landform, ladybug, façade, ...)      │
│  - Calls tools advertised by the server                          │
└──────────────────────────┬──────────────────────────────────────┘
                           │ MCP (stdio or streamable-http)
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│  rhino-gh-mcp (Python 3.11+, FastMCP)                            │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Tool surface — every tool is always registered            │  │
│  │  ├─ gh_read.*       canvas inspection                      │  │
│  │  ├─ gh_write.*      place / wire / set / recompute         │  │
│  │  ├─ gh_script.*     scripting: Py3 / Py2 / C# injection    │  │
│  │  ├─ rhino_tools.*   scene + execute_code                   │  │
│  │  └─ multimodal.*    viewport + canvas capture              │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  Soft capability gate at CALL time:                              │
│  - CapabilitiesProvider polls canvas every ~3 s                  │
│  - Each tool checks `caps.allows(name)` before running           │
│  - Denial returns a clean LLM-readable string                    │
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
│ - Capability flags as inputs:    │ │ - Handles: scene/layers/   │
│   AllowParameters, AllowComp,    │ │   set_view/list_blocks/    │
│   AllowScripting, ComponentScope │ │   execute_code/...         │
│ - get_capabilities reports state │ │                            │
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

## Why soft-gate capabilities instead of hard-tier policy?

Earlier versions of this project used a startup-time tier (`--policy
parameter/curated/full`) that filtered which tools were registered with the
MCP server. That gave a strong "the LLM literally cannot see this tool"
guarantee, but had two real costs:

1. **The control sat in a CLI flag, hidden from anyone using the canvas.**
   The most consequential setting on the project — what the LLM is allowed
   to do — wasn't visible to the human operator.
2. **Changing the tier required restarting the server.** Mid-conversation
   re-tiering broke the chat. For teaching scenarios (TA hands a `.gh`
   file to a student) this was a serious limit.

v0.1.5+ moves the gate from registration time to call time. All tools are
always advertised; each tool consults the live `Capabilities` state before
running. The state lives on the canvas as Boolean / Integer inputs on the
MCP Server component, so it's both visible and adjustable mid-session.
The CLI `--policy` flag is preserved as a *preset* that seeds the default
capability state when the canvas isn't reachable.

The tradeoff: a determined LLM can attempt a forbidden call and receive
the denial. For most use cases (single-user research, classroom teaching
with trusted prompts) this is fine. If a future use case needs the
stronger guarantee (untrusted user input flowing back into the model's
context), a `--strict-gate` flag could be added to revert to
registration-time filtering. Not built yet.

## What gets logged where

- **Python server** logs to stderr (so stdio transport is clean). Set
  `--log-level DEBUG` for wire-protocol traces.
- **Grasshopper .gha** keeps a 1000-line ring buffer via `LogDebug` / `LogError`.
  Read it with the `get_debug_log` bridge command.
- **Rhino .rhp** writes to Rhino's CommandWindow on errors and connection
  events. Equivalent ring buffer is TBD.

## Concurrency model

- The MCP server is single-process. stdio transport is synchronous by nature;
  streamable-http transport may multiplex requests but each tool call hits one
  bridge call.
- Both bridges serialize requests at the Python level. Inside the C# plugins,
  command handlers marshal back to the Rhino/GH UI thread with
  `RhinoApp.InvokeOnUiThread`.
- No queue, no batching yet. If contention becomes an issue a bounded queue
  per bridge is the obvious next step.

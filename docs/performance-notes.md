# Performance notes

A pass through the v0.1.6 server / bridge code looking for places where
latency or token use could be reduced. **Nothing here is acted on yet** —
this is a list of opportunities sorted roughly by impact / effort ratio,
for you to decide what's worth doing.

## Big wins (high impact, low-to-medium effort)

### 1. Cache `get_context` on the Python side between read tools

Currently `gh_list_sliders`, `gh_list_panels`, `gh_list_toggles`,
`gh_list_value_lists`, `gh_canvas_summary`, and `gh_find_components` each
independently call `gh.send("get_context", simplified=True)`. An LLM
exploring a canvas often calls 3-5 of these in sequence — that's 3-5
identical round-trips to the .gha.

**Fix**: add a small `ContextCache` next to `CapabilitiesProvider`. TTL
of ~1 second is fine; reset on any write tool. The list tools then
become CPU-side projections of the cached payload, instant after the
first call.

Estimated cost: +50 lines of Python. Risk: low (the .gha already returns
deterministic JSON; cache invalidation is trivial since every mutating
tool already exists in `_PARAMETER_WRITE_TOOLS` / `_COMPONENT_WRITE_TOOLS`
/ `_SCRIPTING_TOOLS` — bump a cache generation counter when any of
those are called).

### 2. Cap `get_context` payload size at the source

Even with `simplified=True`, `get_context` returns every object's full
input/output array. On a 50-component canvas, the JSON can hit
hundreds of KB. The LLM rarely needs every input/output for every
component — it usually wants either: (a) just the top-level shape, or
(b) one specific component's full detail.

**Fix**: add a `compact=True` mode to `get_context` that returns only
`{guid, name, nickName, kind, value-if-widget}` per object. `gh_canvas_summary`
and the list tools all want this shape. Full detail stays available via
the existing `simplified=False` mode.

Estimated cost: +20 lines of C#, +20 of Python. Reduces typical
payload by ~5x.

### 3. Trim tool docstrings

Every tool's docstring goes into the MCP `tools/list` response and from
there into the LLM's system context **every turn**. The current
docstrings average ~200 tokens each — across 39 tools that's ~8K
tokens of overhead per request, before the user has typed anything.

**Fix**: split docstrings into a short user-facing description (~30
tokens, suitable for the MCP schema) and a longer "guidance" section
that only renders when the tool is actually called. The MCP SDK
supports this via the `description` field on `@app.tool()` separately
from the function docstring.

Estimated cost: per-tool edit (~30 tools × 2 mins = ~1 hour). Saves
~6K tokens per request — meaningful at scale.

## Medium wins

### 4. Capability cache: tune TTL or make it event-driven

The `CapabilitiesProvider` polls `get_capabilities` from the canvas
every 3 seconds via call-time staleness check. For a single canvas
flag change to propagate, that's a worst-case 3-second delay. For
chains of fast tool calls (~5/sec), it's also one extra
round-trip per ~15 calls.

**Options:**
- Lower TTL to 1s (more responsive, more overhead)
- Raise to 10s (less overhead, sluggish flip)
- **Better: piggyback** — every bridge call returns the current
  capability state as a header in its reply, so the cache refreshes
  for free on every successful call. The 3-second poll becomes a
  fallback for *no-recent-call* scenarios.

Estimated cost: +10 lines C#, +15 Python.

### 5. Skip the GH bridge proxy refresh on every `gh_list_available_components`

The `refresh=True` flag rebuilds the proxy cache on the .gha side
(querying `Grasshopper.Instances.ComponentServer.ObjectProxies`).
That's expensive on installs with many third-party plug-ins. The LLM
sometimes passes `refresh=True` defensively.

**Fix**: change the Python tool default from `refresh=False` to actually
ignore the flag and use a longer-lived cache (per-server-process) of
~60s. New installs rare; user can restart server to refresh.

Estimated cost: 5 lines Python.

### 6. Batch wire creation

The LLM often creates several `gh_connect_components` calls in a row
(one per wire). Each is a separate round-trip. A `gh_connect_many` /
batch variant taking a list of `{source, source_output, target, target_input}`
would collapse N calls into 1.

Estimated cost: +30 lines C#, +30 Python. Trickier rollback if one
wire fails mid-batch (current: each call independently succeeds/fails).

## Small wins

### 7. Drop `Inputs` / `Outputs` arrays from `get_context` for parameter-widgets

Top-level GH_NumberSlider / GH_Panel / etc don't have meaningful
`Inputs[]` arrays (they're parameters themselves). `GetParamInfo` emits
`Sources[]` / `Targets[]` arrays already. The extra empty `Inputs[]`
/ `Outputs[]` is dead JSON.

Tiny savings; mostly a polish item.

### 8. Switch GH bridge from per-call `httpx.Client` ping to persistent connection

The current `GrasshopperBridge.send()` reuses an `httpx.Client` instance
but each `send()` is a separate POST. HTTP/1.1 keep-alive should
already pool the connection, but a quick check with `tcpdump` would
confirm. If not, the fix is to enable HTTP/2 (`httpx.Client(http2=True)`)
or explicit connection reuse.

This is *probably* already free via keep-alive; verify before optimising.

## Non-fixes worth knowing

### 9. Don't cache `capture_viewport` / `capture_canvas`

These return live PNG bytes; the user's intent is always "show me now".
Caching here would be wrong even though the payload is big.

### 10. Don't try to cache across server restarts

The bridge state is ephemeral by design. A persistent cache would
introduce stale-data bugs that are much worse than the current ~hundreds-
of-ms hit on cold-start.

## Suggested order if acting

1. (1) **`get_context` cache between read tools** — biggest user-felt win,
   touches only Python.
2. (3) **Trim tool docstrings** — biggest token-cost win, but most
   tedious work.
3. (2) **Compact get_context mode** — for big canvases.
4. (4) **Capability cache: piggyback on every reply** — for the dynamic
   flip story to feel snappier.

Items 5-8 are nice-to-haves.

# rhino-gh-mcp (server)

Python MCP server. See the [top-level README](../README.md) for the project
overview.

## Layout

```
src/rhino_gh_mcp/
├── server.py            # FastMCP app, registers tools according to policy
├── config.py            # CLI / env config — policy, transport, bridge ports
├── __main__.py          # CLI entry point: `python -m rhino_gh_mcp` or `rhino-gh-mcp`
├── bridges/
│   ├── grasshopper.py   # HTTP client → :9999 (the .gha)
│   └── rhino.py         # TCP-JSON client → :9876 (the .rhp)
├── policies/
│   ├── base.py          # Policy = which tool names are allowed
│   ├── parameter.py     # L1
│   ├── curated.py       # L2
│   └── full.py          # L3
└── tools/
    ├── gh_read.py       # Canvas inspection
    ├── gh_write.py      # Place / wire / set / remove
    ├── gh_script.py     # Script component injection (P3+)
    ├── rhino_tools.py   # Rhino-side tools
    └── multimodal.py    # Viewport capture, image utilities
```

## Run

```bash
uv sync                   # install
uv run rhino-gh-mcp       # stdio, default policy = curated
uv run rhino-gh-mcp --policy full              # L3
uv run rhino-gh-mcp --policy parameter         # L1 (safe for students)
uv run rhino-gh-mcp --transport http --port 8765   # Streamable HTTP
```

## Test

```bash
uv run pytest
```

## Bridge protocols

The server is the *client* of two services running inside Rhino/Grasshopper:

| Bridge | Where it lives | Wire format |
|--------|----------------|-------------|
| Grasshopper | C# `.gha` component | HTTP POST `:9999`, JSON body `{"type": "...", ...flat params}` |
| Rhino | C# `.rhp` plugin | TCP `:9876`, line-delimited JSON `{"type": "...", "params": {...}}` |

The server keeps a singleton connection per bridge. Reconnects on failure.

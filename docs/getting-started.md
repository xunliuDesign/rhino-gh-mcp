# Getting started

## 0. What you need

- Rhino 8 (macOS or Windows)
- Python 3.11+ (3.12 recommended)
- [`uv`](https://docs.astral.sh/uv/) (`brew install uv` on Mac, `winget install astral.uv` on Windows)
- An MCP client: Claude Desktop, Claude Code, Cursor, or your own

## 1. Install the server

```bash
git clone https://github.com/xunliuDesign/rhino-gh-mcp.git
cd rhino-gh-mcp/server
uv sync
```

Verify it starts:

```bash
uv run rhino-gh-mcp --help
```

## 2. Install the Grasshopper plugin (v0)

Until the v1 plugin lands in `plugins/grasshopper/`, build the v0 source from
the archive:

```bash
cd "../_archive project/rhino_gh_mcp-main/rhino_gh_mcp"
dotnet build -c Release
```

Copy the produced `.gha` to:
- **Mac**: `~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper/Libraries/`
- **Windows**: `%APPDATA%\Grasshopper\Libraries\`

Restart Rhino, open Grasshopper. You'll find a new tab **MCP** with a single
**MCP Server** component. Drop it on the canvas, set `Run = True`. It should
report `Listening on 127.0.0.1:9999`.

## 3. Configure your MCP client

### Claude Desktop

Edit `~/Library/Application Support/Claude/claude_desktop_config.json` (Mac)
or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "rhino-gh": {
      "command": "uv",
      "args": [
        "--directory",
        "/absolute/path/to/rhino-gh-mcp/server",
        "run",
        "rhino-gh-mcp"
      ]
    }
  }
}
```

For a different control tier:

```json
"args": [
  "--directory", "/absolute/path/to/rhino-gh-mcp/server",
  "run", "rhino-gh-mcp",
  "--policy", "full"
]
```

Restart Claude Desktop. The `rhino-gh` server should appear under
**Settings → Developer**, and tool calls like `gh_status` should work.

### Claude Code / Cursor

Same JSON shape, in `~/.cursor/mcp.json` or your `.mcp.json`.

## 4. Smoke test it

In the client, ask:

> Call `gh_status` — is the bridge up?

If it reports OK, you're done. If not, the most common failures:

| Symptom | Fix |
|---------|-----|
| `Grasshopper bridge unreachable` | The MCP Server component isn't on the canvas, or `Run = False`. |
| `address already in use` in Grasshopper | Port 9999 collision — pass `--gh-port` to the server and the same port to the component. |
| Server doesn't appear in client | Check the absolute path in your config. Use `uv run rhino-gh-mcp --help` from a terminal first. |

## 5. Pick a Skill

Drop one of the Skills bundles from `skills/` into your client's skills
directory. The [landform-from-contours](../skills/landform-from-contours/) skill
is the canonical example.

"""CLI entry point: `python -m rhino_gh_mcp` or `rhino-gh-mcp`."""

from __future__ import annotations

import argparse
import logging
import os
import sys

from .config import Config, Policy, Transport
from .server import build_app


def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="rhino-gh-mcp",
        description="MCP server for tiered LLM control of Rhino 3D and Grasshopper.",
    )
    parser.add_argument(
        "--policy",
        choices=[p.value for p in Policy],
        default=os.environ.get("RHINO_GH_MCP_POLICY", Policy.CURATED.value),
        help=(
            "Tool surface: 'parameter' (read+sliders), 'curated' (allow-list of "
            "components), 'full' (everything including script-component injection)."
        ),
    )
    parser.add_argument(
        "--transport",
        choices=[t.value for t in Transport],
        default=os.environ.get("RHINO_GH_MCP_TRANSPORT", Transport.STDIO.value),
        help="MCP transport. 'stdio' for desktop clients, 'http' for web/agent clients.",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.environ.get("RHINO_GH_MCP_HTTP_PORT", "8765")),
        help="HTTP port when --transport=http (default 8765).",
    )
    parser.add_argument(
        "--gh-port",
        type=int,
        default=int(os.environ.get("RHINO_GH_MCP_GH_PORT", "9999")),
        help="Grasshopper bridge port (default 9999, must match .gha component).",
    )
    parser.add_argument(
        "--rhino-port",
        type=int,
        default=int(os.environ.get("RHINO_GH_MCP_RHINO_PORT", "9876")),
        help="Rhino bridge port (default 9876, must match .rhp plugin).",
    )
    parser.add_argument(
        "--curated-group",
        default=os.environ.get("RHINO_GH_MCP_CURATED_GROUP", ""),
        help="Comma-separated component category allow-list when --policy=curated.",
    )
    parser.add_argument(
        "--log-level",
        default=os.environ.get("RHINO_GH_MCP_LOG_LEVEL", "INFO"),
        help="Python logging level (DEBUG / INFO / WARNING / ERROR).",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = _parse_args(argv)

    logging.basicConfig(
        level=getattr(logging, args.log_level.upper(), logging.INFO),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        stream=sys.stderr,
    )
    log = logging.getLogger("rhino_gh_mcp")

    config = Config(
        policy=Policy(args.policy),
        transport=Transport(args.transport),
        http_port=args.port,
        gh_port=args.gh_port,
        rhino_port=args.rhino_port,
        curated_group=tuple(s.strip() for s in args.curated_group.split(",") if s.strip()),
    )
    log.info("Starting rhino-gh-mcp v0.1.0 with config: %s", config)

    app = build_app(config)

    if config.transport is Transport.STDIO:
        app.run(transport="stdio")
    elif config.transport is Transport.HTTP:
        app.run(transport="streamable-http", host="127.0.0.1", port=config.http_port)
    else:
        raise ValueError(f"Unknown transport: {config.transport}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

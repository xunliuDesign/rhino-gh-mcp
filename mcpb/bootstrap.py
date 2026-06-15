"""Launcher for the bundled rhino-gh-mcp server inside an MCPB / .dxt.

Why this exists:

  The MCPB spec (v0.3) for ``type: python`` expects the bundle to ship its
  dependencies. We can't easily pre-vendor cross-platform native wheels
  (pillow / pydantic-core / cryptography differ per OS+arch), so this
  launcher does a one-time pip install into a per-extension ``server/lib``
  directory on first run, then re-exec's the real server with that lib
  on ``sys.path``.

  After the first launch the bundle starts in <500 ms — no network, no
  pip, just sys.path setup and the real ``rhino_gh_mcp.__main__``.

  Requires Python 3.11+ on the user's PATH (or whichever interpreter
  Claude Desktop picks up via ``mcp_config.command``). Does not require
  uv. Internet only required on the very first launch; subsequent
  launches are fully offline.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

# Resolve paths relative to this file so it works no matter where the
# extension is installed.
HERE = Path(__file__).resolve().parent          # .../server/
BUNDLE = HERE.parent                            # .../ (extension root)
SRC = HERE / "src"                              # .../server/src
LIB = HERE / "lib"                              # .../server/lib (vendored deps)
REQS = HERE / "requirements.txt"
STAMP = LIB / ".rhino-gh-mcp.installed"
MIN_PY = (3, 11)


def _log(msg: str) -> None:
    # The MCP server uses stdout for protocol traffic, so all bootstrap
    # noise must go to stderr.
    print(f"[rhino-gh-mcp bootstrap] {msg}", file=sys.stderr, flush=True)


def _check_python() -> None:
    if sys.version_info < MIN_PY:
        _log(
            f"Python {MIN_PY[0]}.{MIN_PY[1]}+ required, "
            f"got {sys.version_info.major}.{sys.version_info.minor}. "
            "Install Python 3.11 or newer and reconfigure the extension."
        )
        sys.exit(78)  # EX_CONFIG


def _ensure_deps() -> None:
    """Vendor deps into ``server/lib`` if not already done."""
    if STAMP.exists():
        return

    if not REQS.is_file():
        _log(f"requirements.txt missing at {REQS} — bundle is malformed.")
        sys.exit(70)  # EX_SOFTWARE

    LIB.mkdir(parents=True, exist_ok=True)

    _log(f"first launch — installing dependencies into {LIB} (one-time, ~30 s)")
    cmd = [
        sys.executable,
        "-m", "pip",
        "install",
        "--quiet",
        "--no-input",
        "--disable-pip-version-check",
        "--target", str(LIB),
        "-r", str(REQS),
    ]
    try:
        result = subprocess.run(cmd, check=False, capture_output=True, text=True)
    except FileNotFoundError as exc:
        _log(f"could not invoke pip via {sys.executable}: {exc}")
        sys.exit(70)

    if result.returncode != 0:
        _log("pip install failed:")
        _log(result.stderr.strip() or result.stdout.strip() or "(no output)")
        # Leave the LIB dir around so the user can inspect what got there.
        sys.exit(result.returncode)

    STAMP.write_text("ok\n", encoding="utf-8")
    _log("dependencies installed.")


def _exec_server() -> None:
    # Put bundled deps + project source at the front of sys.path. This is
    # done by re-exec'ing python with PYTHONPATH set, so the child process
    # has a clean import environment (no leaked state from this script).
    paths = [str(SRC), str(LIB)]
    existing = os.environ.get("PYTHONPATH", "")
    if existing:
        paths.append(existing)
    env = os.environ.copy()
    env["PYTHONPATH"] = os.pathsep.join(paths)

    cmd = [sys.executable, "-m", "rhino_gh_mcp", *sys.argv[1:]]
    os.execvpe(cmd[0], cmd, env)  # noqa: S606  — intentional re-exec


def main() -> None:
    _check_python()
    _ensure_deps()
    _exec_server()


if __name__ == "__main__":
    main()

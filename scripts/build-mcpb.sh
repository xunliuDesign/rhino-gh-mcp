#!/usr/bin/env bash
# Build the rhino-gh-mcp Desktop Extension (.mcpb / .dxt) for Claude Desktop.
#
# Output: dist/rhino-gh-mcp-<VERSION>.mcpb
#
# Layout inside the bundle:
#   manifest.json              MCPB v0.3 manifest (see mcpb/manifest.json)
#   icon.png
#   server/
#     bootstrap.py             First-launch dep installer + server re-exec
#     requirements.txt         Pinned via server/pyproject.toml's deps
#     src/rhino_gh_mcp/        The server package (pure Python)
#   skills/                    Bundled skill recipes (landform, ladybug, ...)
#
# Why a bootstrap launcher: the MCPB v0.3 'python' server type expects
# you to bundle deps, but pre-vendoring cross-platform native wheels
# (pillow, pydantic-core, cryptography) is painful. Instead the bundle
# ships pure source + requirements.txt, and bootstrap.py does a one-time
# `pip install --target server/lib -r requirements.txt` on first run.
# Subsequent launches are offline + sub-second.
#
# Prereqs: zip, python3, a clean working tree under server/src/.
#
# Usage:
#   ./scripts/build-mcpb.sh           # build dist/rhino-gh-mcp-<VERSION>.mcpb
#   ./scripts/build-mcpb.sh --version 0.1.8

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

VERSION=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="$2"; shift 2 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    VERSION=$(python3 -c "import json; print(json.load(open('mcpb/manifest.json'))['version'])")
fi

OUTPUT="$REPO_ROOT/dist/rhino-gh-mcp-${VERSION}.mcpb"
STAGING="$(mktemp -d -t rhino-gh-mcp-mcpb-XXXX)"
trap 'rm -rf "$STAGING"' EXIT

echo "→ Staging $VERSION in $STAGING"

# 1. Manifest + launcher + requirements + icon.
cp mcpb/manifest.json "$STAGING/manifest.json"
mkdir -p "$STAGING/server"
cp mcpb/bootstrap.py "$STAGING/server/bootstrap.py"
cp mcpb/requirements.txt "$STAGING/server/requirements.txt"
cp plugins/grasshopper/src/Resources/Icon.png "$STAGING/icon.png"

# 2. Server source (Python package).
mkdir -p "$STAGING/server/src"
cp -R server/src/rhino_gh_mcp "$STAGING/server/src/rhino_gh_mcp"

# 3. Skills directory — kept at bundle root because skills.py resolves it
#    via Path(__file__).parents[3]/'skills', matching this layout.
cp -R skills "$STAGING/skills"

# 4. Clean caches + macOS detritus.
find "$STAGING" -type d -name __pycache__ -exec rm -rf {} + 2>/dev/null || true
find "$STAGING" -name ".DS_Store" -delete 2>/dev/null || true

# 5. Sanity check: manifest references entry_point we actually shipped.
python3 - <<PY
import json, pathlib, sys
manifest = json.load(open("$STAGING/manifest.json"))
ep = pathlib.Path("$STAGING") / manifest["server"]["entry_point"]
if not ep.is_file():
    sys.exit(f"manifest entry_point missing in bundle: {ep}")
print(f"   entry_point ok: {ep.relative_to('$STAGING')}")
PY

# 6. Zip — MCPB / .dxt is a plain zip with manifest.json at the root.
mkdir -p dist
rm -f "$OUTPUT"
(cd "$STAGING" && zip -r -q -X "$OUTPUT" . -x "*.DS_Store" -x "*/__pycache__/*")

SIZE=$(du -h "$OUTPUT" | cut -f1)
echo ""
echo "Built $(basename "$OUTPUT") (${SIZE})"
echo "  path: $OUTPUT"
echo ""
echo "Install in Claude Desktop: double-click the .mcpb file, or drag it into the"
echo "Extensions panel. First launch pip-installs ~5 deps into the extension dir"
echo "(takes ~30 s, one-time). Python 3.11+ must be on PATH."

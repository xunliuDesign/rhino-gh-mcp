#!/usr/bin/env bash
# Build + reinstall the rhino-gh-mcp .gha into Grasshopper's Libraries folder.
# Handles the macOS quirks: forces Rhino to quit if running, removes the
# Gatekeeper quarantine xattr so the new .gha loads without manual approval,
# and reports the final install path + assembly version.
#
# Usage:
#   ./reinstall.sh           # build + install
#   ./reinstall.sh --skip-build   # install whatever's already in bin/Release
#   ./reinstall.sh --no-kill      # don't try to kill Rhino if it's running

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

SKIP_BUILD=0
NO_KILL=0
for arg in "$@"; do
    case "$arg" in
        --skip-build) SKIP_BUILD=1 ;;
        --no-kill)    NO_KILL=1 ;;
        *) echo "Unknown arg: $arg" >&2; exit 2 ;;
    esac
done

# 1. Make sure Rhino isn't running — otherwise the new .gha won't load.
if [[ $NO_KILL -eq 0 ]]; then
    if pgrep -x "Rhinoceros" >/dev/null 2>&1; then
        echo "⚠️  Rhino is running. Quitting it cleanly so the new .gha can load."
        echo "    (Re-run with --no-kill to skip this.)"
        osascript -e 'quit app "Rhinoceros"' 2>/dev/null || true
        sleep 2
        if pgrep -x "Rhinoceros" >/dev/null 2>&1; then
            echo "    Rhino didn't quit cleanly. Force-killing."
            killall Rhinoceros 2>/dev/null || true
            sleep 1
        fi
        echo "    Done."
    fi
fi

# 2. Build.
if [[ $SKIP_BUILD -eq 0 ]]; then
    echo "→ Building (Release)..."
    dotnet build -c Release -v quiet | tail -5
fi

BUILT="$SCRIPT_DIR/bin/Release/net7.0/RhinoGhMcp.gha"
if [[ ! -f "$BUILT" ]]; then
    echo "❌ Expected built .gha not found at: $BUILT" >&2
    exit 3
fi

# 3. Locate Grasshopper's Libraries folder (the path includes a profile UUID
# on Mac, so glob).
shopt -s nullglob
LIBRARIES_CANDIDATES=( "$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper "*"/Libraries" )
shopt -u nullglob
if [[ ${#LIBRARIES_CANDIDATES[@]} -eq 0 ]]; then
    echo "❌ Could not find Grasshopper's Libraries folder under ~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/" >&2
    echo "   Start Rhino once + launch Grasshopper to create the folder, then re-run." >&2
    exit 4
fi
LIBRARIES="${LIBRARIES_CANDIDATES[0]}"

# 4. Remove any prior rhino-gh-mcp .gha (v0 or v1) so we don't get duplicates.
echo "→ Removing prior MCP .gha files in $LIBRARIES"
for f in "$LIBRARIES"/RhinoGhMcp.gha "$LIBRARIES"/rhino_gh_mcp*.gha; do
    [[ -f "$f" ]] && { echo "   removing $(basename "$f")"; rm "$f"; }
done

# 5. Copy + strip quarantine xattr.
DEST="$LIBRARIES/RhinoGhMcp.gha"
echo "→ Copying built .gha to $DEST"
cp "$BUILT" "$DEST"
xattr -d com.apple.quarantine "$DEST" 2>/dev/null || true

# 6. Report.
echo ""
echo "✅ Installed $(basename "$DEST")"
echo "   path:     $DEST"
echo "   md5:      $(md5 -q "$DEST" 2>/dev/null || md5sum "$DEST" | cut -d' ' -f1)"
echo "   version:  $(strings "$DEST" | grep -E '^0\.1\.[0-9]+\.[0-9]+$' | sort -u | head -1)"
echo ""
echo "Now launch Rhino 8, open Grasshopper, drop the MCP Server (v1) component"
echo "on a NEW canvas, set Run=True, and check the Version output."

#!/usr/bin/env bash
# Build the rhino-gh-mcp Rhino plugin (.rhp). Unlike the .gha (which auto-loads
# from the Grasshopper Libraries folder), Rhino's plugin loader requires
# explicit registration the first time. This script handles both cases:
#
#   - First-time install: prints the absolute path to the built .rhp and
#     instructs you to drag it onto Rhino's main window OR install via
#     _PluginManager. Rhino registers the plugin in its config and remembers
#     the path.
#
#   - Subsequent updates: detects the registered install location and
#     overwrites the .rhp there, so a normal Rhino restart picks up the
#     new build.
#
# Usage:
#   ./reinstall.sh           # build + try to update existing install
#   ./reinstall.sh --skip-build
#   ./reinstall.sh --no-kill # don't force-quit Rhino if running

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

# 1. Force-quit Rhino if running.
if [[ $NO_KILL -eq 0 ]]; then
    if pgrep -x "Rhinoceros" >/dev/null 2>&1; then
        echo "⚠️  Rhino is running. Quitting it so the new .rhp can load."
        osascript -e 'quit app "Rhinoceros"' 2>/dev/null || true
        sleep 2
        if pgrep -x "Rhinoceros" >/dev/null 2>&1; then
            killall Rhinoceros 2>/dev/null || true
            sleep 1
        fi
    fi
fi

# 2. Build.
if [[ $SKIP_BUILD -eq 0 ]]; then
    echo "→ Building (Release)..."
    dotnet build -c Release -v quiet | tail -5
fi

BUILT="$SCRIPT_DIR/bin/Release/net7.0/RhinoGhMcpRhino.rhp"
if [[ ! -f "$BUILT" ]]; then
    echo "❌ Expected built .rhp not found at: $BUILT" >&2
    exit 3
fi
xattr -d com.apple.quarantine "$BUILT" 2>/dev/null || true

# 3. Look for an existing registered install. Rhino's plugin folder format is
#    "$DISPLAY_NAME ($PLUGIN_GUID)" under ~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/.
PLUGIN_GUID="3f88bb55-3368-4204-9d0a-55911c9349ee"
shopt -s nullglob
INSTALLED=( "$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/"*"${PLUGIN_GUID}"*"/"*.rhp )
shopt -u nullglob

if [[ ${#INSTALLED[@]} -gt 0 ]]; then
    DEST="${INSTALLED[0]}"
    echo "→ Updating existing install at:"
    echo "    $DEST"
    cp "$BUILT" "$DEST"
    xattr -d com.apple.quarantine "$DEST" 2>/dev/null || true
    echo ""
    echo "✅ Updated. Restart Rhino, then run \`_ToggleMcpService\` to flip the listener on."
    echo "   md5:     $(md5 -q "$DEST" 2>/dev/null || md5sum "$DEST" | cut -d' ' -f1)"
    echo "   version: $(strings "$DEST" | grep -E '^0\.1\.[0-9]+\.[0-9]+$' | sort -u | head -1)"
else
    cat <<EOF

⚠️  First-time install — Rhino doesn't know about this plugin yet.

Drag the file below onto Rhino's main window, OR open Rhino and run the
\`_PluginManager\` command -> Install... -> browse to:

    $BUILT

Then restart Rhino, and run \`_ToggleMcpService\` to start the listener.

After that, this script will auto-update the install on subsequent runs.
EOF
fi

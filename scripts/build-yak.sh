#!/usr/bin/env bash
# Build Yak packages for both plugins. Output: dist/*.yak
#
# Yak is the McNeel package manager that ships with every Rhino install.
# A .yak file is a zip with manifest.yml at the root plus the .gha/.rhp
# (and any DLL dependencies) in target-framework subdirectories.
#
# The distribution tag (e.g. rh8_0-any) is auto-derived by `yak build` from
# the Rhino reference assemblies the plugin was compiled against.
#
# Prereqs:
#   - dotnet SDK 7+ (build the plugins)
#   - Rhino 8 installed (we use its bundled yak CLI)
#
# Usage:
#   ./scripts/build-yak.sh               # build both packages
#   ./scripts/build-yak.sh grasshopper   # just the .gha package
#   ./scripts/build-yak.sh rhino         # just the .rhp package
#   ./scripts/build-yak.sh --skip-build  # skip dotnet build, reuse bin/Release/

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Resolve yak. Prefer system Rhino's bundled yak; fall back to PATH.
YAK=""
for cand in \
    "/Applications/Rhino 8.app/Contents/Resources/bin/yak" \
    "/c/Program Files/Rhino 8/System/yak.exe" \
    "$(command -v yak 2>/dev/null || true)"
do
    if [[ -x "$cand" ]]; then YAK="$cand"; break; fi
done
if [[ -z "$YAK" ]]; then
    echo "yak not found — install Rhino 8 first." >&2
    exit 2
fi
echo "→ Using yak: $YAK"

WHICH="both"
SKIP_BUILD=0
for arg in "$@"; do
    case "$arg" in
        grasshopper|rhino|both) WHICH="$arg" ;;
        --skip-build) SKIP_BUILD=1 ;;
        *) echo "Unknown arg: $arg" >&2; exit 2 ;;
    esac
done

ICON="$REPO_ROOT/plugins/grasshopper/src/Resources/Icon.png"
mkdir -p "$REPO_ROOT/dist"

build_one() {
    local kind="$1"             # grasshopper | rhino
    local plugin_dir="$REPO_ROOT/plugins/$kind"
    local manifest="$plugin_dir/manifest.yml"
    local ext tfms
    if [[ "$kind" == "grasshopper" ]]; then
        ext="gha"; tfms="net7.0 net7.0-windows"
    else
        ext="rhp"; tfms="net7.0"
    fi

    if [[ ! -f "$manifest" ]]; then
        echo "no manifest at $manifest" >&2; return 3
    fi

    if [[ $SKIP_BUILD -eq 0 ]]; then
        echo "→ Building $kind (Release)"
        (cd "$plugin_dir" && dotnet build -c Release -v quiet | tail -5)
    fi

    local staging
    staging="$(mktemp -d -t rhino-gh-mcp-yak-${kind}-XXXX)"

    cp "$manifest" "$staging/manifest.yml"
    cp "$ICON" "$staging/icon.png"

    local found=0
    for tfm in $tfms; do
        local built="$plugin_dir/bin/Release/$tfm"
        # Find the plugin output deterministically by extension.
        local file
        file=$(find "$built" -maxdepth 1 -name "*.$ext" -print -quit 2>/dev/null || true)
        if [[ -z "$file" ]]; then
            echo "  (skipping $tfm — no $ext under $built)" >&2
            continue
        fi
        mkdir -p "$staging/$tfm"
        cp "$file" "$staging/$tfm/"
        # Ship runtime dep alongside the plugin.
        for dll in "$built/Newtonsoft.Json.dll"; do
            [[ -f "$dll" ]] && cp "$dll" "$staging/$tfm/"
        done
        found=1
    done
    if [[ $found -eq 0 ]]; then
        echo "no .$ext outputs found — did the build succeed?" >&2; return 4
    fi

    echo "→ yak build for $kind"
    (cd "$staging" && "$YAK" build) | tail -5

    local pkg
    pkg=$(find "$staging" -maxdepth 1 -name "*.yak" -print -quit)
    if [[ -z "$pkg" ]]; then
        echo "yak build produced no .yak" >&2; return 5
    fi
    cp "$pkg" "$REPO_ROOT/dist/"
    echo "   $(basename "$pkg") ($(du -h "$pkg" | cut -f1)) -> dist/"
    rm -rf "$staging"
}

if [[ "$WHICH" == "both" || "$WHICH" == "grasshopper" ]]; then
    build_one grasshopper
fi
if [[ "$WHICH" == "both" || "$WHICH" == "rhino" ]]; then
    build_one rhino
fi

echo ""
echo "Done. To publish to the McNeel Yak server:"
echo "  $YAK login                                      # one-time, opens browser"
echo "  $YAK push dist/rhinogh-mcp-grasshopper-*.yak"
echo "  $YAK push dist/rhinogh-mcp-rhino-*.yak"

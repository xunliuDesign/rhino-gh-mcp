#!/usr/bin/env bash
# "I just landed on this machine, get me up to speed" — one-shot sync.
#
# Pulls latest, refreshes Python deps, runs the server-side tests, and
# prints the recent commit history + the tail of docs/handoff.md so you
# can immediately see what happened on the other machine.
#
# This does NOT rebuild the C# plugins. Plugins are per-machine: run
# plugins/grasshopper/reinstall.sh or plugins/rhino/reinstall.sh when
# you actually need a fresh .gha / .rhp.
#
# Usage:
#   ./scripts/sync.sh
#   ./scripts/sync.sh --no-test    # skip pytest

set -euo pipefail

cd "$(dirname "$0")/.."
REPO_ROOT="$PWD"

RUN_TESTS=1
for arg in "$@"; do
    case "$arg" in
        --no-test) RUN_TESTS=0 ;;
        *) echo "Unknown arg: $arg" >&2; exit 2 ;;
    esac
done

echo "→ Pulling latest from main..."
git fetch origin
git pull --rebase --autostash

echo ""
echo "→ Syncing Python deps in server/..."
( cd server && uv sync --extra dev )

if [[ $RUN_TESTS -eq 1 ]]; then
    echo ""
    echo "→ Running server smoke tests..."
    ( cd server && uv run pytest -q )
fi

echo ""
echo "→ Recent commits:"
git log --oneline -5

echo ""
echo "→ Tail of docs/handoff.md (session log):"
echo "  --------------------------------------"
tail -25 "$REPO_ROOT/docs/handoff.md" | sed 's/^/  /'
echo "  --------------------------------------"
echo ""
echo "✅ Ready. To resume in Claude Code, the canonical first prompt is:"
echo '   "Read docs/handoff.md and tell me what to work on next."'

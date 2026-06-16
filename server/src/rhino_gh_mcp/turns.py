"""Turn-bounded change tracking — Coach mode infrastructure.

Each AI response is one "turn". The server brackets it with
`gh_begin_turn` / `gh_end_turn` calls, and the bridge records every canvas
GUID that gets created, modified, or wired during that turn. At end_turn,
the bridge surfaces the change set so the AI can self-narrate
("What I changed in this turn: …") and — in v0.2.x — paint highlight
badges on the canvas.

This module owns the *server-side* state. The bridge (C#) has its own
parallel state — see `RecordTurnChange` in McpServerComponent.cs — but the
canvas-side state and the server-side state can diverge briefly (e.g. if
the bridge restarts mid-turn). The server-side state is the source of truth
for the AI's "what I just did" prose; the canvas state is what gets painted.

Why not just rely on the canvas? Because some MCP clients reset the bridge
connection between turns (e.g. Claude Desktop's "New chat" button), and we
still want to be able to summarise a single completed AI response without
depending on a long-lived bridge channel.
"""

from __future__ import annotations

import threading
from dataclasses import dataclass, field


@dataclass
class TurnState:
    """Mutable per-turn record of the canvas GUIDs the AI touched."""

    turn_id: int
    added_guids: set[str] = field(default_factory=set)
    modified_guids: set[str] = field(default_factory=set)
    wired_pairs: list[tuple[str, str]] = field(default_factory=list)
    is_complete: bool = False


class TurnTracker:
    """Thread-safe registry of in-progress + completed turns.

    Lifecycle: one instance per server process, owned by the FastMCP app
    lifespan. Lives in the lifespan dict so tools can fetch it through
    Context.request_context (mirrors the bridges' pattern).
    """

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._next_id = 0
        self._turns: dict[int, TurnState] = {}
        self._current_turn_id: int = 0  # 0 means "no active turn"

    @property
    def current_turn_id(self) -> int:
        return self._current_turn_id

    def begin(self) -> int:
        """Start a new turn. Returns the new turn id (monotonic, > 0)."""
        with self._lock:
            self._next_id += 1
            turn_id = self._next_id
            self._turns[turn_id] = TurnState(turn_id=turn_id)
            self._current_turn_id = turn_id
            return turn_id

    def end(self, turn_id: int | None = None) -> TurnState | None:
        """Mark a turn complete. Defaults to ending the current turn.

        Returns the finalized TurnState (or None if the id was unknown).
        Does NOT clear the highlight set — call `dismiss(turn_id)` to do that.
        """
        with self._lock:
            target = turn_id if turn_id is not None else self._current_turn_id
            if target == 0:
                return None
            state = self._turns.get(target)
            if state is None:
                return None
            state.is_complete = True
            if target == self._current_turn_id:
                self._current_turn_id = 0
            return state

    def dismiss(self, turn_id: int | None = None) -> int:
        """Clear highlight state for one turn (or all turns).

        Returns the number of turns cleared. Mirrors the bridge-side
        `dismiss_highlights` command — but the bridge is the only thing
        with canvas pixels to clear; this server-side state is just
        bookkeeping.

        Passing turn_id=None or turn_id=0 clears ALL turns (matches the
        C# bridge's identical convention so tools that pass through 0 from
        an unset MCP arg behave consistently with explicit `None`).
        """
        with self._lock:
            if turn_id is None or turn_id == 0:
                n = len(self._turns)
                self._turns.clear()
                self._current_turn_id = 0
                return n
            if turn_id in self._turns:
                del self._turns[turn_id]
                if self._current_turn_id == turn_id:
                    self._current_turn_id = 0
                return 1
            return 0

    def record_added(self, guid: str, *, turn_id: int | None = None) -> None:
        """Tag a GUID as added during a turn (defaults to the current one)."""
        with self._lock:
            target = turn_id if turn_id is not None else self._current_turn_id
            if target == 0:
                return
            state = self._turns.get(target)
            if state is None or state.is_complete:
                return
            state.added_guids.add(guid)

    def record_modified(self, guid: str, *, turn_id: int | None = None) -> None:
        with self._lock:
            target = turn_id if turn_id is not None else self._current_turn_id
            if target == 0:
                return
            state = self._turns.get(target)
            if state is None or state.is_complete:
                return
            state.modified_guids.add(guid)

    def record_wire(self, src_guid: str, dst_guid: str, *,
                    turn_id: int | None = None) -> None:
        with self._lock:
            target = turn_id if turn_id is not None else self._current_turn_id
            if target == 0:
                return
            state = self._turns.get(target)
            if state is None or state.is_complete:
                return
            state.wired_pairs.append((src_guid, dst_guid))

    def get(self, turn_id: int) -> TurnState | None:
        with self._lock:
            return self._turns.get(turn_id)

    def turn_count(self) -> int:
        with self._lock:
            return len(self._turns)

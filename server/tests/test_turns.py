"""Tests for the turn-tracking state machine (Coach mode infrastructure)."""

from __future__ import annotations

import threading

import pytest

from rhino_gh_mcp.turns import TurnTracker


def test_fresh_tracker_has_no_active_turn():
    t = TurnTracker()
    assert t.current_turn_id == 0
    assert t.turn_count() == 0


def test_begin_then_end_single_turn():
    t = TurnTracker()
    turn_id = t.begin()
    assert turn_id == 1
    assert t.current_turn_id == 1

    state = t.end(turn_id)
    assert state is not None
    assert state.is_complete is True
    # Current turn cleared after end()
    assert t.current_turn_id == 0


def test_begin_increments_monotonically():
    t = TurnTracker()
    a = t.begin()
    t.end(a)
    b = t.begin()
    assert b == a + 1
    assert b == 2


def test_record_added_attributes_to_current_turn():
    t = TurnTracker()
    turn_id = t.begin()
    t.record_added("guid-1")
    t.record_added("guid-2")
    state = t.end(turn_id)
    assert state is not None
    assert state.added_guids == {"guid-1", "guid-2"}


def test_record_outside_turn_is_noop():
    """Recording with no active turn must not raise — it just drops the event."""
    t = TurnTracker()
    t.record_added("guid-x")  # no current turn
    assert t.turn_count() == 0


def test_record_after_end_is_noop():
    t = TurnTracker()
    turn_id = t.begin()
    t.end(turn_id)
    # Records on a complete turn should be ignored.
    t.record_added("guid-late")
    assert t.get(turn_id).added_guids == set()


def test_record_modified_separate_from_added():
    t = TurnTracker()
    tid = t.begin()
    t.record_added("a")
    t.record_modified("b")
    state = t.end(tid)
    assert state.added_guids == {"a"}
    assert state.modified_guids == {"b"}


def test_record_wire_keeps_ordered_pairs():
    t = TurnTracker()
    tid = t.begin()
    t.record_wire("src1", "dst1")
    t.record_wire("src2", "dst2")
    state = t.end(tid)
    assert state.wired_pairs == [("src1", "dst1"), ("src2", "dst2")]


def test_dismiss_specific_turn_only_clears_that_one():
    t = TurnTracker()
    a = t.begin()
    t.end(a)
    b = t.begin()
    t.record_added("guid-b")
    cleared = t.dismiss(a)
    assert cleared == 1
    # turn b should still be alive
    assert t.current_turn_id == b
    assert t.get(b).added_guids == {"guid-b"}


def test_dismiss_all_with_no_argument():
    t = TurnTracker()
    t.begin()
    t.begin()
    t.begin()
    cleared = t.dismiss()
    assert cleared == 3
    assert t.turn_count() == 0
    assert t.current_turn_id == 0


def test_dismiss_unknown_id_returns_zero():
    t = TurnTracker()
    assert t.dismiss(999) == 0


def test_end_unknown_id_returns_none():
    t = TurnTracker()
    assert t.end(999) is None


def test_end_default_uses_current_turn():
    t = TurnTracker()
    tid = t.begin()
    state = t.end()  # no argument
    assert state is not None
    assert state.turn_id == tid


def test_explicit_turn_id_targets_specific_turn():
    """Record into a non-current turn by passing turn_id explicitly."""
    t = TurnTracker()
    a = t.begin()  # current
    b = t.begin()  # now current
    t.record_added("for-a", turn_id=a)
    t.record_added("for-b")  # defaults to current = b
    state_a = t.end(a)
    state_b = t.end(b)
    assert state_a.added_guids == {"for-a"}
    assert state_b.added_guids == {"for-b"}


def test_concurrent_begin_calls_are_safe():
    """Begin() under contention must hand out unique monotonic ids."""
    t = TurnTracker()
    ids: list[int] = []
    lock = threading.Lock()

    def go():
        for _ in range(50):
            tid = t.begin()
            with lock:
                ids.append(tid)

    threads = [threading.Thread(target=go) for _ in range(4)]
    for th in threads:
        th.start()
    for th in threads:
        th.join()

    assert len(ids) == 200
    assert len(set(ids)) == 200  # all unique
    assert sorted(ids) == list(range(1, 201))


@pytest.mark.parametrize("turn_id_arg", [None, 0])
def test_dismiss_none_or_zero_clears_all(turn_id_arg):
    """dismiss(None) and dismiss(0) both clear everything (matches C# bridge)."""
    t = TurnTracker()
    t.begin()
    t.begin()
    if turn_id_arg is None:
        n = t.dismiss()
    else:
        n = t.dismiss(turn_id_arg)
    assert n == 2
    assert t.turn_count() == 0

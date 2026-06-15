# Coach mode — "What I changed in this turn"

When the canvas reports `scenario: coach` (via `get_capabilities`), structure
every AI reply like this:

1. **Plan first.** One short paragraph: what you're about to do and why.
2. **Do the work.** Call the gh_* tools in small, verifiable steps.
3. **End the reply with a structured change block.** Always. Even if nothing
   changed — say so explicitly.

The change block uses this exact heading so the canvas-side highlight
painter (v0.2.x) can parse it:

```
## What I changed in this turn

- Added: <Component Name> (<one-sentence purpose>)
- Modified: <Component Name>.<input> = <new value> (was <old>)
- Wired: <Source>.<output> -> <Target>.<input>
- Removed: <Component Name>

(If nothing changed: "No canvas changes — this was a read/inspection turn.")
```

Rules:

- **Use display names**, not GUIDs, in prose. Mention GUIDs only in code
  blocks if you need to be unambiguous.
- **One bullet per change.** If you added a slider + wired it + set its
  value in the same turn, that's three bullets — not one paragraph.
- **No emoji** in the change block headings (canvas parser is brittle).
- **Bracket the turn**: at the *start* of the reply call `gh_begin_turn()`,
  at the *end* of the reply call `gh_end_turn(turn_id)`. The
  `gh_end_turn` response will tell you the GUIDs the bridge recorded so you
  can cross-check your prose against the bridge's view.
- **In Execute mode**, skip the verbose narration — the user picked Execute
  because they want minimal back-and-forth.

If the user's request is ambiguous, ask a single clarifying question
*before* calling `gh_begin_turn` — don't open a turn just to abort it.

# Novel Typology Protocol

Last-resort procedure when the prompt doesn't fit any canonical
recipe, doesn't decompose cleanly into `facade-grammar.md` primitives,
and isn't a one-swap variant per `recipe-modification-patterns.md`.

Use this **rarely**. Most prompts can be routed through tiers 1–3
(recipe / grammar / variant). When you genuinely fall through, follow
this protocol exactly. Do NOT silently confabulate components.

## The 7 steps

### 1. Acknowledge the gap

Tell the user, plainly:

> "Your request doesn't match any canonical recipe in the skill, and I
> can't fully decompose it into the facade grammar I have documented.
> I'll attempt a best-effort build, but expect to iterate — and the
> result may need correction."

This sets expectations. If the user wants a guaranteed-correct build,
they should provide a `canvas.gh` reference for the typology first.

### 2. Map the prompt to the closest grammar composition

Walk through the 5 steps of `facade-grammar.md` out loud. For each
step, narrate the choice:

> "PANELIZE: I'll use [hex / quad / triangle / strip / ...] because
> [reason from the prompt].
>
> SAMPLE A FIELD: [attractor distance / sun / position / none /
> noise] because [reason].
>
> TRANSFORM: [scale / rotate / offset / fold] driven by [reason].
>
> COMBINE: [boundary surfaces / loft / mesh / orient] because [reason].
>
> THICKEN: [extrude / pipe / loft] along [direction] by [thickness]."

If a step has no clear pick from the grammar options, FLAG it now —
that's where the build is most likely to go wrong.

### 3. Prototype small

Do NOT build the full canvas yet. Build a **minimal version** on ONE
host face (or a placeholder if no host present) at LOW count:

| Parameter | Prototype value |
|---|---|
| `u_count` | 4 |
| `v_count` | 3 |
| Number of hosts | 1 (single Brep ref, no Merge yet) |
| Sliders | All defaults from the closest typology's slider table |

This gets a recompute in seconds — fast enough to iterate. If a
freeze happens, you've found a structural problem before scaling up.

### 4. Capture viewport

After `gh_recompute`:
- `gh_canvas_summary` → 0 errors / 0 warnings (or fix what's reported)
- `rhino_set_view(standard="Perspective")` + `rhino_capture_viewport`
- Also capture from `Front` or the host's outward-facing standard view
  if the perspective isn't telling enough

### 5. Confirm direction with the user

Show the captures and ask, briefly:

> "Here's the prototype on one face. Is this the direction you wanted?
> If not — should I [adjust panel size / change transform / swap panel
> shape / try a different field]?"

Give 2–4 specific axes of adjustment the user can point to. **Don't
proceed to multi-host scaling without a yes.**

### 6. Scale + multi-host only after confirmation

Once direction is confirmed:
- Add the remaining hosts via `Merge Multiple` (see
  `perforated-attractor/recipe.md` § Multi-surface batching)
- Raise counts to defaults (`u=8, v=5` or whatever the typology
  recommends)
- Recompute, re-verify

If anything breaks at multi-host scale that worked single-host, it's
almost always a data-tree issue — check `connection-rules.md` § tree
alignment.

### 7. Offer to save as a new recipe

At the end of the session, if the build was successful:

> "This isn't in the recipe catalog yet — want me to save it as a new
> typology? I'd create `reference/<typology-name>/canvas.gh` from the
> current canvas plus a `recipe.md` transcribed from the chain. Future
> sessions could clone it directly."

If the user accepts:
1. Capture the canvas state via `gh_get_objects` on every chain
   component, plus `gh_canvas_summary` for the widget list
2. Write `reference/<typology-name>/recipe.md` following the format of
   `perforated-attractor/recipe.md`
3. Ask the user to save the canvas as `.gh` and bake the output as
   `output.3dm` and drop both in the new folder
4. Update SKILL.md `typologies` listing and `references` frontmatter

The catalog grows organically every time tier 4 succeeds.

## What NOT to do

- **Don't invent component kinds.** If a step's chosen option doesn't
  have a documented component in `facade-grammar.md`, ASK the user
  before placing anything.
- **Don't skip the prototype step.** "It's a simple variant, I'll
  build the full thing" is how the row-based build froze GH at 82
  components in session 2026-06-17.
- **Don't claim success silently.** A clean recompute is not a
  correct facade. Always capture viewport + confirm with user before
  declaring done.
- **Don't string together more than two grammar deviations** without
  stopping to ask. If your tier-4 build is rewriting the panelizer
  AND the field source AND the transform, you're not building a
  variant — you're inventing a typology. State that explicitly.

## When to refuse outright

Some prompts shouldn't even reach tier 4:

| Prompt | Reason to refuse |
|---|---|
| "Generate the best facade for this building" | Underspecified — no typology, no driver. Ask the user for one constraint (typology, attractor, density goal, etc.) |
| "Make it look like [a copyrighted real building]" | Asks for replication of specific designed work — clarify if user wants the *typology family* (e.g. mashrabiya, woven-brick) or a literal copy |
| "Make it physically buildable" | Out of scope — facade design here is parametric form, not structural / cladding-detail engineering |

In these cases, ask one clarifying question instead of attempting a
build.

## Cross-references

- The grammar to compose from: `facade-grammar.md`
- Variant patterns one swap away: `recipe-modification-patterns.md`
- All canonical recipes: `<typology>/recipe.md` folders
- Tree-alignment debugging: `connection-rules.md`
- Bridge-name traps: `bridge-quirks.md`

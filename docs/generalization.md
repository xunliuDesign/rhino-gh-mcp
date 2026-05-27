# Generalization — architectural modeling toolkit, teaching tool

A thinking-out-loud document on extending `rhino-gh-mcp` beyond "drive
my own research" into something useful to other architects and to
students. Not a plan — a starting point for you to react to.

## Where the project sits today

The substrate is already general-purpose enough that **most** of what
follows is a packaging / docs / curation question, not a re-architecture
question. The tool surface (39 tools across read / parameter-write /
component-write / scripting) covers the basic vocabulary that any
architectural workflow needs. The capability gate + ComponentScope
inputs already encode the safe-by-default story for non-expert users.

So the question is mostly: **what extra layers turn a research substrate
into a product?**

## Audience-level framing

Three audience archetypes worth designing for separately, even though
the underlying server is one binary:

### A. The architect using it for their own practice

What they need:
- A working install that survives Rhino updates
- A library of Skills covering the workflows they actually do daily
  (site analysis, massing, panelisation, schematic floor plans,
  documentation export)
- A way to bring their own user-objects into a Skill so they can
  curate their personal style

What we should provide:
- **Skill marketplace** (informal, just a curated index) — a `docs/skills-catalog.md`
  listing community-contributed Skills with screenshots, scope, and
  required plugins.
- **Skill authoring guide** going beyond `skills/README.md` — show how to
  go from "system prompt I keep pasting" → packaged Skill in 30 minutes.
- **Curated component packs**: ship small `.ghuser` bundles for common
  parametric primitives (massing, façade, structural grid) so a Skill
  can rely on a well-defined vocabulary instead of stock GH gymnastics.

### B. The teacher / TA running a studio or workshop

What they need:
- Confidence the system won't run away (no random script execution by
  students)
- Per-student session isolation (one canvas per student, one MCP server
  per student)
- A "graduate the capabilities" arc (start parameter-only, add
  components later, scripting only for advanced exercises)
- Worksheet-style Skills aligned with their syllabus

What we should provide:
- **A `--max-policy` CLI flag** that sets the *ceiling* of what canvas
  flips can enable — the two-layer model we discussed but never built.
  Teacher launches the server with `--max-policy curated`, the student
  cannot escalate to scripting no matter what they flip on the canvas.
- **A "classroom mode" Skill** that's just a tutorial-style scripted
  walkthrough: "Step 1: place a slider. Now drag it. Step 2: add a
  Circle component. Connect them."
- **Per-student port allocation** — currently both ports are
  hardcoded. A teacher running this in a lab needs each student's
  server on a different port. Trivial config change but needs to be
  documented.
- **An attestation log** — for grading, optionally record every tool
  call the student-driven LLM makes. Already most of the way there
  (the .gha has a 1000-line ring buffer); just needs to be persistable
  and exportable.

### C. The student learning Rhino / Grasshopper by collaborating with the LLM

What they need:
- Friction-free install (no command line)
- Honest pedagogy — the LLM should be a tutor, not just a button
- Immediate visual feedback after every step

What we should provide:
- **`.dxt` package** for one-click install in Claude Desktop. (Already
  on the roadmap as P7.)
- **Skills with "explain mode" prompting** — the SKILL.md tells the LLM
  to narrate each action it takes, not just take it. Could add a
  `pedagogical: true` frontmatter flag that triggers this behavior.
- **A welcome canvas** — when the user first opens the MCP component
  the very first time, the .gha could auto-place a Panel that says
  "AI is connected. Try asking it 'What can you see on this canvas?'".
- **An "undo bias" in writes** — every component-create call could
  return enough info to undo it in one call, so the LLM can offer
  "want me to undo that?" naturally.

## Cross-cutting ideas

### Skill composition

Right now Skills are flat — one workflow, no inheritance. Real
architectural projects layer workflows: site analysis ↑ massing ↑
façade ↑ documentation. A `requires:` frontmatter that lists other
Skill IDs would let a composite Skill ("schematic-design") import its
parts. Implementation isn't trivial (depends on the MCP client's
Skill loader supporting composition) but worth thinking about.

### "Inspect the LLM's plan first" affordance

For complex tasks (anything > 5 tool calls), it'd be useful for the
LLM to show its plan in chat *before* it starts mutating the canvas.
This is mostly a prompt-engineering pattern that lives in Skills —
add a `multi_step_plan: true` flag that prompts the LLM to draft a
numbered plan, get user buy-in, then execute. Could become a SKILL
authoring convention.

### Capture-and-replay for debugging

Every bridge call has a JSON envelope. Recording all calls in a
session to a file gives you a reproducible bug report. *"Look, this
sequence of 8 tool calls produces wrong output."* Useful both for
contributor bug reports and for testing changes to .gha handlers
without needing live Rhino each time.

### Generic, plugin-aware Skills

A Skill could auto-detect what's installed (Ladybug? Karamba? Pufferfish?)
via `gh_list_available_components` and adapt its workflow accordingly.
A "structural analysis" Skill might pick Karamba if available, fall
back to a simpler bending-moment approximation otherwise. The
infrastructure is there; just needs a `detect_plugin` helper in
`gh_read.py` and a documented convention for adaptive Skills.

### `gh_bake_to_rhino` — the missing piece

The single biggest gap in turning this into a real production tool is
that the LLM can build a parametric definition in GH but can't easily
"commit" it as actual Rhino geometry the user can edit. This is on the
P4 roadmap; once it lands, the architectural-toolkit story becomes much
more compelling.

## Honest limits

Some things won't fit this substrate cleanly and shouldn't be forced:

- **Long-running simulations** (CFD, energy modelling that takes
  minutes). The current synchronous bridge will time out. Need an
  async-job-with-callback pattern.
- **Large geometry transfers** between Rhino and external tools.
  Base64-encoded mesh data over the bridge is fine for screenshots,
  bad for production-scale geometry. For real geometry interop, the
  user should still use Rhino's native export → tool → import flow,
  not the MCP bridge.
- **Multi-document workflows**. Both bridges assume a single active
  Rhino document. Switching documents mid-session is untested and
  likely fragile.

## Suggested first steps if generalizing

1. **Write the "Skill authoring" guide** (one short document) so others
   can contribute Skills without reading the codebase.
2. **Add `--max-policy`** (the two-layer capability ceiling). Small
   feature, large pedagogical value.
3. **Ship a `.dxt` package** (P7 on the roadmap). Removes the install
   barrier for non-developer users.
4. **Curate 5-8 reference Skills** covering the daily-architectural
   vocabulary (site analysis, massing, façade panelisation, structural
   grid, documentation prep, environmental, landform, schematic floor
   plan). Each well-tested with a screenshot.
5. **Find one teacher to pilot a workshop**. Real classroom feedback
   beats any amount of design speculation.

Stop reading and go sleep.

# Morning status — overnight work (2026-05-21)

You went to sleep around midnight; this is what's staged. **Nothing is
committed.** Read this top-to-bottom, decide what to keep, then commit
(or ask me to commit) in whatever batch shape you like.

## TL;DR

12 files touched / created across docs, README, the first proper Skill,
and a Ladybug Skill drafted from the Ladybug Primer. Plugin compiles
clean (warnings 22 → 16 after dead-code cleanup). All 27 server tests
still pass. Component Scope default flipped to `1` (GH defaults) per
your last-minute fix. No pushes, no commits.

## Changes file-by-file

### New files (4)

| File | Purpose | Lines |
|---|---|---|
| `skills/landform/SKILL.md` | Proper Skill from your `systemprompt.txt`, translated to v0.1.6 tool names. References the 8 `.ghuser` files in `MCP_UserObjects_Landscape example/`. | ~140 |
| `skills/ladybug-environmental/SKILL.md` | Documentation-only draft. Three workflows: sun path, sun-hours analysis, wind rose. Authored from the [Ladybug Primer](https://docs.ladybug.tools/ladybug-primer/) and component reference. **Not tested live** since you don't have Ladybug installed (or do you? I don't know). | ~180 |
| `docs/performance-notes.md` | Audit of perf opportunities. 8 items sorted by impact/effort. Biggest win: cache `get_context` between read tools (one PR, ~50 lines, eliminates 3-5x redundant round-trips per LLM exploration). | ~110 |
| `docs/generalization.md` | Design note on extending into architectural-modeling toolkit + teaching tool. Three audience archetypes (architect / teacher / student), proposed `--max-policy` ceiling feature for classroom use, Skill composition ideas. | ~140 |

### Modified files (6)

| File | What changed |
|---|---|
| `README.md` | Full rewrite, install-first focus. Audience: new user discovering the repo. Covers prerequisites, install both plugins on Mac+Win, wire into Claude Desktop / Code, smoke-test, basic usage prompts, troubleshooting, roadmap. Old status moved to `handoff.md` (where it lived already). **Zoning removed entirely.** |
| `docs/handoff.md` | (1) Goal #2 reworded to remove zoning reference. (2) Architecture diagram updated (`zoning` → `ladybug` in the skills list). (3) P5 row reworded. (4) P7 row genericised to "domain-specific RAG... not part of this repo". |
| `docs/architecture.md` | Rewritten. Reflects v0.1.5+ soft-gate model (was still describing the old hard-tier). New section explaining why soft-gate replaced hard-tier. Zoning removed. |
| `skills/README.md` | Conventions updated for new YAML frontmatter shape (`recommended_capabilities` / `recommended_scope`). Catalog rows for `landform` + `ladybug-environmental`. |
| `plugins/grasshopper/src/McpServerComponent.cs` | (1) `ComponentScope` default 0 → 1 (per your request). (2) Internal `currentComponentScope` field default 0 → 1. (3) Dead code cleanup: removed ~140 lines of unreachable mode-1 / mode-2 branches in `SetComponentParameter` (they were behind a const-0 since SetParameterMode input was dropped). (4) Removed unused `connectionLog` field. Build went 22 warnings → 16. |
| `plugins/grasshopper/RhinoGhMcp.csproj` | Version 0.1.5 → 0.1.6 (already bumped during your test session, before sleep). |

### Deleted files (2)

| File | Why |
|---|---|
| `skills/landform-from-contours/SKILL.md` | Stub, superseded by your new `skills/landform/` folder. You approved deletion. |
| `skills/landform-from-contours/userobjects/README.md` | Same. |

## What I deliberately did NOT do

- **Did not commit anything.** Per the rules, I don't commit without your explicit say-so each round.
- **Did not push.** Same.
- **Did not install v0.1.6** in your Grasshopper Libraries — your Rhino is running with v0.1.5 in memory. Install in the morning when you can restart Rhino.
- **Did not start `.dxt` packaging.** It benefits from your testing and Anthropic spec is still pre-1.0.
- **Did not act on any of the perf opportunities.** They're in `docs/performance-notes.md` waiting for your judgment.
- **Did not act on the generalization ideas.** Also waiting on you.

## Open questions to decide

1. **Do you actually have Ladybug installed in your Rhino?** If yes, the `ladybug-environmental` SKILL is worth a live test. If no, leave it as a documentation-only stub. (The SKILL file already says "documentation-only" upfront so it's honest either way.)
2. **Is the README's install flow actually correct for Mac?** I wrote both Mac and Windows install paths, but only the Windows one has been tested this session. The Mac path is based on what's already in the existing shell scripts.
3. **Want to act on perf items 1, 2, 3 today?** Or leave for later? Item 1 (context cache) is small and high-impact.
4. **Should the new `landform` SKILL use the older v0 tool names** (`get_all_component_proxies`, `expire_and_get_info`, `get_gh_context`, `recompute_all`) **alongside** the current ones for back-compat? I translated to current names but mentioned the old names in one place.

## Suggested commit shape

Either one fat commit or three small ones — your call:

**One commit (fast):**
- "Skills + docs overhaul + .gha cleanup (v0.1.6)"

**Three commits (cleaner history):**
1. `skills: add landform + ladybug-environmental, drop stub` — `skills/` only
2. `docs: install-first README, scrub zoning, refresh architecture, add perf + generalization notes` — `README.md`, `docs/**`
3. `.gha v0.1.6: ComponentScope default=1, dead-code cleanup` — `plugins/grasshopper/**`

If you want me to do the commits, just say which shape and I'll execute. Then we can talk about installing v0.1.6 + pushing to GitHub.

## Verification you can run right now

```bash
# Server still healthy
cd server && uv run pytest -q

# Repo state
cd .. && git status

# Read the new Skills (look for tool-name mistakes I might have made)
cat skills/landform/SKILL.md
cat skills/ladybug-environmental/SKILL.md
```

When you're done reading, your call on commit shape and whether to push.

Sleep was good for me too.

# Research & planning notes — 2026-05-27

> Compiled at the end of the Mac session, just before the project moves to a
> fresh VS Code session on the OneDrive copy. Mix of (a) web research into
> the AI-for-architecture landscape, (b) critical look at how this project's
> plan stands up against that landscape, (c) RA task suggestions, (d) future
> scope as a teaching tool. Followed by a curated prompt for a deep-thinking
> session in a new Claude chat.

## A. The landscape, 2026 — where this project sits

### A.1 Direct neighbours (LLM ↔ CAD)

The closest comparables sort into three tiers:

**Vendor-official (matters most for trajectory):**
- **Autodesk Fusion MCP** (local) and **Fusion Data MCP** (cloud) — first big-vendor MCP. Autodesk has publicly said Revit-MCP is next. Signal: MCP is now the accepted "AI surface" for CAD vendors. [Autodesk Fusion blog](https://www.autodesk.com/products/fusion-360/blog/introducing-the-fusion-mcp-opening-fusion-to-ai-powered-workflows/)
- **McNeel has NOT announced AI for Rhino 9** despite open community pressure. Rhino 9 dev meetings (Stockholm Oct 2025) focused on extensions, web tech, Rhino.Inside. This is the gap rhino-gh-mcp fills.

**Community efforts (the cohort):**
- Already-known: rhinomcp (jingcheng-chen, 406★), grasshopper-mcp (alfredatnycu, 76★), Cordyceps (forum, Jan 2026), blender-mcp.
- **ArchiLabs Revit MCP** — closest analogue in the BIM-side adjacent space.

**Generative-design SaaS (the commercial frame):**
- **Hypar** — Python design-recipes + agent platform. Sold to AEC firms as in-house automation.
- **Autodesk Forma** (née Spacemaker) — site-scale AI; "Neural CAD" demoed at AU 2025 for interior-layout generation from typology + materials.
- **SWAPP** — Revit *documentation* AI (tagging, annotation, sheet production). Mid-large firm market.
- **ArkDesign** — schematic plans + multi-family code compliance.
- **TestFit** — yield/feasibility for developers.
- **FORMAS.AI** — multimodal "AI orchestrator" (sketch→image→3D→video), pre-seed April 2026.
- **Speckle** — not an LLM product, but THE AEC data layer; the substrate everything else builds on. Community Speckle MCP exists.

**Where rhino-gh-mcp uniquely sits:** none of the above ship as `MCP-server + tiered-capability-flags + Skills` for *parametric* (not BIM) design, *teaching*-oriented, *open* (not seat-licensed). Closest competitor is Hypar (commercial, BIM-leaning) and Cordyceps (community, narrower scope).

### A.2 Academic literature (2024–2026)

Most-relevant papers to cite/read:

- **Savov, Yoo, Lin, Dillenburger — "Generalist Generative Agent: Open-Ended Design Exploration with LLMs"**, CAADRIA 2025. ETH CAD++/DBT. 1,000 designs from 20 metaphors. Closest academic precedent for this project. [ETH page](https://designplusplus.ethz.ch/research/ongoing-projects/interacting-with-architectural-generative-design-models-using-la.html)
- **"Mediating Modes of Thought: LLMs for Design Scripting"** (arXiv 2411.14485) — *parametric scripting* as the right mediation layer between LLMs and geometry. Justifies this project's whole stance.
- **CAD-Llama** (arXiv 2505.04481) — pretrained LLM for parametric 3D CAD generation.
- **CadVLM** (arXiv 2409.17457) — first multimodal LLM applied to parametric CAD sketches.
- **"Generative AI for CAD Automation"** survey (arXiv 2508.00843).
- **Building-energy LLM agents** (ScienceDirect S0926580525002845, 2025) — LLM agent + library for automated EnergyPlus modelling. Directly relevant if/when this project hits the environmental-analysis goal.
- **Multimodal LLMs in parametric CAD survey** (ScienceDirect S095741742501142X, 2025).

Gap: no peer-reviewed RAG-over-building-codes paper. UpCodes and ArkDesign do this commercially; academic literature on it is thin.

### A.3 Teaching tools

This is where the project has the strongest open ground.

- **Zhejiang University 2024–25** integrated AI into undergrad architecture studios — sequencing Grasshopper, environmental analysis, space syntax, and GenAI in Year 2. Closest published precedent for the virtual-TA goal. [MDPI Buildings 15/17/3069](https://www.mdpi.com/2075-5309/15/17/3069)
- **TU Delft "Computational Design for Industrial Designers using Rhino Grasshopper"** — strongest open-source GH curriculum. Excellent corpus to ground a RAG-tutor on. [interactivetextbooks.tudelft.nl](https://interactivetextbooks.tudelft.nl/rhino-grasshopper/Grasshopper_Rhino_course/intro.html)
- **SCI-Arc M.S. Architectural Intelligence** — explicit LLM/agentic-design curriculum.
- **Bartlett Architectural Computation MSc** — "latest AI techniques and AI-focused architectural theory."
- **Pedagogically-controlled AI tutor for CS** (arXiv 2512.11882) — curriculum-constrained GPT-4 tutor with no-spoiler policy. Methodology transferable to GH teaching.

Honest finding: the "AI design-crit partner" pattern is well-discussed in HCI but the architecture-studio version is mostly conference posters and Medium write-ups, not peer-reviewed. **There's a publishable thesis here.**

### A.4 Skills + MCP as a composable primitive

- Agent Skills launched Oct 2025, open standard Dec 2025. Reference SDK at `agentskills.io`.
- Production partners as of mid-2026: Atlassian, Figma, Canva, Stripe, Notion, Zapier — **all SaaS, no design/CAD bundle yet.**
- Distribution model: git folders of Markdown. Closer to dotfiles than to a vetted marketplace.
- **Skills (how to think) + MCP (how to act)** as a paired primitive is what this project is implicitly betting on. It's not unique conceptually but it's underexplored in AEC.

### A.5 Critical perspective

**Mature/real:** generative massing (Forma, Hypar), BIM doc automation (SWAPP), RAG-over-codes on bounded rule sets.

**Hype:** "AI architect" claims. LLMs hallucinate dimensions confidently. RLHF-tuned models gravitate toward generic-modernist aesthetic outputs (the implicit Carpo critique).

**Failure modes to design against:**
1. *Hallucinated dimensions* — model produces plausible-but-wrong setbacks, FAR, slab thicknesses. **Mitigation already in this project:** capability flags + hard-validated tool calls + slider range bounds.
2. *Loss of design intent* — agent optimises locally (max yield) and erodes the parti.
3. *Aesthetic flattening* — model defaults toward visual cliché.

**Critical voices worth citing in any published work:**
- **Mario Carpo, *Beyond Digital: Design and Automation at the End of Modernity*** (2023). Most coherent skeptical-but-engaged frame.
- **Daniel Cardoso Llach (CMU)** — *Builders of the Vision*; rigorous critical-history voice on CAD's military/managerial origins. His CodeLab now interrogates ML in architecture as a sociotechnical phenomenon.
- **Patrik Schumacher** — opposite stance ("ZHA uses AI across projects, authorship-by-curation"). Read as the bullish-industry frame.

## B. Critical look at this project's plan

What's working and what isn't, looking at it cold:

### Strong

- **Capability-flag refactor (v0.1.5+).** Soft-gate is a better fit than hard policy tiers — it lets the runtime react to canvas state without restart. Few competitors do this.
- **Two-plugin split** (.gha + .rhp). Reflects real Rhino/GH threading reality. Robust pattern.
- **Open + MIT + GitHub-first.** Aligns with where the academic-research mindshare is, vs. closed seat-licensed competitors.
- **Skills-as-recipe-bundles.** Skills + MCP is the right composable primitive, and AEC is empty space for it.
- **Teaching as a first-class goal** — not retrofit. This is the unique angle vs. Forma/Hypar/etc.
- **Cross-platform from day one.** Rhino's Mac/Win parity is one of its strengths; this project doesn't waste it.

### Risky / weak

- **No published-work output yet.** ETH (Dillenburger) and the CAADRIA/eCAADe crowd are publishing in this area; without a paper/preprint the project is invisible to academic citation. **High-leverage move: prepare a CAADRIA 2027 (Australia/NZ) or ACADIA 2027 submission this calendar year.**
- **Skills library has one entry.** Until 3–5 real workflows exist with `.ghuser` files and verifiable outputs, "Skills-as-recipes" is a claim not a feature. The landform Skill is now in the repo — needs to ship two more (façade panelisation, daylighting, structural grid).
- **Zero student/RA user-testing yet.** All design decisions so far reflect the developer's intuition. Run two GH learners through the L1 surface in a single afternoon and you'll find six unknowns.
- **Multimodal input not used.** Image-in for site context, sketch-to-massing — these are the obvious next experiments and they fit the current architecture without a refactor.
- **Web frontend deferred indefinitely.** Without a non-Claude-Desktop entry point the project's ceiling is "people who have a paid Claude subscription and tolerate JSON config." Will limit reach.
- **No public Rhino plugin (Yak) distribution.** Building from source is friction for non-CS architects. `yak push` is the missing distribution step.
- **The handoff document is good but session-scoped.** Long-term project memory should split: `README.md` (audience = new user), `docs/handoff.md` (audience = next Claude session), `CHANGELOG.md` (audience = downstream users). Currently `handoff.md` is doing all three.

### Architectural questions worth surfacing before they get baked

1. **Whose model gets used for the canonical `.ghuser` set?** If the Skills bundle includes the developer's own user-objects, the toolkit reflects one design pedagogy. Worth deciding deliberately whether to (a) curate one canonical set, (b) accept community contributions, (c) let each school ship its own.
2. **Should script-component injection be the dividing line between "open tool" and "research artifact"?** Allowing the LLM to write arbitrary C# inside Rhino's process is genuinely dangerous in classroom settings. The current `AllowScripting=False` default is the right call but the line should be drawn loudly in the docs.
3. **What's the right pedagogy for L1 / L2 / L3?** Is "graduate from L1 to L2 to L3" actually how skill builds, or is it more like Bloom's taxonomy (remember → apply → analyse → evaluate → create)? Worth an empirical look.

## C. RA task recommendations (undergrad, time-rich, less expert)

Your RA is a great asset *if* you give them work that:
- Doesn't require deep computational-design intuition (yet)
- Benefits from steady time investment
- Produces artifacts you can review without context-switching cost
- Builds their own expertise as a side-effect (so they become more useful over time)

**Don't give them:** core C# bridge work, capability-system architecture, anything that demands "I see why this whole thing fits together."

### Tier 1 — Immediate-value, ship-this-month

1. **Build the Skills library.** Take 3 published GH workflows you trust (façade panelisation, structural grid, daylighting setup) and convert each into a Skill bundle. Each Skill = `.ghuser` files + `SKILL.md` step-by-step + a sample `.gh` reference file. **2 weeks per Skill** including testing with you. Outcome: the project goes from "1 Skill" to "4 Skills" — feature-claim becomes real.
2. **Write the "smoke-test catalogue."** For every Skill, produce a 5-step verification script (similar to `examples/smoke_test_bridge.py`) that confirms it works end-to-end. Catches Skill regressions on plugin updates. **1 week.**
3. **Author the Getting-Started video (3–5 min).** Screen recording of: install → drop canvas component → start MCP → Claude prompts the canvas → result. RA can do the recording, you do the voiceover. Adoption multiplier.

### Tier 2 — Builds RA's expertise, ships in 1–2 months

4. **TU Delft GH textbook → RAG corpus.** Scrape, chunk, embed the open TU Delft computational-design textbook. Build a tiny "GH-question" RAG endpoint that answers "what component does X?" by citing textbook sections. This is the first piece of the virtual-TA. RA learns embeddings + retrieval. **~3 weeks.**
5. **User-testing protocol.** Recruit 4–6 GH learners (your students, friends, online), run 1-hour sessions each with the L1 surface, observe + transcript-code. Produce a "what we learned" doc. RA does the recruitment, scheduling, observation, transcript-coding. You read the doc. **~3 weeks (mostly scheduling).**
6. **Build the workshop curriculum.** A 4-session workshop (8 hours total) that takes a learner from zero to "I can use rhino-gh-mcp to model a small project." RA drafts, you review. Outcome: directly reusable for both teaching and conference workshops.

### Tier 3 — Stretch, builds toward thesis-level output

7. **Comparative review of the AI-for-architecture commercial landscape.** Hands-on demos of Forma, Hypar, TestFit, FORMAS.AI; written critique of each (10–15 pages). What rhino-gh-mcp does differently and why. Becomes a literature-review chapter / preprint section. RA learns the field.
8. **Annotated bibliography of 20 papers** from the academic list in §A.2. Each entry: 1-paragraph summary, 1-sentence relevance to this project. Goes into `docs/related-work.md`. Foundation for a future paper.

### What NOT to do

- Don't have them touch the C# plugins. The cost of breakage is too high relative to learning rate.
- Don't have them work in the same files you're working in simultaneously. Either give them their own branch, their own folder (Skills! `docs/`!), or run sequential not parallel.
- Don't ask them to "improve the prompts" without a baseline + comparison protocol. That's a research project, not an RA task.

## D. Future scope as a software-teaching tool

This is the most promising direction commercially *and* academically, and it's underexplored. Three layers of ambition, pick where to sit:

### Layer 1 — "AI-augmented studio TA" (1 semester)

A version of rhino-gh-mcp + a curated Skill set + a RAG over your course materials, deployed as the assistant for a real Grasshopper course you teach. Students in L1 / L2 modes can ask "why isn't my circle showing?" and "how do I get the radius onto a slider?" — TA-style.

Pedagogical guardrails to add (cribbed from arXiv 2512.11882):
- No-spoiler default mode (gives hints, not answers)
- Curriculum-aware (knows what's been covered)
- Per-student session history (recognise repeated confusion)

**Output:** workshop paper at CAADRIA / eCAADe with empirical data from one semester.

### Layer 2 — "Teaching platform" (2–3 semesters)

Bundled product: server + plugins + curriculum + assessment rubric, packaged for other instructors. Hosted Claude or BYO-API. Adopted by 3–5 architecture programs.

Required investment: web frontend, multi-tenancy, classroom-mode controls (max-capability ceiling per student, time-locked Skills, instructor dashboard). This is where it becomes real software, not a research artifact.

**Output:** journal paper (Frontiers in Built Environment, Computer-Aided Design) + open-source release of curriculum.

### Layer 3 — "Standard tool for architectural-AI education" (3+ years)

The way Grasshopper *itself* became the standard parametric tool. Requires: (a) something Forma/Hypar can't lock down because they're seat-licensed, (b) curriculum that lives in textbooks not just MOOCs, (c) the kind of organic adoption pyRevit had.

Hard to engineer this — but the choice of MIT + open + Rhino-not-Revit + teaching-first puts the project in the right position. The thing that would tip it: a *foundational* paper, like Tedeschi's *AAD* book did for Grasshopper itself, but for AI-augmented parametric design.

## E. Open questions worth deep thinking

Curated list — these are the things to take to a fresh Opus session for unhurried thinking:

1. **The teaching pedagogy.** L1/L2/L3 is the current scaffold. Is it pedagogically defensible? What does the literature on scaffolded computational-thinking instruction say? Should the capability axes (params/components/scripting) be reframed as *cognitive* axes (recall/apply/synthesise) instead?
2. **The publication strategy.** CAADRIA 2027 vs ACADIA 2027 vs a Frontiers paper vs a preprint dump. What's the right venue for the first paper, and what experimental design would make the data publishable?
3. **The RA's first 3 months.** Of the Tier 1–3 tasks above, what's the right sequencing? What's a realistic "by end of summer" deliverable list?
4. **The product/research split.** This project can be (a) a research artifact for one paper, (b) a teaching tool used at UBC, (c) an open-source project with community contributors, (d) a startup. (a) (b) and (c) compose well; adding (d) changes everything. Decide explicitly.
5. **The competitive moat (if any).** Forma and Hypar will not stand still. Speckle's data layer is becoming the substrate. Where does rhino-gh-mcp's defensible niche end up — *parametric* (not BIM)? *teaching* (not production)? *open* (not seat-licensed)? Be specific.
6. **The RAG-over-zoning original goal.** Still in scope? If yes — what's the smallest publishable version? Pick one zoning code (Vancouver? a NYC sub-district?), one envelope-generation routine, one set of environmental metrics, end-to-end pipeline. Phase 1.
7. **Multimodal as a deferred axis.** Image-in (site photos, sketches) and image-out (renders) — when does this enter the roadmap? Replicate / SDXL / Veras integration? Worth a serious look as Phase 6 → 4 or 5.

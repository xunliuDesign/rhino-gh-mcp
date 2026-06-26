# Skills

Each skill is an Anthropic Agent Skills bundle: a directory with a `SKILL.md`
that describes one architectural workflow plus any assets it needs
(user-objects, reference images, reusable Python snippets).

Skills are loaded by the **client** (Claude Desktop, Claude Code, etc),
not the server. To use a skill, point your client at this directory or
copy the bundle into the client's skills directory.

## Conventions

- One directory per workflow. The directory name is the skill ID.
- `SKILL.md` must have YAML frontmatter with at minimum:
  - `name` — the skill ID (matches the directory)
  - `description` — what the skill is for, in one paragraph
  - `recommended_capabilities` — which canvas flags the skill expects
    (`allow_parameters` / `allow_components` / `allow_scripting`)
  - `recommended_scope` — `curated` / `defaults` / `all`
  - `recommended_category_filter` — when scope is `curated`, the
    comma-separated category names the skill expects to be visible
- Bundled assets (user-objects, example `.gh` files, reference images)
  live alongside `SKILL.md`. Reference them with relative paths.

## Authoring a new skill

1. Create `skills/<id>/` with a `SKILL.md` matching the conventions above.
2. Adapt any system-prompt prose you've already used into the body — it
   maps cleanly to "here's the workflow the LLM should follow".
3. Translate any old / client-specific tool names into the current
   `gh_*` / `rhino_*` surface (see [server tool docstrings](../server/src/rhino_gh_mcp/tools/)).
4. Bundle the necessary `.ghuser` files into a subfolder.
5. Add a row to the catalog below.

## Catalog

| Skill | Scope | Description |
|-------|-------|-------------|
| [rhino-gh-bridge-basics](./rhino-gh-bridge-basics/) | defaults | Prerequisite knowledge for every rhino-gh-mcp session — the five fundamentals of Grasshopper modeling (geometry hierarchy, data flow, modifiers, data trees, execution state + baking), data-type casting rules, Null-result troubleshooting, debugging components, and the full bridge-specific quirks list (name traps, persistent-data limits, Scenario gating). Pair with a task skill for the actual recipe. |
| [landform](./landform/) | curated (`MCP` category) | Build a parametric landform / terrain definition using the bundled MCP landscape user-objects. |
| [ladybug-environmental](./ladybug-environmental/) | curated (`Ladybug` category) | Set up environmental analysis on a Rhino site model using the Ladybug Tools components (sun path, sun hours, wind rose). Requires Ladybug Tools to be installed in Rhino. |
| [facade-design](./facade-design/) | defaults (`Surface, Curve, Vector, Transform, Mesh, Maths, Sets, Params`) | Generate parametric building facades from simple prompts — from scratch, onto existing Rhino host geometry, or by tiling a premade module — across multiple typologies (louver, perforated, diagrid, tessellation, panelized, folded), with sensible defaults and tunable sliders. |

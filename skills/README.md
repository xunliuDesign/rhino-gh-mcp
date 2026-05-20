# Skills

Each skill is an Anthropic Agent Skills bundle: a directory with a `SKILL.md`
that describes one architectural workflow plus any assets it needs
(user-objects, reference images, reusable Python snippets).

Skills are loaded by the **client**, not the server. To use a skill, point your
client at this directory or copy the bundle into the client's skills directory.

## Conventions

- One directory per workflow. The directory name is the skill ID.
- `SKILL.md` must have YAML frontmatter with `name`, `description`, and (for
  rhino-gh-mcp skills) `policy` — the recommended L1/L2/L3 tier.
- Assets (user-objects, example `.gh` files, reference images) live alongside
  `SKILL.md`. Reference them with relative paths.

## Catalog

| Skill | Policy | Description |
|-------|--------|-------------|
| [landform-from-contours](./landform-from-contours/) | curated | Convert a layer of imported contour curves into a watertight terrain mesh and a derived analysis surface. |

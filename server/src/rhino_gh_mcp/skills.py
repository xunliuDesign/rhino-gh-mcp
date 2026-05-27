"""Skill discovery + loading.

A "skill" is a directory under the repo's `skills/` folder containing a
`SKILL.md` file. The file starts with YAML frontmatter (between `---`
fences) and is followed by the skill's body — the workflow recipe the
LLM follows.

The MCP server exposes two meta-tools:

  - `list_skills()`  — returns the frontmatter manifest for every skill
    found on disk. Cheap; called by the LLM at session start (or before
    starting a non-trivial task) to discover what recipes exist.
  - `load_skill(id)` — returns the full SKILL.md body for one skill.
    Called only when the LLM decides a skill matches the user's request.

This is the two-stage retrieval pattern: cheap discovery (manifest only),
expensive load (full body) only when needed.

The skills directory is resolved relative to this module — we walk up
from `server/src/rhino_gh_mcp/skills.py` to the repo root and look at
`./skills/`. If the server is repackaged (e.g. inside a .dxt), this
resolution may need a different strategy; for now, the hardcoded
relative path is the simplest thing that works for development.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml

log = logging.getLogger(__name__)

# repo_root/server/src/rhino_gh_mcp/skills.py -> repo_root
_REPO_ROOT = Path(__file__).resolve().parents[3]
SKILLS_DIR = _REPO_ROOT / "skills"


@dataclass(frozen=True)
class SkillManifest:
    """One skill's discovery-time metadata. Cheap to serialise."""

    id: str
    description: str
    frontmatter: dict[str, Any]
    path: Path


def list_skills(skills_dir: Path = SKILLS_DIR) -> list[SkillManifest]:
    """Scan `skills_dir` for `<id>/SKILL.md` files and return their manifests.

    Returns an empty list if the directory doesn't exist. Skips any skill
    whose SKILL.md fails to parse, with a warning to the log — better to
    surface most of the catalog than to fail-closed because one file is
    malformed.
    """
    if not skills_dir.is_dir():
        log.warning("skills directory does not exist: %s", skills_dir)
        return []

    out: list[SkillManifest] = []
    for entry in sorted(skills_dir.iterdir()):
        if not entry.is_dir():
            continue
        skill_md = entry / "SKILL.md"
        if not skill_md.is_file():
            continue
        try:
            frontmatter = _parse_frontmatter(skill_md.read_text(encoding="utf-8"))
        except Exception as exc:
            log.warning("could not parse %s: %s", skill_md, exc)
            continue

        skill_id = frontmatter.get("name") or entry.name
        description = (frontmatter.get("description") or "").strip()
        out.append(
            SkillManifest(
                id=str(skill_id),
                description=description,
                frontmatter=frontmatter,
                path=skill_md,
            )
        )
    return out


def load_skill(skill_id: str, skills_dir: Path = SKILLS_DIR) -> str | None:
    """Return the full SKILL.md text (frontmatter + body) for `skill_id`.

    Looks up by manifest id first (the YAML `name` field), then falls
    back to directory name. Returns None if not found.
    """
    manifests = list_skills(skills_dir)
    for m in manifests:
        if m.id == skill_id or m.path.parent.name == skill_id:
            return m.path.read_text(encoding="utf-8")
    return None


def _parse_frontmatter(text: str) -> dict[str, Any]:
    """Extract the YAML frontmatter from a SKILL.md.

    Standard convention: file starts with `---\n`, frontmatter YAML,
    `---\n`, body. Returns an empty dict if no frontmatter is present.
    """
    if not text.startswith("---"):
        return {}
    # Find the closing fence
    rest = text[3:]
    end_idx = rest.find("\n---")
    if end_idx == -1:
        return {}
    raw = rest[:end_idx]
    data = yaml.safe_load(raw) or {}
    if not isinstance(data, dict):
        return {}
    return data

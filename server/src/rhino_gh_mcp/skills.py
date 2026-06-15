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
from dataclasses import dataclass, field
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


# --- v0.2 Skills schema v2 ---------------------------------------------------
# A Skill is the typed view of a SKILL.md's frontmatter, suitable for code
# that wants to reason about scenarios, allowed components, required plugins,
# reference files, etc. v0.1's free-form `recommended_capabilities` /
# `recommended_scope` keys still parse via `SkillManifest` (unchanged) — v0.2
# Skill adds the new structured fields on top while keeping backwards compat
# with skills that only ship the body text.


@dataclass(frozen=True)
class Skill:
    """A parsed v0.2 Skill (frontmatter + path resolution).

    Every field except `name` is optional, so an old (v0.1) SKILL.md whose
    frontmatter has only `name` and `description` still produces a valid
    Skill — it just has empty collections. New (v0.2) Skills can populate
    any subset of the structured fields documented in
    docs/v0.2-redesign.md §"SKILL.md frontmatter".
    """

    name: str
    version: str = ""
    description: str = ""
    modes: tuple[str, ...] = ()
    required_plugins: tuple[str, ...] = ()
    required_capabilities: dict[str, Any] = field(default_factory=dict)
    allowed_categories: tuple[str, ...] = ()
    required_components: tuple[dict[str, Any], ...] = ()
    reference_examples: tuple[dict[str, Any], ...] = ()
    # images is a mapping label -> {file, used_in}
    images: dict[str, dict[str, Any]] = field(default_factory=dict)
    prompts: dict[str, str] = field(default_factory=dict)
    commands: dict[str, dict[str, Any]] = field(default_factory=dict)
    # v0.2 Execute-mode tool allowlist. When non-empty AND scenario=execute,
    # the gate restricts the AI to these real MCP tool names (in addition to
    # the always-available read tools). Use this to let a Skill expose the
    # raw tools it actually needs (gh_add_component, gh_set_slider, ...)
    # without waiting for FastMCP dynamic command-tool registration.
    allow_tools: tuple[str, ...] = ()
    # Path to the SKILL.md so reference / image relative-path resolution works.
    path: Path | None = None

    def reference_path(self, ref_name: str) -> Path | None:
        """Resolve a reference-example name to an absolute path.

        Looks up `reference_examples[].file` (matched by basename minus
        extension, or by the explicit `name:` field if present, falling back
        to direct file match).
        """
        if self.path is None:
            return None
        base = self.path.parent
        for ref in self.reference_examples:
            if not isinstance(ref, dict):
                continue
            name = ref.get("name") or Path(str(ref.get("file", ""))).stem
            if name == ref_name or ref.get("file") == ref_name:
                file_str = ref.get("file")
                if file_str:
                    return (base / str(file_str)).resolve()
        return None


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


def _coerce_str_tuple(value: Any) -> tuple[str, ...]:
    """Best-effort: turn a YAML list into a tuple[str, ...]. None -> ()."""
    if not value:
        return ()
    if isinstance(value, str):
        return (value,)
    if isinstance(value, (list, tuple)):
        return tuple(str(v) for v in value if v is not None)
    return ()


def _coerce_dict_tuple(value: Any) -> tuple[dict[str, Any], ...]:
    """Best-effort: turn a list-of-dicts into a tuple. None -> ()."""
    if not value:
        return ()
    if isinstance(value, (list, tuple)):
        return tuple(v for v in value if isinstance(v, dict))
    return ()


def _frontmatter_to_skill(fm: dict[str, Any], skill_md_path: Path,
                          fallback_id: str) -> Skill:
    """Build a Skill from raw frontmatter. Tolerant of missing / malformed fields."""
    name = str(fm.get("name") or fallback_id)
    images_raw = fm.get("images") or {}
    if not isinstance(images_raw, dict):
        images_raw = {}
    prompts_raw = fm.get("prompts") or {}
    if not isinstance(prompts_raw, dict):
        prompts_raw = {}
    commands_raw = fm.get("commands") or {}
    if not isinstance(commands_raw, dict):
        commands_raw = {}
    required_caps_raw = fm.get("required_capabilities") or {}
    if not isinstance(required_caps_raw, dict):
        required_caps_raw = {}
    return Skill(
        name=name,
        version=str(fm.get("version") or ""),
        description=str(fm.get("description") or "").strip(),
        modes=_coerce_str_tuple(fm.get("modes")),
        required_plugins=_coerce_str_tuple(fm.get("required_plugins")),
        required_capabilities=dict(required_caps_raw),
        allowed_categories=_coerce_str_tuple(fm.get("allowed_categories")),
        required_components=_coerce_dict_tuple(fm.get("required_components")),
        reference_examples=_coerce_dict_tuple(fm.get("reference_examples")),
        images={k: dict(v) for k, v in images_raw.items() if isinstance(v, dict)},
        prompts={str(k): str(v) for k, v in prompts_raw.items()},
        commands={str(k): dict(v) for k, v in commands_raw.items() if isinstance(v, dict)},
        allow_tools=_coerce_str_tuple(fm.get("allow_tools")),
        path=skill_md_path,
    )


def load_skills(skills_dir: Path | str = SKILLS_DIR) -> dict[str, Skill]:
    """Load all Skills in a directory as a {name: Skill} mapping.

    Skills with malformed YAML are skipped (logged as warnings). If two
    skills declare the same `name:`, the last one wins — the caller can
    spot the collision by comparing the result to disk.
    """
    skills_dir = Path(skills_dir)
    out: dict[str, Skill] = {}
    if not skills_dir.is_dir():
        return out
    for entry in sorted(skills_dir.iterdir()):
        if not entry.is_dir():
            continue
        skill_md = entry / "SKILL.md"
        if not skill_md.is_file():
            continue
        try:
            fm = _parse_frontmatter(skill_md.read_text(encoding="utf-8"))
        except Exception as exc:
            log.warning("could not parse %s: %s", skill_md, exc)
            continue
        skill = _frontmatter_to_skill(fm, skill_md, fallback_id=entry.name)
        out[skill.name] = skill
    return out


def get_skill(skill_id: str, skills_dir: Path | str = SKILLS_DIR) -> Skill | None:
    """Return one Skill by name (the YAML `name:` field) or directory name."""
    skills = load_skills(skills_dir)
    if skill_id in skills:
        return skills[skill_id]
    # Fall back to directory-name match.
    for s in skills.values():
        if s.path is not None and s.path.parent.name == skill_id:
            return s
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

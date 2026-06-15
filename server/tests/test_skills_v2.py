"""Tests for the v0.2 Skills schema — typed Skill dataclass + load_skills.

Existing test_skills.py covers the manifest path (cheap discovery). This
file exercises the typed Skill API: full frontmatter parsing including the
new v0.2-only fields, behavior on missing fields, and frontmatter parsing
for the two migrated skills shipped in the repo.
"""

from __future__ import annotations

import textwrap
from pathlib import Path

import pytest

from rhino_gh_mcp import skills as skills_mod
from rhino_gh_mcp.skills import Skill, get_skill, load_skills


# --- Fixtures ----------------------------------------------------------------


@pytest.fixture
def v2_skill_dir(tmp_path: Path) -> Path:
    """Synthesise a skills/ dir with one well-formed v0.2 skill + one minimal."""
    full = tmp_path / "facade-panelization"
    full.mkdir()
    (full / "SKILL.md").write_text(
        textwrap.dedent(
            """\
            ---
            name: facade-panelization
            version: 0.2.0
            description: Panelise a surface façade.
            modes: [coach, execute]
            required_plugins: [some-plugin]
            required_capabilities:
              allow_scripting: false
            allowed_categories: [Surface, Curve, MCP]
            required_components:
              - { category: Surface, name: "Surface Box" }
              - { category: MCP, name: "Facade Panel" }
            reference_examples:
              - { name: basic, file: reference/basic.gh, when: "from scratch" }
              - { name: with-glazing, file: reference/glazing.gh, when: "windows" }
            images:
              parti: { file: images/parti.png, used_in: [inspect, coach] }
            prompts:
              coach: Coach-mode preamble.
            commands:
              panelize:
                description: Generate panelisation
                args: { surface: "Surface", divisions: int }
                steps: [step1, step2]
            allow_tools:
              - gh_add_component
              - gh_connect_components
              - gh_set_slider
            ---

            # Body

            Markdown body text.
            """
        ),
        encoding="utf-8",
    )

    minimal = tmp_path / "old-style"
    minimal.mkdir()
    (minimal / "SKILL.md").write_text(
        textwrap.dedent(
            """\
            ---
            name: old-style
            description: A skill that predates the v0.2 frontmatter.
            ---

            Body only.
            """
        ),
        encoding="utf-8",
    )
    return tmp_path


@pytest.fixture
def malformed_skill_dir(tmp_path: Path) -> Path:
    """Skill with intentionally broken YAML frontmatter to verify graceful skip."""
    broken = tmp_path / "broken"
    broken.mkdir()
    (broken / "SKILL.md").write_text(
        "---\n  name: broken\n :\n  bad-yaml: : :\n---\nbody\n", encoding="utf-8"
    )
    good = tmp_path / "good"
    good.mkdir()
    (good / "SKILL.md").write_text(
        "---\nname: good\ndescription: alive\n---\n# body\n", encoding="utf-8"
    )
    return tmp_path


# --- Skill dataclass behaviour ----------------------------------------------


def test_load_skills_parses_v2_frontmatter(v2_skill_dir: Path):
    skills = load_skills(v2_skill_dir)
    assert "facade-panelization" in skills
    s = skills["facade-panelization"]
    assert isinstance(s, Skill)
    assert s.version == "0.2.0"
    assert s.description == "Panelise a surface façade."
    assert s.modes == ("coach", "execute")
    assert s.required_plugins == ("some-plugin",)
    assert s.allowed_categories == ("Surface", "Curve", "MCP")
    assert len(s.required_components) == 2
    assert s.required_components[0]["category"] == "Surface"
    assert s.required_components[0]["name"] == "Surface Box"
    assert len(s.reference_examples) == 2
    assert "panelize" in s.commands
    assert s.commands["panelize"]["description"] == "Generate panelisation"
    # v0.2.0-beta: allow_tools is the Execute-mode real-tool allowlist.
    assert s.allow_tools == (
        "gh_add_component",
        "gh_connect_components",
        "gh_set_slider",
    )


def test_load_skills_legacy_skill_has_empty_v2_collections(v2_skill_dir: Path):
    skills = load_skills(v2_skill_dir)
    assert "old-style" in skills
    s = skills["old-style"]
    assert s.modes == ()
    assert s.required_plugins == ()
    assert s.allowed_categories == ()
    assert s.commands == {}
    # but name + description still parse
    assert s.name == "old-style"
    assert "predates" in s.description


def test_get_skill_by_name(v2_skill_dir: Path):
    s = get_skill("facade-panelization", v2_skill_dir)
    assert s is not None
    assert s.name == "facade-panelization"


def test_get_skill_by_dir_name(v2_skill_dir: Path):
    # If someone passes the directory name and it doesn't match `name:`, the
    # lookup should fall back. Here name == dir so this is symmetric.
    s = get_skill("old-style", v2_skill_dir)
    assert s is not None
    assert s.name == "old-style"


def test_get_skill_unknown_returns_none(v2_skill_dir: Path):
    assert get_skill("not-a-skill", v2_skill_dir) is None


def test_load_skills_skips_malformed_keeps_rest(malformed_skill_dir: Path):
    """Bad YAML on one skill must not blow up the whole directory load."""
    skills = load_skills(malformed_skill_dir)
    # The good skill must still load even if "broken" failed.
    assert "good" in skills
    # The broken skill is either skipped entirely OR parsed with whatever
    # YAML salvaged out of the malformed block. We tolerate both as long
    # as load_skills() doesn't raise.
    assert skills["good"].description == "alive"


def test_load_skills_returns_empty_dict_for_missing_dir(tmp_path: Path):
    assert load_skills(tmp_path / "does-not-exist") == {}


# --- Real on-disk skills migration check ------------------------------------


def test_real_landform_has_v2_frontmatter():
    """The repo's landform skill should have been migrated to v0.2 frontmatter."""
    skills = load_skills()
    assert "landform" in skills
    s = skills["landform"]
    assert s.version == "0.2.0"
    assert "coach" in s.modes
    assert "execute" in s.modes
    assert s.allowed_categories  # non-empty


def test_real_ladybug_environmental_declares_required_plugin():
    """The Ladybug skill must declare its `ladybug` plugin requirement."""
    skills = load_skills()
    assert "ladybug-environmental" in skills
    s = skills["ladybug-environmental"]
    assert s.required_plugins == ("ladybug",)


# --- Reference path resolution ---------------------------------------------


def test_reference_path_resolves_by_name(v2_skill_dir: Path):
    s = get_skill("facade-panelization", v2_skill_dir)
    assert s is not None
    p = s.reference_path("basic")
    assert p is not None
    # Should be skills/facade-panelization/reference/basic.gh — file doesn't
    # actually exist in tmp, but the path should resolve correctly.
    assert p.name == "basic.gh"
    assert "facade-panelization" in str(p)


def test_reference_path_unknown_returns_none(v2_skill_dir: Path):
    s = get_skill("facade-panelization", v2_skill_dir)
    assert s.reference_path("nope") is None

"""Tests for the skill discovery + load mechanism."""

from __future__ import annotations

import textwrap
from pathlib import Path

import pytest

from rhino_gh_mcp import skills


@pytest.fixture
def fake_skills_dir(tmp_path: Path) -> Path:
    """Build a temp dir with two stub skills (one well-formed, one minimal)."""
    well_formed = tmp_path / "alpha"
    well_formed.mkdir()
    (well_formed / "SKILL.md").write_text(
        textwrap.dedent(
            """\
            ---
            name: alpha
            description: First test skill.
            recommended_capabilities:
              allow_parameters: true
              allow_components: false
            recommended_scope: curated
            ---

            # Alpha body

            Step 1: do a thing.
            Step 2: do another.
            """
        ),
        encoding="utf-8",
    )

    minimal = tmp_path / "beta"
    minimal.mkdir()
    (minimal / "SKILL.md").write_text(
        textwrap.dedent(
            """\
            ---
            name: beta
            description: |
              Second test skill with a longer multi-line description that
              spans two lines for the wrap test.
            ---

            Body text only.
            """
        ),
        encoding="utf-8",
    )

    # An entry without SKILL.md should be ignored
    (tmp_path / "not-a-skill").mkdir()
    (tmp_path / "not-a-skill" / "README.md").write_text("nope", encoding="utf-8")

    return tmp_path


def test_list_skills_returns_manifests(fake_skills_dir: Path):
    manifests = skills.list_skills(fake_skills_dir)
    ids = sorted(m.id for m in manifests)
    assert ids == ["alpha", "beta"]


def test_list_skills_skips_dirs_without_skill_md(fake_skills_dir: Path):
    manifests = skills.list_skills(fake_skills_dir)
    assert "not-a-skill" not in [m.id for m in manifests]


def test_list_skills_parses_nested_frontmatter(fake_skills_dir: Path):
    manifests = skills.list_skills(fake_skills_dir)
    alpha = next(m for m in manifests if m.id == "alpha")
    assert alpha.frontmatter["recommended_capabilities"]["allow_parameters"] is True
    assert alpha.frontmatter["recommended_capabilities"]["allow_components"] is False
    assert alpha.frontmatter["recommended_scope"] == "curated"


def test_list_skills_returns_empty_for_missing_dir(tmp_path: Path):
    assert skills.list_skills(tmp_path / "nope") == []


def test_load_skill_returns_full_body_by_id(fake_skills_dir: Path):
    text = skills.load_skill("alpha", fake_skills_dir)
    assert text is not None
    assert "# Alpha body" in text
    assert "Step 1: do a thing." in text


def test_load_skill_returns_full_body_by_dir_name(fake_skills_dir: Path):
    # Even if the id matches the dir name, we should find it
    text = skills.load_skill("beta", fake_skills_dir)
    assert text is not None
    assert "Body text only." in text


def test_load_skill_returns_none_for_unknown(fake_skills_dir: Path):
    assert skills.load_skill("does-not-exist", fake_skills_dir) is None


def test_parse_frontmatter_handles_no_fence():
    out = skills._parse_frontmatter("# Just a heading, no frontmatter")
    assert out == {}


def test_parse_frontmatter_handles_unclosed_fence():
    out = skills._parse_frontmatter("---\nname: foo\nnever ends")
    assert out == {}


def test_real_skills_directory_lists_at_least_landform():
    """Smoke check against the repo's actual skills/ folder.

    Not a unit test per se - depends on the repo layout - but cheap and
    useful for catching frontmatter typos when authoring new skills.
    """
    manifests = skills.list_skills()
    ids = {m.id for m in manifests}
    assert "landform" in ids, f"got: {ids}"

"""Meta tools: skill discovery + loading.

These are not Grasshopper- or Rhino-specific - they expose the server's
own catalog of workflow recipes (Skills) to the LLM. The intended use
pattern is the two-stage retrieval one Anthropic's skills-plugin uses:

  1. LLM calls `list_skills` at the start of any non-trivial task to
     see what recipes exist. Cheap - just manifests.
  2. If a skill matches the user's request, LLM calls
     `load_skill(<id>)` to get the full workflow body.

Both tools are always-allowed (read-only, no canvas state mutation),
so they bypass the capability gate.
"""

from __future__ import annotations

import json
import logging

from mcp.server.fastmcp import FastMCP

from .. import skills as skills_mod
from ..capabilities import CapabilitiesProvider
from ._gate import gated

log = logging.getLogger(__name__)


def register(app: FastMCP, caps: CapabilitiesProvider) -> None:
    """Register the meta tools."""

    @app.tool(name="list_skills")
    @gated(caps, "list_skills")
    def list_skills() -> str:
        """List every skill available on this server with id + description.

        A skill is a documented workflow recipe (a SKILL.md file in the
        server's skills/ directory). Call this BEFORE tackling any
        non-trivial Grasshopper or Rhino task — if a skill matches the
        user's request, calling load_skill(<id>) is much cheaper than
        re-deriving the workflow from scratch.

        Returns: JSON list of {id, description, recommended_capabilities,
        recommended_scope} for each skill, suitable for a fast scan.
        """
        manifests = skills_mod.list_skills()
        out = []
        for m in manifests:
            entry = {
                "id": m.id,
                "description": m.description,
            }
            # Surface a few of the most useful frontmatter fields directly
            # so the LLM can match without a second load_skill call.
            for key in (
                "recommended_capabilities",
                "recommended_scope",
                "recommended_category_filter",
                "prerequisites",
            ):
                if key in m.frontmatter:
                    entry[key] = m.frontmatter[key]
            out.append(entry)
        return json.dumps({"count": len(out), "skills": out}, indent=2, default=str)

    @app.tool(name="load_skill")
    @gated(caps, "load_skill")
    def load_skill(skill_id: str) -> str:
        """Load the full SKILL.md body for the named skill.

        Call this only after list_skills has shown that this skill_id
        matches the user's request. The body is the workflow recipe -
        follow its steps to complete the task.

        Args:
            skill_id: The skill's id (from list_skills' output).
        """
        body = skills_mod.load_skill(skill_id)
        if body is None:
            manifests = skills_mod.list_skills()
            available = ", ".join(m.id for m in manifests) or "(none)"
            return f"Error: skill {skill_id!r} not found. Available: [{available}]"
        return body

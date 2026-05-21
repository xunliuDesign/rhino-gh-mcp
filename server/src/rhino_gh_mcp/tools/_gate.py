"""Shared `@gated` decorator — soft capability gate at tool-call time.

Usage at a tool registration site:

    @app.tool(name="gh_xyz")
    @gated(caps, "gh_xyz")
    def gh_xyz(arg: str) -> str:
        '''Docstring becomes the tool description.'''
        ...

The outer @app.tool is FastMCP's registration. The inner @gated wraps the
function so that, at every call, it checks the current Capabilities and
short-circuits with a clean denial string if the capability is off.

@functools.wraps preserves __wrapped__, which inspect.signature follows by
default — so FastMCP still sees the original function's typed parameters
when building the tool's JSON schema.
"""

from __future__ import annotations

import functools
from typing import Callable, TypeVar

from ..capabilities import CapabilitiesProvider, denial_message

F = TypeVar("F", bound=Callable[..., str])


def gated(caps: CapabilitiesProvider, name: str) -> Callable[[F], F]:
    def decorator(fn: F) -> F:
        @functools.wraps(fn)
        def wrapper(*args, **kwargs):
            c = caps.current()
            if not c.allows(name):
                return denial_message(name, c)
            return fn(*args, **kwargs)
        return wrapper  # type: ignore[return-value]
    return decorator

"""Configuration: policy, transport, ports."""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum


class Policy(str, Enum):
    """Tool-surface tier exposed to the LLM."""

    PARAMETER = "parameter"
    CURATED = "curated"
    FULL = "full"


class Transport(str, Enum):
    """MCP transport selection."""

    STDIO = "stdio"
    HTTP = "http"


@dataclass(frozen=True)
class Config:
    """Frozen runtime configuration. One per server process."""

    policy: Policy = Policy.CURATED
    transport: Transport = Transport.STDIO
    http_port: int = 8765
    gh_port: int = 9999
    rhino_port: int = 9876
    curated_group: tuple[str, ...] = field(default_factory=tuple)

    def __str__(self) -> str:
        bits = [
            f"policy={self.policy.value}",
            f"transport={self.transport.value}",
            f"gh:{self.gh_port}",
            f"rhino:{self.rhino_port}",
        ]
        if self.transport is Transport.HTTP:
            bits.append(f"http:{self.http_port}")
        if self.curated_group:
            bits.append(f"groups={','.join(self.curated_group)}")
        return "Config(" + ", ".join(bits) + ")"

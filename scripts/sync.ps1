<#
.SYNOPSIS
    "I just landed on this machine, get me up to speed" — one-shot sync.

.DESCRIPTION
    Pulls latest, refreshes Python deps, runs the server-side tests, and
    prints the recent commit history + the tail of docs/handoff.md so you
    can immediately see what happened on the other machine.

    Does NOT rebuild the C# plugins. Plugins are per-machine: run
    plugins\grasshopper\reinstall.ps1 or plugins\rhino\reinstall.ps1 when
    you actually need a fresh .gha / .rhp.

.PARAMETER NoTest
    Skip the pytest run.
#>
[CmdletBinding()]
param(
    [switch]$NoTest
)

$ErrorActionPreference = "Stop"
Set-Location -Path (Join-Path $PSScriptRoot "..")
$repoRoot = Get-Location

Write-Host "→ Pulling latest from main..." -ForegroundColor Cyan
git fetch origin
git pull --rebase --autostash

Write-Host ""
Write-Host "→ Syncing Python deps in server\..." -ForegroundColor Cyan
Push-Location server
uv sync --extra dev
Pop-Location

if (-not $NoTest) {
    Write-Host ""
    Write-Host "→ Running server smoke tests..." -ForegroundColor Cyan
    Push-Location server
    uv run pytest -q
    Pop-Location
}

Write-Host ""
Write-Host "→ Recent commits:" -ForegroundColor Cyan
git log --oneline -5

Write-Host ""
Write-Host "→ Tail of docs/handoff.md (session log):" -ForegroundColor Cyan
Write-Host "  --------------------------------------"
Get-Content (Join-Path $repoRoot "docs\handoff.md") -Tail 25 |
    ForEach-Object { Write-Host "  $_" }
Write-Host "  --------------------------------------"
Write-Host ""
Write-Host "✅ Ready. To resume in Claude Code, the canonical first prompt is:" -ForegroundColor Green
Write-Host '   "Read docs/handoff.md and tell me what to work on next."'

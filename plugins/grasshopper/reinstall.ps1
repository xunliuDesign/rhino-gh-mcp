<#
.SYNOPSIS
    Build + reinstall the rhino-gh-mcp Grasshopper .gha on Windows.

.DESCRIPTION
    Mirrors plugins/grasshopper/reinstall.sh. Handles the Windows-specific
    quirks: force-quits Rhino if running so the new .gha loads, removes the
    "blocked" zone-identifier xattr so Rhino accepts the unsigned plugin,
    reports the installed path + MD5 + assembly version.

.PARAMETER SkipBuild
    Install whatever is already in bin\Release without rebuilding.

.PARAMETER NoKill
    Don't try to kill Rhino if it's currently running.

.EXAMPLE
    .\reinstall.ps1
    .\reinstall.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$NoKill
)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

# 1. Force-quit Rhino if running.
if (-not $NoKill) {
    $rhinoProcs = Get-Process -Name "Rhino" -ErrorAction SilentlyContinue
    if ($rhinoProcs) {
        Write-Host "[WARN]Rhino is running. Quitting it so the new .gha can load." -ForegroundColor Yellow
        $rhinoProcs | ForEach-Object { $_.CloseMainWindow() | Out-Null }
        Start-Sleep -Seconds 2
        $rhinoProcs = Get-Process -Name "Rhino" -ErrorAction SilentlyContinue
        if ($rhinoProcs) {
            Write-Host "   Rhino didn't quit cleanly. Force-killing." -ForegroundColor Yellow
            $rhinoProcs | Stop-Process -Force
            Start-Sleep -Seconds 1
        }
    }
}

# 2. Build.
if (-not $SkipBuild) {
    Write-Host "->Building (Release)..." -ForegroundColor Cyan
    dotnet build -c Release -v quiet | Select-Object -Last 5
}

$built = Join-Path $PSScriptRoot "bin\Release\net7.0-windows\RhinoGhMcp.gha"
if (-not (Test-Path $built)) {
    Write-Error "Expected built .gha not found at: $built"
    exit 3
}

# 3. Locate Grasshopper's Libraries folder.
$libraries = Join-Path $env:APPDATA "Grasshopper\Libraries"
if (-not (Test-Path $libraries)) {
    Write-Error "Grasshopper Libraries folder not found at $libraries - start Rhino + launch Grasshopper once to create it."
    exit 4
}

# 4. Remove any prior rhino-gh-mcp .gha (v0 or v1) to avoid duplicates.
Write-Host "->Removing prior MCP .gha files in $libraries" -ForegroundColor Cyan
Get-ChildItem -Path $libraries -Filter "RhinoGhMcp.gha" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "   removing $($_.Name)"
    Remove-Item $_.FullName -Force
}
Get-ChildItem -Path $libraries -Filter "rhino_gh_mcp*.gha" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "   removing $($_.Name)"
    Remove-Item $_.FullName -Force
}

# 5. Copy + Unblock.
$dest = Join-Path $libraries "RhinoGhMcp.gha"
Write-Host "->Copying built .gha to $dest" -ForegroundColor Cyan
Copy-Item -Path $built -Destination $dest -Force
Unblock-File -Path $dest -ErrorAction SilentlyContinue

# 6. Report.
$hash = (Get-FileHash -Path $dest -Algorithm MD5).Hash.ToLower()
$verString = (Select-String -Path $dest -Pattern '^\d+\.\d+\.\d+\.\d+$' -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Line -First 1)

Write-Host ""
Write-Host "[OK]Installed RhinoGhMcp.gha" -ForegroundColor Green
Write-Host "   path:     $dest"
Write-Host "   md5:      $hash"
Write-Host "   version:  $verString"
Write-Host ""
Write-Host "Now launch Rhino 8, open Grasshopper, drop the MCP Server (v1) component"
Write-Host "on a NEW canvas, set Run=True, and check the Version output."

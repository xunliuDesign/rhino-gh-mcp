<#
.SYNOPSIS
    Build the rhino-gh-mcp Rhino plugin (.rhp) on Windows. Mirrors reinstall.sh.

.DESCRIPTION
    Unlike the .gha (auto-loaded from Grasshopper Libraries), Rhino's plugin
    loader requires explicit registration the first time. This script handles
    both cases:
      - First-time install: prints the path and instructs you to drag the .rhp
        onto Rhino's main window, or install via _PluginManager.
      - Subsequent updates: detects the registered install location and
        overwrites the .rhp there.

.PARAMETER SkipBuild
    Install whatever is already in bin\Release without rebuilding.

.PARAMETER NoKill
    Don't try to kill Rhino if it's currently running.
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
        Write-Host "[WARN]Rhino is running. Quitting it so the new .rhp can load." -ForegroundColor Yellow
        $rhinoProcs | ForEach-Object { $_.CloseMainWindow() | Out-Null }
        Start-Sleep -Seconds 2
        $rhinoProcs = Get-Process -Name "Rhino" -ErrorAction SilentlyContinue
        if ($rhinoProcs) {
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

$built = Join-Path $PSScriptRoot "bin\Release\net7.0\RhinoGhMcpRhino.rhp"
if (-not (Test-Path $built)) {
    Write-Error "Expected built .rhp not found at: $built"
    exit 3
}
Unblock-File -Path $built -ErrorAction SilentlyContinue

# 3. Look for an existing registered install.
$pluginGuid = "3f88bb55-3368-4204-9d0a-55911c9349ee"
$pluginsRoot = Join-Path $env:APPDATA "McNeel\Rhinoceros\8.0\Plug-ins"
$installed = $null
if (Test-Path $pluginsRoot) {
    $installed = Get-ChildItem -Path $pluginsRoot -Filter "*$pluginGuid*" -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem -Path $_.FullName -Filter "*.rhp" -ErrorAction SilentlyContinue } |
        Select-Object -First 1
}

if ($installed) {
    $dest = $installed.FullName
    Write-Host "->Updating existing install at:" -ForegroundColor Cyan
    Write-Host "    $dest"
    Copy-Item -Path $built -Destination $dest -Force
    Unblock-File -Path $dest -ErrorAction SilentlyContinue

    $hash = (Get-FileHash -Path $dest -Algorithm MD5).Hash.ToLower()
    $verString = (Select-String -Path $dest -Pattern '^\d+\.\d+\.\d+\.\d+$' -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Line -First 1)

    Write-Host ""
    Write-Host "[OK]Updated. Restart Rhino, then run `_ToggleMcpService` to flip the listener on." -ForegroundColor Green
    Write-Host "   md5:     $hash"
    Write-Host "   version: $verString"
}
else {
    Write-Host ""
    Write-Host "[WARN]First-time install - Rhino doesn't know about this plugin yet." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Drag the file below onto Rhino's main window, OR open Rhino and run the"
    Write-Host "`_PluginManager` command -> Install... -> browse to:"
    Write-Host ""
    Write-Host "    $built"
    Write-Host ""
    Write-Host "Then restart Rhino, and run `_ToggleMcpService` to start the listener."
    Write-Host ""
    Write-Host "After that, this script will auto-update the install on subsequent runs."
}

# =====================================================
# restart-api.ps1
# CDM-OneServe-API helper
#
# WHY THIS EXISTS
#   Running `dotnet run` while an older API process is still alive fails with
#   "Failed to bind to address http://0.0.0.0:5212: address already in use"
#   and, worse, leaves bin\Debug\...\CDM-OneServe-API.exe locked so the next
#   BUILD fails too (MSB3027). This script kills the old instance first, then
#   builds and starts a clean one.
#
# USAGE
#   .\restart-api.ps1          build + run (default)
#   .\restart-api.ps1 -Stop    just kill it
#   .\restart-api.ps1 -Build   kill + build only
# =====================================================

param(
    [switch]$Stop,
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $PSScriptRoot

function Stop-Api {
    $running = Get-Process -Name "CDM-OneServe-API" -ErrorAction SilentlyContinue

    if (-not $running) {
        Write-Host "No API process running." -ForegroundColor DarkGray
        return
    }

    foreach ($p in $running) {
        Write-Host "Stopping API (PID $($p.Id))..." -ForegroundColor Yellow
        Stop-Process -Id $p.Id -Force
    }

    # Give Kestrel a moment to release the socket and release the .exe lock.
    Start-Sleep -Seconds 2
}

# Also stop any orphaned dotnet run host that would re-spawn the app.
$watchers = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*CDM-OneServe-API*" }
foreach ($w in $watchers) {
    Write-Host "Stopping dotnet run host (PID $($w.ProcessId))..." -ForegroundColor Yellow
    Stop-Process -Id $w.ProcessId -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 1

Stop-Api

if ($Stop) {
    Write-Host "API stopped." -ForegroundColor Green
    return
}

Push-Location $projectDir
try {
    Write-Host "Building..." -ForegroundColor Cyan
    dotnet build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed." -ForegroundColor Red
        return
    }

    if ($Build) {
        Write-Host "Build OK. Not starting ( -Build )." -ForegroundColor Green
        return
    }

    Write-Host "Starting API on http://0.0.0.0:5212 ..." -ForegroundColor Cyan
    dotnet run
}
finally {
    Pop-Location
}
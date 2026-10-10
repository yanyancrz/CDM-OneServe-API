# =====================================================
# stop-api.ps1
# CDM-OneServe-API helper  -  kills every running API instance
#
# BIND THIS TO A HOTKEY
#   Desktop\Stop API.lnk  (Ctrl+Alt+K) already points here.
#   Ctrl+Shift+K also works - see the second shortcut.
#
# It stops BOTH kinds of process:
#   * CDM-OneServe-API.exe   - the app itself, which holds port 5212 and
#                             locks bin\Debug\...\CDM-OneServe-API.exe
#   * dotnet.exe running "dotnet run" for this project - the host that
#                             respawns the app if you only kill the first one
#
#   -Quiet  used by the Ctrl+Alt+K desktop shortcut: kill silently, no window
#           stays open waiting for Enter.
# =====================================================

param([switch]$Quiet)

$projectRoot = "C:\Users\Administrator\Desktop\CAPSTONE\CDM-OneServe-API"
$killed = 0

# 1) The app process.
Get-Process -Name "CDM-OneServe-API" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping API (PID $($_.Id))..." -ForegroundColor Yellow
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    $killed++
}

# 2) The "dotnet run" host for this project - otherwise it restarts the app.
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*CDM-OneServe-API*" } |
    ForEach-Object {
        Write-Host "Stopping dotnet run host (PID $($_.ProcessId))..." -ForegroundColor Yellow
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        $killed++
    }

# 3) Also clear anything still squatting on the port, whatever its name.
$holders = Get-NetTCPConnection -LocalPort 5212 -State Listen -ErrorAction SilentlyContinue
foreach ($h in $holders) {
    Write-Host "Port 5212 still held by PID $($h.OwningProcess)..." -ForegroundColor Yellow
    Stop-Process -Id $h.OwningProcess -Force -ErrorAction SilentlyContinue
    $killed++
}

Start-Sleep -Seconds 1

if ($Quiet) { return }

if ($killed -eq 0) {
    Write-Host "No API was running." -ForegroundColor DarkGray
}
else {
    # Clear-Buffer so a stray Enter cannot start a new one on accident.
    Write-Host ""
    Write-Host "API stopped. You can now press F5 or run dotnet run." -ForegroundColor Green
    Write-Host "  .\$([IO.Path]::GetFileName($PSScriptRoot))\restart-api.ps1   # build + start" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "[Enter] to close this window..." -ForegroundColor DarkGray
$null = Read-Host
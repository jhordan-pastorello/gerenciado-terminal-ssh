param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\StorageTerm"),
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

Get-Process StorageTerm -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $runKey -Name "StorageTerm" -ErrorAction SilentlyContinue
Write-Host "Auto-start removed."

$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "StorageTerm.lnk"
Remove-Item $shortcutPath -Force -ErrorAction SilentlyContinue
Write-Host "Start Menu shortcut removed."

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Install folder removed: $InstallDir"
}

if ($RemoveData) {
    $data = Join-Path $env:LOCALAPPDATA "StorageTerm"
    if (Test-Path $data) {
        Remove-Item $data -Recurse -Force
        Write-Host "Settings and logs removed: $data"
    }
} else {
    Write-Host "Settings and logs kept at: $(Join-Path $env:LOCALAPPDATA 'StorageTerm')"
}

Write-Host "StorageTerm uninstalled."

param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\StorageTerm"),
    [switch]$NoAutoStart,
    [switch]$NoLaunch,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$running = Get-Process StorageTerm -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing the running StorageTerm instance..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

Write-Host "Installing StorageTerm to: $InstallDir"

if ($FrameworkDependent) {
    & (Join-Path $root "publish.ps1") -Output $InstallDir
} else {
    & (Join-Path $root "publish.ps1") -SelfContained -Output $InstallDir
}

$exe = Join-Path $InstallDir "StorageTerm.exe"
if (-not (Test-Path $exe)) {
    throw "Installation failed: StorageTerm.exe was not created in $InstallDir."
}

# Marker read by the app: only installed copies auto-update.
$marker = Join-Path $InstallDir "install.json"
@{
    autoUpdate = $true
    installedAt = (Get-Date).ToString("o")
    version = (Get-Item $exe).VersionInfo.FileVersion
} | ConvertTo-Json | Set-Content -Path $marker -Encoding UTF8

# Start Menu shortcut for the current user.
$programs = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $programs "StorageTerm.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $InstallDir
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = "StorageTerm - SSH terminal manager"
$shortcut.Save()
Write-Host "Start Menu shortcut: $shortcutPath"

# Auto-start at logon, hidden in the tray.
if (-not $NoAutoStart) {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name "StorageTerm" -Value "`"$exe`" --tray"
    Write-Host "Auto-start enabled for the current user (--tray)."
} else {
    Write-Host "Auto-start skipped."
}

if (-not $NoLaunch) {
    Start-Process $exe
    Write-Host "StorageTerm started."
}

Write-Host ""
Write-Host "Done. Use 'Sair' in the tray menu to quit, or run .\uninstall.ps1 to remove it."

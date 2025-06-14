# Relaunch as admin if not already
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator))
{
    Start-Process powershell "-ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$playniteExe = "$env:LOCALAPPDATA\Playnite\Playnite.DesktopApp.exe"
$buildOutput = "C:\Users\Krach\RiderProjects\SaveTracker\bin\Debug*"
$extensionDest = "$env:LOCALAPPDATA\Playnite\Extensions\SaveTracker"

Write-Host "`n>> Stopping Playnite if running..."

# Try to close Playnite gracefully
$proc = Get-Process -Name "Playnite.DesktopApp" -ErrorAction SilentlyContinue
if ($proc) {
    $proc.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 3

    # Force kill if still running
    $proc = Get-Process -Name "Playnite.DesktopApp" -ErrorAction SilentlyContinue
    if ($proc) {
        try {
            Stop-Process -Id $proc.Id -Force
            Write-Host "Force closed Playnite."
        } catch {
            Write-Warning "Could not stop Playnite: $($_.Exception.Message)"
        }
    } else {
        Write-Host "Playnite closed gracefully."
    }
} else {
    Write-Host "Playnite not running."
}

# Wait until process is fully exited
while (Get-Process "Playnite.DesktopApp" -ErrorAction SilentlyContinue) {
    Start-Sleep -Milliseconds 500
}

# Ensure destination exists
if (!(Test-Path $extensionDest)) {
    New-Item -ItemType Directory -Path $extensionDest | Out-Null
    Write-Host "Created extension directory at: $extensionDest"
}

Write-Host "`n>> Copying files to extension folder..."
try {
    Copy-Item -Path $buildOutput -Destination $extensionDest -Recurse -Force
    Write-Host "✅ Copied all build files to Playnite extension directory."
} catch {
    Write-Warning "❌ Failed to copy files: $($_.Exception.Message)"
    exit 1
}

Write-Host "`n>> Launching Playnite..."
Start-Process $playniteExe
Write-Host "✅ Playnite restarted."

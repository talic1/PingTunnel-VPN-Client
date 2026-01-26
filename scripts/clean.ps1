# PingTunnelVPN Clean Script
# Removes build artifacts and temporary files

param(
    [switch]$Deep  # Also remove downloaded third-party binaries
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path $PSScriptRoot -Parent

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PingTunnelVPN Clean Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Define paths
$srcDir = Join-Path $RootDir "src"
$appDir = Join-Path $srcDir "PingTunnelVPN.App"
$coreDir = Join-Path $srcDir "PingTunnelVPN.Core"
$systemDir = Join-Path $srcDir "PingTunnelVPN.System"
$thirdPartyDir = Join-Path $RootDir "third_party"

# Directories to clean
$dirsToClean = @(
    (Join-Path $RootDir "dist"),
    (Join-Path $appDir "bin"),
    (Join-Path $appDir "obj"),
    (Join-Path $coreDir "bin"),
    (Join-Path $coreDir "obj"),
    (Join-Path $systemDir "bin"),
    (Join-Path $systemDir "obj")
)

Write-Host "Cleaning build directories..." -ForegroundColor Yellow

foreach ($dir in $dirsToClean) {
    if (Test-Path $dir) {
        Remove-Item -Path $dir -Recurse -Force
        Write-Host "  Removed: $dir" -ForegroundColor Green
    }
}

# Clean Resources folder (copied binaries)
$resourcesDir = Join-Path $appDir "Resources"
$binariesToClean = @("pingtunnel.exe", "tun2socks.exe", "wintun.dll")

foreach ($binary in $binariesToClean) {
    $binaryPath = Join-Path $resourcesDir $binary
    if (Test-Path $binaryPath) {
        Remove-Item -Path $binaryPath -Force
        Write-Host "  Removed: Resources\$binary" -ForegroundColor Green
    }
}

# Deep clean: remove downloaded third-party binaries
if ($Deep) {
    Write-Host ""
    Write-Host "Deep cleaning third-party binaries..." -ForegroundColor Yellow
    
    $pingTunnelDir = Join-Path $thirdPartyDir "pingtunnel"
    $tun2socksDir = Join-Path $thirdPartyDir "tun2socks"
    $wintunDir = Join-Path $thirdPartyDir "wintun"
    
    $thirdPartyBinaries = @(
        (Join-Path $pingTunnelDir "pingtunnel.exe"),
        (Join-Path $tun2socksDir "tun2socks.exe"),
        (Join-Path $wintunDir "wintun.dll"),
        (Join-Path $wintunDir "WINTUN_LICENSE.txt")
    )
    
    foreach ($binary in $thirdPartyBinaries) {
        if (Test-Path $binary) {
            Remove-Item -Path $binary -Force
            Write-Host "  Removed: $binary" -ForegroundColor Green
        }
    }
}

Write-Host ""
Write-Host "Clean completed!" -ForegroundColor Green
Write-Host ""

if (-not $Deep) {
    Write-Host "TIP: Use -Deep to also remove downloaded third-party binaries" -ForegroundColor Yellow
    Write-Host ""
}

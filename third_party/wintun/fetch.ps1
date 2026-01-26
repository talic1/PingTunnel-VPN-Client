# Fetch Wintun v0.14.1 for Windows
# This script downloads the official Wintun driver from WireGuard

param(
    [string]$Version = "0.14.1",
    [string]$OutputDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$releaseUrl = "https://www.wintun.net/builds/wintun-$Version.zip"
$zipFile = Join-Path $OutputDir "wintun.zip"
$dllFile = Join-Path $OutputDir "wintun.dll"

Write-Host "Fetching Wintun v$Version..." -ForegroundColor Cyan

# Clean up existing files
if (Test-Path $zipFile) { Remove-Item $zipFile -Force }

try {
    # Download the release
    Write-Host "Downloading from: $releaseUrl"
    
    # Use TLS 1.2
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    
    Invoke-WebRequest -Uri $releaseUrl -OutFile $zipFile -UseBasicParsing
    
    Write-Host "Extracting..."
    $tempDir = Join-Path $OutputDir "wintun_temp"
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    
    Expand-Archive -Path $zipFile -DestinationPath $tempDir -Force
    
    # Copy the x64 DLL
    $sourceDll = Join-Path $tempDir "wintun\bin\amd64\wintun.dll"
    if (Test-Path $sourceDll) {
        Copy-Item -Path $sourceDll -Destination $dllFile -Force
        Write-Host "Copied wintun.dll (amd64)" -ForegroundColor Green
    }
    else {
        throw "wintun.dll not found in expected location"
    }
    
    # Copy the license
    $sourceLicense = Get-ChildItem -Path $tempDir -Filter "LICENSE*" -Recurse | Select-Object -First 1
    if ($sourceLicense) {
        Copy-Item -Path $sourceLicense.FullName -Destination (Join-Path $OutputDir "WINTUN_LICENSE.txt") -Force
    }
    
    # Clean up
    Remove-Item $zipFile -Force
    Remove-Item $tempDir -Recurse -Force
    
    # Verify the DLL exists
    if (Test-Path $dllFile) {
        Write-Host "Successfully downloaded wintun.dll" -ForegroundColor Green
        Write-Host "Location: $dllFile"
    }
    else {
        throw "wintun.dll not found after extraction"
    }
}
catch {
    Write-Host "Error downloading Wintun: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Manual download:" -ForegroundColor Yellow
    Write-Host "1. Download from: https://www.wintun.net/"
    Write-Host "2. Extract wintun-$Version.zip"
    Write-Host "3. Copy wintun\bin\amd64\wintun.dll to: $OutputDir"
    exit 1
}

Write-Host ""
Write-Host "Wintun v$Version ready!" -ForegroundColor Green
Write-Host ""
Write-Host "IMPORTANT: Wintun is distributed under a proprietary license for" -ForegroundColor Yellow
Write-Host "prebuilt binaries. See WINTUN_LICENSE.txt for terms." -ForegroundColor Yellow

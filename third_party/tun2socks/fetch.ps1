# Fetch tun2socks v2.5.2 for Windows x64
# This script downloads the pre-built release from GitHub
# Note: Using v2.5.2 as it's a stable release with good Windows support

param(
    [string]$Version = "2.5.2",
    [string]$OutputDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$releaseUrl = "https://github.com/xjasonlyu/tun2socks/releases/download/v$Version/tun2socks-windows-amd64.zip"
$zipFile = Join-Path $OutputDir "tun2socks.zip"
$exeFile = Join-Path $OutputDir "tun2socks.exe"

Write-Host "Fetching tun2socks v$Version..." -ForegroundColor Cyan

# Clean up existing files
if (Test-Path $zipFile) { Remove-Item $zipFile -Force }
if (Test-Path $exeFile) { Remove-Item $exeFile -Force }

try {
    # Download the release
    Write-Host "Downloading from: $releaseUrl"
    
    # Use TLS 1.2 for GitHub
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    
    Invoke-WebRequest -Uri $releaseUrl -OutFile $zipFile -UseBasicParsing
    
    Write-Host "Extracting..."
    Expand-Archive -Path $zipFile -DestinationPath $OutputDir -Force
    
    # Clean up zip
    Remove-Item $zipFile -Force
    
    # The archive might have a nested structure, find the exe
    $foundExe = Get-ChildItem -Path $OutputDir -Filter "tun2socks*.exe" -Recurse | Select-Object -First 1
    if ($foundExe -and $foundExe.FullName -ne $exeFile) {
        Move-Item -Path $foundExe.FullName -Destination $exeFile -Force
    }
    
    # Verify the executable exists
    if (Test-Path $exeFile) {
        Write-Host "Successfully downloaded tun2socks.exe" -ForegroundColor Green
        Write-Host "Location: $exeFile"
    }
    else {
        throw "tun2socks.exe not found after extraction"
    }
}
catch {
    Write-Host "Error downloading tun2socks: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Alternative: Build from source" -ForegroundColor Yellow
    Write-Host "1. Install Go 1.22+"
    Write-Host "2. git clone https://github.com/xjasonlyu/tun2socks.git"
    Write-Host "3. cd tun2socks"
    Write-Host "4. go build -o tun2socks.exe ./cmd/tun2socks"
    Write-Host "5. Copy tun2socks.exe to: $OutputDir"
    exit 1
}

Write-Host ""
Write-Host "tun2socks v$Version ready!" -ForegroundColor Green

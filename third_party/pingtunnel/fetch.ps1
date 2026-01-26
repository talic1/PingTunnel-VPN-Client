# Fetch pingtunnel v2.8 for Windows x64
# This script downloads the pre-built release from GitHub

param(
    [string]$Version = "2.8",
    [string]$OutputDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$releaseUrl = "https://github.com/esrrhs/pingtunnel/releases/download/$Version/pingtunnel_windows_amd64.zip"
$zipFile = Join-Path $OutputDir "pingtunnel.zip"
$exeFile = Join-Path $OutputDir "pingtunnel.exe"

Write-Host "Fetching pingtunnel v$Version..." -ForegroundColor Cyan

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
    
    # Verify the executable exists
    if (Test-Path $exeFile) {
        Write-Host "Successfully downloaded pingtunnel.exe" -ForegroundColor Green
        Write-Host "Location: $exeFile"
    }
    else {
        throw "pingtunnel.exe not found after extraction"
    }
}
catch {
    Write-Host "Error downloading pingtunnel: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Alternative: Build from source" -ForegroundColor Yellow
    Write-Host "1. Install Go 1.20+"
    Write-Host "2. git clone https://github.com/esrrhs/pingtunnel.git"
    Write-Host "3. cd pingtunnel && go build -o pingtunnel.exe"
    Write-Host "4. Copy pingtunnel.exe to: $OutputDir"
    exit 1
}

Write-Host ""
Write-Host "pingtunnel v$Version ready!" -ForegroundColor Green

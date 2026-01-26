# PingTunnelVPN Build Script
# This script builds the complete application including third-party dependencies

param(
    [switch]$SkipDependencies,
    [switch]$Release,
    [string]$OutputDir = (Join-Path (Join-Path $PSScriptRoot "..") "dist")
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path $PSScriptRoot -Parent

# Load build configuration
$BuildConfigPath = Join-Path $RootDir "build.config.ps1"
if (Test-Path $BuildConfigPath) {
    . $BuildConfigPath
    Write-Host "Loaded build configuration from build.config.ps1" -ForegroundColor Green
    Write-Host "  App Name: $AppName" -ForegroundColor Cyan
    Write-Host "  App Version: $AppVersion" -ForegroundColor Cyan
    Write-Host "  GitHub URL: $GitHubUrl" -ForegroundColor Cyan
    Write-Host ""
} else {
    Write-Host "WARNING: build.config.ps1 not found. Using defaults." -ForegroundColor Yellow
    $AppName = "PingTunnelVPN"
    $AppVersion = "1.0.0.0"
    $GitHubUrl = "https://github.com/DrSaeedHub/PingTunnel-VPN-Client"
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PingTunnelVPN Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

# Check .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Host "  .NET SDK: $dotnetVersion" -ForegroundColor Green
}
catch {
    Write-Host "  ERROR: .NET SDK not found. Please install .NET 8 SDK." -ForegroundColor Red
    Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

# Verify .NET 8
if (-not $dotnetVersion.StartsWith("8.")) {
    Write-Host "  WARNING: .NET 8 SDK recommended. Found: $dotnetVersion" -ForegroundColor Yellow
}

Write-Host ""

# Step 1: Fetch third-party dependencies (skip if already present)
if (-not $SkipDependencies) {
    Write-Host "Step 1: Fetching third-party dependencies..." -ForegroundColor Yellow
    
    $thirdPartyDir = Join-Path $RootDir "third_party"
    
    # Fetch pingtunnel (skip if pingtunnel.exe already exists)
    $pingtunnelExe = Join-Path (Join-Path $thirdPartyDir "pingtunnel") "pingtunnel.exe"
    if (Test-Path $pingtunnelExe) {
        Write-Host "  pingtunnel.exe already present, skipping download" -ForegroundColor Green
    } else {
        Write-Host "  Fetching pingtunnel..." -ForegroundColor Cyan
        & (Join-Path (Join-Path $thirdPartyDir "pingtunnel") "fetch.ps1")
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  WARNING: Failed to fetch pingtunnel. Please download manually." -ForegroundColor Yellow
        }
    }
    
    # Fetch tun2socks (skip if tun2socks.exe already exists)
    $tun2socksExe = Join-Path (Join-Path $thirdPartyDir "tun2socks") "tun2socks.exe"
    if (Test-Path $tun2socksExe) {
        Write-Host "  tun2socks.exe already present, skipping download" -ForegroundColor Green
    } else {
        Write-Host "  Fetching tun2socks..." -ForegroundColor Cyan
        & (Join-Path (Join-Path $thirdPartyDir "tun2socks") "fetch.ps1")
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  WARNING: Failed to fetch tun2socks. Please download manually." -ForegroundColor Yellow
        }
    }
    
    # Fetch wintun (skip if wintun.dll already exists)
    $wintunDll = Join-Path (Join-Path $thirdPartyDir "wintun") "wintun.dll"
    if (Test-Path $wintunDll) {
        Write-Host "  wintun.dll already present, skipping download" -ForegroundColor Green
    } else {
        Write-Host "  Fetching wintun..." -ForegroundColor Cyan
        & (Join-Path (Join-Path $thirdPartyDir "wintun") "fetch.ps1")
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  WARNING: Failed to fetch wintun. Please download manually." -ForegroundColor Yellow
        }
    }
    
    Write-Host ""
}
else {
    Write-Host "Step 1: Skipping dependency fetch (--SkipDependencies)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 2: Copy binaries to Resources folder
Write-Host "Step 2: Copying binaries to Resources folder..." -ForegroundColor Yellow

$srcDir = Join-Path $RootDir "src"
$appDir = Join-Path $srcDir "PingTunnelVPN.App"
$resourcesDir = Join-Path $appDir "Resources"
if (-not (Test-Path $resourcesDir)) {
    New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null
}

$thirdPartyDir = Join-Path $RootDir "third_party"
$binaries = @(
    @{ Source = Join-Path (Join-Path $thirdPartyDir "pingtunnel") "pingtunnel.exe"; Dest = "pingtunnel.exe" },
    @{ Source = Join-Path (Join-Path $thirdPartyDir "tun2socks") "tun2socks.exe"; Dest = "tun2socks.exe" },
    @{ Source = Join-Path (Join-Path $thirdPartyDir "wintun") "wintun.dll"; Dest = "wintun.dll" }
)

$missingBinaries = @()
foreach ($bin in $binaries) {
    if (Test-Path $bin.Source) {
        Copy-Item -Path $bin.Source -Destination (Join-Path $resourcesDir $bin.Dest) -Force
        Write-Host "  Copied: $($bin.Dest)" -ForegroundColor Green
    }
    else {
        Write-Host "  Missing: $($bin.Dest)" -ForegroundColor Yellow
        $missingBinaries += $bin.Dest
    }
}

# Copy app icons from icon/ to Resources (icon.ico = app/tray connected, icon-off.ico = tray disconnected)
$iconSourceDir = Join-Path $RootDir "icon"
foreach ($ico in @("icon.ico", "icon-off.ico")) {
    $src = Join-Path $iconSourceDir $ico
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination (Join-Path $resourcesDir $ico) -Force
        Write-Host "  Copied: $ico" -ForegroundColor Green
    }
    else {
        Write-Host "  Missing: $ico" -ForegroundColor Yellow
    }
}

if ($missingBinaries.Count -gt 0) {
    Write-Host ""
    Write-Host "WARNING: Some binaries are missing. The app may not work correctly." -ForegroundColor Yellow
    Write-Host "Missing: $($missingBinaries -join ', ')" -ForegroundColor Yellow
    Write-Host "Run the fetch scripts in third_party/ to download them." -ForegroundColor Yellow
}

Write-Host ""

# Step 2.5: Apply build configuration (version, app name, etc.)
Write-Host "Step 2.5: Applying build configuration..." -ForegroundColor Yellow

$appDir = Join-Path $RootDir "src\PingTunnelVPN.App"
$csprojPath = Join-Path $appDir "PingTunnelVPN.App.csproj"
$manifestPath = Join-Path $appDir "app.manifest"
$appInfoPath = Join-Path $appDir "AppInfo.cs"

# Read current AssemblyName from .csproj to determine executable name
$actualExeName = $AppName
if (Test-Path $csprojPath) {
    $csprojContent = Get-Content $csprojPath -Raw
    if ($csprojContent -match '<AssemblyName>([^<]*)</AssemblyName>') {
        $actualExeName = $matches[1]
        Write-Host "  Detected AssemblyName: $actualExeName" -ForegroundColor Cyan
    }
}

# Update .csproj with version
if (Test-Path $csprojPath) {
    $csprojContent = Get-Content $csprojPath -Raw
    
    # Add or update Version property
    if ($csprojContent -match '<Version>[^<]*</Version>') {
        $csprojContent = $csprojContent -replace '<Version>[^<]*</Version>', "<Version>$AppVersion</Version>"
    } else {
        # Add Version property after AssemblyName
        $csprojContent = $csprojContent -replace '(<AssemblyName>[^<]*</AssemblyName>)', "`$1`n    <Version>$AppVersion</Version>"
    }
    
    # Add or update AssemblyName if AppName is different
    if ($AppName -ne "PingTunnelVPN") {
        if ($csprojContent -match '<AssemblyName>[^<]*</AssemblyName>') {
            $csprojContent = $csprojContent -replace '<AssemblyName>[^<]*</AssemblyName>', "<AssemblyName>$AppName</AssemblyName>"
        }
    }
    
    Set-Content -Path $csprojPath -Value $csprojContent -NoNewline
    Write-Host "  Updated .csproj with version: $AppVersion" -ForegroundColor Green
    
    # Re-read AssemblyName after potential update
    if ($csprojContent -match '<AssemblyName>([^<]*)</AssemblyName>') {
        $actualExeName = $matches[1]
        Write-Host "  Final AssemblyName: $actualExeName" -ForegroundColor Cyan
    }
}

# Update app.manifest with version
if (Test-Path $manifestPath) {
    $manifestContent = Get-Content $manifestPath -Raw
    
    # Ensure the manifest has the correct structure first
    if ($manifestContent -notmatch '<assemblyIdentity') {
        Write-Host "  WARNING: Manifest appears corrupted, restoring structure..." -ForegroundColor Yellow
        $manifestContent = @"
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="PingTunnelVPN.App"/>
  
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <!-- Request administrator privileges -->
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>

  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 and Windows 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>

  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
"@
    }
    
    # Fix manifest XML attributes if needed (version should be 1.0, manifestVersion should be 1.0)
    $manifestContent = $manifestContent -replace 'version="1\.0\.0"', 'version="1.0"'
    $manifestContent = $manifestContent -replace 'manifestversion=', 'manifestVersion='
    
    # Update version in assemblyIdentity tag (use 4-part version for assemblyIdentity)
    $manifestVersion = if ($AppVersion -match '^(\d+)\.(\d+)\.(\d+)$') {
        "$($matches[1]).$($matches[2]).$($matches[3]).0"
    } elseif ($AppVersion -match '^(\d+)\.(\d+)\.(\d+)\.(\d+)$') {
        $AppVersion
    } else {
        "$AppVersion.0"
    }
    
    # More careful replacement - only replace the version attribute in assemblyIdentity
    # Use a more specific pattern to avoid corrupting the tag
    if ($manifestContent -match '(<assemblyIdentity[^>]*\s+version=")[^"]*(")') {
        $manifestContent = $manifestContent -replace '(<assemblyIdentity[^>]*\s+version=")[^"]*(")', "`$1$manifestVersion`$2"
    } elseif ($manifestContent -match '<assemblyIdentity') {
        # If assemblyIdentity exists but pattern didn't match, try a safer approach
        $manifestContent = $manifestContent -replace '(version=")[^"]*(")', "`$1$manifestVersion`$2"
    }
    
    Set-Content -Path $manifestPath -Value $manifestContent -NoNewline
    Write-Host "  Updated app.manifest with version: $manifestVersion" -ForegroundColor Green
}

# Generate AppInfo.cs
$appInfoContent = @"
using System.Reflection;

namespace PingTunnelVPN.App;

/// <summary>
/// Application metadata information.
/// This file is auto-generated by the build script.
/// </summary>
public static class AppInfo
{
    /// <summary>
    /// Gets the application name.
    /// </summary>
    public static string Name => "$AppName";

    /// <summary>
    /// Gets the application version.
    /// </summary>
    public static string Version => "$AppVersion";

    /// <summary>
    /// Gets the GitHub repository URL.
    /// </summary>
    public static string GitHubUrl => "$GitHubUrl";

    /// <summary>
    /// Gets the application version from the assembly.
    /// </summary>
    public static string AssemblyVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? Version;
        }
    }
}
"@

Set-Content -Path $appInfoPath -Value $appInfoContent
Write-Host "  Generated AppInfo.cs" -ForegroundColor Green

Write-Host ""

# Step 3: Restore NuGet packages
Write-Host "Step 3: Restoring NuGet packages..." -ForegroundColor Yellow
Push-Location $RootDir
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed"
    }
    Write-Host "  NuGet packages restored" -ForegroundColor Green
}
finally {
    Pop-Location
}

Write-Host ""

# Step 4: Build the solution
Write-Host "Step 4: Building solution..." -ForegroundColor Yellow

$configuration = if ($Release) { "Release" } else { "Debug" }
Write-Host "  Configuration: $configuration" -ForegroundColor Cyan

Push-Location $RootDir
try {
    dotnet build -c $configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    Write-Host "  Build succeeded" -ForegroundColor Green
}
finally {
    Pop-Location
}

Write-Host ""

# Step 5: Publish self-contained executable
Write-Host "Step 5: Publishing self-contained executable..." -ForegroundColor Yellow

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Push-Location $RootDir
try {
    $publishDir = Join-Path $OutputDir "publish"
    
    dotnet publish "src\PingTunnelVPN.App\PingTunnelVPN.App.csproj" `
        -c $configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeAllContentForSelfExtract=true `
        -o $publishDir
    
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed"
    }
    
    # Copy the main executable to dist root
    # The executable name matches the AssemblyName from .csproj
    $exeName = "$actualExeName.exe"
    $exePath = Join-Path $publishDir $exeName
    
    # If the expected exe doesn't exist, try to find any .exe in the publish directory
    if (-not (Test-Path $exePath)) {
        $foundExe = Get-ChildItem -Path $publishDir -Filter "*.exe" | Where-Object { $_.Name -notlike "*tun2socks*" -and $_.Name -notlike "*pingtunnel*" } | Select-Object -First 1
        if ($foundExe) {
            $exeName = $foundExe.Name
            $exePath = $foundExe.FullName
            Write-Host "  Found executable: $exeName" -ForegroundColor Yellow
        }
    }
    
    if (Test-Path $exePath) {
        # Copy executable first
        $finalExePath = Join-Path $OutputDir $exeName
        Copy-Item -Path $exePath -Destination $finalExePath -Force
        
        # Try to re-embed manifest using mt.exe if available (Windows SDK tool)
        # This can help fix side-by-side configuration issues with single-file apps
        $mtPath = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\mt.exe"
        if (-not (Test-Path $mtPath)) {
            # Try to find mt.exe in common locations
            $possiblePaths = @(
                "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\mt.exe",
                "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\mt.exe"
            )
            foreach ($pattern in $possiblePaths) {
                $found = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($found) {
                    $mtPath = $found.FullName
                    break
                }
            }
        }
        
        if (Test-Path $mtPath) {
            try {
                Write-Host "  Re-embedding manifest using mt.exe..." -ForegroundColor Cyan
                & $mtPath -manifest $manifestPath -outputresource:"$finalExePath;1" | Out-Null
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "  Manifest re-embedded successfully" -ForegroundColor Green
                }
            } catch {
                Write-Host "  WARNING: Failed to re-embed manifest: $_" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  NOTE: mt.exe not found, manifest embedded by build system" -ForegroundColor Yellow
        }
        
        Write-Host "  Published: $OutputDir\$exeName" -ForegroundColor Green
    } else {
        Write-Host "  WARNING: Executable not found in publish directory" -ForegroundColor Yellow
        Write-Host "  Expected: $exePath" -ForegroundColor Yellow
        Write-Host "  Available files:" -ForegroundColor Yellow
        Get-ChildItem -Path $publishDir -Filter "*.exe" | ForEach-Object {
            Write-Host "    - $($_.Name)" -ForegroundColor Yellow
        }
    }
    
    # Create Resources folder in dist and copy binaries and icons there
    $distResourcesDir = Join-Path $OutputDir "Resources"
    if (-not (Test-Path $distResourcesDir)) {
        New-Item -ItemType Directory -Path $distResourcesDir -Force | Out-Null
    }
    
    # Copy binaries
    foreach ($bin in $binaries) {
        $srcPath = Join-Path $resourcesDir $bin.Dest
        if (Test-Path $srcPath) {
            Copy-Item -Path $srcPath -Destination (Join-Path $distResourcesDir $bin.Dest) -Force
            Write-Host "  Copied to Resources: $($bin.Dest)" -ForegroundColor Green
        }
    }
    
    # Copy icon files (try source Resources, then icon/, then publish directory)
    $iconFiles = @("icon.ico", "icon-off.ico")
    $iconSourceDir = Join-Path $RootDir "icon"
    foreach ($iconFile in $iconFiles) {
        $copied = $false
        $srcIconPath = Join-Path $resourcesDir $iconFile
        if (-not (Test-Path $srcIconPath)) { $srcIconPath = Join-Path $iconSourceDir $iconFile }
        if (Test-Path $srcIconPath) {
            Copy-Item -Path $srcIconPath -Destination (Join-Path $distResourcesDir $iconFile) -Force
            Write-Host "  Copied to Resources: $iconFile" -ForegroundColor Green
            $copied = $true
        }
        if (-not $copied) {
            $publishResourcesDir = Join-Path $publishDir "Resources"
            $publishIconPath = Join-Path $publishResourcesDir $iconFile
            if (Test-Path $publishIconPath) {
                Copy-Item -Path $publishIconPath -Destination (Join-Path $distResourcesDir $iconFile) -Force
                Write-Host "  Copied to Resources: $iconFile (from publish)" -ForegroundColor Green
                $copied = $true
            }
        }
        if (-not $copied) {
            Write-Host "  WARNING: Icon file not found: $iconFile" -ForegroundColor Yellow
        }
    }
}
finally {
    Pop-Location
}

Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output directory: $OutputDir" -ForegroundColor White
Write-Host ""
Write-Host "Files:" -ForegroundColor White
Get-ChildItem $OutputDir -Filter "*.exe" | ForEach-Object {
    Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Green
}
Get-ChildItem $OutputDir -Filter "*.dll" | ForEach-Object {
    Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1KB, 2)) KB)" -ForegroundColor Green
}

Write-Host ""
Write-Host "To run the application:" -ForegroundColor Yellow
$exeFiles = Get-ChildItem $OutputDir -Filter "*.exe" | Where-Object { $_.Name -notlike "*tun2socks*" -and $_.Name -notlike "*pingtunnel*" }
if ($exeFiles) {
    $mainExe = $exeFiles[0].Name
    Write-Host "  1. Run as Administrator: $OutputDir\$mainExe" -ForegroundColor White
} else {
    Write-Host "  1. Run as Administrator: $OutputDir\$actualExeName.exe" -ForegroundColor White
}
Write-Host ""

<#
.SYNOPSIS
    Copies the built plugin into a vatSys profile's Plugins folder.

.DESCRIPTION
    vatSys loads plugins from two places:
      * <Documents>\vatSys Files\Profiles\<Profile>\Plugins   (per profile, no admin needed)
      * <install>\bin\Plugins                                 (all profiles, needs admin)

    This script uses the per-profile folder. Close vatSys before running it -
    the DLL is locked while vatSys is open.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -Profile 'New Zealand'
    .\install.ps1 -Profile All
#>
[CmdletBinding()]
param(
    [string]$Profile,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot "src\CannedMessages\bin\$Configuration"
if (-not (Test-Path (Join-Path $source 'vatSysCannedMessages.dll'))) {
    throw "Nothing built in '$source'. Run .\build.ps1 first."
}

$profilesRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'vatSys Files\Profiles'
if (-not (Test-Path $profilesRoot)) {
    throw "No vatSys profiles found at '$profilesRoot'. Start vatSys once and load a profile first."
}

$available = Get-ChildItem -Path $profilesRoot -Directory | Select-Object -ExpandProperty Name

if (-not $Profile) {
    if ($available.Count -eq 1) {
        $Profile = $available[0]
    }
    else {
        Write-Host "Available profiles: $($available -join ', ')"
        throw 'Pass -Profile with the profile name, or -Profile All.'
    }
}

$targets = if ($Profile -eq 'All') { $available } else { @($Profile) }

foreach ($name in $targets) {
    $profileDir = Join-Path $profilesRoot $name
    if (-not (Test-Path $profileDir)) {
        Write-Warning "Profile '$name' not found - skipping."
        continue
    }

    $destination = Join-Path $profileDir 'Plugins\CannedMessages'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
    Write-Host "Installed to $destination" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Restart vatSys, then open Messages > Canned Messages.'

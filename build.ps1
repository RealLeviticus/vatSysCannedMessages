<#
.SYNOPSIS
    Builds the vatSys Canned Messages plugin.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Debug
    .\build.ps1 -VatSysPath 'D:\vatSys\bin'
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$VatSysPath = 'C:\Program Files (x86)\vatSys\bin'
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\CannedMessages\CannedMessages.csproj'

if (-not (Test-Path (Join-Path $VatSysPath 'vatSys.exe'))) {
    throw "vatSys.exe not found in '$VatSysPath'. Pass -VatSysPath with your install location."
}

$msbuild = Get-ChildItem -Path @(
    'C:\Program Files\Microsoft Visual Studio\2022',
    'C:\Program Files (x86)\Microsoft Visual Studio\2022'
) -Filter 'MSBuild.exe' -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\Bin\\MSBuild.exe$' } |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $msbuild) {
    $msbuild = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'
}

if (-not (Test-Path $msbuild)) {
    throw 'Could not find MSBuild. Install Visual Studio 2022 or the Build Tools for Visual Studio 2022.'
}

Write-Host "MSBuild : $msbuild"
Write-Host "vatSys  : $VatSysPath"

& $msbuild $project "/p:Configuration=$Configuration" "/p:VatSysPath=$VatSysPath" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

$output = Join-Path $PSScriptRoot "src\CannedMessages\bin\$Configuration"
Write-Host ''
Write-Host "Built to $output" -ForegroundColor Green
Write-Host 'Run .\install.ps1 to copy it into a vatSys profile.'

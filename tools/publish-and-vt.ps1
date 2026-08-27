# Publish Release to dist\, then upload a ZIP of dist\ to VirusTotal (local API key).
#
#   .\tools\publish-and-vt.ps1
#   .\tools\publish-and-vt.ps1 -Wait
#   .\tools\publish-and-vt.ps1 -SkipPublish -Wait

[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$Wait,
    [switch]$ExeOnly
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

if (-not $SkipPublish) {
    Write-Host "Publishing to dist\..."
    dotnet publish src\Tarkovy\Tarkovy.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$scan = Join-Path $PSScriptRoot "vt-scan-dist.ps1"
$args = @{}
if ($Wait) { $args.Wait = $true }
if ($ExeOnly) { $args.ExeOnly = $true }
& $scan @args

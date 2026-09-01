# Publish Release to dist\ (single Tarkovy.exe), then upload to VirusTotal (local API key).
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
    $running = Get-Process Tarkovy -ErrorAction SilentlyContinue
    if ($running) {
        throw "Close Tarkovy.exe (PID $($running.Id -join ', ')) before publish. Output is always dist\ — never dist-new."
    }
    if (Test-Path dist-new) {
        Write-Host "Removing leftover dist-new\..."
        Remove-Item dist-new -Recurse -Force
    }
    if (Test-Path dist) {
        Write-Host "Cleaning dist\..."
        Remove-Item dist\* -Recurse -Force -ErrorAction SilentlyContinue
    }
    dotnet publish src\Tarkovy\Tarkovy.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$scan = Join-Path $PSScriptRoot "vt-scan-dist.ps1"
$scanArgs = @{ ExeOnly = $true }
if ($Wait) { $scanArgs.Wait = $true }
& $scan @scanArgs

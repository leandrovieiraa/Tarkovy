# VirusTotal scan for Tarkovy dist (local only — API key never committed).
# Docs: https://docs.virustotal.com/reference/files-scan
# Large files: https://docs.virustotal.com/reference/files-upload-url
#
# Setup (once):
#   copy tools\vt.local.env.example tools\vt.local.env
#   put your key in VT_API_KEY=...
#
# Usage:
#   .\tools\vt-scan-dist.ps1
#   .\tools\vt-scan-dist.ps1 -DistDir .\dist -Wait
#   .\tools\vt-scan-dist.ps1 -ExeOnly

[CmdletBinding()]
param(
    [string]$DistDir = "",
    [switch]$ExeOnly,
    [switch]$Wait,
    [int]$PollSeconds = 15,
    [int]$MaxPolls = 40
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $DistDir) { $DistDir = Join-Path $Root "dist" }
$DistDir = (Resolve-Path $DistDir).Path

function Import-VtLocalEnv {
    $envFile = Join-Path $PSScriptRoot "vt.local.env"
    if (-not (Test-Path $envFile)) { return }
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith("#")) { return }
        $i = $line.IndexOf("=")
        if ($i -lt 1) { return }
        $k = $line.Substring(0, $i).Trim()
        $v = $line.Substring($i + 1).Trim().Trim('"').Trim("'")
        if ($k) { Set-Item -Path "Env:$k" -Value $v }
    }
}

Import-VtLocalEnv
$ApiKey = $env:VT_API_KEY
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "VT_API_KEY not set. Copy tools\vt.local.env.example to tools\vt.local.env and set VT_API_KEY=..."
}

if (-not (Test-Path (Join-Path $DistDir "Tarkovy.exe"))) {
    throw "Tarkovy.exe not found in $DistDir - publish first."
}

$OutDir = Join-Path $Root "tools\_vt-out"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
if ($ExeOnly) {
    $UploadPath = Join-Path $DistDir "Tarkovy.exe"
    $label = "Tarkovy.exe"
} else {
    $UploadPath = Join-Path $OutDir "Tarkovy-dist-$stamp.zip"
    if (Test-Path $UploadPath) { Remove-Item $UploadPath -Force }
    Write-Host "Zipping $DistDir -> $UploadPath"
    Compress-Archive -Path (Join-Path $DistDir "*") -DestinationPath $UploadPath -CompressionLevel Optimal
    $label = Split-Path $UploadPath -Leaf
}

$size = (Get-Item $UploadPath).Length
$hash = (Get-FileHash -Algorithm SHA256 -Path $UploadPath).Hash
Write-Host ("File: {0}" -f $label)
Write-Host ("Size: {0:N1} MB" -f ($size / 1MB))
Write-Host ("SHA-256: {0}" -f $hash)

$headers = @{ "x-apikey" = $ApiKey }

# >32MB needs upload_url (VT admits up to ~650MB on that path).
$uploadUri = "https://www.virustotal.com/api/v3/files"
if ($size -gt 32MB) {
    Write-Host "Requesting large-file upload URL..."
    $urlResp = Invoke-RestMethod -Method Get -Uri "https://www.virustotal.com/api/v3/files/upload_url" -Headers $headers
    $uploadUri = [string]$urlResp.data
    if (-not $uploadUri) { Write-Error "No upload_url in VirusTotal response." }
}

Write-Host "Uploading to VirusTotal..."
# curl builds a correct multipart body (HttpClient multipart was rejected as malformed).
$curlArgs = @(
    "-sS", "-X", "POST", $uploadUri,
    "-H", "x-apikey: $ApiKey",
    "-F", "file=@$UploadPath"
)
$body = & curl.exe @curlArgs
if ($LASTEXITCODE -ne 0) {
    throw "curl upload failed with exit $LASTEXITCODE"
}
$json = $body | ConvertFrom-Json
if (-not $json.data.id) {
    throw "Upload failed: $body"
}

$analysisId = $json.data.id
if (-not $analysisId) { Write-Error "No analysis id in response: $body" }

$analysisUrl = "https://www.virustotal.com/gui/file-analysis/$analysisId"
$fileUrl = "https://www.virustotal.com/gui/file/$hash"
Write-Host ""
Write-Host "Analysis ID: $analysisId"
Write-Host "Open (analysis): $analysisUrl"
Write-Host "Open (file SHA): $fileUrl"

$resultPath = Join-Path $OutDir "last-scan.json"
@{
    uploadedAt   = (Get-Date).ToString("o")
    label        = $label
    path         = $UploadPath
    sha256       = $hash
    sizeBytes    = $size
    analysisId   = $analysisId
    analysisUrl  = $analysisUrl
    fileUrl      = $fileUrl
} | ConvertTo-Json | Set-Content $resultPath -Encoding UTF8
Write-Host "Saved: $resultPath"

if (-not $Wait) {
    Write-Host ""
    Write-Host "Tip: re-run with -Wait to poll until the report finishes."
    return
}

Write-Host ""
Write-Host "Waiting for analysis..."
for ($i = 1; $i -le $MaxPolls; $i++) {
    Start-Sleep -Seconds $PollSeconds
    $a = Invoke-RestMethod -Method Get -Uri "https://www.virustotal.com/api/v3/analyses/$analysisId" -Headers $headers
    $status = $a.data.attributes.status
    Write-Host ("  [{0}/{1}] {2}" -f $i, $MaxPolls, $status)
    if ($status -eq "completed") {
        $stats = $a.data.attributes.stats
        Write-Host ""
        Write-Host ("Malicious: {0}  Suspicious: {1}  Undetected: {2}  Harmless: {3}" -f `
            $stats.malicious, $stats.suspicious, $stats.undetected, $stats.harmless)
        Write-Host "Report: $fileUrl"
        return
    }
}

Write-Warning "Still pending after polling. Open: $analysisUrl"

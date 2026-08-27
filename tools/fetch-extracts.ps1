# Fetch extracts from tarkov.dev (optional; the app also does this at runtime)
$query = @{ query = '{ maps { normalizedName extracts { name faction position { x y z } } } }' } | ConvertTo-Json -Compress
$resp = Invoke-RestMethod -Method Post -Uri 'https://api.tarkov.dev/graphql' -ContentType 'application/json' -Body $query
$out = @{}
foreach ($m in $resp.data.maps) {
  $list = @()
  foreach ($e in $m.extracts) {
    if ($null -eq $e.position) { continue }
    $list += [pscustomobject]@{
      name = $e.name
      faction = $e.faction
      x = $e.position.x
      y = $e.position.y
      z = $e.position.z
    }
  }
  $out[$m.normalizedName] = $list
}
$dir = Join-Path $PSScriptRoot '..\src\Tarkovy\Assets'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$out | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $dir 'extracts.json') -Encoding UTF8
Write-Host "Wrote Assets/extracts.json"

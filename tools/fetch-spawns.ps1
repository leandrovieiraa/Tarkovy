# Fetch PMC spawn zones from json.tarkov.dev and write Assets/spawns.json (clustered).
$ErrorActionPreference = "Stop"
$cell = 55.0

$mapToId = @{
    "customs"            = "customs"
    "factory"            = "factory"
    "night-factory"      = "factory"
    "woods"              = "woods"
    "shoreline"          = "shoreline"
    "interchange"        = "interchange"
    "reserve"            = "reserve"
    "lighthouse"         = "lighthouse"
    "streets-of-tarkov"  = "streets-of-tarkov"
    "ground-zero"        = "ground-zero"
    "ground-zero-21"     = "ground-zero"
    "the-lab"            = "the-lab"
    "the-lab-dark"       = "the-lab"
    "terminal"           = "terminal"
    "the-labyrinth"      = "the-labyrinth"
}

function Test-PmcSpawn($spawn) {
    if (-not $spawn.position) { return $false }
    $sides = @($spawn.sides | ForEach-Object { "$_".ToLowerInvariant() })
    $cats = @($spawn.categories | ForEach-Object { "$_".ToLowerInvariant() })
    if ($cats -notcontains "player") { return $false }
    return ($sides -contains "pmc") -or ($sides -contains "all")
}

function Get-ClusterKey($x, $z) {
    $gx = [Math]::Round($x / $cell)
    $gz = [Math]::Round($z / $cell)
    return "${gx}_${gz}"
}

Write-Host "Fetching https://json.tarkov.dev/regular/maps ..."
$resp = Invoke-RestMethod -Uri "https://json.tarkov.dev/regular/maps"
$out = @{}

foreach ($prop in $resp.data.maps.PSObject.Properties) {
    $map = $prop.Value
    $norm = [string]$map.normalizedName
    if (-not $mapToId.ContainsKey($norm)) { continue }
    $id = $mapToId[$norm]

    $buckets = @{}
    foreach ($spawn in @($map.spawns)) {
        if (-not (Test-PmcSpawn $spawn)) { continue }
        $x = [double]$spawn.position.x
        $y = [double]$spawn.position.y
        $z = [double]$spawn.position.z
        $key = Get-ClusterKey $x $z
        if (-not $buckets.ContainsKey($key)) {
            $buckets[$key] = [System.Collections.Generic.List[object]]::new()
        }
        [void]$buckets[$key].Add([pscustomobject]@{ x = $x; y = $y; z = $z; zone = [string]$spawn.zoneName })
    }

    if ($buckets.Count -eq 0) { continue }

    if (-not $out.ContainsKey($id)) { $out[$id] = [System.Collections.Generic.List[object]]::new() }
    $existing = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in $out[$id]) { [void]$existing.Add("$($m.x)|$($m.z)") }

    $idx = $out[$id].Count + 1
    foreach ($pts in $buckets.Values) {
        $cx = ($pts | Measure-Object -Property x -Average).Average
        $cy = ($pts | Measure-Object -Property y -Average).Average
        $cz = ($pts | Measure-Object -Property z -Average).Average
        $dedupe = "${cx}|${cz}"
        if ($existing.Contains($dedupe)) { continue }
        [void]$existing.Add($dedupe)
        $name = if ($buckets.Count -eq 1) { "PMC Spawn" } else { "PMC Spawn $idx" }
        [void]$out[$id].Add([pscustomobject]@{
            name = $name
            x    = [Math]::Round($cx, 3)
            y    = [Math]::Round($cy, 3)
            z    = [Math]::Round($cz, 3)
        })
        $idx++
    }
}

$dir = Join-Path $PSScriptRoot "..\src\Tarkovy\Assets"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$path = Join-Path $dir "spawns.json"
$out | ConvertTo-Json -Depth 6 | Set-Content $path -Encoding UTF8
Write-Host "Wrote $path ($($out.Keys.Count) maps)"

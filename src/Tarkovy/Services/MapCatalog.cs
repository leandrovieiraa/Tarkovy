using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class MapCatalog
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly Dictionary<string, List<ExtractMarker>> _extracts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<HazardMarker>> _mines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SpawnMarker>> _spawns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<MapPoi>> _pois = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<QuestDefinition>> _quests = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MapDefinition> Maps { get; private set; } = [];

    public MapDefinition? FindById(string? id) =>
        Maps.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public MapDefinition? ResolveFromLog(string token)
    {
        token = token.Trim();
        foreach (var map in Maps)
        {
            if (map.LogIds.Any(id => string.Equals(id, token, StringComparison.OrdinalIgnoreCase)))
                return map;
            if (map.SceneTokens.Any(s => token.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return map;
        }

        return null;
    }

    public event Action? MarkersUpdated;

    public IReadOnlyList<ExtractMarker> ExtractsFor(string mapId) =>
        _extracts.TryGetValue(mapId, out var list) ? list : [];

    public IReadOnlyList<HazardMarker> MinesFor(string mapId) =>
        _mines.TryGetValue(mapId, out var list) ? list : [];

    public IReadOnlyList<SpawnMarker> SpawnsFor(string mapId) =>
        _spawns.TryGetValue(mapId, out var list) ? list : [];

    public IReadOnlyList<MapPoi> PoisFor(string mapId) =>
        _pois.TryGetValue(mapId, out var list) ? list : [];

    public IReadOnlyList<QuestDefinition> QuestsFor(string mapId) =>
        _quests.TryGetValue(mapId, out var list) ? list : [];

    public void LoadBundled(string assetsDir)
    {
        var path = Path.Combine(assetsDir, "maps.json");
        if (!File.Exists(path))
        {
            Maps = [];
            return;
        }

        Maps = JsonSerializer.Deserialize<List<MapDefinition>>(File.ReadAllText(path), JsonOptions()) ?? [];

        var extractsPath = Path.Combine(assetsDir, "extracts.json");
        if (File.Exists(extractsPath))
            MergeExtractsFile(File.ReadAllText(extractsPath));

        var minesPath = Path.Combine(assetsDir, "mines.json");
        if (File.Exists(minesPath))
            MergeMinesFile(File.ReadAllText(minesPath));

        var spawnsPath = Path.Combine(assetsDir, "spawns.json");
        if (File.Exists(spawnsPath))
            MergeSpawnsFile(File.ReadAllText(spawnsPath));

        var questsPath = Path.Combine(assetsDir, "quests.json");
        if (File.Exists(questsPath))
            MergeQuestsFile(File.ReadAllText(questsPath));
    }

    public async Task RefreshMarkersAsync(CancellationToken ct = default)
    {
        var withHazards = """{"query":"{ maps { normalizedName extracts { name faction position { x y z } } hazards { name hazardType position { x y z } outline { x y z } } } }"}""";
        var extractsOnly = """{"query":"{ maps { normalizedName extracts { name faction position { x y z } } } }"}""";
        if (!await TryFetchAsync(withHazards, ct).ConfigureAwait(false))
            await TryFetchAsync(extractsOnly, ct).ConfigureAwait(false);
        await RefreshSpawnsAsync(ct).ConfigureAwait(false);
    }

    public async Task RefreshSpawnsAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync("https://json.tarkov.dev/regular/maps", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            MergeSpawnsFromTarkovJson(json);
        }
        catch
        {
            /* keep bundled spawns.json */
        }
    }

    private async Task<bool> TryFetchAsync(string body, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync("https://api.tarkov.dev/graphql", content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (json.Contains("\"errors\"", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("\"data\"", StringComparison.OrdinalIgnoreCase))
                return false;
            MergeFromGraphql(json);
            var cache = Path.Combine(SettingsStore.AppDataDir, "markers.json");
            Directory.CreateDirectory(SettingsStore.AppDataDir);
            await File.WriteAllTextAsync(cache, json, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            var cache = Path.Combine(SettingsStore.AppDataDir, "markers.json");
            if (File.Exists(cache))
            {
                MergeFromGraphql(await File.ReadAllTextAsync(cache, ct).ConfigureAwait(false));
                return true;
            }

            return false;
        }
    }

    private void MergeExtractsFile(string json)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, List<ExtractMarker>>>(json, JsonOptions());
            if (dict == null) return;
            foreach (var kv in dict)
                _extracts[kv.Key] = kv.Value;
        }
        catch { /* ignore */ }
    }

    private void MergeMinesFile(string json)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, List<HazardMarker>>>(json, JsonOptions());
            if (dict == null) return;
            foreach (var kv in dict)
                _mines[kv.Key] = kv.Value;
        }
        catch { /* ignore */ }
    }

    private void MergeSpawnsFile(string json)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, List<SpawnMarker>>>(json, JsonOptions());
            if (dict == null) return;
            foreach (var kv in dict)
                _spawns[kv.Key] = kv.Value;
        }
        catch { /* ignore */ }
    }

    private void MergeSpawnsFromTarkovJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            if (!data.TryGetProperty("maps", out var mapsEl)) return;

            var byNorm = Maps.ToDictionary(m => m.Id, m => m, StringComparer.OrdinalIgnoreCase);
            var normToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customs"] = "customs",
                ["factory"] = "factory",
                ["night-factory"] = "factory",
                ["woods"] = "woods",
                ["shoreline"] = "shoreline",
                ["interchange"] = "interchange",
                ["reserve"] = "reserve",
                ["lighthouse"] = "lighthouse",
                ["streets-of-tarkov"] = "streets-of-tarkov",
                ["ground-zero"] = "ground-zero",
                ["ground-zero-21"] = "ground-zero",
                ["the-lab"] = "the-lab",
                ["the-lab-dark"] = "the-lab",
                ["terminal"] = "terminal",
                ["the-labyrinth"] = "the-labyrinth"
            };

            var collected = new Dictionary<string, List<(double x, double y, double z)>>(StringComparer.OrdinalIgnoreCase);
            var idToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapProp in mapsEl.EnumerateObject())
            {
                var map = mapProp.Value;
                if (!map.TryGetProperty("normalizedName", out var nEl)) continue;
                var n = nEl.GetString();
                if (!string.IsNullOrWhiteSpace(n))
                    idToName[mapProp.Name] = n;
                if (map.TryGetProperty("id", out var idEl))
                {
                    var tid = idEl.GetString();
                    if (!string.IsNullOrWhiteSpace(tid) && !string.IsNullOrWhiteSpace(n))
                        idToName[tid] = n;
                }
            }

            foreach (var mapProp in mapsEl.EnumerateObject())
            {
                var map = mapProp.Value;
                if (!map.TryGetProperty("normalizedName", out var normEl)) continue;
                var norm = normEl.GetString();
                if (string.IsNullOrWhiteSpace(norm) || !normToId.TryGetValue(norm, out var mapId)) continue;
                if (!byNorm.ContainsKey(mapId)) continue;

                if (map.TryGetProperty("spawns", out var spawnsEl) && spawnsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var spawn in spawnsEl.EnumerateArray())
                    {
                        if (!IsPmcPlayerSpawn(spawn)) continue;
                        if (!TryPos(spawn, "position", out var x, out var y, out var z)) continue;
                        if (!collected.TryGetValue(mapId, out var list))
                        {
                            list = [];
                            collected[mapId] = list;
                        }
                        list.Add((x, y, z));
                    }
                }

                var pois = CollectPoisFromMap(map, idToName);
                if (pois.Count > 0)
                    _pois[mapId] = pois;
            }

            foreach (var (mapId, points) in collected)
            {
                var clustered = ClusterSpawnPoints(points);
                if (clustered.Count > 0)
                    _spawns[mapId] = clustered;
            }

            MarkersUpdated?.Invoke();
        }
        catch { /* ignore */ }
    }

    private static bool IsPmcPlayerSpawn(JsonElement spawn)
    {
        if (!spawn.TryGetProperty("categories", out var cats) || cats.ValueKind != JsonValueKind.Array)
            return false;
        var hasPlayer = false;
        foreach (var c in cats.EnumerateArray())
        {
            if (string.Equals(c.GetString(), "player", StringComparison.OrdinalIgnoreCase))
                hasPlayer = true;
        }
        if (!hasPlayer) return false;

        if (!spawn.TryGetProperty("sides", out var sides) || sides.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var s in sides.EnumerateArray())
        {
            var side = s.GetString()?.ToLowerInvariant();
            if (side is "pmc" or "all") return true;
        }
        return false;
    }

    private static List<SpawnMarker> ClusterSpawnPoints(List<(double x, double y, double z)> points, double cell = 55)
    {
        var buckets = new Dictionary<(long, long), List<(double x, double y, double z)>>();
        foreach (var (x, y, z) in points)
        {
            var key = ((long)Math.Round(x / cell), (long)Math.Round(z / cell));
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add((x, y, z));
        }

        var result = new List<SpawnMarker>();
        var idx = 1;
        foreach (var pts in buckets.Values)
        {
            var cx = pts.Average(p => p.x);
            var cy = pts.Average(p => p.y);
            var cz = pts.Average(p => p.z);
            result.Add(new SpawnMarker
            {
                Name = buckets.Count == 1 ? "PMC Spawn" : $"PMC Spawn {idx}",
                X = cx,
                Y = cy,
                Z = cz
            });
            idx++;
        }
        return result;
    }

    private List<MapPoi> CollectPoisFromMap(JsonElement map, Dictionary<string, string> idToName)
    {
        var loot = new List<MapPoi>();
        var scavs = new List<MapPoi>();
        var loose = new List<MapPoi>();
        var rest = new List<MapPoi>();

        var bosses = ReadBossIndex(map);

        if (map.TryGetProperty("spawns", out var spawnsEl) && spawnsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var spawn in spawnsEl.EnumerateArray())
            {
                if (!TryPos(spawn, "position", out var x, out var y, out var z)) continue;
                var cats = ReadStringArray(spawn, "categories");
                var sides = ReadStringArray(spawn, "sides");
                var zone = Str(spawn, "zoneName");

                if (cats.Contains("boss"))
                {
                    var matched = bosses.Where(b =>
                        b.Keys.Contains(zone, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (matched.Count == 0 && cats.Contains("bot") && sides.Contains("scav"))
                    {
                        scavs.Add(PoiCatalog.Create("scav", "Scav", x, y, z));
                        continue;
                    }
                    if (matched.Count == 0) continue;

                    var type = "boss";
                    if (matched.All(b => b.Type == "cultist")) type = "cultist";
                    else if (matched.All(b => b.Type == "black-division")) type = "black-division";
                    else if (matched.All(b => b.Type == "rogue")) type = "rogue";
                    else if (matched.All(b => b.Type == "scav-sniper")) type = "scav-sniper";

                    var names = matched.Select(b =>
                        b.Chance > 0 ? $"{b.Name} {Math.Round(b.Chance * 100)}%" : b.Name);
                    rest.Add(PoiCatalog.Create(type, string.Join(", ", names.Distinct()), x, y, z,
                        string.Join(" · ", matched.Select(b => b.Name).Distinct())));
                    continue;
                }

                if (cats.Contains("player")) continue;

                if (cats.Contains("sniper"))
                {
                    rest.Add(PoiCatalog.Create("scav-sniper", "Scav sniper", x, y, z));
                    continue;
                }

                if (sides.Contains("scav") && (cats.Contains("bot") || cats.Contains("all")))
                    scavs.Add(PoiCatalog.Create("scav", "Scav", x, y, z));
            }
        }

        if (map.TryGetProperty("lootContainers", out var lootEl) && lootEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in lootEl.EnumerateArray())
            {
                if (!TryPos(c, "position", out var x, out var y, out var z)) continue;
                var id = ContainerId(c);
                var type = PoiCatalog.ContainerType(id);
                if (type == null) continue;
                var def = PoiCatalog.Find(type);
                loot.Add(PoiCatalog.Create(type, def?.Name ?? type, x, y, z));
            }
        }

        if (map.TryGetProperty("lootLoose", out var looseEl) && looseEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in looseEl.EnumerateArray())
            {
                if (!TryPos(c, "position", out var x, out var y, out var z)) continue;
                loose.Add(PoiCatalog.Create("loose-loot", "Loose loot", x, y, z));
            }
        }

        if (map.TryGetProperty("locks", out var locksEl) && locksEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var lk in locksEl.EnumerateArray())
            {
                if (!TryPos(lk, "position", out var x, out var y, out var z)) continue;
                var keyId = KeyId(lk);
                var keyName = App.Items.FindById(keyId)?.Name;
                rest.Add(PoiCatalog.Create("locked-door",
                    string.IsNullOrWhiteSpace(keyName) ? "Locked door" : keyName,
                    x, y, z, keyId));
            }
        }

        if (map.TryGetProperty("transits", out var trEl) && trEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tr in trEl.EnumerateArray())
            {
                if (!TryPos(tr, "position", out var x, out var y, out var z) &&
                    !TryPos(tr, null, out x, out y, out z))
                    continue;
                var destId = TransitMapId(tr);
                var dest = "Transit";
                if (!string.IsNullOrWhiteSpace(destId) && idToName.TryGetValue(destId, out var destNorm))
                {
                    var local = Maps.FirstOrDefault(m =>
                        string.Equals(m.Id, destNorm, StringComparison.OrdinalIgnoreCase));
                    dest = local?.Name ?? destNorm;
                }
                rest.Add(PoiCatalog.Create("transit", dest, x, y, z));
            }
        }

        if (map.TryGetProperty("btrStops", out var btrEl) && btrEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var stop in btrEl.EnumerateArray())
            {
                if (!TryNamedPos(stop, out var x, out var y, out var z)) continue;
                rest.Add(PoiCatalog.Create("btr", "BTR Stop", x, y, z));
            }
        }

        if (map.TryGetProperty("switches", out var swEl) && swEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var sw in swEl.EnumerateArray())
            {
                if (!TryPos(sw, "position", out var x, out var y, out var z)) continue;
                rest.Add(PoiCatalog.Create("switch", "Switch", x, y, z));
            }
        }

        if (map.TryGetProperty("stationaryWeapons", out var stEl) && stEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var st in stEl.EnumerateArray())
            {
                if (!TryPos(st, "position", out var x, out var y, out var z)) continue;
                rest.Add(PoiCatalog.Create("emplacement", "Emplacement", x, y, z));
            }
        }

        var result = new List<MapPoi>(loot.Count + rest.Count + 32);
        result.AddRange(PoiCatalog.Cluster(loot, 48));
        result.AddRange(rest);
        result.AddRange(PoiCatalog.Cluster(scavs, 55));
        result.AddRange(PoiCatalog.Cluster(loose, 48));
        return result;
    }

    private static List<BossRef> ReadBossIndex(JsonElement map)
    {
        var list = new List<BossRef>();
        if (!map.TryGetProperty("bosses", out var bosses) || bosses.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var b in bosses.EnumerateArray())
        {
            var mob = Str(b, "mob");
            if (string.IsNullOrWhiteSpace(mob) && b.TryGetProperty("boss", out var bossEl))
                mob = Str(bossEl, "normalizedName");
            var (type, name) = PoiCatalog.ResolveMob(mob);
            if (b.TryGetProperty("boss", out var bossObj))
            {
                var n = Str(bossObj, "name");
                if (!string.IsNullOrWhiteSpace(n)) name = n;
            }
            var chance = b.TryGetProperty("spawnChance", out var ch) && ch.TryGetDouble(out var cv) ? cv : 0;
            var keys = new List<string>();
            if (b.TryGetProperty("spawnLocations", out var locs) && locs.ValueKind == JsonValueKind.Array)
            {
                foreach (var loc in locs.EnumerateArray())
                {
                    var key = Str(loc, "spawnKey");
                    if (string.IsNullOrWhiteSpace(key)) key = Str(loc, "name");
                    if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
                }
            }
            list.Add(new BossRef(type, name, chance, keys));
        }
        return list;
    }

    private readonly record struct BossRef(string Type, string Name, double Chance, List<string> Keys);

    private static HashSet<string> ReadStringArray(JsonElement el, string name)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return set;
        foreach (var v in arr.EnumerateArray())
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
        }
        return set;
    }

    private static string ContainerId(JsonElement el)
    {
        if (!el.TryGetProperty("lootContainer", out var c)) return "";
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind == JsonValueKind.Object) return Str(c, "id");
        return "";
    }

    private static string KeyId(JsonElement el)
    {
        if (!el.TryGetProperty("key", out var k)) return "";
        if (k.ValueKind == JsonValueKind.String) return k.GetString() ?? "";
        if (k.ValueKind == JsonValueKind.Object) return Str(k, "id");
        return "";
    }

    private static string TransitMapId(JsonElement el)
    {
        if (!el.TryGetProperty("map", out var m)) return "";
        if (m.ValueKind == JsonValueKind.String) return m.GetString() ?? "";
        if (m.ValueKind == JsonValueKind.Object)
            return Str(m, "id").Length > 0 ? Str(m, "id") : Str(m, "normalizedName");
        return "";
    }

    private static bool TryNamedPos(JsonElement el, out double x, out double y, out double z)
    {
        if (TryPos(el, out x, out y, out z)) return true;
        x = ReadNum(el, "x");
        y = ReadNum(el, "y");
        z = ReadNum(el, "z");
        return el.TryGetProperty("x", out _);
    }

    private void MergeQuestsFile(string json)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, List<QuestDefinition>>>(json, JsonOptions());
            if (dict == null) return;
            foreach (var kv in dict)
                _quests[kv.Key] = kv.Value;
        }
        catch { /* ignore */ }
    }

    private void MergeFromGraphql(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            if (!data.TryGetProperty("maps", out var maps)) return;
            foreach (var map in maps.EnumerateArray())
            {
                var id = map.GetProperty("normalizedName").GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;

                var extracts = new List<ExtractMarker>();
                if (map.TryGetProperty("extracts", out var exEl) && exEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ex in exEl.EnumerateArray())
                    {
                        if (!TryPos(ex, out var x, out var y, out var z) &&
                            !TryPos(ex, "position", out x, out y, out z))
                            continue;
                        extracts.Add(new ExtractMarker
                        {
                            Name = Str(ex, "name"),
                            Faction = Str(ex, "faction", "any"),
                            X = x, Y = y, Z = z
                        });
                    }
                }

                var mines = new List<HazardMarker>();
                if (map.TryGetProperty("hazards", out var hzEl) && hzEl.ValueKind == JsonValueKind.Array)
                {
                    var localMap = Maps.FirstOrDefault(m =>
                        string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                    foreach (var hz in hzEl.EnumerateArray())
                    {
                        var type = (Str(hz, "hazardType") + " " + Str(hz, "name")).ToLowerInvariant();
                        if (!IsMineHazard(type))
                            continue;
                        var label = string.IsNullOrWhiteSpace(Str(hz, "name")) ? Loc.T("Hazard.MineFallback") : Str(hz, "name");
                        foreach (var (x, y, z) in ReadMinePoints(hz))
                        {
                            if (localMap != null && !IsInsideMapBounds(localMap, x, z))
                                continue;
                            mines.Add(new HazardMarker
                            {
                                Name = label,
                                Kind = "mine",
                                X = x, Y = y, Z = z
                            });
                        }
                    }
                }

                if (extracts.Count > 0)
                    _extracts[id] = extracts;
                // API vazia não apaga fallback local; lista com pontos substitui.
                if (mines.Count > 0)
                    _mines[id] = mines;
                else if (!_mines.ContainsKey(id))
                    _mines[id] = mines;
                var local = Maps.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                if (local != null)
                {
                    if (extracts.Count > 0)
                        _extracts[local.Id] = extracts;
                    if (mines.Count > 0)
                        _mines[local.Id] = mines;
                    else if (!_mines.ContainsKey(local.Id))
                        _mines[local.Id] = mines;
                }
            }

            MarkersUpdated?.Invoke();
        }
        catch { /* ignore */ }
    }

    private static bool IsMineHazard(string type) =>
        type.Contains("mine", StringComparison.Ordinal) ||
        type.Contains("mina", StringComparison.Ordinal) ||
        type.Contains("minefield", StringComparison.Ordinal);

    /// <summary>
    /// Preferência: position; senão amostra pontos do outline (campo de minas).
    /// Evita centroid de polígonos de borda, que cai fora do terreno.
    /// </summary>
    private static IEnumerable<(double x, double y, double z)> ReadMinePoints(JsonElement el)
    {
        if (TryPos(el, "position", out var x, out var y, out var z))
        {
            yield return (x, y, z);
            yield break;
        }

        var outline = ReadOutline(el);
        if (outline.Count == 0)
            yield break;

        if (outline.Count == 1)
        {
            yield return outline[0];
            yield break;
        }

        // Amostra ao longo do perímetro (máx. 8), em vez de um único centroide.
        const int maxSamples = 8;
        var step = Math.Max(1, outline.Count / maxSamples);
        for (var i = 0; i < outline.Count; i += step)
            yield return outline[i];
    }

    private static List<(double x, double y, double z)> ReadOutline(JsonElement el)
    {
        var pts = new List<(double x, double y, double z)>();
        foreach (var key in new[] { "outline", "coordinates" })
        {
            if (!el.TryGetProperty(key, out var coords) || coords.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var c in coords.EnumerateArray())
            {
                if (TryPos(c, out var x, out var y, out var z))
                    pts.Add((x, y, z));
            }

            if (pts.Count > 0) break;
        }

        return pts;
    }

    private static bool IsInsideMapBounds(MapDefinition map, double x, double z)
    {
        var b = map.SvgBounds is { Length: >= 2 } ? map.SvgBounds : map.Bounds;
        if (b is not { Length: >= 2 } || b[0].Length < 2 || b[1].Length < 2)
            return true;

        // Mesma projeção do Sayser / map.js (rotate; com transform se houver).
        var rot = map.CoordinateRotation * Math.PI / 180.0;
        var cos = Math.Cos(rot);
        var sin = Math.Sin(rot);
        (double lng, double lat) Rotate(double gx, double gz) =>
            (gx * cos - gz * sin, gx * sin + gz * cos);

        var corners = new (double x, double z)[]
        {
            (b[0][0], b[0][1]),
            (b[1][0], b[0][1]),
            (b[0][0], b[1][1]),
            (b[1][0], b[1][1])
        };

        double px, py, minX, maxX, minY, maxY;
        if (map.Transform is { Length: >= 4 } t)
        {
            var scaleX = t[0];
            var scaleY = t[2] * -1;
            var marginX = t[1];
            var marginY = t[3];
            (double px, double py) ToPx(double gx, double gz)
            {
                var (lng, lat) = Rotate(gx, gz);
                return (scaleX * lng + marginX, scaleY * lat + marginY);
            }

            (px, py) = ToPx(x, z);
            minX = double.PositiveInfinity;
            maxX = double.NegativeInfinity;
            minY = double.PositiveInfinity;
            maxY = double.NegativeInfinity;
            foreach (var (cx, cz) in corners)
            {
                var (rx, ry) = ToPx(cx, cz);
                minX = Math.Min(minX, rx);
                maxX = Math.Max(maxX, rx);
                minY = Math.Min(minY, ry);
                maxY = Math.Max(maxY, ry);
            }
        }
        else
        {
            (px, py) = Rotate(x, z);
            minX = double.PositiveInfinity;
            maxX = double.NegativeInfinity;
            minY = double.PositiveInfinity;
            maxY = double.NegativeInfinity;
            foreach (var (cx, cz) in corners)
            {
                var (rx, ry) = Rotate(cx, cz);
                minX = Math.Min(minX, rx);
                maxX = Math.Max(maxX, rx);
                minY = Math.Min(minY, ry);
                maxY = Math.Max(maxY, ry);
            }
        }

        var padX = (maxX - minX) * 0.02;
        var padY = (maxY - minY) * 0.02;
        return px >= minX + padX && px <= maxX - padX &&
               py >= minY + padY && py <= maxY - padY;
    }

    private static bool TryPos(JsonElement el, out double x, out double y, out double z) =>
        TryPos(el, null, out x, out y, out z);

    private static bool TryPos(JsonElement el, string? child, out double x, out double y, out double z)
    {
        x = y = z = 0;
        var obj = el;
        if (child != null)
        {
            if (!el.TryGetProperty(child, out obj) || obj.ValueKind != JsonValueKind.Object)
                return false;
        }

        if (obj.ValueKind != JsonValueKind.Object) return false;
        if (!obj.TryGetProperty("x", out _)) return false;
        x = ReadNum(obj, "x");
        y = ReadNum(obj, "y");
        z = ReadNum(obj, "z");
        return true;
    }

    private static string Str(JsonElement el, string name, string fallback = "") =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static double ReadNum(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.TryGetDouble(out var v) ? v : 0;

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

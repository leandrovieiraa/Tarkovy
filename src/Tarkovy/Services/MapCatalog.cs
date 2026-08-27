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

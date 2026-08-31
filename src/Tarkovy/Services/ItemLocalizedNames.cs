using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tarkovy.Models;

namespace Tarkovy.Services;

/// <summary>Localized item names from json.tarkov.dev — PT overlay when UI/game is Portuguese.</summary>
internal static class ItemLocalizedNames
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly object Gate = new();
    private static Dictionary<string, LocalizedItemNames> _byId = new(StringComparer.OrdinalIgnoreCase);
    private static string _loadedLang = Loc.English;
    private static string? _lastError;

    private static readonly Regex ItemIdRegex = new(@"^[0-9a-f]{24}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private sealed record LocalizedItemNames(string Name, string ShortName);

    /// <summary>True when scan can use the active language (EN = catalog; PT = overlay loaded).</summary>
    public static bool IsReady
    {
        get
        {
            lock (Gate)
                return !NeedsOverlay(Loc.Code) || _byId.Count > 0;
        }
    }

    public static string ActiveLanguage
    {
        get { lock (Gate) return _loadedLang; }
    }

    public static string? LastError
    {
        get { lock (Gate) return _lastError; }
    }

    public static void EnsureLoading() => _ = LoadAsync();

    public static Task ReloadAsync(CancellationToken ct = default)
    {
        lock (Gate) _loadedLang = "";
        return LoadAsync(ct);
    }

    public static async Task LoadAsync(CancellationToken ct = default)
    {
        var lang = Loc.Code;
        lock (Gate)
        {
            if (_loadedLang == lang && (!NeedsOverlay(lang) || _byId.Count > 0))
                return;
        }

        if (!NeedsOverlay(lang))
        {
            lock (Gate)
            {
                _byId = new Dictionary<string, LocalizedItemNames>(StringComparer.OrdinalIgnoreCase);
                _loadedLang = lang;
                _lastError = null;
            }
            return;
        }

        var cachePath = CachePathFor(lang);
        if (File.Exists(cachePath))
        {
            try
            {
                var parsed = ParseAny(await File.ReadAllTextAsync(cachePath, ct).ConfigureAwait(false));
                if (parsed.Count > 0)
                {
                    lock (Gate)
                    {
                        _byId = parsed;
                        _loadedLang = lang;
                        _lastError = null;
                    }
                    _ = RefreshAsync(lang, cachePath, ct);
                    return;
                }
            }
            catch { /* fetch below */ }
        }

        await RefreshAsync(lang, cachePath, ct).ConfigureAwait(false);
    }

    private static bool NeedsOverlay(string lang) =>
        Loc.Normalize(lang) == Loc.Portuguese;

    private static string JsonUrlFor(string lang) =>
        $"https://json.tarkov.dev/regular/items_{Loc.Normalize(lang)}";

    private static async Task RefreshAsync(string lang, string cachePath, CancellationToken ct)
    {
        Exception? lastEx = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var resp = await Http.GetAsync(JsonUrlFor(lang), ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    lastEx = new HttpRequestException($"HTTP {(int)resp.StatusCode}");
                    continue;
                }

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = ParseJsonTarkovTranslations(json);
                if (parsed.Count == 0) continue;

                Directory.CreateDirectory(SettingsStore.AppDataDir);
                await File.WriteAllTextAsync(cachePath, json, ct).ConfigureAwait(false);
                lock (Gate)
                {
                    _byId = parsed;
                    _loadedLang = lang;
                    _lastError = null;
                }
                return;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct).ConfigureAwait(false);
            }
        }

        lock (Gate) _lastError = lastEx?.Message ?? "Falha ao baixar nomes localizados";
    }

    /// <summary>Lightweight GraphQL lookup when local fuzzy match fails.</summary>
    public static async Task<string?> LookupItemIdOnlineAsync(string tooltipText, CancellationToken ct = default)
    {
        var term = ExtractSearchTerm(tooltipText);
        if (term.Length < 3) return null;

        var gqlLang = Loc.IsPortuguese ? "pt" : "en";
        var payload = JsonSerializer.Serialize(new
        {
            query = "query($n:String!,$lang:LanguageCode!){ items(name:$n, lang:$lang, limit:8){ id name shortName } }",
            variables = new { n = term, lang = gqlLang }
        });

        try
        {
            using var resp = await Http.PostAsync(
                "https://api.tarkov.dev/graphql",
                new StringContent(payload, Encoding.UTF8, "application/json"),
                ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return null;

            var q = NormalizeForMatch(tooltipText);
            string? bestId = null;
            var bestScore = double.MaxValue;

            foreach (var el in items.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var shortName = el.TryGetProperty("shortName", out var s) ? s.GetString() ?? "" : "";

                foreach (var label in new[] { name, shortName })
                {
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    var norm = NormalizeForMatch(label);
                    if (q.Length >= 8 && (norm.StartsWith(q, StringComparison.Ordinal) || q.StartsWith(norm, StringComparison.Ordinal)))
                        return id;
                    var score = LevenshteinRatio(q, norm);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestId = id;
                    }
                }
            }

            return bestScore <= 0.35 ? bestId : null;
        }
        catch
        {
            return null;
        }
    }

    private static string CachePathFor(string lang) =>
        Path.Combine(SettingsStore.AppDataDir, $"items-i18n-{Loc.Normalize(lang)}.json");

    private static Dictionary<string, LocalizedItemNames> ParseAny(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Object && !data.TryGetProperty("items", out _))
                return ParseTranslationObject(data);
            if (data.TryGetProperty("items", out var items))
                return ParseGraphQlItemsArray(items);
        }

        if (root.ValueKind == JsonValueKind.Array)
            return ParseGraphQlItemsArray(root);

        return [];
    }

    private static Dictionary<string, LocalizedItemNames> ParseJsonTarkovTranslations(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return [];
        return ParseTranslationObject(data);
    }

    private static Dictionary<string, LocalizedItemNames> ParseTranslationObject(JsonElement data)
    {
        var names = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var shortNames = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in data.EnumerateObject())
        {
            var key = prop.Name;
            if (key.EndsWith(" Name", StringComparison.Ordinal))
            {
                var id = key[..^5].Trim();
                if (IsItemId(id))
                    names[id] = prop.Value.GetString();
            }
            else if (key.EndsWith(" ShortName", StringComparison.Ordinal))
            {
                var id = key[..^10].Trim();
                if (IsItemId(id))
                    shortNames[id] = prop.Value.GetString();
            }
        }

        var map = new Dictionary<string, LocalizedItemNames>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in names.Keys.Union(shortNames.Keys, StringComparer.OrdinalIgnoreCase))
        {
            names.TryGetValue(id, out var name);
            shortNames.TryGetValue(id, out var shortName);
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(shortName)) continue;
            map[id] = new LocalizedItemNames(name ?? "", shortName ?? "");
        }

        return map;
    }

    private static Dictionary<string, LocalizedItemNames> ParseGraphQlItems(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("items", out var items))
            return [];
        return ParseGraphQlItemsArray(items);
    }

    private static Dictionary<string, LocalizedItemNames> ParseGraphQlItemsArray(JsonElement items)
    {
        var map = new Dictionary<string, LocalizedItemNames>(StringComparer.OrdinalIgnoreCase);
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in items.EnumerateArray())
                AddGraphQlItem(map, el);
        }
        else if (items.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in items.EnumerateObject())
                AddGraphQlItem(map, prop.Value);
        }

        return map;
    }

    private static void AddGraphQlItem(Dictionary<string, LocalizedItemNames> map, JsonElement el)
    {
        var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return;
        var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var shortName = el.TryGetProperty("shortName", out var s) ? s.GetString() ?? "" : "";
        if (name.Length == 0 && shortName.Length == 0) return;
        map[id] = new LocalizedItemNames(name, shortName);
    }

    private static bool IsItemId(string id) => ItemIdRegex.IsMatch(id);

    private static string ExtractSearchTerm(string text)
    {
        var line = text.Split('\n', '\r')[0].Trim();
        if (line.Length <= 32) return line;
        var cut = line.LastIndexOf(' ', 32);
        return cut > 8 ? line[..cut] : line[..32];
    }

    private static string NormalizeForMatch(string text) =>
        new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    private static double LevenshteinRatio(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return (double)d[a.Length, b.Length] / Math.Max(a.Length, b.Length);
    }

    public static bool TryGet(string id, out string? name, out string? shortName)
    {
        lock (Gate)
        {
            if (_byId.TryGetValue(id, out var loc))
            {
                name = loc.Name;
                shortName = loc.ShortName;
                return true;
            }
        }

        name = null;
        shortName = null;
        return false;
    }

    /// <summary>Labels for tooltip OCR — PT overlay when UI is PT, else English catalog names.</summary>
    public static IEnumerable<string> LabelsFor(ItemDefinition item)
    {
        if (Loc.IsPortuguese)
        {
            if (TryGet(item.Id, out var ptName, out var ptShort))
            {
                if (!string.IsNullOrWhiteSpace(ptName)) yield return ptName!;
                if (!string.IsNullOrWhiteSpace(ptShort)) yield return ptShort!;
            }

            var enName = ItemDisplayNames.CatalogName(item);
            var enShort = ItemDisplayNames.CatalogShortName(item);
            if (!string.IsNullOrWhiteSpace(enName)) yield return enName;
            if (!string.IsNullOrWhiteSpace(enShort)) yield return enShort;
        }
        else
        {
            var enName = ItemDisplayNames.CatalogName(item);
            var enShort = ItemDisplayNames.CatalogShortName(item);
            if (!string.IsNullOrWhiteSpace(enName)) yield return enName;
            if (!string.IsNullOrWhiteSpace(enShort)) yield return enShort;
        }
    }
}

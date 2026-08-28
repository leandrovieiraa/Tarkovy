using System.IO;
using System.Net.Http;
using System.Text.Json;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class ItemCatalog
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Dictionary<string, ItemDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int W, int H), List<ItemDefinition>> _bySize = new();

    public IReadOnlyList<ItemDefinition> Items { get; private set; } = [];
    public bool IsReady { get; private set; }
    public string? LastError { get; private set; }

    public event Action? ItemsUpdated;

    public ItemDefinition? FindById(string? id) =>
        id != null && _byId.TryGetValue(id, out var item) ? item : null;

    public IReadOnlyList<ItemDefinition> ForSize(int width, int height) =>
        _bySize.TryGetValue((width, height), out var list) ? list : [];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var cache = Path.Combine(SettingsStore.AppDataDir, "items-cache.json");
        if (File.Exists(cache))
        {
            try
            {
                ParseItems(await File.ReadAllTextAsync(cache, ct).ConfigureAwait(false));
                IsReady = Items.Count > 0;
                ItemsUpdated?.Invoke();
            }
            catch { /* refresh below */ }
        }

        _ = RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync("https://json.tarkov.dev/regular/items", ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            ParseItems(json);
            IsReady = Items.Count > 0;
            LastError = null;
            Directory.CreateDirectory(SettingsStore.AppDataDir);
            await File.WriteAllTextAsync(Path.Combine(SettingsStore.AppDataDir, "items-cache.json"), json, ct)
                .ConfigureAwait(false);
            ItemsUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private void ParseItems(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("items", out var itemsEl))
            return;

        var list = new List<ItemDefinition>();
        foreach (var prop in itemsEl.EnumerateObject())
        {
            var el = prop.Value;
            if (!el.TryGetProperty("width", out var wEl) || !el.TryGetProperty("height", out var hEl))
                continue;
            var w = wEl.GetInt32();
            var h = hEl.GetInt32();
            if (w <= 0 || h <= 0 || w > 4 || h > 4) continue;

            var item = new ItemDefinition
            {
                Id = Str(el, "id") ?? prop.Name,
                Name = Str(el, "name") ?? "",
                ShortName = Str(el, "shortName") ?? "",
                NormalizedName = Str(el, "normalizedName") ?? "",
                IconLink = Str(el, "iconLink"),
                GridImageLink = Str(el, "gridImageLink"),
                Link = Str(el, "link"),
                Width = w,
                Height = h,
                BasePrice = el.TryGetProperty("basePrice", out var bp) ? bp.GetInt64() : 0,
                Avg24hPrice = el.TryGetProperty("avg24hPrice", out var avg) && avg.ValueKind == JsonValueKind.Number
                    ? avg.GetInt64() : null,
                Low24hPrice = el.TryGetProperty("low24hPrice", out var lo) && lo.ValueKind == JsonValueKind.Number
                    ? lo.GetInt64() : null,
                High24hPrice = el.TryGetProperty("high24hPrice", out var hi) && hi.ValueKind == JsonValueKind.Number
                    ? hi.GetInt64() : null,
                Types = ParseStringArray(el, "types"),
                SellToTrader = ParseTraderPrices(el, "sellToTrader")
            };
            if (string.IsNullOrWhiteSpace(item.Name)) continue;
            list.Add(item);
        }

        _byId.Clear();
        _bySize.Clear();
        foreach (var item in list)
        {
            _byId[item.Id] = item;
            var key = (item.Width, item.Height);
            if (!_bySize.TryGetValue(key, out var bucket))
            {
                bucket = [];
                _bySize[key] = bucket;
            }
            bucket.Add(item);
        }

        Items = list;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string[] ParseStringArray(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .Where(s => s.Length > 0)
            .ToArray();
    }

    private static ItemTraderPrice[] ParseTraderPrices(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<ItemTraderPrice>();
        foreach (var t in arr.EnumerateArray())
        {
            var price = t.TryGetProperty("price", out var p) ? p.GetInt64() : 0;
            var cur = Str(t, "currency") ?? "RUB";
            list.Add(new ItemTraderPrice
            {
                Trader = Str(t, "name") ?? Str(t, "normalizedName") ?? "?",
                Price = price,
                Currency = cur,
                PriceRub = ToRub(price, cur)
            });
        }
        return list.ToArray();
    }

    private static long ToRub(long price, string currency) => currency.ToUpperInvariant() switch
    {
        "USD" => price * 145,
        "EUR" => price * 158,
        _ => price
    };

    public ItemDefinition? MatchByName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var q = Normalize(text);
        if (q.Length < 2) return null;

        ItemDefinition? best = null;
        var bestScore = double.MaxValue;
        foreach (var item in Items)
        {
            foreach (var candidate in new[] { item.Name, item.ShortName })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var score = LevenshteinRatio(q, Normalize(candidate));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = item;
                }
            }
        }

        return bestScore <= 0.42 ? best : null;
    }

    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static double LevenshteinRatio(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
        }
        return (double)d[a.Length, b.Length] / Math.Max(a.Length, b.Length);
    }
}

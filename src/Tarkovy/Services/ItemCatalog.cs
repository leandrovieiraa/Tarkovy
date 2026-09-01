using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class ItemCatalog
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Dictionary<string, ItemDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int W, int H), List<ItemDefinition>> _bySize = new();
    private HashSet<string> _questItemIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hideoutItemIds = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ItemDefinition> Items { get; private set; } = [];
    public bool IsReady { get; private set; }
    public string? LastError { get; private set; }

    public event Action? ItemsUpdated;

    public ItemDefinition? FindById(string? id) =>
        id != null && _byId.TryGetValue(id, out var item) ? item : null;

    /// <summary>Type-ahead search (EN catalog + PT overlay). Weapons/equips that fail icon scan can be looked up here.</summary>
    public IReadOnlyList<ItemDefinition> Search(string? query, int max = 12)
    {
        var q = NormalizeTooltip(query ?? "");
        if (q.Length < 2 || Items.Count == 0) return [];

        var hits = new List<(ItemDefinition Item, int Score)>(64);
        foreach (var item in Items)
        {
            var score = 0;
            ScoreName(q, ItemDisplayNames.Name(item), ref score);
            ScoreName(q, ItemDisplayNames.ShortName(item), ref score);
            ScoreName(q, ItemDisplayNames.CatalogName(item), ref score);
            ScoreName(q, ItemDisplayNames.CatalogShortName(item), ref score);
            ScoreName(q, item.NormalizedName, ref score);
            if (ItemLocalizedNames.TryGet(item.Id, out var ptName, out var ptShort))
            {
                ScoreName(q, ptName, ref score);
                ScoreName(q, ptShort, ref score);
            }
            if (score > 0)
                hits.Add((item, score));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Item.Ammo != null)
            .ThenBy(h => ItemDisplayNames.Name(h.Item).Length)
            .Take(max)
            .Select(h => h.Item)
            .ToList();
    }

    /// <summary>
    /// Ballistics for a round, or for an ammo pack (boxes have no PEN in the catalog).
    /// </summary>
    public ItemAmmoStats? ResolveAmmo(ItemDefinition? item)
    {
        if (item == null) return null;
        if (item.Ammo != null) return item.Ammo;

        var stem = AmmoPackStem(item.NormalizedName);
        if (stem.Length >= 4)
        {
            ItemAmmoStats? prefixHit = null;
            var prefixHits = 0;
            foreach (var other in Items)
            {
                if (other.Ammo == null) continue;
                var nn = other.NormalizedName ?? "";
                if (nn.Equals(stem, StringComparison.OrdinalIgnoreCase))
                    return other.Ammo;
                if (nn.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                    && (nn.Length == stem.Length || nn[stem.Length] == '-'))
                {
                    prefixHits++;
                    prefixHit = other.Ammo;
                }
            }
            if (prefixHits == 1) return prefixHit;
        }

        var isBox = item.Types.Any(t => t.Equals("ammoBox", StringComparison.OrdinalIgnoreCase));
        if (!isBox) return null;

        var catalogSn = NormalizeShortLabel(ItemDisplayNames.CatalogShortName(item));
        var locSn = NormalizeShortLabel(ItemDisplayNames.ShortName(item));
        if (catalogSn.Length < 2 && locSn.Length < 2) return null;
        if (ItemDisplayNames.IsPlaceholder(item.ShortName, item.Id, "ShortName")
            && ItemDisplayNames.IsPlaceholder(ItemDisplayNames.CatalogShortName(item), item.Id, "ShortName"))
            return null;

        ItemAmmoStats? snHit = null;
        var snHits = 0;
        foreach (var other in Items)
        {
            if (other.Ammo == null) continue;
            var otherSn = NormalizeShortLabel(ItemDisplayNames.ShortName(other));
            var otherCat = NormalizeShortLabel(ItemDisplayNames.CatalogShortName(other));
            if ((catalogSn.Length >= 2 && (otherSn == catalogSn || otherCat == catalogSn))
                || (locSn.Length >= 2 && (otherSn == locSn || otherCat == locSn)))
            {
                snHits++;
                snHit = other.Ammo;
            }
        }

        return snHits == 1 ? snHit : null;
    }

    private static string AmmoPackStem(string? normalized)
    {
        if (string.IsNullOrEmpty(normalized)) return "";
        foreach (var marker in new[] { "-ammo-pack-", "-pack-" })
        {
            var i = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i > 0) return normalized[..i];
        }
        return normalized;
    }

    private static void ScoreName(string q, string? name, ref int best)
    {
        var n = NormalizeTooltip(name ?? "");
        if (n.Length == 0) return;
        int score;
        if (n == q) score = 1000;
        else if (n.StartsWith(q, StringComparison.Ordinal)) score = 800 - Math.Min(n.Length, 200);
        else if (n.Contains(q, StringComparison.Ordinal)) score = 400 - Math.Min(n.Length, 200);
        else return;
        if (score > best) best = score;
    }

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
                (_questItemIds, _hideoutItemIds) = ItemUsageIndex.LoadCache();
                ApplyUsageFlags();
                IsReady = Items.Count > 0;
                ItemsUpdated?.Invoke();
            }
            catch { /* refresh below */ }
        }

        _ = RefreshAsync(ct);
        ItemLocalizedNames.EnsureLoading();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var itemsTask = Http.GetStringAsync("https://json.tarkov.dev/regular/items", ct);
            var tasksTask = Http.GetStringAsync("https://json.tarkov.dev/regular/tasks", ct);
            var hideoutTask = Http.GetStringAsync("https://json.tarkov.dev/regular/hideout", ct);
            var craftsTask = Http.GetStringAsync("https://json.tarkov.dev/regular/crafts", ct);

            string? tasksJson = null, hideoutJson = null, craftsJson = null;
            try { tasksJson = await tasksTask.ConfigureAwait(false); } catch { /* usage optional */ }
            try { hideoutJson = await hideoutTask.ConfigureAwait(false); } catch { /* usage optional */ }
            try { craftsJson = await craftsTask.ConfigureAwait(false); } catch { /* usage optional */ }

            var json = await itemsTask.ConfigureAwait(false);
            ParseItems(json);
            TryApplyUsageJson(tasksJson, hideoutJson, craftsJson);
            IsReady = Items.Count > 0;
            LastError = null;
            Directory.CreateDirectory(SettingsStore.AppDataDir);
            await File.WriteAllTextAsync(Path.Combine(SettingsStore.AppDataDir, "items-cache.json"), json, ct)
                .ConfigureAwait(false);
            if (_questItemIds.Count > 0 || _hideoutItemIds.Count > 0)
                await ItemUsageIndex.SaveCacheAsync(_questItemIds, _hideoutItemIds, ct).ConfigureAwait(false);
            ItemsUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private void TryApplyUsageJson(string? tasksJson, string? hideoutJson, string? craftsJson)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(tasksJson))
                _questItemIds = ItemUsageIndex.ParseQuestItems(tasksJson);
            if (!string.IsNullOrWhiteSpace(hideoutJson) || !string.IsNullOrWhiteSpace(craftsJson))
                _hideoutItemIds = ItemUsageIndex.ParseHideoutItems(hideoutJson ?? "{}", craftsJson);
            ApplyUsageFlags();
        }
        catch
        {
            /* keep previous usage flags */
        }
    }

    private void ApplyUsageFlags()
    {
        foreach (var item in Items)
        {
            item.IsQuestItem = _questItemIds.Contains(item.Id);
            item.IsHideoutItem = _hideoutItemIds.Contains(item.Id);
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

            var rawName = Str(el, "name");
            var rawShort = Str(el, "shortName");
            var id = Str(el, "id") ?? prop.Name;
            var wikiLink = Str(el, "wikiLink");
            var normalized = Str(el, "normalizedName") ?? "";

            var item = new ItemDefinition
            {
                Id = id,
                Name = rawName ?? "",
                ShortName = rawShort ?? "",
                NormalizedName = normalized,
                WikiLink = wikiLink,
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
                SellToTrader = ParseTraderPrices(el, "sellToTrader"),
                Ammo = ParseAmmo(el)
            };
            item.Name = ItemDisplayNames.Name(item);
            item.ShortName = ItemDisplayNames.ShortName(item);
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
        ApplyUsageFlags();
    }

    private static ItemAmmoStats? ParseAmmo(JsonElement el)
    {
        JsonElement props = default;
        var hasProps = el.TryGetProperty("properties", out props) && props.ValueKind == JsonValueKind.Object;
        var type = hasProps ? Str(props, "propertiesType") : null;
        if (string.Equals(type, "ItemPropertiesGrenade", StringComparison.OrdinalIgnoreCase))
            return null;

        var ammoType = hasProps ? Str(props, "ammoType") : Str(el, "ammoType");
        if (ammoType is "grenade" or "flashbang")
            return null;

        var pen = hasProps ? Int(props, "penetrationPower") : null;
        pen ??= Int(el, "penetrationPower");
        if (pen == null) return null;

        var isAmmoType = el.TryGetProperty("types", out var typesEl)
            && typesEl.ValueKind == JsonValueKind.Array
            && typesEl.EnumerateArray().Any(t =>
                t.ValueKind == JsonValueKind.String
                && string.Equals(t.GetString(), "ammo", StringComparison.OrdinalIgnoreCase));
        if (!isAmmoType && type != "ItemPropertiesAmmo")
            return null;

        var dmg = (hasProps ? Int(props, "damage") : null) ?? Int(el, "damage") ?? 0;
        var armor = (hasProps ? Int(props, "armorDamage") : null) ?? Int(el, "armorDamage") ?? 0;
        var frag = (hasProps ? Dbl(props, "fragmentationChance") : null) ?? Dbl(el, "fragmentationChance") ?? 0;
        var count = (hasProps ? Int(props, "projectileCount") : null) ?? Int(el, "projectileCount") ?? 1;
        var caliber = (hasProps ? Str(props, "caliber") : null) ?? Str(el, "caliber") ?? "";

        return new ItemAmmoStats
        {
            Caliber = AmmoBallistics.FormatCaliber(caliber),
            Damage = dmg,
            PenetrationPower = pen.Value,
            ArmorDamage = armor,
            FragmentationChance = frag,
            ProjectileCount = Math.Max(1, count)
        };
    }

    private static int? Int(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return (int)Math.Round(d);
        return null;
    }

    private static double? Dbl(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number)
            return null;
        return p.TryGetDouble(out var d) ? d : null;
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

    public ItemDefinition? MatchByName(string? text) => MatchByTooltip(text).Item;

    /// <summary>Match hover tooltip OCR (PT/EN) — exact name first, then tokens.</summary>
    public (ItemDefinition? Item, double Score) MatchByTooltip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, 1);
        if (text.IndexOfAny(['?', '¿', '\\']) >= 0 && text.Count(char.IsLetter) < 8)
            return (null, 1);
        text = RepairOcrGarbage(text);

        var q = NormalizeTooltip(text);
        if (q.Length < 3) return (null, 1);
        if (q.Count(char.IsLetter) < 3) return (null, 1);

        if (IsGenericTooltipToken(q)) return (null, 1);

        var exact = MatchExactNormalizedLabel(q);
        if (exact != null) return (exact, 0.001);

        var tokenHit = MatchByTooltipTokens(text);
        if (tokenHit.Score < 0) return (null, 1);
        if (tokenHit.Item != null) return tokenHit;

        var prefixHit = UniqueLeadingLetterPrefix(q, LooksLikeAmmoOcr(text));
        if (prefixHit != null) return (prefixHit, 0.028);

        if (q.Length is >= 3 and <= 6)
        {
            var shortHit = MatchTooltipToken(q);
            if (shortHit != null) return (shortHit, 0.04);
        }

        ItemDefinition? best = null;
        var bestScore = double.MaxValue;

        foreach (var item in Items)
        {
            foreach (var label in ItemLocalizedNames.LabelsFor(item))
            {
                if (string.IsNullOrWhiteSpace(label)) continue;
                var n = NormalizeTooltip(label);
                if (n.Length < 3) continue;

                if (q.Length >= 8 && n == q)
                    return (item, 0.02);

                var score = LevenshteinRatio(q, n);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = item;
                }
            }
        }

        var maxDist = q.Length >= 14 ? 0.40 : q.Length >= 10 ? 0.36 : 0.32;
        if (best != null && bestScore <= 0.22 && q.Length >= 10)
            return (PreferShorterSameName(best, q), bestScore);
        if (best != null && !OcrSupportsItem(q, best))
            return (null, bestScore);
        return bestScore <= maxDist ? (PreferShorterSameName(best!, q), bestScore) : (null, bestScore);
    }

    private static string RepairOcrGarbage(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch is '•' or '·') sb.Append('l');
            else if (ch is '*') sb.Append('c');
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Disposable Tala vs Tala Alu share the same PT name — pick the shorter short name unless OCR has the extra.</summary>
    private ItemDefinition PreferShorterSameName(ItemDefinition best, string normalizedOcr)
    {
        var name = NormalizeTooltip(ItemDisplayNames.Name(best));
        if (name.Length < 6) return best;
        ItemDefinition? shortest = best;
        var shortestSn = NormalizeShortLabel(ItemDisplayNames.ShortName(best));
        foreach (var item in Items)
        {
            if (item.Id == best.Id) continue;
            if (NormalizeTooltip(ItemDisplayNames.Name(item)) != name) continue;
            var sn = NormalizeShortLabel(ItemDisplayNames.ShortName(item));
            if (sn.Length < shortestSn.Length)
            {
                shortest = item;
                shortestSn = sn;
            }
        }

        if (shortest.Id == best.Id) return best;
        var extra = NormalizeShortLabel(ItemDisplayNames.ShortName(best));
        if (extra.Length > shortestSn.Length && extra.StartsWith(shortestSn, StringComparison.Ordinal)
            && normalizedOcr.Contains(extra[shortestSn.Length..], StringComparison.Ordinal))
            return best;
        return shortest;
    }

    /// <summary>
    /// Exact catalog name ("9x19mm FMJ M882") beats a shared short name (M882 pack vs loose round).
    /// </summary>
    private ItemDefinition? MatchExactNormalizedLabel(string q)
    {
        ItemDefinition? nameHit = null;
        var nameHits = 0;
        ItemDefinition? shortHit = null;
        var shortHits = 0;
        foreach (var item in Items)
        {
            var name = NormalizeTooltip(ItemDisplayNames.Name(item));
            if (name == q)
            {
                nameHits++;
                nameHit = item;
            }

            var sn = NormalizeTooltip(ItemDisplayNames.ShortName(item));
            if (sn == q)
            {
                shortHits++;
                shortHit = item;
            }
        }

        if (nameHits == 1) return nameHit;
        if (nameHits > 1 && nameHit != null)
            return PreferShorterSameName(nameHit, q);
        if (shortHits == 1) return shortHit;
        return null;
    }

    private static bool OcrSupportsItem(string normalizedOcr, ItemDefinition item)
    {
        if (normalizedOcr.Length < 3) return false;
        if (IsGenericTooltipToken(normalizedOcr)) return false;

        foreach (var sn in ShortLabelsOf(item))
        {
            if (ShortNameAgreesWithOcr(sn, normalizedOcr))
                return true;
        }

        foreach (var label in ItemLocalizedNames.LabelsFor(item))
        {
            if (string.IsNullOrWhiteSpace(label)) continue;
            var n = NormalizeTooltip(label);
            if (n.Length >= 4 && normalizedOcr.Length >= 4
                && (normalizedOcr.Contains(n, StringComparison.Ordinal) || n.Contains(normalizedOcr, StringComparison.Ordinal)))
                return true;

            foreach (var token in TokenizeTooltip(label))
            {
                if (IsGenericTooltipToken(token)) continue;
                var t = NormalizeShortLabel(token);
                if (t.Length >= 3 && normalizedOcr.Contains(t, StringComparison.Ordinal))
                    return true;
                if (t.Length >= 6 && normalizedOcr.Contains(t[..6], StringComparison.Ordinal))
                    return true;
                if (t.Length >= 6)
                {
                    foreach (var ot in TokenizeTooltip(normalizedOcr))
                    {
                        var o = NormalizeShortLabel(ot);
                        if (o.Length >= 6 && LevenshteinRatio(t, o) <= 0.28)
                            return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>"uga"→água, "analgesic"→analgésicos, shared prefix of a short name.</summary>
    private static bool ShortNameAgreesWithOcr(string shortName, string ocr)
    {
        if (shortName.Length < 3 || ocr.Length < 3) return false;
        if (IsGenericTooltipToken(ocr) || IsGenericTooltipToken(shortName)) return false;
        if (shortName == ocr) return true;
        if (ocr.Contains(shortName, StringComparison.Ordinal) || shortName.Contains(ocr, StringComparison.Ordinal))
            return true;
        if (shortName.EndsWith(ocr, StringComparison.Ordinal) && shortName.Length - ocr.Length <= 2)
            return true;
        if (ocr.EndsWith(shortName, StringComparison.Ordinal) && ocr.Length - shortName.Length <= 4)
            return true;

        var n = 0;
        var lim = Math.Min(shortName.Length, ocr.Length);
        while (n < lim && shortName[n] == ocr[n]) n++;
        return n >= 4 && n >= Math.Min(shortName.Length, 6) - 1;
    }

    private static readonly HashSet<string> GenericTooltipTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "kit", "item", "pack", "day", "de", "mochila", "bolso", "bolsos", "arma", "food", "drink",
        "lens", "itemlens", "ffem", "iens", "msanna", "ch", "the", "and"
    };

    /// <summary>Extract short tokens (CMS, IFAK, Água…) from noisy tooltip OCR.</summary>
    private (ItemDefinition? Item, double Score) MatchByTooltipTokens(string text)
    {
        var tokens = TokenizeTooltip(text)
            .Where(t => !IsGenericTooltipToken(t))
            .ToList();
        if (tokens.Count == 0) return (null, 1);

        var q = NormalizeTooltip(text);
        var ammoHint = LooksLikeAmmoOcr(text);

        for (var n = Math.Min(3, tokens.Count); n >= 2; n--)
        {
            for (var i = 0; i <= tokens.Count - n; i++)
            {
                var glued = NormalizeShortLabel(string.Concat(tokens.Skip(i).Take(n)));
                if (glued.Length < 5) continue;
                var exact = AllExactShortLabel(glued);
                if (ammoHint) exact = exact.Where(IsAmmoFamily).ToList();
                var picked = UniqueOrNull(exact) ?? PreferAmmoVariant(exact, text);
                if (picked != null) return (picked, 0.004);
            }
        }

        var covered = RankByTokenCoverage(tokens, text, ammoHint);
        if (covered.Count > 0)
        {
            var top = covered[0];
            var runner = covered.Count > 1 ? covered[1].Hits : 0;
            if (top.Hits >= 2 && top.Hits > runner)
                return (top.Item, 0.006);
            if (top.Hits >= 1 && covered.Count == 1 && (top.ExactShort || tokens.Count == 1))
                return (PreferAmmoVariant([top.Item], text) ?? top.Item, 0.008);
        }

        foreach (var token in tokens)
        {
            if (IsBroadCaliberToken(token)) continue;
            var hit = MatchTooltipToken(token);
            if (hit == null) continue;
            if (ammoHint && !IsAmmoFamily(hit)) continue;
            var tn = NormalizeShortLabel(token);
            if (OcrSupportsItem(q, hit) || ShortLabelsOf(hit).Any(sn => ShortNameAgreesWithOcr(sn, tn)))
                return (hit, 0.01 + (16 - Math.Min(token.Length, 16)) * 0.001);
        }

        foreach (var token in tokens)
        {
            if (NormalizeShortLabel(token).Length < 6) continue;
            var hit = MatchDistinctiveNameToken(token, q);
            if (hit == null) continue;
            if (ammoHint && !IsAmmoFamily(hit)) continue;
            return (hit, 0.03);
        }

        var prefix = UniqueLeadingLetterPrefix(q, ammoHint);
        if (prefix != null) return (prefix, 0.032);

        var ammo = PreferAmmoVariant(MatchAmmoCodes(text, 8, 8), text);
        if (ammo != null) return (ammo, 0.035);

        return (null, 1);
    }

    private static bool LooksLikeAmmoOcr(string text)
    {
        var n = NormalizeTooltip(text);
        if (n.Contains("jsp", StringComparison.Ordinal)
            || n.Contains("fmj", StringComparison.Ordinal)
            || n.Contains("chumbo", StringComparison.Ordinal)
            || n.Contains("grosso", StringComparison.Ordinal)
            || n.Contains("buck", StringComparison.Ordinal)
            || n.Contains("slugs", StringComparison.Ordinal)
            || n.Contains("vmax", StringComparison.Ordinal)
            || n.Contains("m855", StringComparison.Ordinal)
            || n.Contains("m882", StringComparison.Ordinal)
            || n.Contains("m80", StringComparison.Ordinal))
            return true;
        foreach (var token in TokenizeTooltip(text))
        {
            if (IsBroadCaliberToken(token)) return true;
            var t = NormalizeShortLabel(token);
            if (t.Contains('x') && t.Any(char.IsDigit)) return true;
        }
        return false;
    }

    private List<(ItemDefinition Item, int Hits, bool ExactShort)> RankByTokenCoverage(
        List<string> tokens, string text, bool ammoHint)
    {
        var norms = new List<string>();
        foreach (var token in tokens)
        {
            var n = NormalizeShortLabel(token);
            if (n.Length < 3 || IsBroadCaliberToken(n)) continue;
            norms.Add(n);
            foreach (var v in AmmoOcrVariants(token))
            {
                if (v.Length >= 3) norms.Add(v);
            }
        }
        norms = norms.Distinct(StringComparer.Ordinal).ToList();
        if (norms.Count == 0) return [];

        var compact = NormalizeTooltip(text);
        var ranked = new List<(ItemDefinition Item, int Hits, bool ExactShort)>();
        foreach (var item in Items)
        {
            if (ammoHint && !IsAmmoFamily(item)) continue;
            var blob = ItemSearchBlob(item);
            if (blob.Length < 3) continue;
            var hits = 0;
            foreach (var t in norms)
            {
                if (blob.Contains(t, StringComparison.Ordinal)) hits++;
            }
            if (hits == 0) continue;
            var exact = ShortLabelsOf(item).Any(sn => sn.Length >= 3 && compact.Contains(sn, StringComparison.Ordinal));
            ranked.Add((item, hits, exact));
        }

        return ranked
            .OrderByDescending(r => r.Hits)
            .ThenByDescending(r => r.ExactShort)
            .ThenBy(r => NormalizeShortLabel(ItemDisplayNames.ShortName(r.Item)).Length)
            .ToList();
    }

    private static string ItemSearchBlob(ItemDefinition item)
    {
        var sb = new StringBuilder();
        foreach (var sn in ShortLabelsOf(item))
            sb.Append(sn).Append(' ');
        foreach (var label in ItemLocalizedNames.LabelsFor(item))
            sb.Append(NormalizeTooltip(label ?? "")).Append(' ');
        return sb.ToString();
    }

    /// <summary>"Chumb030ss" → leading letters "chumb" uniquely match Chumbo Grosso.</summary>
    private ItemDefinition? UniqueLeadingLetterPrefix(string q, bool ammoHint)
    {
        var i = 0;
        while (i < q.Length && char.IsLetter(q[i])) i++;
        var prefix = q[..i];
        if (prefix.Length < 5) return null;

        var hits = new List<ItemDefinition>();
        foreach (var item in Items)
        {
            if (ammoHint && !IsAmmoFamily(item)) continue;
            var matched = false;
            foreach (var sn in ShortLabelsOf(item))
            {
                if (StartsWithPrefix(sn, prefix))
                {
                    hits.Add(item);
                    matched = true;
                    break;
                }
            }
            if (matched) continue;
            foreach (var label in ItemLocalizedNames.LabelsFor(item))
            {
                var n = NormalizeTooltip(label ?? "");
                if (StartsWithPrefix(n, prefix))
                {
                    hits.Add(item);
                    break;
                }
            }
        }

        hits = hits.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (hits.Count == 1) return hits[0];
        return PreferAmmoVariant(hits, q);
    }

    private static bool StartsWithPrefix(string name, string prefix)
    {
        if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
        if (prefix.Length < 6 || name.Length < prefix.Length) return false;
        return LevenshteinDistance(prefix, name[..prefix.Length]) <= 1;
    }

    /// <summary>"ImobilizaçEa" → unique (or shorter) Tala; two items sharing the full name drop the Alu variant.</summary>
    private ItemDefinition? MatchDistinctiveNameToken(string token, string fullOcr)
    {
        var q = NormalizeShortLabel(token);
        if (q.Length < 6) return null;

        var hits = new List<ItemDefinition>();
        foreach (var item in Items)
        {
            var matched = false;
            foreach (var label in ItemLocalizedNames.LabelsFor(item))
            {
                foreach (var tok in TokenizeTooltip(label))
                {
                    var t = NormalizeShortLabel(tok);
                    if (t.Length < 6) continue;
                    if (t == q || t.StartsWith(q, StringComparison.Ordinal) || q.StartsWith(t, StringComparison.Ordinal)
                        || (t.Length >= 6 && q.Length >= 6 && (t.Contains(q[..6], StringComparison.Ordinal) || q.Contains(t[..6], StringComparison.Ordinal)))
                        || LevenshteinRatio(q, t) <= 0.25)
                    {
                        hits.Add(item);
                        matched = true;
                        break;
                    }
                }
                if (matched) break;
            }
        }

        hits = hits.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (hits.Count == 1) return hits[0];
        if (hits.Count < 2) return null;

        var ordered = hits.OrderBy(i => NormalizeShortLabel(ItemDisplayNames.ShortName(i)).Length).ToList();
        var shortest = ordered[0];
        var shortSn = NormalizeShortLabel(ItemDisplayNames.ShortName(shortest));
        var othersAreExtensions = ordered.Skip(1).All(i =>
        {
            var sn = NormalizeShortLabel(ItemDisplayNames.ShortName(i));
            return sn.StartsWith(shortSn, StringComparison.Ordinal) && sn.Length > shortSn.Length
                   && !fullOcr.Contains(sn[shortSn.Length..], StringComparison.Ordinal);
        });
        return othersAreExtensions ? shortest : null;
    }

    private ItemDefinition? PreferAmmoVariant(List<ItemDefinition> hits, string text)
    {
        if (hits.Count == 0) return null;
        if (hits.Count == 1) return hits[0];
        var loose = hits.Where(i => IsAmmoFamily(i) && !i.Types.Any(t => t.Equals("ammoBox", StringComparison.OrdinalIgnoreCase))).ToList();
        var boxes = hits.Where(i => i.Types.Any(t => t.Equals("ammoBox", StringComparison.OrdinalIgnoreCase))).ToList();
        if (loose.Count == 1 && boxes.Count >= 1)
        {
            if (text.Contains("Pacote", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Pack", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Caixa", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Box", StringComparison.OrdinalIgnoreCase))
                return boxes[0];
            return loose[0];
        }
        return null;
    }

    private ItemDefinition? UniqueSuffixShortName(string q)
    {
        ItemDefinition? hit = null;
        var nHits = 0;
        foreach (var item in Items)
        {
            foreach (var n in ShortLabelsOf(item))
            {
                if (n.Length < 3 || n.Length > 16) continue;
                if (!n.EndsWith(q, StringComparison.Ordinal)) continue;
                if (n.Length - q.Length > 2) continue;
                nHits++;
                hit = item;
                break;
            }
            if (nHits > 1) return null;
        }
        return nHits == 1 ? hit : null;
    }

    private static bool IsGenericTooltipToken(string token) =>
        token.Length < 2 || GenericTooltipTokens.Contains(token);

    private ItemDefinition? MatchTooltipToken(string token)
    {
        var q = NormalizeShortLabel(token);
        if (q.Length < 2 || q.Length > 16) return null;
        if (IsGenericTooltipToken(q) || IsBroadCaliberToken(token)) return null;

        var exact = AllExactShortLabel(q);
        if (exact.Count == 1) return exact[0];
        if (exact.Count > 1) return null;

        ItemDefinition? prefixHit = null;
        foreach (var item in Items)
        {
            foreach (var n in ShortLabelsOf(item))
            {
                if (q.Length >= 4 && n.StartsWith(q, StringComparison.Ordinal) && n.Length > q.Length)
                {
                    if (prefixHit != null && prefixHit.Id != item.Id) return null;
                    prefixHit = item;
                    break;
                }
            }
        }
        if (prefixHit != null) return prefixHit;

        if (q.Length >= 3)
        {
            var suffix = UniqueSuffixShortName(q);
            if (suffix != null) return suffix;
        }

        if (q.Length is >= 3 and <= 6)
            return MatchNearShortLabel(q, 8, 8);

        return null;
    }

    public bool IsBroadShortLabel(ItemDefinition item)
    {
        var sn = NormalizeShortLabel(ItemDisplayNames.ShortName(item));
        return IsBroadCaliberToken(sn);
    }

    private static bool IsBroadCaliberToken(string token)
    {
        var q = NormalizeShortLabel(token);
        if (q.Length is >= 3 and <= 8 && q.EndsWith("mm", StringComparison.Ordinal))
            return true;
        return q is "fmj" or "hp" or "ap" or "sp" or "tracer";
    }

    private static List<string> TokenizeTooltip(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }

        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>Exact in-cell label (CMS, Água…) ignoring slot dimensions.</summary>
    public ItemDefinition? MatchExactShortLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return UniqueOrNull(AllExactShortLabel(text));
    }

    /// <summary>Match OCR text on icon label (CMS, Água…) — unique hits only.</summary>
    public ItemDefinition? MatchByShortName(string? text, int slotW, int slotH)
    {
        var hits = MatchShortNameCandidates(text, slotW, slotH);
        return UniqueOrNull(hits);
    }

    /// <summary>
    /// Ammo in-cell codes (M882, M855, M80, SOST) — hyphenated names like V-Max included.
    /// Returns pack + loose round when they share a short name.
    /// </summary>
    public List<ItemDefinition> MatchAmmoCodes(string? text, int slotW, int slotH)
    {
        var result = new List<ItemDefinition>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in AmmoCodeTokens(text))
        {
            var q = NormalizeShortLabel(raw);
            if (q.Length < 2 || q.Length > 12) continue;
            if (IsBroadCaliberToken(q)) continue;

            var hits = new List<ItemDefinition>();
            foreach (var item in Items)
            {
                if (!IsAmmoFamily(item) || !FitsInside(item, slotW, slotH)) continue;
                foreach (var sn in ShortLabelsOf(item))
                {
                    if (sn == q || (q.Length >= 3 && sn.StartsWith(q, StringComparison.Ordinal) && sn.Length - q.Length <= 1))
                    {
                        hits.Add(item);
                        break;
                    }
                }
            }

            foreach (var item in FilterBySlot(hits, slotW, slotH))
            {
                if (seen.Add(item.Id))
                    result.Add(item);
            }
        }

        return result;
    }

    /// <summary>OCR "Tala" must not also keep "Tala Alu" unless the extra qualifier is in the text.</summary>
    private List<ItemDefinition> DropUnqualifiedLongerNames(List<ItemDefinition> items, string text)
    {
        if (items.Count < 2) return items;
        var compact = NormalizeShortLabel(text);
        var drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in items)
        foreach (var b in items)
        {
            if (a.Id == b.Id) continue;
            var sa = ShortLabelsOf(a).OrderBy(s => s.Length).FirstOrDefault() ?? "";
            var sb = ShortLabelsOf(b).OrderBy(s => s.Length).FirstOrDefault() ?? "";
            if (sa.Length < 3 || !sb.StartsWith(sa, StringComparison.Ordinal) || sb.Length <= sa.Length)
                continue;
            var extra = sb[sa.Length..];
            if (extra.Length < 2) continue;
            if (compact.Contains(extra, StringComparison.Ordinal)
                && (compact.StartsWith(sa, StringComparison.Ordinal) || compact.Contains(sb, StringComparison.Ordinal)))
                drop.Add(a.Id);
            else if (!compact.Contains(extra, StringComparison.Ordinal))
                drop.Add(b.Id);
        }

        var kept = items.Where(i => !drop.Contains(i.Id)).ToList();
        return kept.Count > 0 ? kept : items;
    }

    private static bool IsAmmoFamily(ItemDefinition item) =>
        item.Types.Any(t => t.Equals("ammo", StringComparison.OrdinalIgnoreCase)
                         || t.Equals("ammoBox", StringComparison.OrdinalIgnoreCase));

    private static List<string> AmmoCodeTokens(string text)
    {
        var list = TokenizeTooltip(text);
        var compact = NormalizeShortLabel(text);
        if (compact.Length is >= 2 and <= 12)
            list.Add(compact);

        foreach (var token in list.ToList())
        {
            foreach (var v in AmmoOcrVariants(token))
            {
                if (!list.Contains(v, StringComparer.OrdinalIgnoreCase))
                    list.Add(v);
            }
        }

        return list;
    }

    /// <summary>OCR confusables on ammo codes: Mgg2→M882, M8O→M80.</summary>
    private static IEnumerable<string> AmmoOcrVariants(string raw)
    {
        var q = NormalizeShortLabel(raw);
        if (q.Length is < 2 or > 8) yield break;
        var alt = q
            .Replace("rn", "m", StringComparison.Ordinal)
            .Replace('g', '8')
            .Replace('b', '8')
            .Replace('o', '0')
            .Replace('s', '5')
            .Replace('i', '1')
            .Replace('l', '1')
            .Replace('z', '2');
        if (alt != q) yield return alt;
    }

    /// <summary>True when OCR actually mentions this item (blocks "CMS"→Pólvora).</summary>
    public bool OcrAgreesWithItem(ItemDefinition item, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return OcrSupportsItem(NormalizeTooltip(SanitizeInCellOcr(text)), item);
    }

    private static readonly Regex DurabilityOcr = new(@"\d+\s*/\s*\d+", RegexOptions.Compiled);

    /// <summary>Drop in-cell durability ("2/3", "55/60") so "CMS 2/3" still matches CMS.</summary>
    public static string SanitizeInCellOcr(string text) =>
        DurabilityOcr.Replace(text, " ").Trim();

    /// <summary>
    /// All unique short-name hits in OCR (every token). Used to confirm template match
    /// instead of returning the first fuzzy hit ("Kit cirúrgico CMS" must not become "Kit").
    /// </summary>
    public List<ItemDefinition> MatchShortNameCandidates(string? text, int slotW, int slotH)
    {
        var result = new List<ItemDefinition>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        text = SanitizeInCellOcr(text.Trim());
        if (string.IsNullOrWhiteSpace(text)) return result;
        if (text.Any(c => c is '¿' or '?' or '\\'))
            return result;

        var tokens = TokenizeTooltip(text);
        if (tokens.Count == 0) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(ItemDefinition? item)
        {
            if (item == null || !seen.Add(item.Id)) return;
            result.Add(item);
        }

        foreach (var q in OcrQueryVariants(tokens, glueCompact: !text.Contains('/')))
        {
            if (q.Length < 2 || q.Length > 16) continue;
            if (IsGenericTooltipToken(q) || IsBroadCaliberToken(q)) continue;

            var exact = FilterBySlot(AllExactShortLabel(q), slotW, slotH);
            if (exact.Count == 1)
            {
                Add(exact[0]);
                var compact = NormalizeShortLabel(text);
                foreach (var longer in AllPrefixShortLabel(q, slotW, slotH))
                {
                    var sn = ShortLabelsOf(longer).OrderBy(s => s.Length).FirstOrDefault() ?? "";
                    if (sn.Length > q.Length && compact.Contains(sn[q.Length..], StringComparison.Ordinal))
                        Add(longer);
                }
                continue;
            }

            if (exact.Count > 1)
            {
                var inside = exact.Where(i => FitsInside(i, slotW, slotH)).ToList();
                if (inside.Count == 1) Add(inside[0]);
                else if (inside.Count > 1)
                {
                    var native = inside.Where(i => FitsSlot(i, slotW, slotH)).ToList();
                    if (native.Count == 1) Add(native[0]);
                    else
                        foreach (var item in native.Count > 0 ? native : inside)
                            Add(item);
                }
                continue;
            }

            if (q.Length >= 4)
                Add(UniqueOrNull(AllPrefixShortLabel(q, slotW, slotH)));

            // "uga"/"gua" → Água via MatchNear; suffix needs 4+ so "alu" ≠ Tala Alu.
            if (q.Length >= 4)
                Add(UniqueOrNull(AllSuffixShortLabel(q, slotW, slotH)));

            if (q.Length is >= 3 and <= 6)
                Add(MatchNearShortLabel(q, slotW, slotH));

            if (q.Length >= 6)
                Add(UniqueOrNull(AllPrefixFullName(q, slotW, slotH)));
        }

        return DropUnqualifiedLongerNames(result, text);
    }

    /// <summary>
    /// Debug OCR: "Ã ala" → aala/tala, "gua" → gua, "gésic" → gesic.
    /// Also compares ShortName with I↔T (Bender font).
    /// </summary>
    private static List<string> OcrQueryVariants(List<string> tokens, bool glueCompact = true)
    {
        var variants = new HashSet<string>(StringComparer.Ordinal);
        if (glueCompact)
        {
            var compact = NormalizeShortLabel(string.Concat(tokens));
            if (compact.Length >= 2)
                variants.Add(compact);

            var lettersOnly = NormalizeShortLabel(string.Concat(tokens.Where(t => t.Length >= 2)));
            if (lettersOnly.Length >= 2)
                variants.Add(lettersOnly);
        }

        foreach (var token in tokens)
        {
            var n = NormalizeShortLabel(token);
            if (n.Length >= 2)
                variants.Add(n);
        }

        // Leading A from "Ã ala" (Tala) — Ã folds to A.
        foreach (var q in variants.ToList())
        {
            if (q.Length is >= 3 and <= 5 && q[0] == 'a')
                variants.Add('t' + q[1..]);
            if (q.Length is >= 3 and <= 8)
            {
                variants.Add(q.Replace('i', 't'));
                variants.Add(q.Replace('t', 'i'));
            }
        }

        return variants.OrderByDescending(v => v.Length).ToList();
    }

    private List<ItemDefinition> AllSuffixShortLabel(string q, int slotW, int slotH)
    {
        var list = new List<ItemDefinition>();
        foreach (var item in Items)
        {
            if (!FitsInside(item, slotW, slotH)) continue;
            foreach (var sn in ShortLabelsOf(item))
            {
                if (sn.Length > q.Length && sn.EndsWith(q, StringComparison.Ordinal))
                {
                    list.Add(item);
                    break;
                }
            }
        }
        return list;
    }

    private List<ItemDefinition> AllPrefixFullName(string q, int slotW, int slotH)
    {
        var list = new List<ItemDefinition>();
        foreach (var item in Items)
        {
            if (!FitsInside(item, slotW, slotH)) continue;
            foreach (var label in ItemLocalizedNames.LabelsFor(item))
            {
                var n = NormalizeShortLabel(label);
                if (n.Length >= q.Length && n.StartsWith(q, StringComparison.Ordinal))
                {
                    list.Add(item);
                    break;
                }
            }
        }
        return list;
    }

    /// <summary>
    /// One edit incl. insert/delete: "aala"/"ala"→Tala, "gua"→Água. Unique only.
    /// </summary>
    private ItemDefinition? MatchNearShortLabel(string q, int slotW, int slotH)
    {
        if (IsGenericTooltipToken(q)) return null;
        ItemDefinition? best = null;
        var hits = 0;
        foreach (var item in Items)
        {
            if (!FitsInside(item, slotW, slotH)) continue;
            foreach (var sn in ShortLabelsOf(item))
            {
                if (sn.Length is < 3 or > 10) continue;
                if (Math.Abs(sn.Length - q.Length) > 1) continue;
                if (LevenshteinDistance(q, sn) > 1) continue;
                hits++;
                best = item;
                break;
            }
        }
        return hits == 1 ? best : null;
    }

    private List<ItemDefinition> AllExactShortLabel(string q)
    {
        q = NormalizeShortLabel(q);
        if (q.Length < 2) return [];
        var list = new List<ItemDefinition>();
        foreach (var item in Items)
        {
            foreach (var sn in ShortLabelsOf(item))
            {
                if (sn == q)
                {
                    list.Add(item);
                    break;
                }
            }
        }
        return list;
    }

    private List<ItemDefinition> AllPrefixShortLabel(string q, int slotW, int slotH)
    {
        var list = new List<ItemDefinition>();
        foreach (var item in Items)
        {
            if (!FitsInside(item, slotW, slotH)) continue;
            foreach (var sn in ShortLabelsOf(item))
            {
                if (sn.Length > q.Length && sn.StartsWith(q, StringComparison.Ordinal))
                {
                    list.Add(item);
                    break;
                }
            }
        }
        return list;
    }

    private static List<ItemDefinition> FilterBySlot(List<ItemDefinition> items, int slotW, int slotH)
    {
        if (items.Count <= 1) return items;
        var native = items.Where(i => FitsSlot(i, slotW, slotH)).ToList();
        if (native.Count > 0) return native;
        var inside = items.Where(i => FitsInside(i, slotW, slotH)).ToList();
        return inside.Count > 0 ? inside : items;
    }

    private static IEnumerable<string> ShortLabelsOf(ItemDefinition item)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in new[]
                 {
                     ItemDisplayNames.ShortName(item),
                     ItemDisplayNames.CatalogShortName(item)
                 })
        {
            var n = NormalizeShortLabel(raw);
            if (n.Length >= 2 && seen.Add(n)) yield return n;
        }
    }

    private static ItemDefinition? UniqueOrNull(List<ItemDefinition> items) =>
        items.Count == 1 ? items[0] : null;

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
        }
        return d[a.Length, b.Length];
    }

    private static bool FitsSlot(ItemDefinition item, int w, int h) =>
        (item.Width == w && item.Height == h) || (item.Width == h && item.Height == w);

    /// <summary>Item can sit inside a (possibly over-detected) highlight box.</summary>
    private static bool FitsInside(ItemDefinition item, int w, int h) =>
        (item.Width <= w && item.Height <= h) || (item.Width <= h && item.Height <= w);

    private static string NormalizeShortLabel(string s)
    {
        var folded = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string Normalize(string s) => NormalizeTooltip(s);

    private static string NormalizeTooltip(string s)
    {
        var folded = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

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

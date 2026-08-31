using System.IO;
using System.Text.Json;

namespace Tarkovy.Services;

/// <summary>Item IDs needed for quests (find/give/plant) and hideout (build + crafts).</summary>
internal static class ItemUsageIndex
{
    private static readonly HashSet<string> CurrencyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "5449016a4bdc2d6f028b456f", // RUB
        "5696686a4bdc2da3298b456a", // USD
        "569668774bdc2da2298b4568", // EUR
    };

    public static string CachePath =>
        Path.Combine(SettingsStore.AppDataDir, "item-usage-cache.json");

    public static (HashSet<string> Quest, HashSet<string> Hideout) Empty =>
        (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public static (HashSet<string> Quest, HashSet<string> Hideout) LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return Empty;
            using var doc = JsonDocument.Parse(File.ReadAllText(CachePath));
            var root = doc.RootElement;
            return (ReadIdSet(root, "quest"), ReadIdSet(root, "hideout"));
        }
        catch
        {
            return Empty;
        }
    }

    public static async Task SaveCacheAsync(
        HashSet<string> quest, HashSet<string> hideout, CancellationToken ct)
    {
        Directory.CreateDirectory(SettingsStore.AppDataDir);
        var json = JsonSerializer.Serialize(new
        {
            quest = quest.ToArray(),
            hideout = hideout.ToArray()
        });
        await File.WriteAllTextAsync(CachePath, json, ct).ConfigureAwait(false);
    }

    public static HashSet<string> ParseQuestItems(string json)
    {
        var dest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return dest;

        var tasks = data;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("tasks", out var nested))
            tasks = nested;

        if (tasks.ValueKind == JsonValueKind.Object)
        {
            foreach (var task in tasks.EnumerateObject())
                CollectObjectives(task.Value, dest);
        }
        else if (tasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var task in tasks.EnumerateArray())
                CollectObjectives(task, dest);
        }

        return dest;
    }

    public static HashSet<string> ParseHideoutItems(string hideoutJson, string? craftsJson)
    {
        var dest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectHideoutStations(hideoutJson, dest);
        if (!string.IsNullOrWhiteSpace(craftsJson))
            CollectCrafts(craftsJson, dest);
        return dest;
    }

    private static void CollectObjectives(JsonElement task, HashSet<string> dest)
    {
        if (!task.TryGetProperty("objectives", out var objs) || objs.ValueKind != JsonValueKind.Array)
            return;

        foreach (var obj in objs.EnumerateArray())
        {
            AddId(Str(obj, "item"), dest);
            if (!obj.TryGetProperty("items", out var items)) continue;
            if (items.ValueKind != JsonValueKind.Array) continue;
            foreach (var it in items.EnumerateArray())
            {
                if (it.ValueKind == JsonValueKind.String)
                    AddId(it.GetString(), dest);
                else if (it.ValueKind == JsonValueKind.Object)
                    AddId(Str(it, "item"), dest);
            }
        }
    }

    private static void CollectHideoutStations(string json, HashSet<string> dest)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return;
        IEnumerable<JsonElement> stations = data.ValueKind switch
        {
            JsonValueKind.Object => data.EnumerateObject().Select(p => p.Value),
            JsonValueKind.Array => data.EnumerateArray(),
            _ => []
        };

        foreach (var station in stations)
        {
            if (!station.TryGetProperty("levels", out var levels) || levels.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var level in levels.EnumerateArray())
                CollectItemReqs(level, "itemRequirements", dest);
        }
    }

    private static void CollectCrafts(string json, HashSet<string> dest)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return;
        IEnumerable<JsonElement> crafts = data.ValueKind switch
        {
            JsonValueKind.Array => data.EnumerateArray(),
            JsonValueKind.Object => data.EnumerateObject().Select(p => p.Value),
            _ => []
        };

        foreach (var craft in crafts)
            CollectItemReqs(craft, "requiredItems", dest);
    }

    private static void CollectItemReqs(JsonElement parent, string name, HashSet<string> dest)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;
        foreach (var req in arr.EnumerateArray())
            AddId(Str(req, "item"), dest);
    }

    private static HashSet<string> ReadIdSet(JsonElement root, string name)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return set;
        foreach (var el in arr.EnumerateArray())
            AddId(el.GetString(), set);
        return set;
    }

    private static void AddId(string? id, HashSet<string> dest)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length < 20) return;
        if (CurrencyIds.Contains(id)) return;
        dest.Add(id);
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

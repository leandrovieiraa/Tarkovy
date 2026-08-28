using System.Windows;
using Tarkovy.Models;

namespace Tarkovy.Services;

public static class Loc
{
    public const string English = "en";
    public const string Portuguese = "pt";

    private static ResourceDictionary? _current;
    private static string _code = English;

    public static string Code => _code;

    public static event Action? LanguageChanged;

    public static void Apply(string? code)
    {
        code = Normalize(code);
        if (_current != null && _code == code && Application.Current != null)
        {
            // still fire so UI rebinds after settings save
        }

        var uri = new Uri($"Themes/Lang.{code}.xaml", UriKind.Relative);
        ResourceDictionary next;
        try
        {
            next = new ResourceDictionary { Source = uri };
        }
        catch
        {
            code = English;
            next = new ResourceDictionary { Source = new Uri("Themes/Lang.en.xaml", UriKind.Relative) };
        }

        if (Application.Current?.Resources.MergedDictionaries is { } merged)
        {
            if (_current != null)
                merged.Remove(_current);
            // remove any stale lang dicts
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.OriginalString ?? "";
                if (src.Contains("Lang.", StringComparison.OrdinalIgnoreCase))
                    merged.RemoveAt(i);
            }
            merged.Add(next);
        }

        _current = next;
        _code = code;
        LanguageChanged?.Invoke();
    }

    public static string Normalize(string? code) =>
        string.Equals(code, Portuguese, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(code, "pt-BR", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(code, "pt-PT", StringComparison.OrdinalIgnoreCase)
            ? Portuguese
            : English;

    public static bool IsPortuguese => _code == Portuguese;

    public static string QuestName(QuestDefinition q) =>
        IsPortuguese && !string.IsNullOrWhiteSpace(q.NamePt) ? q.NamePt : q.Name;

    public static string QuestTrader(QuestDefinition q) =>
        IsPortuguese && !string.IsNullOrWhiteSpace(q.TraderPt) ? q.TraderPt : q.Trader;

    public static string T(string key)
    {
        if (Application.Current?.TryFindResource(key) is string s && !string.IsNullOrEmpty(s))
            return s;
        return key;
    }

    public static string T(string key, params object[] args)
    {
        var fmt = T(key);
        try { return string.Format(fmt, args); }
        catch { return fmt; }
    }

    public static Dictionary<string, string> MapUiBundle() => new()
    {
        ["waiting"] = T("Map.Waiting"),
        ["svgUnavailable"] = T("Map.SvgUnavailable"),
        ["loadFailed"] = T("Map.LoadFailed"),
        ["rotateLeft"] = T("Map.Rotate.Left"),
        ["rotateReset"] = T("Map.Rotate.Reset"),
        ["rotateRight"] = T("Map.Rotate.Right"),
        ["extract"] = T("Map.Tip.Extract"),
        ["mine"] = T("Map.Tip.Mine"),
        ["spawn"] = T("Map.Tip.Spawn"),
        ["quest"] = T("Map.Tip.Quest"),
        ["waypoint"] = T("Map.Waypoint"),
        ["clearWaypoint"] = T("Map.ClearWaypoint"),
        ["placeWaypoint"] = T("Map.PlaceWaypoint"),
        ["placeWaypointActive"] = T("Map.PlaceWaypoint.Active"),
        ["placeWaypointHint"] = T("Map.PlaceWaypoint.Hint"),
        ["customWaypoint"] = T("Map.CustomWaypoint"),
        ["layerExtracts"] = T("Map.Layer.Extracts"),
        ["layerMines"] = T("Map.Layer.Mines"),
        ["layerSpawns"] = T("Map.Layer.Spawns"),
        ["layerQuests"] = T("Map.Layer.Quests"),
        ["layerLabels"] = T("Map.Layer.Labels"),
        ["floorUp"] = T("Map.Floor.Up"),
        ["floorDown"] = T("Map.Floor.Down"),
        ["floorCurrent"] = T("Map.Floor.Current")
    };
}

using System.Text.Json.Serialization;

namespace Tarkovy.Models;

public sealed class AppSettings
{
    public string LogsFolder { get; set; } = @"C:\Battlestate Games\Escape from Tarkov\Logs";
    public string ScreenshotsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Escape from Tarkov",
        "Screenshots");

    public double OverlayOpacity { get; set; } = 0.72;
    public bool FollowPlayer { get; set; } = true;
    public bool DeleteAfterRead { get; set; } = true;
    public bool KeepLastScreenshot { get; set; } = false;
    public string SelectedMapId { get; set; } = "customs";
    public bool ShowExtracts { get; set; } = true;
    public bool ShowMines { get; set; } = true;
    public bool ShowSpawns { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool OverlayVisible { get; set; } = false;
    /// <summary>Ao detectar fim de raid nos logs, encerra sessão e limpa leftovers.</summary>
    public bool AutoCleanupOnRaidEnd { get; set; } = true;
    /// <summary>Mostra nomes das marcações no mapa; se false, só tooltip no hover.</summary>
    public bool ShowMarkerLabels { get; set; } = true;
    public bool ShowQuests { get; set; } = true;
    /// <summary>Quest slugs enabled as active waypoints for the current character/session.</summary>
    public List<string> EnabledQuestSlugs { get; set; } = [];
    /// <summary>Quest slugs marked complete — no longer tracked on the map.</summary>
    public List<string> CompletedQuestSlugs { get; set; } = [];
    /// <summary>UI language: en (default) or pt.</summary>
    public string UiLanguage { get; set; } = "en";

    [JsonIgnore]
    public MapWaypoint? ActiveWaypoint { get; set; }
    [JsonIgnore]
    public string HotkeyToggleOverlay { get; } = "F8";

    [JsonIgnore]
    public string HotkeyToggleSize { get; } = "F9";
}

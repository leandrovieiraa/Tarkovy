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
    /// <summary>Troca andar do SVG pela altura Y do screenshot (quando o mapa tem floors).</summary>
    public bool AutoFloorFromHeight { get; set; } = true;
    public bool DeleteAfterRead { get; set; } = true;
    public bool KeepLastScreenshot { get; set; } = false;
    public string SelectedMapId { get; set; } = "customs";
    public bool ShowExtracts { get; set; } = true;
    public bool ShowMines { get; set; } = true;
    public bool ShowSpawns { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool OverlayVisible { get; set; } = false;
    public bool ItemLensVisible { get; set; } = false;
    public bool ItemScanEnabled { get; set; } = true;
    /// <summary>Save scan captures + JSON report to AppData item-scan-debug.</summary>
    public bool ItemScanDebugEnabled { get; set; } = false;
    /// <summary>Optional vision fallback: Claude / ChatGPT / Gemini identify the highlight against the local catalog.</summary>
    public bool ItemScanAiEnabled { get; set; }
    /// <summary>claude | openai | gemini</summary>
    public string ItemScanAiProvider { get; set; } = "claude";
    public string ItemScanAiApiKey { get; set; } = "";
    /// <summary>0 = auto from screen height; else 63/84/126 for 1080p/1440p/4K.</summary>
    public int ItemScanSlotPx { get; set; } = 0;
    /// <summary>0 = derive from slot / screen; else explicit game width (e.g. 1920).</summary>
    public int ItemScanGameWidth { get; set; }
    /// <summary>0 = derive from slot / screen; else explicit game height (e.g. 1080).</summary>
    public int ItemScanGameHeight { get; set; }
    /// <summary>Match 90° CCW rotated stash icons.</summary>
    public bool ItemScanRotatedIcons { get; set; } = true;
    public double ItemLensOpacity { get; set; } = 0.88;
    /// <summary>Ao detectar fim de raid nos logs, encerra sessão e limpa leftovers.</summary>
    public bool AutoCleanupOnRaidEnd { get; set; } = true;
    /// <summary>Mostra nomes das marcações no mapa; se false, só tooltip no hover.</summary>
    public bool ShowMarkerLabels { get; set; } = true;
    public bool ShowQuests { get; set; } = true;
    /// <summary>Opt-in POI types (loot / bosses / locations). Empty = map stays clean.</summary>
    public List<string> EnabledPoiTypes { get; set; } = [];
    /// <summary>Quest slugs enabled as active waypoints for the current character/session.</summary>
    public List<string> EnabledQuestSlugs { get; set; } = [];
    /// <summary>Quest slugs marked complete — no longer tracked on the map.</summary>
    public List<string> CompletedQuestSlugs { get; set; } = [];
    /// <summary>UI language: en (default) or pt.</summary>
    public string UiLanguage { get; set; } = "en";

    public WindowPlacement MainWindowPlacement { get; set; } = new();
    public WindowPlacement OverlayWindowPlacement { get; set; } = new();
    public WindowPlacement ItemLensWindowPlacement { get; set; } = new();
    public bool OverlaySidePanelOpen { get; set; }

    [JsonIgnore]
    public MapWaypoint? ActiveWaypoint { get; set; }
    [JsonIgnore]
    public string HotkeyToggleOverlay { get; } = "F8";

    [JsonIgnore]
    public string HotkeyToggleSize { get; } = "F9";

    [JsonIgnore]
    public string HotkeyToggleItemLens { get; } = "F10";

    /// <summary>Change map floor up (Factory / Interchange / Ground Zero).</summary>
    public string HotkeyFloorUp { get; } = "PageUp";

    /// <summary>Change map floor down.</summary>
    public string HotkeyFloorDown { get; } = "PageDown";
}

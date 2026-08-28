using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy.Controls;

public partial class MapView : UserControl
{
    private static int _instance;
    private readonly int _id = Interlocked.Increment(ref _instance);
    private bool _ready;
    private readonly Queue<string> _pending = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event Action<string, bool>? LayerToggled;
    public event Action<MapWaypoint?>? WaypointChanged;

    public MapView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var userData = Path.Combine(SettingsStore.AppDataDir, "webview", $"wv{_id}");
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);
            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    HandleWebMessage(e.WebMessageAsJson);
                }
                catch
                {
                    /* ignore malformed host messages */
                }
            };

            var assets = AssetBootstrap.Ensure();
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "tarkovy.assets",
                assets,
                CoreWebView2HostResourceAccessKind.Allow);
            Web.Source = new Uri("https://tarkovy.assets/map.html");
        }
        catch (Exception ex)
        {
            Web.NavigateToString(
                "<html><body style='background:#000;color:#fff;font-family:Bahnschrift;padding:16px'>WEBVIEW2: " +
                System.Net.WebUtility.HtmlEncode(ex.Message) + "</body></html>");
        }
    }

    private void HandleWebMessage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString() ?? "";

        if (string.Equals(type, "dragWindow", StringComparison.OrdinalIgnoreCase))
        {
            try { Window.GetWindow(this)?.DragMove(); }
            catch { /* ignore */ }
            return;
        }

        if (string.Equals(type, "ready", StringComparison.OrdinalIgnoreCase))
        {
            _ready = true;
            ApplyLanguage();
            while (_pending.Count > 0)
                Web.CoreWebView2.PostWebMessageAsJson(_pending.Dequeue());
            return;
        }

        if (string.Equals(type, "layer", StringComparison.OrdinalIgnoreCase))
        {
            var key = root.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
            var value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True;
            if (!string.IsNullOrEmpty(key))
                LayerToggled?.Invoke(key, value);
            return;
        }

        if (string.Equals(type, "waypoint", StringComparison.OrdinalIgnoreCase))
        {
            MapWaypoint? wp = null;
            if (root.TryGetProperty("waypoint", out var wpEl) && wpEl.ValueKind == JsonValueKind.Object)
                wp = JsonSerializer.Deserialize<MapWaypoint>(wpEl.GetRawText(), Json);
            WaypointChanged?.Invoke(wp);
        }
    }

    public void LoadMap(MapDefinition map, IReadOnlyList<ExtractMarker> extracts, IReadOnlyList<HazardMarker>? mines = null, IReadOnlyList<SpawnMarker>? spawns = null, bool? showLabels = null)
    {
        Post(new { type = "loadMap", map = BuildMapPayload(map) });
        SetMarkers(extracts, mines, spawns, showLabels);
    }

    public void SetAutoFloor(bool auto) => Post(new { type = "autoFloor", value = auto });

    public void RefreshMapPayload(MapDefinition map) =>
        Post(new { type = "loadMap", map = BuildMapPayload(map) });

    public void SetMarkers(IReadOnlyList<ExtractMarker> extracts, IReadOnlyList<HazardMarker>? mines = null, IReadOnlyList<SpawnMarker>? spawns = null, bool? showLabels = null)
    {
        Post(new
        {
            type = "markers",
            extracts,
            mines = mines ?? Array.Empty<HazardMarker>(),
            spawns = spawns ?? Array.Empty<SpawnMarker>(),
            showLabels = showLabels ?? true
        });
    }

    public void SetQuests(IReadOnlyList<QuestDefinition> quests, IEnumerable<string>? enabledSlugs = null)
    {
        var completed = new HashSet<string>(App.Settings.CompletedQuestSlugs, StringComparer.OrdinalIgnoreCase);
        var enabled = (enabledSlugs ?? App.Settings.EnabledQuestSlugs)
            .Where(s => !completed.Contains(s))
            .ToArray();

        var payload = quests.Select(q => new
        {
            slug = q.Slug,
            name = Loc.QuestName(q),
            trader = Loc.QuestTrader(q),
            objectives = q.Objectives,
            completed = completed.Contains(q.Slug)
        }).ToArray();
        Post(new
        {
            type = "quests",
            quests = payload,
            enabled,
            completed = App.Settings.CompletedQuestSlugs.ToArray()
        });
    }

    public void SetLayers(bool extracts, bool mines, bool spawns, bool quests, bool labels) =>
        Post(new
        {
            type = "layers",
            layers = new { extracts, mines, spawns, quests, labels }
        });

    public void SetWaypoint(MapWaypoint? waypoint) =>
        Post(new { type = "waypoint", waypoint });

    public void SetShowLabels(bool show) => Post(new { type = "showLabels", value = show });

    public void SetPlayer(PlayerFix? fix)
    {
        if (fix == null)
        {
            Post(new { type = "player", player = (object?)null });
            return;
        }

        Post(new { type = "player", player = new { x = fix.X, y = fix.Y, z = fix.Z, yaw = fix.Yaw } });
    }

    public void SetFollow(bool follow) => Post(new { type = "follow", value = follow });

    public void ResetView() => Post(new { type = "resetView" });

    public void ApplyLanguage() =>
        Post(new { type = "lang", strings = Loc.MapUiBundle() });

    private void Post(object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        Dispatcher.Invoke(() =>
        {
            if (!_ready || Web.CoreWebView2 == null)
            {
                _pending.Enqueue(json);
                return;
            }

            Web.CoreWebView2.PostWebMessageAsJson(json);
        });
    }

    private static object BuildMapPayload(MapDefinition map)
    {
        object? floors = null;
        if (map.Floors is { Count: > 0 })
        {
            floors = map.Floors.Select(f => new
            {
                id = f.Id,
                name = Loc.IsPortuguese && !string.IsNullOrWhiteSpace(f.NamePt) ? f.NamePt : f.Name,
                shortLabel = f.Short,
                svgLayer = f.SvgLayer,
                minHeight = f.MinHeight,
                maxHeight = f.MaxHeight
            }).ToArray();
        }

        return new
        {
            id = map.Id,
            name = map.Name,
            svgPath = map.SvgPath,
            coordinateRotation = map.CoordinateRotation,
            transform = map.Transform,
            bounds = map.Bounds,
            svgBounds = map.SvgBounds,
            floors,
            autoFloor = App.Settings.AutoFloorFromHeight
        };
    }
}

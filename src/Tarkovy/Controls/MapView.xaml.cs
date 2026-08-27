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
                var json = e.WebMessageAsJson ?? "";
                if (json.Contains("dragWindow", StringComparison.OrdinalIgnoreCase))
                {
                    try { Window.GetWindow(this)?.DragMove(); }
                    catch { /* ignore */ }
                    return;
                }
                if (json.Contains("ready", StringComparison.OrdinalIgnoreCase))
                {
                    _ready = true;
                    ApplyLanguage();
                    while (_pending.Count > 0)
                        Web.CoreWebView2.PostWebMessageAsJson(_pending.Dequeue());
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

    public void LoadMap(MapDefinition map, IReadOnlyList<ExtractMarker> extracts, IReadOnlyList<HazardMarker>? mines = null, bool? showLabels = null)
    {
        Post(new { type = "loadMap", map });
        Post(new
        {
            type = "markers",
            extracts,
            mines = mines ?? Array.Empty<HazardMarker>(),
            showLabels = showLabels ?? true
        });
    }

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
}

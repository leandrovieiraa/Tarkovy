using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Tarkovy.Models;

namespace Tarkovy.Services;

internal sealed class IconTemplate : IDisposable
{
    public required ItemDefinition Item { get; init; }
    public required Mat Color { get; init; }
    public required Mat Mask { get; init; }
    public bool FromGameCache { get; init; }

    public void Dispose()
    {
        Color.Dispose();
        Mask.Dispose();
    }
}

/// <summary>
/// 1080p icon templates: EFT Icon Cache first, then tarkov.dev sprites sized to n×63+1.
/// </summary>
internal sealed class IconBank : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _iconDir;
    private readonly Dictionary<(int W, int H), List<IconTemplate>> _bySize = new();
    private readonly object _gate = new();
    private int _builtForCount;
    private int _gameCacheCount;

    public IconBank()
    {
        _iconDir = Path.Combine(SettingsStore.AppDataDir, "item-icons");
        Directory.CreateDirectory(_iconDir);
    }

    public bool IsReady
    {
        get { lock (_gate) return _builtForCount > 0 && _bySize.Count > 0; }
    }

    public int Count
    {
        get { lock (_gate) return _bySize.Values.Sum(l => l.Count); }
    }

    public int GameCacheCount
    {
        get { lock (_gate) return _gameCacheCount; }
    }

    public string? LastError { get; private set; }

    public IReadOnlyList<IconTemplate> ForSize(int w, int h)
    {
        lock (_gate)
            return _bySize.TryGetValue((w, h), out var list) ? list : [];
    }

    public void Reset()
    {
        lock (_gate) DisposeTemplates();
    }

    public async Task BuildAsync(ItemCatalog catalog, IProgress<string>? progress, CancellationToken ct)
    {
        if (catalog.Items.Count == 0) return;
        lock (_gate)
        {
            if (_builtForCount == catalog.Items.Count && _bySize.Count > 0) return;
        }

        progress?.Report("Preparing item icons…");
        var cacheMap = LoadGameCacheIndex();
        var built = new Dictionary<(int, int), List<IconTemplate>>();
        var cacheHits = 0;
        var n = 0;

        foreach (var item in catalog.Items)
        {
            ct.ThrowIfCancellationRequested();
            n++;
            if (n % 120 == 0)
                progress?.Report($"Icons {n}/{catalog.Items.Count}");

            var fromCache = false;
            Mat? color = null;
            Mat? mask = null;
            if (cacheMap.TryGetValue(item.Id, out var cachePath))
            {
                var prepared = LoadPrepared(cachePath, item.Width, item.Height);
                if (prepared != null)
                {
                    color = prepared.Value.Color;
                    mask = prepared.Value.Mask;
                    fromCache = true;
                    cacheHits++;
                }
            }

            if (color == null)
            {
                var prepared = await LoadFromCatalogAsync(item, ct).ConfigureAwait(false);
                if (prepared == null) continue;
                color = prepared.Value.Color;
                mask = prepared.Value.Mask;
            }

            var key = (item.Width, item.Height);
            if (!built.TryGetValue(key, out var list))
            {
                list = [];
                built[key] = list;
            }

            list.Add(new IconTemplate
            {
                Item = item,
                Color = color,
                Mask = mask!,
                FromGameCache = fromCache
            });
        }

        lock (_gate)
        {
            DisposeTemplates();
            foreach (var kv in built)
                _bySize[kv.Key] = kv.Value;
            _builtForCount = _bySize.Count > 0 ? catalog.Items.Count : 0;
            _gameCacheCount = cacheHits;
            LastError = _bySize.Count == 0 ? "no templates" : null;
        }
    }

    public static string GameCacheDirectory =>
        Path.Combine(Path.GetTempPath(), "Battlestate Games", "EscapeFromTarkov", "Icon Cache");

    private static Dictionary<string, string> LoadGameCacheIndex()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = GameCacheDirectory;
        if (!Directory.Exists(dir)) return map;

        foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (LooksLikeItemId(id))
                map[id] = file;
        }

        var indexPath = Path.Combine(dir, "index.json");
        if (!File.Exists(indexPath)) return map;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
            AbsorbIndex(doc.RootElement, dir, map);
        }
        catch
        {
            // keep filename-based hits
        }

        return map;
    }

    private static void AbsorbIndex(JsonElement root, string dir, Dictionary<string, string> map)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name is "items" or "index" or "Data" or "data" or "Icons" or "icons")
                    {
                        AbsorbIndex(prop.Value, dir, map);
                        continue;
                    }

                    if (LooksLikeItemId(prop.Name))
                        TryAdd(map, prop.Name, ResolveCacheFile(dir, prop.Value));
                    else
                        AbsorbIndex(prop.Value, dir, map);
                }
                break;

            case JsonValueKind.Array:
                var i = 0;
                foreach (var el in root.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String && LooksLikeItemId(el.GetString()))
                        TryAdd(map, el.GetString()!, Path.Combine(dir, $"{i}.png"));
                    else if (el.ValueKind == JsonValueKind.Object)
                    {
                        string? id = null;
                        string? file = null;
                        foreach (var p in el.EnumerateObject())
                        {
                            if (p.NameEquals("id") || p.NameEquals("uid") || p.NameEquals("tpl") || p.NameEquals("_id"))
                                id = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                            else if (p.NameEquals("file") || p.NameEquals("path") || p.NameEquals("icon") || p.NameEquals("name"))
                                file = ResolveCacheFile(dir, p.Value);
                        }
                        if (id != null)
                            TryAdd(map, id, file ?? Path.Combine(dir, $"{i}.png"));
                    }
                    i++;
                }
                break;
        }
    }

    private static void TryAdd(Dictionary<string, string> map, string id, string? path)
    {
        if (!LooksLikeItemId(id) || string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path))
            map[id] = path;
    }

    private static string? ResolveCacheFile(string dir, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var s = value.GetString();
                if (string.IsNullOrWhiteSpace(s)) return null;
                var p = Path.IsPathRooted(s) ? s : Path.Combine(dir, s);
                if (File.Exists(p)) return p;
                if (File.Exists(p + ".png")) return p + ".png";
                return p;
            case JsonValueKind.Number:
                if (value.TryGetInt32(out var n))
                {
                    var numbered = Path.Combine(dir, n + ".png");
                    if (File.Exists(numbered)) return numbered;
                }
                return null;
            case JsonValueKind.Object:
                foreach (var key in new[] { "file", "path", "icon", "name", "hash" })
                {
                    if (value.TryGetProperty(key, out var inner))
                    {
                        var resolved = ResolveCacheFile(dir, inner);
                        if (resolved != null) return resolved;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    private static bool LooksLikeItemId(string? id) =>
        id is { Length: >= 16 and <= 36 } && id.All(c => char.IsLetterOrDigit(c));

    private async Task<(Mat Color, Mat Mask)?> LoadFromCatalogAsync(ItemDefinition item, CancellationToken ct)
    {
        var slots = item.Width * item.Height;
        var url = slots == 1
            ? item.IconLink ?? item.GridImageLink
            : item.GridImageLink ?? item.IconLink;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var ext = url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".png";
        var path = Path.Combine(_iconDir, $"{item.Id}{ext}");
        try
        {
            if (!File.Exists(path))
            {
                var bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            }

            return LoadPrepared(path, item.Width, item.Height);
        }
        catch
        {
            return null;
        }
    }

    private static (Mat Color, Mat Mask)? LoadPrepared(string path, int slotW, int slotH)
    {
        try
        {
            using var src = Cv2.ImRead(path, ImreadModes.Unchanged);
            if (src.Empty())
            {
                using var bmp = new Bitmap(path);
                using var mat = BitmapConverter.ToMat(bmp);
                return PrepareTemplate(mat, slotW, slotH);
            }

            return PrepareTemplate(src, slotW, slotH);
        }
        catch
        {
            return null;
        }
    }

    private static (Mat Color, Mat Mask) PrepareTemplate(Mat src, int slotW, int slotH)
    {
        var (tw, th) = LensConfig.SlotPixelSize1080(slotW, slotH);
        using var bgr = ToBgr(src);
        using var alpha = ExtractAlpha(src);
        using var resized = new Mat();
        if (bgr.Width != tw || bgr.Height != th)
            Cv2.Resize(bgr, resized, new OpenCvSharp.Size(tw, th), interpolation: InterpolationFlags.Area);
        else
            bgr.CopyTo(resized);

        var color = new Mat();
        resized.CopyTo(color);

        Mat mask;
        if (alpha != null && !alpha.Empty() && Cv2.CountNonZero(alpha) > 24)
        {
            mask = new Mat();
            if (alpha.Width != tw || alpha.Height != th)
                Cv2.Resize(alpha, mask, new OpenCvSharp.Size(tw, th), interpolation: InterpolationFlags.Nearest);
            else
                alpha.CopyTo(mask);
            Cv2.Threshold(mask, mask, 16, 255, ThresholdTypes.Binary);
        }
        else
        {
            mask = OpaqueIconMask(tw, th);
        }

        return (color, mask);
    }

    /// <summary>Wiki sprites bake in-cell name / durability — ignore those bands when there is no alpha.</summary>
    private static Mat OpaqueIconMask(int tw, int th)
    {
        var mask = new Mat(th, tw, MatType.CV_8UC1, Scalar.All(255));
        var labelH = Math.Max(3, th / 4);
        var barH = Math.Max(3, th / 6);
        Cv2.Rectangle(mask, new Rect(0, 0, tw, labelH), Scalar.All(0), -1);
        Cv2.Rectangle(mask, new Rect(0, th - barH, tw, barH), Scalar.All(0), -1);
        return mask;
    }

    private static Mat ToBgr(Mat src)
    {
        var dst = new Mat();
        switch (src.Channels())
        {
            case 1:
                Cv2.CvtColor(src, dst, ColorConversionCodes.GRAY2BGR);
                return dst;
            case 4:
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2BGR);
                return dst;
            default:
                return src.Clone();
        }
    }

    private static Mat? ExtractAlpha(Mat src)
    {
        if (src.Channels() != 4) return null;
        var chans = Cv2.Split(src);
        try
        {
            return chans[3].Clone();
        }
        finally
        {
            foreach (var c in chans) c.Dispose();
        }
    }

    private void DisposeTemplates()
    {
        foreach (var list in _bySize.Values)
        {
            foreach (var t in list)
                t.Dispose();
        }
        _bySize.Clear();
        _builtForCount = 0;
        _gameCacheCount = 0;
    }

    public void Dispose() => Reset();
}

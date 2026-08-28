using System.Drawing;
using System.IO;
using System.Net.Http;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class ItemIconMatcher : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _iconDir;
    private readonly Dictionary<(int W, int H), List<TemplateEntry>> _templates = new();
    private readonly object _gate = new();
    private int _builtForCount;

    private sealed class TemplateEntry
    {
        public required ItemDefinition Item { get; init; }
        public required Mat Gray { get; init; }
    }

    public ItemIconMatcher()
    {
        _iconDir = Path.Combine(SettingsStore.AppDataDir, "item-icons");
        Directory.CreateDirectory(_iconDir);
    }

    public bool IsReady => _builtForCount > 0;

    public async Task EnsureIndexAsync(ItemCatalog catalog, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (catalog.Items.Count == 0) return;
        lock (_gate)
        {
            if (_builtForCount == catalog.Items.Count && _templates.Count > 0) return;
        }

        progress?.Report("Preparing item icons…");
        var sizes = new[] { (1, 1), (1, 2), (2, 1), (2, 2), (2, 3), (3, 2), (1, 3), (3, 1) };
        var built = new Dictionary<(int, int), List<TemplateEntry>>();

        foreach (var (w, h) in sizes)
        {
            ct.ThrowIfCancellationRequested();
            var items = catalog.ForSize(w, h);
            if (items.Count == 0) continue;
            var list = new List<TemplateEntry>();
            var n = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                var mat = await LoadTemplateAsync(item, ct).ConfigureAwait(false);
                if (mat == null) continue;
                list.Add(new TemplateEntry { Item = item, Gray = mat });
                n++;
                if (n % 120 == 0)
                    progress?.Report($"Icons {w}x{h}: {n}/{items.Count}");
            }
            if (list.Count > 0) built[(w, h)] = list;
        }

        lock (_gate)
        {
            foreach (var kv in _templates)
                foreach (var t in kv.Value) t.Gray.Dispose();
            _templates.Clear();
            foreach (var kv in built) _templates[kv.Key] = kv.Value;
            _builtForCount = catalog.Items.Count;
        }
    }

    public (ItemDefinition? Item, double Confidence) MatchIcon(Bitmap capture, int slotW, int slotH)
    {
        List<TemplateEntry>? entries;
        lock (_gate)
        {
            if (!_templates.TryGetValue((slotW, slotH), out entries) || entries.Count == 0)
                return (null, 0);
        }

        using var src = BitmapConverter.ToMat(capture);
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        using var resized = new Mat();
        Cv2.Resize(gray, resized, new OpenCvSharp.Size(64 * slotW, 64 * slotH));

        ItemDefinition? bestItem = null;
        var best = 0.0;
        foreach (var entry in entries)
        {
            if (entry.Gray.Width != resized.Width || entry.Gray.Height != resized.Height) continue;
            using var result = new Mat();
            Cv2.MatchTemplate(resized, entry.Gray, result, TemplateMatchModes.SqDiffNormed);
            Cv2.MinMaxLoc(result, out var minVal, out _, out _, out _);
            var conf = 1.0 - minVal;
            if (conf > best)
            {
                best = conf;
                bestItem = entry.Item;
            }
        }

        return (bestItem, best);
    }

    private async Task<Mat?> LoadTemplateAsync(ItemDefinition item, CancellationToken ct)
    {
        var path = Path.Combine(_iconDir, $"{item.Id}.webp");
        var url = item.GridImageLink ?? item.IconLink;
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            if (!File.Exists(path))
            {
                var bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            }

            using var color = Cv2.ImRead(path, ImreadModes.Color);
            if (color.Empty()) return null;

            var targetW = 64 * item.Width;
            var targetH = 64 * item.Height;
            using var resized = new Mat();
            Cv2.Resize(color, resized, new OpenCvSharp.Size(targetW, targetH));
            var gray = new Mat();
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var kv in _templates)
                foreach (var t in kv.Value) t.Gray.Dispose();
            _templates.Clear();
        }
    }
}

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
    private readonly Dictionary<(int W, int H, bool Highlighted), List<TemplateEntry>> _templates = new();
    private readonly object _gate = new();
    private int _builtForCount;
    private const int TemplateFormat = 3;
    private int _builtFormat;

    private sealed class TemplateEntry
    {
        public required ItemDefinition Item { get; init; }
        public required Mat Color { get; init; }
        public required Mat Mask { get; init; }
    }

    public ItemIconMatcher()
    {
        _iconDir = Path.Combine(SettingsStore.AppDataDir, "item-icons");
        Directory.CreateDirectory(_iconDir);
    }

    public bool IsReady => _builtForCount > 0 && _templates.Count > 0;

    public int IndexedTemplateCount
    {
        get
        {
            lock (_gate) return _templates.Values.Sum(l => l.Count);
        }
    }

    public void ResetIndex()
    {
        lock (_gate) DisposeTemplates();
    }

    private void DisposeTemplates()
    {
        foreach (var kv in _templates)
        {
            foreach (var t in kv.Value)
            {
                t.Color.Dispose();
                t.Mask.Dispose();
            }
        }
        _templates.Clear();
        _builtForCount = 0;
        _builtFormat = 0;
    }

    public async Task EnsureIndexAsync(ItemCatalog catalog, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (catalog.Items.Count == 0) return;
        lock (_gate)
        {
            if (_builtForCount == catalog.Items.Count && _builtFormat == TemplateFormat && _templates.Count > 0) return;
        }

        progress?.Report("Preparing item icons…");
        var sizes = new[] { (1, 1), (1, 2), (2, 1), (2, 2), (2, 3), (3, 2) };
        var built = new Dictionary<(int, int, bool), List<TemplateEntry>>();

        foreach (var highlighted in new[] { true, false })
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
                var prepared = await LoadTemplateAsync(item, highlighted, ct).ConfigureAwait(false);
                if (prepared == null) continue;
                list.Add(new TemplateEntry { Item = item, Color = prepared.Value.Color, Mask = prepared.Value.Mask });
                n++;
                if (n % 120 == 0)
                    progress?.Report($"Icons {w}x{h}: {n}/{items.Count}");
            }
            if (list.Count > 0) built[(w, h, highlighted)] = list;
        }

        lock (_gate)
        {
            DisposeTemplates();
            foreach (var kv in built) _templates[kv.Key] = kv.Value;
            _builtForCount = _templates.Count > 0 ? catalog.Items.Count : 0;
            _builtFormat = _templates.Count > 0 ? TemplateFormat : 0;
        }
    }

    /// <summary>
    /// Match the highlighted slot at its detected size. Smaller templates only when the
    /// exact size is weak (over-detected highlight box).
    /// </summary>
    public (ItemDefinition? Item, double Confidence, double SecondConfidence, int SlotW, int SlotH) MatchIconInRegion(
        Bitmap capture, int maxSlotW, int maxSlotH, bool highlighted = true, bool searchSmaller = false)
    {
        using var src = ToBgr(BitmapConverter.ToMat(capture));
        using var scaled = RescaleTo1080(src);

        ItemDefinition? bestItem = null;
        var best = 0.0;
        var second = 0.0;
        var bestW = maxSlotW;
        var bestH = maxSlotH;

        MatchSize(scaled, maxSlotW, maxSlotH, highlighted, rotate: false,
            ref bestItem, ref best, ref second, ref bestW, ref bestH);
        if (maxSlotW != maxSlotH)
        {
            MatchSize(scaled, maxSlotH, maxSlotW, highlighted, rotate: true,
                ref bestItem, ref best, ref second, ref bestW, ref bestH);
        }

        if (best >= 0.80 || !searchSmaller)
            return (bestItem, best, second, bestW, bestH);

        foreach (var (w, h) in CandidateSizes(maxSlotW, maxSlotH))
        {
            if ((w == maxSlotW && h == maxSlotH) || (w == maxSlotH && h == maxSlotW))
                continue;
            MatchSize(scaled, w, h, highlighted, rotate: false,
                ref bestItem, ref best, ref second, ref bestW, ref bestH);
            if (w == h) continue;
            MatchSize(scaled, h, w, highlighted, rotate: true,
                ref bestItem, ref best, ref second, ref bestW, ref bestH);
        }

        return (bestItem, best, second, bestW, bestH);
    }

    private void MatchSize(
        Mat scaled, int w, int h, bool highlighted, bool rotate,
        ref ItemDefinition? bestItem, ref double best, ref double second,
        ref int bestW, ref int bestH)
    {
        var (item, conf, sec) = MatchOnceSliding(scaled, w, h, highlighted, rotate);
        Consider(item, conf, sec, w, h, ref bestItem, ref best, ref second, ref bestW, ref bestH);
    }

    private static void Consider(
        ItemDefinition? item, double conf, double sec, int w, int h,
        ref ItemDefinition? bestItem, ref double best, ref double second,
        ref int bestW, ref int bestH)
    {
        if (item == null || conf <= 0) return;
        if (conf > best)
        {
            second = Math.Max(best, sec);
            best = conf;
            bestItem = item;
            bestW = w;
            bestH = h;
        }
        else if (conf > second)
        {
            second = conf;
        }
    }

    private static IEnumerable<(int W, int H)> CandidateSizes(int maxW, int maxH)
    {
        var seen = new HashSet<(int, int)>();
        foreach (var (w, h) in new[] { (maxW, maxH), (1, 1), (2, 1), (1, 2), (2, 2) })
        {
            if (w < 1 || h < 1 || w > maxW || h > maxH) continue;
            if (!seen.Add((w, h))) continue;
            yield return (w, h);
        }
    }

    private (ItemDefinition? Item, double Confidence, double SecondConfidence) MatchOnceSliding(
        Mat source1080, int slotW, int slotH, bool highlighted, bool rotate)
    {
        List<TemplateEntry>? entries;
        lock (_gate)
        {
            if (!_templates.TryGetValue((slotW, slotH, highlighted), out entries) || entries.Count == 0)
                return (null, 0, 0);
        }

        using var oriented = rotate ? RotateCcw(source1080) : null;
        var sampleSrc = oriented ?? source1080;
        using var prepared = PrepareCapture(sampleSrc, slotW, slotH, highlighted);
        using var padded = PadForSearch(prepared, prepared.Width, prepared.Height, highlighted);

        ItemDefinition? bestItem = null;
        var best = 0.0;
        var second = 0.0;
        var sync = new object();

        Parallel.ForEach(entries, entry =>
        {
            if (entry.Color.Width > padded.Width || entry.Color.Height > padded.Height) return;
            var score = MaskedSqDiffScore(padded, entry.Color, entry.Mask);
            lock (sync)
            {
                if (score > best)
                {
                    second = best;
                    best = score;
                    bestItem = entry.Item;
                }
                else if (score > second)
                {
                    second = score;
                }
            }
        });

        return (bestItem, best, second);
    }

    public IReadOnlyList<(ItemDefinition Item, double Score, double SecondScore)> MatchTopCandidates(
        Bitmap capture, int slotW, int slotH, bool highlighted = true, int max = 8)
    {
        List<TemplateEntry>? entries;
        lock (_gate)
        {
            if (!_templates.TryGetValue((slotW, slotH, highlighted), out entries) || entries.Count == 0)
                return [];
        }

        using var src = ToBgr(BitmapConverter.ToMat(capture));
        using var scaled = RescaleTo1080(src);
        using var prepared = PrepareCapture(scaled, slotW, slotH, highlighted);
        using var padded = PadForSearch(prepared, prepared.Width, prepared.Height, highlighted);

        var scores = new List<(ItemDefinition Item, double Score)>();
        foreach (var entry in entries)
        {
            if (entry.Color.Width > padded.Width || entry.Color.Height > padded.Height) continue;
            scores.Add((entry.Item, MaskedSqDiffScore(padded, entry.Color, entry.Mask)));
        }

        var ordered = scores.OrderByDescending(s => s.Score).Take(max).ToList();
        var result = new List<(ItemDefinition, double, double)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var second = i + 1 < ordered.Count ? ordered[i + 1].Score : 0;
            result.Add((ordered[i].Item, ordered[i].Score, second));
        }
        return result;
    }

    /// <summary>Score only these catalog ids — used when OCR finds a shared short name (M882 round vs pack).</summary>
    public (ItemDefinition? Item, double Confidence, double SecondConfidence) MatchAmong(
        Bitmap capture, IReadOnlyCollection<string> itemIds, int slotW, int slotH, bool highlighted)
    {
        if (itemIds.Count == 0) return (null, 0, 0);
        var idSet = new HashSet<string>(itemIds, StringComparer.OrdinalIgnoreCase);

        List<TemplateEntry> subset;
        lock (_gate)
        {
            if (!_templates.TryGetValue((slotW, slotH, highlighted), out var entries) || entries.Count == 0)
                return (null, 0, 0);
            subset = entries.Where(e => idSet.Contains(e.Item.Id)).ToList();
        }

        if (subset.Count == 0) return (null, 0, 0);
        if (subset.Count == 1) return (subset[0].Item, 0.92, 0);

        using var src = ToBgr(BitmapConverter.ToMat(capture));
        using var scaled = RescaleTo1080(src);
        using var prepared = PrepareCapture(scaled, slotW, slotH, highlighted);
        using var padded = PadForSearch(prepared, prepared.Width, prepared.Height, highlighted);

        ItemDefinition? bestItem = null;
        var best = 0.0;
        var second = 0.0;
        foreach (var entry in subset)
        {
            if (entry.Color.Width > padded.Width || entry.Color.Height > padded.Height) continue;
            var score = MaskedSqDiffScore(padded, entry.Color, entry.Mask);
            if (score > best)
            {
                second = best;
                best = score;
                bestItem = entry.Item;
            }
            else if (score > second)
            {
                second = score;
            }
        }

        return (bestItem, best, second);
    }

    /// <summary>Inventory cell at 1080p: (n × 63) + 1.</summary>
    private static (int W, int H) SlotPixelSize(int slotW, int slotH) =>
        (slotW * 63 + 1, slotH * 63 + 1);

    private static Mat RescaleTo1080(Mat src)
    {
        var slot = ScreenCapture.ItemSlotPx();
        if (slot == 63) return src.Clone();
        var scale = 63.0 / slot;
        var dst = new Mat();
        Cv2.Resize(src, dst,
            new OpenCvSharp.Size(
                Math.Max(8, (int)Math.Round(src.Width * scale)),
                Math.Max(8, (int)Math.Round(src.Height * scale))),
            interpolation: scale < 1 ? InterpolationFlags.Area : InterpolationFlags.Linear);
        return dst;
    }

    private static Mat PadForSearch(Mat src, int templateW, int templateH, bool highlighted)
    {
        var needW = Math.Max(src.Width, templateW + 4);
        var needH = Math.Max(src.Height, templateH + 4);
        if (needW == src.Width && needH == src.Height) return src.Clone();

        var bg = highlighted ? new Scalar(255, 255, 255) : new Scalar(26, 26, 26);
        var dst = new Mat(new OpenCvSharp.Size(needW, needH), src.Type(), bg);
        var x = Math.Max(0, (needW - src.Width) / 2);
        var y = Math.Max(0, (needH - src.Height) / 2);
        using var roi = new Mat(dst, new Rect(x, y, src.Width, src.Height));
        src.CopyTo(roi);
        return dst;
    }

    private static Mat RotateCcw(Mat src)
    {
        var dst = new Mat();
        Cv2.Rotate(src, dst, RotateFlags.Rotate90Counterclockwise);
        return dst;
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

    private static Mat PrepareCapture(Mat bgr, int slotW, int slotH, bool highlighted)
    {
        var (tw, th) = SlotPixelSize(slotW, slotH);
        using var resized = new Mat();
        if (bgr.Width != tw || bgr.Height != th)
            Cv2.Resize(bgr, resized, new OpenCvSharp.Size(tw, th), interpolation: InterpolationFlags.Area);
        else
            bgr.CopyTo(resized);

        var (color, mask) = PrepareSample(resized, tw, th, highlighted, buildMask: false);
        mask.Dispose();
        return color;
    }

    /// <summary>
    /// Same prep on catalog icons and on the screenshot: mask the in-cell name and
    /// durability bar, ignore slot chrome (dark wiki background vs white highlight).
    /// </summary>
    private static (Mat Color, Mat Mask) PrepareSample(Mat bgr, int targetW, int targetH, bool highlighted, bool buildMask)
    {
        using var composed = CompositeOnBackground(bgr, targetW, targetH, highlighted);
        var color = new Mat();
        if (composed.Width != targetW || composed.Height != targetH)
            Cv2.Resize(composed, color, new OpenCvSharp.Size(targetW, targetH), interpolation: InterpolationFlags.Area);
        else
            composed.CopyTo(color);

        var barH = Math.Max(3, targetH / 6);
        var labelH = Math.Max(3, targetH / 4);
        var mask = new Mat(targetH, targetW, MatType.CV_8UC1, Scalar.All(255));
        Cv2.Rectangle(mask, new Rect(0, 0, targetW, labelH), Scalar.All(0), -1);
        Cv2.Rectangle(mask, new Rect(0, targetH - barH, targetW, barH), Scalar.All(0), -1);
        if (!buildMask)
        {
            mask.Dispose();
            mask = new Mat();
        }

        Cv2.Rectangle(color, new Rect(0, 0, targetW, labelH), Scalar.Black, -1);
        Cv2.Rectangle(color, new Rect(0, targetH - barH, targetW, barH), Scalar.Black, -1);

        if (highlighted)
            DrawGridBorder(color);

        return (color, mask);
    }

    private static Mat CompositeOnBackground(Mat icon, int targetW, int targetH, bool highlighted)
    {
        var bgColor = highlighted ? new Scalar(255, 255, 255) : new Scalar(26, 26, 26);
        var bg = new Mat(new OpenCvSharp.Size(targetW, targetH), MatType.CV_8UC3, bgColor);

        if (icon.Width <= targetW && icon.Height <= targetH)
        {
            var x = (targetW - icon.Width) / 2;
            var y = (targetH - icon.Height) / 2;
            using var roi = new Mat(bg, new Rect(x, y, icon.Width, icon.Height));
            icon.CopyTo(roi);
        }
        else
        {
            using var resized = new Mat();
            Cv2.Resize(icon, resized, new OpenCvSharp.Size(targetW, targetH), interpolation: InterpolationFlags.Area);
            resized.CopyTo(bg);
        }

        return bg;
    }

    private static void DrawGridBorder(Mat bgr)
    {
        // In-game / wiki icon grid: Vec3b BGR (84, 81, 73).
        var c = new Scalar(84, 81, 73);
        Cv2.Rectangle(bgr, new Rect(0, 0, bgr.Width, bgr.Height), c, 1);
    }

    /// <summary>Masked SQDIFF — compare item art only, ignore slot chrome / name / durability.</summary>
    private static double MaskedSqDiffScore(Mat sample, Mat template, Mat mask)
    {
        if (mask.Empty() || Cv2.CountNonZero(mask) < 24)
            return SqDiffScore(sample, template);

        try
        {
            using var result = new Mat();
            Cv2.MatchTemplate(sample, template, result, TemplateMatchModes.SqDiff, mask);
            Cv2.MinMaxLoc(result, out var minVal, out _, out _, out _);
            var n = Cv2.CountNonZero(mask) * Math.Max(1, template.Channels());
            var norm = minVal / (n * 255.0 * 255.0);
            return 1.0 - Math.Sqrt(Math.Clamp(norm, 0, 1));
        }
        catch
        {
            return SqDiffScore(sample, template);
        }
    }

    private static double SqDiffScore(Mat sample, Mat template)
    {
        using var result = new Mat();
        Cv2.MatchTemplate(sample, template, result, TemplateMatchModes.SqDiffNormed);
        Cv2.MinMaxLoc(result, out var minVal, out _, out _, out _);
        return 1.0 - minVal;
    }

    private async Task<(Mat Color, Mat Mask)?> LoadTemplateAsync(ItemDefinition item, bool highlighted, CancellationToken ct)
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

            using var color = Cv2.ImRead(path, ImreadModes.Color);
            if (color.Empty()) return null;

            var (tw, th) = SlotPixelSize(item.Width, item.Height);
            using var resized = new Mat();
            Cv2.Resize(color, resized, new OpenCvSharp.Size(tw, th), interpolation: InterpolationFlags.Area);
            var prepared = PrepareSample(resized, tw, th, highlighted, buildMask: true);
            return prepared;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => ResetIndex();
}

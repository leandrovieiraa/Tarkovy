using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Tarkovy.Models;

namespace Tarkovy.Services;

internal static class TooltipScanner
{
    /// <summary>
    /// Tilda passive scan: isolate the hover tooltip as pixels that vanished after the click,
    /// plus dark rounded boxes already visible in the pre-click frame.
    /// </summary>
    public static List<(string Label, Bitmap Bmp)> IsolateFromFrames(
        Bitmap before, Bitmap after, int localX, int localY)
    {
        var list = new List<(string, Bitmap)>();
        foreach (var box in ExtractDarkBoxes(before, localX, localY))
            list.Add(("tooltip-box", box));

        var diffCrop = IsolateVanishedRegion(before, after, localX, localY);
        if (diffCrop != null)
            list.Add(("tooltip-diff", diffCrop));

        return list;
    }

    /// <summary>
    /// Grab the hover tooltip as an isolated dark box (not a 360px strip that also
    /// contains the neighbor's in-cell label).
    /// </summary>
    public static List<(string Label, Bitmap Bmp)> CaptureNow(int clickX, int clickY)
    {
        var s = ScreenCapture.GameScale();
        return CaptureDarkBoxes(
            clickX, clickY,
            (int)Math.Round(260 * s), (int)Math.Round(72 * s),
            (int)Math.Round(64 * s), (int)Math.Round(58 * s),
            "tooltip-box");
    }

    /// <summary>Second-chance tooltip: still dark boxes only — never OCR the raw stash strip.</summary>
    public static List<(string Label, Bitmap Bmp)> CaptureTight(int clickX, int clickY)
    {
        var s = ScreenCapture.GameScale();
        return CaptureDarkBoxes(
            clickX, clickY,
            (int)Math.Round(220 * s), (int)Math.Round(48 * s),
            (int)Math.Round(52 * s), (int)Math.Round(42 * s),
            "screen-tight");
    }

    private static List<(string Label, Bitmap Bmp)> CaptureDarkBoxes(
        int clickX, int clickY, int w, int h, int dx, int dy, string label)
    {
        var left = clickX - dx;
        var top = clickY - dy;
        using var region = ScreenCapture.CaptureRegion(left, top, w, h);
        var localX = clickX - left;
        var localY = clickY - top;
        var list = new List<(string, Bitmap)>();
        foreach (var box in ExtractDarkBoxes(region, localX, localY))
            list.Add((label, box));
        return list;
    }

    public static async Task<(ItemDefinition? Item, List<OcrDebugLine> Lines)> TryMatchCapturedAsync(
        IReadOnlyList<(string Label, Bitmap Bmp)> strips, ItemCatalog catalog, List<OcrDebugLine>? lines)
    {
        lines ??= [];
        ItemDefinition? best = null;
        var bestScore = double.MaxValue;

        for (var i = 0; i < strips.Count; i++)
        {
            var (label, crop) = strips[i];
            try
            {
                var text = await OcrHelper.ReadTooltipTextAsync(crop).ConfigureAwait(false);
                var line = new OcrDebugLine { Crop = label, RawText = text };
                lines.Add(line);

                if (string.IsNullOrWhiteSpace(text)) continue;

                var (item, score) = await MatchTooltipTextAsync(catalog, text).ConfigureAwait(false);
                score += StripPriorityPenalty(label);
                line.MatchedItemId = item?.Id;
                line.MatchedShortName = item != null ? ItemDisplayNames.Name(item) : null;

                if (item == null || score >= bestScore) continue;
                bestScore = score;
                best = item;
            }
            finally
            {
                crop.Dispose();
            }
        }

        return (best, lines);
    }

    public static void DisposeStrips(IReadOnlyList<(string Label, Bitmap Bmp)> strips, int from = 0)
    {
        for (var i = from; i < strips.Count; i++)
            strips[i].Bmp.Dispose();
    }

    private static async Task<(ItemDefinition? Item, double Score)> MatchTooltipTextAsync(
        ItemCatalog catalog, string text)
    {
        if (IsInventoryChrome(text)) return (null, 1);

        var (item, score) = catalog.MatchByTooltip(text);
        if (item != null) return (item, score);

        // Online lookup only for longer, mostly alphabetic tooltips (skip OCR garbage).
        var letters = text.Count(char.IsLetter);
        if (letters >= 8 && text.Length >= 10 && !text.Contains("ITEM LENS", StringComparison.OrdinalIgnoreCase))
        {
            var id = await ItemLocalizedNames.LookupItemIdOnlineAsync(text).ConfigureAwait(false);
            if (id != null)
            {
                var found = catalog.FindById(id);
                if (found != null) return (found, 0.08);
            }
        }

        return (null, score);
    }

    private static bool IsInventoryChrome(string text)
    {
        var n = text.ToUpperInvariant();
        return n.Contains("SLOT", StringComparison.Ordinal)
               || n.Contains("ORGAN", StringComparison.Ordinal)
               || n.Contains("STASH", StringComparison.Ordinal)
               || n.Contains("ITEM LENS", StringComparison.Ordinal)
               || n.Contains("EQUIPAMENTO", StringComparison.Ordinal)
               || n.Contains("BOLSOS", StringComparison.Ordinal);
    }

    private static double StripPriorityPenalty(string label) => label switch
    {
        "tooltip-box" => 0,
        "tooltip-diff" => 0.01,
        "screen-tight" => 0.02,
        _ => 0.05
    };

    /// <summary>Black hover tooltip (white text, thin border) near the click.</summary>
    private static List<Bitmap> ExtractDarkBoxes(Bitmap region, int localX, int localY)
    {
        var list = new List<Bitmap>();
        if (region.Width < 16 || region.Height < 16) return list;

        using var bgr = BitmapConverter.ToMat(region);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV_FULL);
        using var dark = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(255, 90, 48), dark);

        var slot = ScreenCapture.ItemSlotPx();
        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 3));
        Cv2.MorphologyEx(dark, dark, MorphTypes.Close, k);

        Cv2.FindContours(dark, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var ranked = contours
            .Select(Cv2.BoundingRect)
            .Where(b => b.Width is >= 36 and <= 420 && b.Height is >= 14 and <= 52)
            .Select(b =>
            {
                var cx = b.X + b.Width / 2;
                var cy = b.Y + b.Height / 2;
                var dx = cx - localX;
                var dy = cy - localY;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                return (Rect: b, Dist: dist, Above: cy <= localY + slot / 4);
            })
            .Where(t => t.Dist < slot * 5 && t.Above)
            .OrderBy(t => t.Dist)
            .Take(1)
            .ToList();

        foreach (var (rect, _, _) in ranked)
        {
            var pad = 2;
            var x = Math.Max(0, rect.X - pad);
            var y = Math.Max(0, rect.Y - pad);
            var w = Math.Min(region.Width - x, rect.Width + pad * 2);
            var h = Math.Min(region.Height - y, rect.Height + pad * 2);
            list.Add(ScreenCapture.Crop(region, x, y, w, h));
        }

        return list;
    }

    /// <summary>
    /// Tilda: screenshot before tooltip change, subtract after, crop the vanished blob from the before frame.
    /// </summary>
    private static Bitmap? IsolateVanishedRegion(Bitmap before, Bitmap after, int localX, int localY)
    {
        if (before.Width != after.Width || before.Height != after.Height) return null;

        using var a = BitmapConverter.ToMat(before);
        using var b = BitmapConverter.ToMat(after);
        using var diff = new Mat();
        Cv2.Absdiff(a, b, diff);
        using var gray = new Mat();
        Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, 16, 255, ThresholdTypes.Binary);

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, k);

        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var slot = ScreenCapture.ItemSlotPx();

        Rect? best = null;
        var bestDist = double.MaxValue;
        foreach (var c in contours)
        {
            var r = Cv2.BoundingRect(c);
            if (r.Width < 36 || r.Height < 12 || r.Height > slot) continue;
            var cx = r.X + r.Width / 2.0;
            var cy = r.Y + r.Height / 2.0;
            if (cy > localY + 8) continue; // tooltip sits on/above the item, not below
            var dist = Math.Abs(cx - localX) + Math.Abs(cy - localY);
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = r;
        }

        if (best == null) return null;
        var box = best.Value;
        var x = Math.Max(0, box.X - 2);
        var y = Math.Max(0, box.Y - 2);
        var w = Math.Min(before.Width - x, box.Width + 4);
        var h = Math.Min(before.Height - y, box.Height + 4);
        if (w < 24 || h < 10) return null;
        return ScreenCapture.Crop(before, x, y, w, h);
    }
}

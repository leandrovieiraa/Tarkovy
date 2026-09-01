using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Tarkovy.Models;

namespace Tarkovy.Services;

internal readonly record struct IconMatch(
    ItemDefinition? Item,
    double Confidence,
    double SecondConfidence,
    int SlotW,
    int SlotH,
    bool Rotated);

/// <summary>Masked template match of a stash cell against <see cref="IconBank"/> (1080p space).</summary>
internal static class IconLocator
{
    public static IconMatch Match(
        Bitmap capture, int slotW, int slotH, IconBank bank, bool tryRotated, bool searchSmaller = false)
    {
        using var src = ToBgr(BitmapConverter.ToMat(capture));
        using var scaled = RescaleTo1080(src);
        return MatchMat(scaled, slotW, slotH, bank, tryRotated, searchSmaller);
    }

    public static IReadOnlyList<(ItemDefinition Item, double Score, double SecondScore)> Top(
        Bitmap capture, int slotW, int slotH, IconBank bank, int max)
    {
        using var src = ToBgr(BitmapConverter.ToMat(capture));
        using var scaled = RescaleTo1080(src);
        using var sample = FitSlot(scaled, slotW, slotH);
        var entries = bank.ForSize(slotW, slotH);
        if (entries.Count == 0 || sample.Empty()) return [];

        var scores = new List<(ItemDefinition Item, double Score)>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.Color.Width > sample.Width + 4 || entry.Color.Height > sample.Height + 4) continue;
            scores.Add((entry.Item, ScoreAgainst(sample, entry)));
        }

        var ordered = scores.OrderByDescending(s => s.Score).Take(max).ToList();
        var result = new List<(ItemDefinition, double, double)>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var second = i + 1 < ordered.Count ? ordered[i + 1].Score : 0;
            result.Add((ordered[i].Item, ordered[i].Score, second));
        }
        return result;
    }

    private static IconMatch MatchMat(
        Mat scaled1080, int slotW, int slotH, IconBank bank, bool tryRotated, bool searchSmaller)
    {
        var best = MatchSize(scaled1080, slotW, slotH, bank, rotated: false);

        if (tryRotated && slotW != slotH)
        {
            using var rotated = new Mat();
            Cv2.Rotate(scaled1080, rotated, RotateFlags.Rotate90Counterclockwise);
            var alt = MatchSize(rotated, slotH, slotW, bank, rotated: true);
            if (alt.Confidence > best.Confidence)
                best = alt;
        }

        if (!searchSmaller || best.Item == null || best.Confidence >= LensConfig.IconConfirmHighlight)
            return best;

        foreach (var (w, h) in SmallerSizes(slotW, slotH))
        {
            var inner = MatchSize(scaled1080, w, h, bank, rotated: false);
            if (inner.Confidence > best.Confidence)
                best = inner;
        }

        return best;
    }

    private static IconMatch MatchSize(Mat scaled1080, int slotW, int slotH, IconBank bank, bool rotated)
    {
        var entries = bank.ForSize(slotW, slotH);
        if (entries.Count == 0) return new IconMatch(null, 0, 0, slotW, slotH, rotated);

        using var sample = FitSlot(scaled1080, slotW, slotH);

        ItemDefinition? bestItem = null;
        var best = 0.0;
        var second = 0.0;
        var sync = new object();

        Parallel.ForEach(entries, entry =>
        {
            if (entry.Color.Width > sample.Width + 8 || entry.Color.Height > sample.Height + 8) return;
            var score = ScoreAgainst(sample, entry);
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

        return new IconMatch(bestItem, best, second, slotW, slotH, rotated);
    }

    private static IEnumerable<(int W, int H)> SmallerSizes(int maxW, int maxH)
    {
        var seen = new HashSet<(int, int)> { (maxW, maxH) };
        foreach (var (w, h) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
        {
            if (w > maxW || h > maxH) continue;
            if (!seen.Add((w, h))) continue;
            yield return (w, h);
        }
    }

    private static Mat FitSlot(Mat src, int slotW, int slotH)
    {
        var (tw, th) = LensConfig.SlotPixelSize1080(slotW, slotH);
        var dst = new Mat();
        if (src.Width == tw && src.Height == th)
        {
            src.CopyTo(dst);
            return dst;
        }

        Cv2.Resize(src, dst, new OpenCvSharp.Size(tw, th),
            interpolation: src.Width * src.Height > tw * th ? InterpolationFlags.Area : InterpolationFlags.Linear);
        return dst;
    }

    private static Mat RescaleTo1080(Mat src)
    {
        var slot = LensConfig.SlotPx;
        if (slot == LensConfig.BaseSlotPx) return src.Clone();
        var scale = LensConfig.BaseSlotPx / (double)slot;
        var dst = new Mat();
        Cv2.Resize(src, dst,
            new OpenCvSharp.Size(
                Math.Max(8, (int)Math.Round(src.Width * scale)),
                Math.Max(8, (int)Math.Round(src.Height * scale))),
            interpolation: scale < 1 ? InterpolationFlags.Area : InterpolationFlags.Linear);
        return dst;
    }

    private static double ScoreAgainst(Mat sample, IconTemplate entry)
    {
        using var hay = PadToFit(sample, entry.Color.Width, entry.Color.Height);
        if (entry.Color.Width > hay.Width || entry.Color.Height > hay.Height)
            return 0;

        try
        {
            if (!entry.Mask.Empty() && Cv2.CountNonZero(entry.Mask) >= 24)
            {
                using var result = new Mat();
                Cv2.MatchTemplate(hay, entry.Color, result, TemplateMatchModes.CCoeffNormed, entry.Mask);
                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);
                return Math.Clamp(maxVal, 0, 1);
            }
        }
        catch
        {
            // fall through
        }

        try
        {
            using var result = new Mat();
            Cv2.MatchTemplate(hay, entry.Color, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);
            return Math.Clamp(maxVal, 0, 1);
        }
        catch
        {
            return 0;
        }
    }

    private static Mat PadToFit(Mat src, int templateW, int templateH)
    {
        var needW = Math.Max(src.Width, templateW);
        var needH = Math.Max(src.Height, templateH);
        if (needW == src.Width && needH == src.Height) return src.Clone();

        var dst = new Mat(new OpenCvSharp.Size(needW, needH), src.Type(), new Scalar(26, 26, 26));
        var x = Math.Max(0, (needW - src.Width) / 2);
        var y = Math.Max(0, (needH - src.Height) / 2);
        using var roi = new Mat(dst, new Rect(x, y, src.Width, src.Height));
        src.CopyTo(roi);
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

    public static bool IsConfirmed(IconMatch match, bool highlighted = true)
    {
        if (match.Item == null) return false;
        var minConf = highlighted ? LensConfig.IconConfirmHighlight : LensConfig.IconConfirmNormal;
        var minMargin = highlighted ? LensConfig.IconMarginHighlight : LensConfig.IconMarginNormal;
        var margin = match.Confidence - match.SecondConfidence;
        if (match.Confidence < minConf) return false;
        if (margin < minMargin && match.Confidence < 0.93) return false;
        if (margin < LensConfig.IconInnerMargin && match.Confidence < 0.91)
            return false;
        return true;
    }
}

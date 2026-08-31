using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace Tarkovy.Services;

/// <summary>
/// Highlight crop via HSV plus Tilda-style frame diff.
/// HSV fill misses items that cover the slot (CMS, water); AbsDiff(before, after)
/// still sees the selection overlay appear after the click.
/// </summary>
internal static class HighlightCrop
{
    private static readonly (int W, int H)[] KnownSizes =
        [(1, 1), (1, 2), (2, 1), (2, 2), (2, 3), (3, 1), (3, 2), (4, 1), (4, 2)];

    /// <summary>
    /// Extract highlight crops: <paramref name="iconCrop"/> is exact slot size for template match;
    /// <paramref name="labelCrop"/> includes top padding for in-cell OCR ("Água", "CMS").
    /// </summary>
    public static bool TryExtract(
        Bitmap region, int localX, int localY,
        out int slotW, out int slotH, out Bitmap? iconCrop, out Bitmap? labelCrop,
        out string method,
        Bitmap? beforeRegion = null)
    {
        slotW = slotH = 0;
        iconCrop = labelCrop = null;
        method = "";

        if (region.Width < 16 || region.Height < 16) return false;

        var slot = ScreenCapture.ItemSlotPx();
        var scale = ScreenCapture.GameScale();

        using var after = BitmapConverter.ToMat(region);
        if (TryFindHighlightRect(after, localX, localY, slot, scale, out var raw, out method)
            && TryCropFromRect(region, raw, slot, localX, localY, out slotW, out slotH, out iconCrop, out labelCrop))
            return true;

        if (beforeRegion != null
            && beforeRegion.Width == region.Width
            && beforeRegion.Height == region.Height)
        {
            using var before = BitmapConverter.ToMat(beforeRegion);
            if (TryFindDiffRect(before, after, localX, localY, slot, out raw)
                && TryCropFromRect(region, raw, slot, localX, localY, out slotW, out slotH, out iconCrop, out labelCrop))
            {
                method = "frame-diff";
                return true;
            }
        }

        method = "";
        return false;
    }

    private static bool TryCropFromRect(
        Bitmap region, Rect raw, int slot, int localX, int localY,
        out int slotW, out int slotH, out Bitmap? iconCrop, out Bitmap? labelCrop)
    {
        slotW = slotH = 0;
        iconCrop = labelCrop = null;

        if (!TryInferSlotSize(raw.Width, raw.Height, slot, out slotW, out slotH))
            return false;

        // A 3×1 / 4×1 box on a 1-slot-tall row is almost always merged 1×1 ammo cells.
        if (slotH == 1 && slotW >= 3)
        {
            slotW = 1;
            slotH = 1;
        }

        var (pw, ph) = ScreenCapture.SlotPixelSize(slotW, slotH);

        // Prefer the click as the anchor so a slightly oversized box still snaps to the item.
        var cx = Math.Clamp(localX, raw.X, raw.X + Math.Max(0, raw.Width - 1));
        var cy = Math.Clamp(localY, raw.Y, raw.Y + Math.Max(0, raw.Height - 1));
        var ix = Math.Clamp(cx - pw / 2, 0, Math.Max(0, region.Width - pw));
        var iy = Math.Clamp(cy - ph / 2, 0, Math.Max(0, region.Height - ph));
        var iw = Math.Min(pw, region.Width - ix);
        var ih = Math.Min(ph, region.Height - iy);
        if (iw < slot / 2 || ih < slot / 2) return false;

        iconCrop = ScreenCapture.Crop(region, ix, iy, iw, ih);

        var padX = slot / 8;
        var padTop = slot / 4;
        var padBottom = slot / 2;
        var padRight = slot / 4;
        var lx = Math.Max(0, ix - padX);
        var ly = Math.Max(0, iy - padTop);
        var lw = Math.Min(region.Width - lx, iw + padX + padRight);
        var lh = Math.Min(region.Height - ly, ih + padTop + padBottom);
        labelCrop = ScreenCapture.Crop(region, lx, ly, lw, lh);
        return true;
    }

    private static bool TryFindHighlightRect(
        Mat bgr, int localX, int localY, int slot, double scale, out Rect raw, out string method)
    {
        raw = default;
        method = "";

        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV_FULL);

        // HSV_FULL: S 0–3, V 80–100 (tight selection fill).
        var ranges = new (Scalar Lo, Scalar Hi, string Name)[]
        {
            (new Scalar(0, 0, 80), new Scalar(255, 3, 100), "hsv-tight"),
            (new Scalar(0, 0, 70), new Scalar(255, 18, 160), "hsv-wide"),
            (new Scalar(0, 0, 190), new Scalar(255, 30, 255), "hsv-border"),
        };

        var k = Math.Max(2, (int)Math.Round(2 * scale));
        using var hKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(k * 3, 1));
        using var vKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(1, k * 3));
        var closeSize = Math.Max(3, slot / 12);
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(closeSize, closeSize));

        foreach (var (lo, hi, name) in ranges)
        {
            using var mask = new Mat();
            Cv2.InRange(hsv, lo, hi, mask);
            Cv2.Dilate(mask, mask, hKernel);
            Cv2.Dilate(mask, mask, vKernel);
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, closeKernel);
            if (!TryPickBoxAtClick(mask, localX, localY, slot, out raw)) continue;
            method = name;
            return true;
        }

        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        using var k2 = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
        foreach (var (thresh, name) in new[] { (185, "gray-185"), (200, "gray-200") })
        {
            using var bright = new Mat();
            Cv2.Threshold(gray, bright, thresh, 255, ThresholdTypes.Binary);
            Cv2.MorphologyEx(bright, bright, MorphTypes.Close, k2);
            Cv2.MorphologyEx(bright, bright, MorphTypes.Close, closeKernel);
            if (!TryPickBoxAtClick(bright, localX, localY, slot, out raw)) continue;
            method = name;
            return true;
        }

        return false;
    }

    /// <summary>Tilda passive scan: pixels that changed after the click = selection overlay.</summary>
    private static bool TryFindDiffRect(Mat before, Mat after, int localX, int localY, int slot, out Rect raw)
    {
        raw = default;
        using var diff = new Mat();
        Cv2.Absdiff(before, after, diff);
        using var gray = new Mat();
        Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, 18, 255, ThresholdTypes.Binary);

        var closeSize = Math.Max(3, slot / 12);
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(closeSize, closeSize));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, closeKernel);
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, openKernel);

        return TryPickBoxAtClick(mask, localX, localY, slot, out raw);
    }

    /// <summary>Merge overlapping boxes and keep the smallest one at the click.</summary>
    private static bool TryPickBoxAtClick(Mat mask, int localX, int localY, int slot, out Rect raw)
    {
        raw = default;
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var minSide = Math.Max(slot / 3, 20);
        var maxSide = slot * 7;
        var boxes = contours
            .Select(Cv2.BoundingRect)
            .Where(b => b.Width >= minSide && b.Height >= minSide
                        && b.Width <= maxSide && b.Height <= maxSide)
            .ToList();

        for (var b = 0; b < boxes.Count; b++)
        {
            for (var i = b + 1; i < boxes.Count; i++)
            {
                if (!Intersects(boxes[b], boxes[i])) continue;
                boxes[b] = Union(boxes[b], boxes[i]);
                boxes.RemoveAt(i);
                i = b;
            }
        }

        Rect? best = null;
        var bestScore = double.MaxValue;
        var pad = slot / 3;
        foreach (var bb in boxes)
        {
            var inside = localX >= bb.X && localX < bb.X + bb.Width
                         && localY >= bb.Y && localY < bb.Y + bb.Height;
            if (!inside)
            {
                var near = localX >= bb.X - pad && localX < bb.X + bb.Width + pad
                           && localY >= bb.Y - pad && localY < bb.Y + bb.Height + pad;
                if (!near) continue;
            }

            var area = bb.Width * (double)bb.Height;
            // Prefer the smallest box that still contains the click.
            var score = inside ? area : area + slot * slot * 4;
            if (score >= bestScore) continue;
            bestScore = score;
            best = bb;
        }

        if (best == null) return false;
        raw = best.Value;
        return true;
    }

    private static bool TryInferSlotSize(int width, int height, int slot, out int slotW, out int slotH)
    {
        slotW = Math.Clamp((int)Math.Round((width - 1.0) / slot), 1, 6);
        slotH = Math.Clamp((int)Math.Round((height - 1.0) / slot), 1, 6);

        var bestDist = double.MaxValue;
        var bestW = slotW;
        var bestH = slotH;
        foreach (var (kw, kh) in KnownSizes)
        {
            var ew = kw * slot + 1;
            var eh = kh * slot + 1;
            var d = Math.Abs(width - ew) + Math.Abs(height - eh);
            if (d >= bestDist) continue;
            bestDist = d;
            bestW = kw;
            bestH = kh;
        }

        if (bestDist <= slot * 1.35)
        {
            slotW = bestW;
            slotH = bestH;
            return true;
        }

        return slotW is >= 1 and <= 6 && slotH is >= 1 and <= 6
               && Math.Abs(width - (slotW * slot + 1)) + Math.Abs(height - (slotH * slot + 1)) <= slot * 1.6;
    }

    private static bool Intersects(Rect a, Rect b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    private static Rect Union(Rect a, Rect b)
    {
        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }
}

using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace Tarkovy.Services;

internal sealed class StashCell : IDisposable
{
    public int SlotW { get; init; }
    public int SlotH { get; init; }
    public required Bitmap Icon { get; init; }
    public Bitmap? Label { get; init; }
    public bool Highlighted { get; init; }
    public string Method { get; init; } = "";
    public Rect Bounds { get; init; }

    public void Dispose()
    {
        Icon.Dispose();
        if (Label != null && !ReferenceEquals(Label, Icon))
            Label.Dispose();
    }
}

/// <summary>
/// Locates the inventory cell under the cursor: HSV highlight, frame-diff, then grid snap.
/// </summary>
internal static class StashGrid
{
    private static readonly (int W, int H)[] KnownSizes =
        [(1, 1), (1, 2), (2, 1), (2, 2), (2, 3), (3, 1), (3, 2), (4, 1), (4, 2), (4, 3), (5, 2), (6, 2)];

    // Inventory chrome (BGR of RGB 89,100,100).
    private static readonly Scalar GridColor = new(100, 100, 89);

    public static StashCell? Locate(
        Bitmap region, Bitmap? beforeRegion, int localX, int localY, ItemScanDebugReport? debug)
    {
        if (region.Width < 16 || region.Height < 16) return null;

        var slot = LensConfig.SlotPx;
        var scale = LensConfig.Scale;
        var boxes = new List<(Rect Rect, string Method)>();

        using var after = BitmapConverter.ToMat(region);
        CollectHighlightRects(after, localX, localY, slot, scale, boxes);

        if (beforeRegion != null
            && beforeRegion.Width == region.Width
            && beforeRegion.Height == region.Height)
        {
            using var before = BitmapConverter.ToMat(beforeRegion);
            using var diffMask = DiffMask(before, after, slot);
            foreach (var rect in BoxesAtClick(diffMask, localX, localY, slot))
                boxes.Add((rect, "frame-diff"));
        }

        var picked = PickBestSlotRect(boxes, slot, localX, localY);
        if (picked != null
            && TryCrop(region, picked.Value.Rect, slot, localX, localY, highlighted: true, picked.Value.Method, out var cell))
        {
            debug?.Notes.Add(
                $"stash-grid ({cell!.Method}): {cell.SlotW}x{cell.SlotH} {cell.Icon.Width}x{cell.Icon.Height}px from {boxes.Count} boxes");
            return cell;
        }

        if (TrySnapOneByOne(region, after, localX, localY, slot, out cell))
        {
            debug?.Notes.Add($"stash-grid (grid-snap 1x1): {cell!.Icon.Width}x{cell.Icon.Height}px");
            return cell;
        }

        debug?.Notes.Add("stash-grid: highlight not found.");
        return null;
    }

    /// <summary>Slot-aligned crops of a given size that contain the click (for multi-size icon match).</summary>
    public static List<Bitmap> CropsAtClick(Bitmap region, int localX, int localY, int slotW, int slotH)
    {
        var list = new List<Bitmap>();
        var (pw, ph) = LensConfig.SlotPixelSize(slotW, slotH);
        if (pw > region.Width || ph > region.Height) return list;

        var seen = new HashSet<string>();
        void Add(int x, int y)
        {
            x = Math.Clamp(x, 0, region.Width - pw);
            y = Math.Clamp(y, 0, region.Height - ph);
            if (!seen.Add($"{x}:{y}")) return;
            list.Add(ScreenCapture.Crop(region, x, y, pw, ph));
        }

        Add(localX - pw / 2, localY - ph / 2);

        var slot = LensConfig.SlotPx;
        var phaseX = ((localX % slot) + slot) % slot;
        var phaseY = ((localY % slot) + slot) % slot;
        var col = (int)Math.Floor((localX - phaseX) / (double)slot);
        var row = (int)Math.Floor((localY - phaseY) / (double)slot);
        for (var dw = 0; dw < slotW; dw++)
        for (var dh = 0; dh < slotH; dh++)
        {
            var x = (col - dw) * slot + phaseX;
            var y = (row - dh) * slot + phaseY;
            if (localX >= x && localX < x + pw && localY >= y && localY < y + ph)
                Add(x, y);
        }

        return list;
    }

    private static bool TryCrop(
        Bitmap region, Rect raw, int slot, int localX, int localY,
        bool highlighted, string method, out StashCell? cell)
    {
        cell = null;
        if (!TryInferSlotSize(raw.Width, raw.Height, slot, out var slotW, out var slotH))
            return false;

        if (slotH == 1 && slotW >= 3)
        {
            slotW = 1;
            slotH = 1;
        }

        var (pw, ph) = LensConfig.SlotPixelSize(slotW, slotH);
        var snapped = SnapToGrid(raw, slot, localX, localY, pw, ph, region.Width, region.Height);

        var ix = snapped.X;
        var iy = snapped.Y;
        var iw = Math.Min(pw, region.Width - ix);
        var ih = Math.Min(ph, region.Height - iy);
        if (iw < slot / 2 || ih < slot / 2) return false;

        var icon = ScreenCapture.Crop(region, ix, iy, iw, ih);
        var padX = slot / 8;
        var padTop = slot / 4;
        var padBottom = slot / 2;
        var padRight = slot / 4;
        var lx = Math.Max(0, ix - padX);
        var ly = Math.Max(0, iy - padTop);
        var lw = Math.Min(region.Width - lx, iw + padX + padRight);
        var lh = Math.Min(region.Height - ly, ih + padTop + padBottom);
        var label = ScreenCapture.Crop(region, lx, ly, lw, lh);

        cell = new StashCell
        {
            SlotW = slotW,
            SlotH = slotH,
            Icon = icon,
            Label = label,
            Highlighted = highlighted,
            Method = method,
            Bounds = new Rect(ix, iy, iw, ih)
        };
        return true;
    }

    private static Rect SnapToGrid(Rect raw, int slot, int localX, int localY, int pw, int ph, int maxW, int maxH)
    {
        var cx = Math.Clamp(localX, raw.X, raw.X + Math.Max(0, raw.Width - 1));
        var cy = Math.Clamp(localY, raw.Y, raw.Y + Math.Max(0, raw.Height - 1));
        var ix = Math.Clamp(cx - pw / 2, 0, Math.Max(0, maxW - pw));
        var iy = Math.Clamp(cy - ph / 2, 0, Math.Max(0, maxH - ph));

        var phaseX = BestPhase(raw.X, slot);
        var phaseY = BestPhase(raw.Y, slot);
        var gx = Math.Clamp(phaseX + (int)Math.Round((cx - phaseX - pw / 2.0) / slot) * slot, 0, Math.Max(0, maxW - pw));
        var gy = Math.Clamp(phaseY + (int)Math.Round((cy - phaseY - ph / 2.0) / slot) * slot, 0, Math.Max(0, maxH - ph));
        if (Math.Abs(gx - raw.X) <= slot / 2 && Math.Abs(gy - raw.Y) <= slot / 2)
            return new Rect(gx, gy, pw, ph);

        return new Rect(ix, iy, pw, ph);
    }

    private static int BestPhase(int origin, int slot)
    {
        var p = origin % slot;
        if (p < 0) p += slot;
        return p;
    }

    private static bool TrySnapOneByOne(
        Bitmap region, Mat bgr, int localX, int localY, int slot, out StashCell? cell)
    {
        cell = null;
        var (pw, ph) = LensConfig.SlotPixelSize(1, 1);
        var phase = DetectGridPhase(bgr, slot);
        var col = (int)Math.Floor((localX - phase.X + slot * 0.5) / (double)slot);
        var row = (int)Math.Floor((localY - phase.Y + slot * 0.5) / (double)slot);
        var ix = Math.Clamp(phase.X + col * slot, 0, Math.Max(0, region.Width - pw));
        var iy = Math.Clamp(phase.Y + row * slot, 0, Math.Max(0, region.Height - ph));
        var icon = ScreenCapture.Crop(region, ix, iy, Math.Min(pw, region.Width - ix), Math.Min(ph, region.Height - iy));
        cell = new StashCell
        {
            SlotW = 1,
            SlotH = 1,
            Icon = icon,
            Label = (Bitmap)icon.Clone(),
            Highlighted = false,
            Method = "grid-snap",
            Bounds = new Rect(ix, iy, icon.Width, icon.Height)
        };
        return true;
    }

    private static (int X, int Y) DetectGridPhase(Mat bgr, int slot)
    {
        using var diff = new Mat();
        Cv2.Absdiff(bgr, new Scalar(GridColor.Val0, GridColor.Val1, GridColor.Val2), diff);
        using var gray = new Mat();
        Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, 28, 255, ThresholdTypes.BinaryInv);

        var bestX = 0;
        var bestY = 0;
        var bestXs = 0;
        var bestYs = 0;
        var step = Math.Max(1, slot / 8);
        for (var p = 0; p < slot; p += step)
        {
            var sx = 0;
            var sy = 0;
            for (var x = p; x < mask.Width; x += slot)
            {
                var col = mask.Col(Math.Clamp(x, 0, mask.Width - 1));
                sx += Cv2.CountNonZero(col);
                col.Dispose();
            }
            for (var y = p; y < mask.Height; y += slot)
            {
                var row = mask.Row(Math.Clamp(y, 0, mask.Height - 1));
                sy += Cv2.CountNonZero(row);
                row.Dispose();
            }
            if (sx > bestXs) { bestXs = sx; bestX = p; }
            if (sy > bestYs) { bestYs = sy; bestY = p; }
        }

        return (bestX, bestY);
    }

    private static void CollectHighlightRects(
        Mat bgr, int localX, int localY, int slot, double scale, List<(Rect Rect, string Method)> into)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV_FULL);

        var ranges = new (Scalar Lo, Scalar Hi, string Name)[]
        {
            (new Scalar(0, 0, 80), new Scalar(255, 3, 100), "hsv-tight"),
            (new Scalar(20, 50, 100), new Scalar(40, 255, 255), "hsv-select"),
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
            foreach (var rect in BoxesAtClick(mask, localX, localY, slot))
                into.Add((rect, name));
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
            foreach (var rect in BoxesAtClick(bright, localX, localY, slot))
                into.Add((rect, name));
        }
    }

    private static Mat DiffMask(Mat before, Mat after, int slot)
    {
        var mask = new Mat();
        using var diff = new Mat();
        Cv2.Absdiff(before, after, diff);
        using var gray = new Mat();
        Cv2.CvtColor(diff, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(gray, mask, 18, 255, ThresholdTypes.Binary);

        var closeSize = Math.Max(3, slot / 12);
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(closeSize, closeSize));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, closeKernel);
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, openKernel);
        return mask;
    }

    /// <summary>
    /// When 1×1 and 2×3 both snap to the grid, keep the larger box (CMS fill looks like a tiny inner highlight).
    /// </summary>
    private static (Rect Rect, string Method)? PickBestSlotRect(
        List<(Rect Rect, string Method)> boxes, int slot, int localX, int localY)
    {
        (Rect Rect, string Method)? best = null;
        var bestDist = double.MaxValue;
        var bestArea = 0;
        foreach (var (rect, method) in boxes)
        {
            if (!NearClick(rect, localX, localY, slot)) continue;
            if (!TryInferSlotSize(rect.Width, rect.Height, slot, out var sw, out var sh))
                continue;
            var ew = sw * slot + 1;
            var eh = sh * slot + 1;
            var dist = Math.Abs(rect.Width - ew) + Math.Abs(rect.Height - eh);
            var area = rect.Width * rect.Height;
            if (dist < bestDist - slot * 0.25
                || (Math.Abs(dist - bestDist) <= slot * 0.25 && area > bestArea))
            {
                bestDist = dist;
                bestArea = area;
                best = (rect, method);
            }
        }

        return best;
    }

    private static List<Rect> BoxesAtClick(Mat mask, int localX, int localY, int slot)
    {
        var list = new List<Rect>();
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

        foreach (var bb in boxes)
        {
            if (NearClick(bb, localX, localY, slot))
                list.Add(bb);
        }

        return list;
    }

    private static bool NearClick(Rect bb, int localX, int localY, int slot)
    {
        var pad = slot / 3;
        return localX >= bb.X - pad && localX < bb.X + bb.Width + pad
               && localY >= bb.Y - pad && localY < bb.Y + bb.Height + pad;
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

using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace Tarkovy.Services;

/// <summary>Finds the inspect-window title strip (dark panel + top text band) without a third-party marker sprite.</summary>
internal static class InspectFrame
{
    public static Bitmap? ExtractTitle(Bitmap region, int localX, int localY, ItemScanDebugReport? debug)
    {
        if (region.Width < 80 || region.Height < 40) return null;

        var scale = LensConfig.Scale;
        var minW = (int)Math.Round(220 * scale);
        var minH = (int)Math.Round(90 * scale);
        var maxW = (int)Math.Round(720 * scale);
        var maxH = (int)Math.Round(640 * scale);
        var titleH = Math.Max(22, (int)Math.Round(36 * scale));

        using var bgr = BitmapConverter.ToMat(region);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV_FULL);
        using var dark = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(255, 70, 46), dark);

        using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(9, 9));
        Cv2.MorphologyEx(dark, dark, MorphTypes.Close, k);

        Cv2.FindContours(dark, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        Rect? best = null;
        var bestScore = double.MaxValue;
        foreach (var c in contours)
        {
            var r = Cv2.BoundingRect(c);
            if (r.Width < minW || r.Height < minH || r.Width > maxW || r.Height > maxH) continue;

            var cx = r.X + r.Width / 2.0;
            var cy = r.Y + r.Height / 2.0;
            var dist = Math.Abs(cx - localX) + Math.Abs(cy - localY) * 0.6;
            var contains = localX >= r.X && localX < r.X + r.Width && localY >= r.Y && localY < r.Y + r.Height;
            var score = contains ? dist * 0.25 : dist;
            if (score >= bestScore) continue;
            bestScore = score;
            best = r;
        }

        if (best == null)
        {
            debug?.Notes.Add("inspect-frame: no dark panel.");
            return CropNameBar(region, localX, localY, scale);
        }

        var box = best.Value;
        var inset = Math.Max(6, (int)Math.Round(8 * scale));
        var x = Math.Clamp(box.X + inset, 0, region.Width - 8);
        var y = Math.Clamp(box.Y + Math.Max(4, inset / 2), 0, region.Height - 8);
        var w = Math.Min(region.Width - x, box.Width - inset * 2);
        var h = Math.Min(region.Height - y, titleH);
        if (w < 40 || h < 12)
        {
            debug?.Notes.Add("inspect-frame: title strip too small.");
            return CropNameBar(region, localX, localY, scale);
        }

        debug?.Notes.Add($"inspect-frame: panel {box.Width}x{box.Height} title {w}x{h}.");
        return ScreenCapture.Crop(region, x, y, w, h);
    }

    public static Bitmap CropNameBar(Bitmap region, int localX, int localY, double scale)
    {
        var w = Math.Min(region.Width, (int)Math.Round(520 * scale));
        var h = Math.Min(region.Height, (int)Math.Round(36 * scale));
        var left = Math.Clamp(localX - (int)Math.Round(10 * scale), 0, Math.Max(0, region.Width - w));
        var top = Math.Clamp(localY - (int)Math.Round(18 * scale), 0, Math.Max(0, region.Height - h));
        return ScreenCapture.Crop(region, left, top, w, h);
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Tarkovy.Services;

internal static class ScreenCapture
{
    public static Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Bitmap CaptureAround(int centerX, int centerY, int width, int height)
    {
        var x = centerX - width / 2;
        var y = centerY - height / 2;
        var maxW = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
        var maxH = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
        x = Math.Clamp(x, 0, Math.Max(0, maxW - width));
        y = Math.Clamp(y, 0, Math.Max(0, maxH - height));
        return CaptureRegion(x, y, width, height);
    }

    public static double GameScale()
    {
        var w = System.Windows.SystemParameters.PrimaryScreenWidth;
        var h = System.Windows.SystemParameters.PrimaryScreenHeight;
        return Math.Min(w / 1920.0, h / 1080.0);
    }
}

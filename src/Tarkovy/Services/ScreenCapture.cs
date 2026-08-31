using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;

namespace Tarkovy.Services;

internal static class ScreenCapture
{
    public static Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var (vx, vy, vw, vh) = VirtualScreen();
        x = Math.Clamp(x, vx, Math.Max(vx, vx + vw - width));
        y = Math.Clamp(y, vy, Math.Max(vy, vy + vh - height));

        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>Square capture region scaled with resolution (896 @ 1080p).</summary>
    public static int IconScanSizePx() => Math.Max(320, (int)Math.Round(896 * GameScale()));

    /// <summary>Square region centered on click.</summary>
    public static (Bitmap Region, int LocalX, int LocalY, int OriginX, int OriginY) CaptureIconScanRegion(int clickX, int clickY)
    {
        var size = IconScanSizePx();
        var (vx, vy, vw, vh) = VirtualScreen();
        var originX = Math.Clamp(clickX - size / 2, vx, Math.Max(vx, vx + vw - size));
        var originY = Math.Clamp(clickY - size / 2, vy, Math.Max(vy, vy + vh - size));
        var region = CaptureRegion(originX, originY, size, size);
        return (region, clickX - originX, clickY - originY, originX, originY);
    }

    /// <summary>Clone with a lime slot box + crosshair at the click (for vision APIs).</summary>
    public static Bitmap MarkClick(Bitmap source, int localX, int localY)
    {
        var marked = (Bitmap)source.Clone();
        var slot = ItemSlotPx();
        var x = Math.Clamp(localX, 0, Math.Max(0, marked.Width - 1));
        var y = Math.Clamp(localY, 0, Math.Max(0, marked.Height - 1));
        using var g = Graphics.FromImage(marked);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Color.FromArgb(55, 50, 255, 50));
        using var pen = new Pen(Color.Lime, 3f);
        using var outer = new Pen(Color.FromArgb(220, 0, 0, 0), 5f);
        var box = new Rectangle(x - slot / 2, y - slot / 2, slot, slot);
        g.FillRectangle(fill, box);
        g.DrawRectangle(outer, box);
        g.DrawRectangle(pen, box);
        var arm = Math.Max(14, slot / 3);
        g.DrawLine(outer, x - arm, y, x + arm, y);
        g.DrawLine(outer, x, y - arm, x, y + arm);
        g.DrawLine(pen, x - arm, y, x + arm, y);
        g.DrawLine(pen, x, y - arm, x, y + arm);
        return marked;
    }

    public static Bitmap Crop(Bitmap source, int x, int y, int width, int height)
    {
        width = Math.Min(width, source.Width - x);
        height = Math.Min(height, source.Height - y);
        if (width < 1 || height < 1) return new Bitmap(1, 1);
        var crop = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(crop);
        g.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(x, y, width, height), GraphicsUnit.Pixel);
        return crop;
    }

    /// <summary>Inventory slot size: 63 @ 1080p, 84 @ 1440p, 126 @ 4K.</summary>
    public static int ItemSlotPx()
    {
        if (App.Settings.ItemScanSlotPx is 63 or 84 or 126)
            return App.Settings.ItemScanSlotPx;

        var h = SystemParameters.PrimaryScreenHeight;
        if (h < 1) h = 1080;
        return Math.Max(40, (int)Math.Round(63.0 * h / 1080.0));
    }

    public static double GameScale() => ItemSlotPx() / 63.0;

    /// <summary>Slot footprint in pixels: (n × slot) + 1.</summary>
    public static (int W, int H) SlotPixelSize(int slotW, int slotH)
    {
        var slot = ItemSlotPx();
        return (slotW * slot + 1, slotH * slot + 1);
    }

    private static (int X, int Y, int W, int H) VirtualScreen() =>
        ((int)SystemParameters.VirtualScreenLeft,
         (int)SystemParameters.VirtualScreenTop,
         (int)SystemParameters.VirtualScreenWidth,
         (int)SystemParameters.VirtualScreenHeight);
}

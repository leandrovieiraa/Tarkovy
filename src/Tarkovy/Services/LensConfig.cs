using System.Windows;

namespace Tarkovy.Services;

/// <summary>Resolution / scale for stash icon matching (63 px slot at 1080p).</summary>
internal static class LensConfig
{
    public const int BaseSlotPx = 63;
    public const int BaseScanSizePx = 896;
    public const int BaseHeight = 1080;
    public const int BaseWidth = 1920;

    public const double IconConfirmHighlight = 0.80;
    public const double IconConfirmNormal = 0.86;
    public const double IconMarginHighlight = 0.03;
    public const double IconMarginNormal = 0.05;
    public const double IconInnerMargin = 0.02;

    public static int GameWidth
    {
        get
        {
            if (App.Settings.ItemScanGameWidth is >= 800 and <= 7680)
                return App.Settings.ItemScanGameWidth;
            return App.Settings.ItemScanSlotPx switch
            {
                63 => 1920,
                84 => 2560,
                126 => 3840,
                _ => Math.Max(800, (int)Math.Round(SystemParameters.PrimaryScreenWidth))
            };
        }
    }

    public static int GameHeight
    {
        get
        {
            if (App.Settings.ItemScanGameHeight is >= 600 and <= 4320)
                return App.Settings.ItemScanGameHeight;
            if (App.Settings.ItemScanSlotPx is 63 or 84 or 126)
                return (int)Math.Round(App.Settings.ItemScanSlotPx * (BaseHeight / (double)BaseSlotPx));
            var h = SystemParameters.PrimaryScreenHeight;
            return h < 1 ? BaseHeight : (int)Math.Round(h);
        }
    }

    public static double Scale => Math.Max(0.5, GameHeight / (double)BaseHeight);

    public static int SlotPx
    {
        get
        {
            if (App.Settings.ItemScanSlotPx is 63 or 84 or 126)
                return App.Settings.ItemScanSlotPx;
            return Math.Max(40, (int)Math.Round(BaseSlotPx * Scale));
        }
    }

    public static bool ScanRotatedIcons => App.Settings.ItemScanRotatedIcons;

    public static int ScanSizePx => Math.Max(320, (int)Math.Round(BaseScanSizePx * Scale));

    public static (int W, int H) SlotPixelSize(int slotW, int slotH)
    {
        var slot = SlotPx;
        return (Math.Max(8, slotW * slot + 1), Math.Max(8, slotH * slot + 1));
    }

    public static (int W, int H) SlotPixelSize1080(int slotW, int slotH) =>
        (Math.Max(8, slotW * BaseSlotPx + 1), Math.Max(8, slotH * BaseSlotPx + 1));

    public static string Describe() =>
        $"{GameWidth}x{GameHeight} scale={Scale:F3} slot={SlotPx}px rotated={ScanRotatedIcons}";
}

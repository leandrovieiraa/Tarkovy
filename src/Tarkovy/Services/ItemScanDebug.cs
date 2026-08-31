using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace Tarkovy.Services;

internal sealed class ItemScanDebugReport
{
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public int ClickX { get; set; }
    public int ClickY { get; set; }
    public int ScanOriginX { get; set; }
    public int ScanOriginY { get; set; }
    public int LocalClickX { get; set; }
    public int LocalClickY { get; set; }
    public int SlotPx { get; set; }
    public int SlotPxSetting { get; set; }
    public double ScreenHeight { get; set; }
    public string ScanMode { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? MatchedItemId { get; set; }
    public string? MatchedItemName { get; set; }
    public double? MatchConfidence { get; set; }
    public string? MatchMode { get; set; }
    public CatalogDebugInfo Catalog { get; set; } = new();
    public List<OcrDebugLine> Ocr { get; set; } = [];
    public List<TemplateDebugLine> Templates { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public string? AiNote { get; set; }
    public string? AiRaw { get; set; }
    public string? SavedFolder { get; set; }
}

internal sealed class CatalogDebugInfo
{
    public bool CatalogReady { get; set; }
    public bool IndexReady { get; set; }
    public int ItemCount { get; set; }
    public string? LastCatalogError { get; set; }
    public bool I18nReady { get; set; }
    public string ApiUrl { get; set; } = "https://json.tarkov.dev/regular/items";
    public string CachePath { get; set; } = "";
    public bool CacheExists { get; set; }
    public long? CacheBytes { get; set; }
    public DateTime? CacheModifiedUtc { get; set; }
}

internal sealed class OcrDebugLine
{
    public string Crop { get; set; } = "";
    public int SlotW { get; set; }
    public int SlotH { get; set; }
    public bool Upscaled { get; set; }
    public string? RawText { get; set; }
    public string? MatchedItemId { get; set; }
    public string? MatchedShortName { get; set; }
}

internal sealed class TemplateDebugLine
{
    public int SlotW { get; set; }
    public int SlotH { get; set; }
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public double Score { get; set; }
    public double SecondScore { get; set; }
}

internal static class ItemScanDebug
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string RootDir => Path.Combine(SettingsStore.AppDataDir, "item-scan-debug");

    public static async Task<string?> SaveAsync(
        ItemScanDebugReport report,
        Bitmap? scanRegion,
        IReadOnlyList<(int W, int H, Bitmap Bmp, string Label)> crops)
    {
        if (!App.Settings.ItemScanDebugEnabled) return null;

        try
        {
            var dir = Path.Combine(RootDir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(dir);

            if (scanRegion != null)
            {
                scanRegion.Save(Path.Combine(dir, "01-scan-region.png"), ImageFormat.Png);
                using var marked = (Bitmap)scanRegion.Clone();
                using (var g = Graphics.FromImage(marked))
                using (var pen = new Pen(Color.Lime, 2))
                {
                    g.DrawLine(pen, report.LocalClickX - 12, report.LocalClickY, report.LocalClickX + 12, report.LocalClickY);
                    g.DrawLine(pen, report.LocalClickX, report.LocalClickY - 12, report.LocalClickX, report.LocalClickY + 12);
                }
                marked.Save(Path.Combine(dir, "02-scan-region-click.png"), ImageFormat.Png);
            }

            var i = 0;
            foreach (var (w, h, bmp, label) in crops)
            {
                i++;
                var name = $"{i:D2}-slot-{w}x{h}-{label}.png";
                bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
            }

            report.SavedFolder = dir;
            var json = JsonSerializer.Serialize(report, JsonOpts);
            await File.WriteAllTextAsync(Path.Combine(dir, "report.json"), json).ConfigureAwait(false);

            PruneOldFolders(40);
            return dir;
        }
        catch (Exception ex)
        {
            report.Notes.Add($"debug-save-failed: {ex.Message}");
            return null;
        }
    }

    private static void PruneOldFolders(int keep)
    {
        if (!Directory.Exists(RootDir)) return;
        var dirs = Directory.GetDirectories(RootDir)
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .Skip(keep)
            .ToList();
        foreach (var d in dirs)
        {
            try { Directory.Delete(d, true); }
            catch { /* ignore */ }
        }
    }

    public static void FillEnvironment(ItemScanDebugReport report)
    {
        report.ScreenHeight = SystemParameters.PrimaryScreenHeight;
        report.SlotPx = ScreenCapture.ItemSlotPx();
        report.SlotPxSetting = App.Settings.ItemScanSlotPx;
        report.Catalog.CachePath = Path.Combine(SettingsStore.AppDataDir, "items-cache.json");
        if (File.Exists(report.Catalog.CachePath))
        {
            var fi = new FileInfo(report.Catalog.CachePath);
            report.Catalog.CacheExists = true;
            report.Catalog.CacheBytes = fi.Length;
            report.Catalog.CacheModifiedUtc = fi.LastWriteTimeUtc;
        }
    }
}

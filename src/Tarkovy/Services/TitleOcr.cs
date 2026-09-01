using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http;
using Tesseract;

namespace Tarkovy.Services;

/// <summary>Tesseract title/label OCR with official tessdata_fast; Windows OCR as fallback.</summary>
internal static class TitleOcr
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static TesseractEngine? _engine;
    private static bool _triedInit;
    private static bool _tessReady;

    public static bool IsReady => _tessReady;

    public static string TessDataDir => Path.Combine(SettingsStore.AppDataDir, "tessdata");

    public static async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        if (_triedInit) return;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_triedInit) return;
            _triedInit = true;
            Directory.CreateDirectory(TessDataDir);
            await EnsureLangAsync("eng", ct).ConfigureAwait(false);
            await EnsureLangAsync("por", ct).ConfigureAwait(false);
            TryCreateEngine();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<string?> ReadTitleAsync(Bitmap bitmap)
    {
        await EnsureReadyAsync().ConfigureAwait(false);
        var tess = await ReadWithTesseractAsync(bitmap, PageSegMode.SingleBlock).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(tess)) return tess.Trim();
        return await OcrHelper.ReadTooltipTextAsync(bitmap).ConfigureAwait(false);
    }

    public static async Task<List<string>> ReadLabelAsync(Bitmap bitmap, bool highlighted)
    {
        await EnsureReadyAsync().ConfigureAwait(false);
        var list = new List<string>();

        var tess = await ReadWithTesseractAsync(bitmap, PageSegMode.SingleLine).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(tess))
            list.Add(tess.Trim());

        var win = await OcrHelper.ReadInCellLabelsAsync(bitmap, highlighted).ConfigureAwait(false);
        foreach (var t in win)
        {
            if (!string.IsNullOrWhiteSpace(t) && !list.Contains(t, StringComparer.OrdinalIgnoreCase))
                list.Add(t);
        }

        return list;
    }

    private static async Task EnsureLangAsync(string lang, CancellationToken ct)
    {
        var dest = Path.Combine(TessDataDir, lang + ".traineddata");
        if (File.Exists(dest) && new FileInfo(dest).Length > 50_000) return;

        var url = $"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{lang}.traineddata";
        try
        {
            var bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            if (bytes.Length > 50_000)
                await File.WriteAllBytesAsync(dest, bytes, ct).ConfigureAwait(false);
        }
        catch
        {
            // Windows OCR remains available
        }
    }

    private static void TryCreateEngine()
    {
        foreach (var langs in new[] { "eng+por", "eng", "por" })
        {
            try
            {
                var parts = langs.Split('+');
                if (parts.Any(p => !File.Exists(Path.Combine(TessDataDir, p + ".traineddata"))))
                    continue;

                _engine?.Dispose();
                _engine = new TesseractEngine(TessDataDir, langs, EngineMode.LstmOnly);
                _tessReady = true;
                return;
            }
            catch
            {
                _engine?.Dispose();
                _engine = null;
            }
        }

        _tessReady = false;
    }

    private static async Task<string?> ReadWithTesseractAsync(Bitmap bitmap, PageSegMode psm)
    {
        if (_engine == null) return null;

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var prepped = UpscaleForOcr(bitmap);
                using var pix = ToPix(prepped);
                if (pix == null) return null;
                using var page = _engine.Process(pix, psm);
                var text = page.GetText()?.Trim();
                return string.IsNullOrWhiteSpace(text) ? null : text.Replace('\n', ' ');
            }).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Pix? ToPix(Bitmap bitmap)
    {
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Pix.LoadFromMemory(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap UpscaleForOcr(Bitmap source)
    {
        var n = source.Width < 220 ? 4 : 3;
        var w = Math.Max(8, source.Width * n);
        var h = Math.Max(8, source.Height * n);
        var scaled = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, 0, 0, w, h);
        return scaled;
    }

    public static void DisposeEngine()
    {
        _engine?.Dispose();
        _engine = null;
        _tessReady = false;
        _triedInit = false;
    }
}

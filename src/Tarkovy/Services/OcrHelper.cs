using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Tarkovy.Services;

internal static class OcrHelper
{
    public static Task<string?> ReadTextAsync(Bitmap bitmap) => RecognizeAsync(bitmap, preprocess: false);

    /// <summary>In-cell short label — tries dark-on-white and light-on-dark; en-US for ammo codes.</summary>
    public static async Task<string?> ReadInCellLabelAsync(Bitmap bitmap, bool highlighted)
    {
        var all = await ReadInCellLabelsAsync(bitmap, highlighted).ConfigureAwait(false);
        return all.Count == 0 ? null : all[0];
    }

    /// <summary>All OCR variants for a cell. Caller should match each — picking only the longest dropped "Água" for "Garrafa".</summary>
    public static async Task<List<string>> ReadInCellLabelsAsync(Bitmap bitmap, bool highlighted)
    {
        _ = highlighted;
        var candidates = new List<string>();

        async Task AbsorbAsync(Bitmap bmp)
        {
            foreach (var engine in EnginesForInCell())
            {
                var t = await RecognizeWithEngineAsync(bmp, engine).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(t)) continue;
                var trimmed = t.Trim();
                candidates.Add(trimmed);
                foreach (var extra in AmmoOcrRepairs(trimmed))
                    candidates.Add(extra);
            }
        }

        using (var dark = PreprocessDarkText(bitmap))
        {
            await AbsorbAsync(dark).ConfigureAwait(false);
            using var darkUp = UpscaleNx(dark, 4);
            await AbsorbAsync(darkUp).ConfigureAwait(false);
        }

        using (var light = PreprocessTooltip(bitmap))
        {
            await AbsorbAsync(light).ConfigureAwait(false);
            using var lightUp = UpscaleNx(light, 4);
            await AbsorbAsync(lightUp).ConfigureAwait(false);
        }

        using (var rawUp = UpscaleNx(bitmap, 4))
            await AbsorbAsync(rawUp).ConfigureAwait(false);

        return RankInCellTexts(candidates);
    }

    private static List<string> RankInCellTexts(List<string> candidates)
    {
        if (candidates.Count == 0) return [];
        return candidates
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ScoreInCellText)
            .ToList();
    }

    /// <summary>CMS/Água (3–5 letters) and ammo codes (M882) beat durability and long misreads.</summary>
    private static int ScoreInCellText(string s)
    {
        var letters = s.Count(char.IsLetter);
        var digits = s.Count(char.IsDigit);
        var len = s.Length;
        if (letters >= 1 && digits >= 2 && len is >= 3 and <= 10)
            return 120 + letters + digits;
        if (len is >= 3 and <= 5 && letters >= 3 && digits == 0)
            return 80 + letters;
        if (digits > 0 && letters == 0)
            return -20 - digits;
        var shortName = len is >= 3 and <= 8 ? 18 : 0;
        return letters * 3 + shortName - Math.Min(digits * 2, 8);
    }

    /// <summary>pt-BR reads M882 as Mgg2 / FMJ as FM-I. Keep both raw and repaired.</summary>
    private static IEnumerable<string> AmmoOcrRepairs(string text)
    {
        var t = text
            .Replace("FM-I", "FMJ", StringComparison.OrdinalIgnoreCase)
            .Replace("FM-l", "FMJ", StringComparison.OrdinalIgnoreCase)
            .Replace("FMl", "FMJ", StringComparison.OrdinalIgnoreCase)
            .Replace("FM1", "FMJ", StringComparison.OrdinalIgnoreCase);
        if (!t.Equals(text, StringComparison.OrdinalIgnoreCase))
            yield return t;

        if (text.Length is >= 3 and <= 12)
        {
            var compact = new string(text.Where(char.IsLetterOrDigit).ToArray());
            if (compact.Length >= 3)
            {
                var alt = compact
                    .Replace("rn", "m", StringComparison.OrdinalIgnoreCase)
                    .Replace('g', '8')
                    .Replace('G', '8')
                    .Replace('o', '0')
                    .Replace('O', '0');
                if (!alt.Equals(compact, StringComparison.OrdinalIgnoreCase))
                    yield return alt;
            }
        }
    }

    /// <summary>White-on-black EFT tooltip — threshold + 2x upscale; pt-BR then en-US for ammo codes.</summary>
    public static async Task<string?> ReadTooltipTextAsync(Bitmap bitmap)
    {
        var texts = new List<string>();
        using (var prepped = PreprocessTooltip(bitmap))
        {
            foreach (var engine in EnginesForInCell())
            {
                var t = await RecognizeWithEngineAsync(prepped, engine).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    texts.Add(t.Trim());
                    foreach (var extra in AmmoOcrRepairs(t.Trim()))
                        texts.Add(extra);
                }
            }

            using var scaled = Upscale2x(prepped);
            foreach (var engine in EnginesForInCell())
            {
                var t = await RecognizeWithEngineAsync(scaled, engine).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    texts.Add(t.Trim());
                    foreach (var extra in AmmoOcrRepairs(t.Trim()))
                        texts.Add(extra);
                }
            }
        }

        return RankInCellTexts(texts).FirstOrDefault();
    }

    private static async Task<string?> RecognizeBestAsync(Bitmap bitmap)
    {
        foreach (var engine in EnginesForInCell())
        {
            var text = await RecognizeWithEngineAsync(bitmap, engine).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return null;
    }

    private static IEnumerable<OcrEngine> EnginesForInCell()
    {
        var en = CreateEngineLang("en-US");
        if (en != null) yield return en;
        if (!Loc.IsPortuguese) yield break;
        var pt = CreateEngineLang("pt-BR");
        if (pt != null && !ReferenceEquals(pt, en)) yield return pt;
    }

    private static async Task<string?> RecognizeAsync(Bitmap bitmap, bool preprocess)
    {
        Bitmap? owned = null;
        try
        {
            var engine = CreateEngine();
            if (engine == null) return null;
            var work = preprocess ? owned = PreprocessTooltip(bitmap) : bitmap;
            return await RecognizeWithEngineAsync(work, engine).ConfigureAwait(false);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static async Task<string?> RecognizeWithEngineAsync(Bitmap bitmap, OcrEngine engine)
    {
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            using var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(ms.ToArray().AsBuffer());
            ras.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(ras);
            using var sb = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(sb);
            var text = result?.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static OcrEngine? _cachedEngine;
    private static string? _cachedLang;
    private static OcrEngine? _cachedEnEngine;

    private static OcrEngine? CreateEngine() =>
        CreateEngineLang(Loc.IsPortuguese ? "pt-BR" : "en-US");

    private static OcrEngine? CreateEngineLang(string lang)
    {
        if (lang == "en-US")
        {
            if (_cachedEnEngine != null) return _cachedEnEngine;
            try { _cachedEnEngine = OcrEngine.TryCreateFromLanguage(new Language("en-US")); }
            catch { _cachedEnEngine = null; }
            _cachedEnEngine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            return _cachedEnEngine;
        }

        if (_cachedEngine != null && _cachedLang == lang)
            return _cachedEngine;

        try
        {
            _cachedEngine = OcrEngine.TryCreateFromLanguage(new Language(lang));
        }
        catch
        {
            _cachedEngine = null;
        }

        _cachedEngine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        _cachedLang = lang;
        return _cachedEngine;
    }

    private static Bitmap PreprocessTooltip(Bitmap src)
    {
        var w = Math.Max(1, src.Width);
        var h = Math.Max(1, src.Height);
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, w, h);
        var srcData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dstData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = y * srcData.Stride + x * 3;
                    var b = System.Runtime.InteropServices.Marshal.ReadByte(srcData.Scan0, i);
                    var g = System.Runtime.InteropServices.Marshal.ReadByte(srcData.Scan0, i + 1);
                    var r = System.Runtime.InteropServices.Marshal.ReadByte(srcData.Scan0, i + 2);
                    var lum = 0.299 * r + 0.587 * g + 0.114 * b;
                    var v = lum > 105 ? (byte)255 : (byte)0;
                    System.Runtime.InteropServices.Marshal.WriteByte(dstData.Scan0, i, v);
                    System.Runtime.InteropServices.Marshal.WriteByte(dstData.Scan0, i + 1, v);
                    System.Runtime.InteropServices.Marshal.WriteByte(dstData.Scan0, i + 2, v);
                }
            }
        }
        finally
        {
            src.UnlockBits(srcData);
            bmp.UnlockBits(dstData);
        }
        return bmp;
    }

    /// <summary>Dark text on white highlight background.</summary>
    private static Bitmap PreprocessDarkText(Bitmap src)
    {
        var w = Math.Max(1, src.Width);
        var h = Math.Max(1, src.Height);
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, w, h);
        var srcData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dstData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * srcData.Stride + x * 3;
                var b = System.Runtime.InteropServices.Marshal.ReadByte(srcData.Scan0, i);
                var g = System.Runtime.InteropServices.Marshal.ReadByte(srcData.Scan0, i + 1);
                var r = System.Runtime.InteropServices.Marshal.ReadByte(srcData.Scan0, i + 2);
                var lum = 0.299 * r + 0.587 * g + 0.114 * b;
                var v = lum < 130 ? (byte)0 : (byte)255;
                System.Runtime.InteropServices.Marshal.WriteByte(dstData.Scan0, i, v);
                System.Runtime.InteropServices.Marshal.WriteByte(dstData.Scan0, i + 1, v);
                System.Runtime.InteropServices.Marshal.WriteByte(dstData.Scan0, i + 2, v);
            }
        }
        finally
        {
            src.UnlockBits(srcData);
            bmp.UnlockBits(dstData);
        }
        return bmp;
    }

    private static Bitmap UpscaleNx(Bitmap source, int n)
    {
        n = Math.Max(2, n);
        var w = source.Width * n;
        var h = source.Height * n;
        var scaled = new Bitmap(w, h, source.PixelFormat);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, 0, 0, w, h);
        return scaled;
    }

    private static Bitmap Upscale2x(Bitmap source) => UpscaleNx(source, 2);
}

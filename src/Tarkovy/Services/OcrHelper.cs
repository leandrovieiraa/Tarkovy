using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Tarkovy.Services;

internal static class OcrHelper
{
    public static async Task<string?> ReadTextAsync(Bitmap bitmap)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return null;

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
}

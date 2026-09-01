using System.Drawing;
using Tarkovy.Models;

namespace Tarkovy.Services;

internal static class IconShortNameScanner
{
    public static async Task<(List<OcrDebugLine> Lines, List<ItemDefinition> Hits)> TryMatchAllWithDebugAsync(
        Bitmap slotBmp, int slotW, int slotH, ItemCatalog catalog, List<OcrDebugLine>? lines,
        bool highlighted = false)
    {
        lines ??= [];
        var hits = new List<ItemDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var crops = BuildLabelCrops(slotBmp, slotW, slotH).ToList();
        try
        {
            async Task AbsorbAsync(Bitmap crop, string cropName)
            {
                var texts = await TitleOcr.ReadLabelAsync(crop, highlighted).ConfigureAwait(false);
                var found = new List<ItemDefinition>();
                foreach (var text in texts)
                {
                    foreach (var item in catalog.MatchShortNameCandidates(text, slotW, slotH))
                    {
                        if (!catalog.OcrAgreesWithItem(item, text)) continue;
                        if (found.Any(i => i.Id == item.Id)) continue;
                        found.Add(item);
                    }
                    foreach (var extra in catalog.MatchAmmoCodes(text, slotW, slotH))
                    {
                        if (found.Any(i => i.Id == extra.Id) || !catalog.OcrAgreesWithItem(extra, text))
                            continue;
                        found.Add(extra);
                    }
                }

                var joined = texts.Count == 0 ? null : string.Join(" | ", texts);
                lines.Add(new OcrDebugLine
                {
                    Crop = cropName,
                    SlotW = slotW,
                    SlotH = slotH,
                    Upscaled = false,
                    RawText = joined,
                    MatchedItemId = found.Count == 1 ? found[0].Id : null,
                    MatchedShortName = found.Count == 1
                        ? ItemDisplayNames.ShortName(found[0])
                        : found.Count > 1
                            ? string.Join(",", found.Select(ItemDisplayNames.ShortName))
                            : null
                });
                foreach (var item in found)
                {
                    if (seen.Add(item.Id))
                        hits.Add(item);
                }
            }

            var idx = 0;
            foreach (var crop in crops)
            {
                idx++;
                var name = idx == 1 ? "top-full" : idx == 2 ? "top-left" : idx == 3 ? "top-right" : "top-tall";
                await AbsorbAsync(crop, name).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var crop in crops)
                crop.Dispose();
        }

        return (lines, hits);
    }

    private static IEnumerable<Bitmap> BuildLabelCrops(Bitmap bmp, int slotW, int slotH)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        if (w < 8 || h < 8) yield break;

        // In-game short names sit across the TOP of the cell (M882, Analgésico, M855).
        var topH = Math.Clamp((int)Math.Round(h * (h <= 80 ? 0.50 : 0.36)), 16, Math.Min(44, h));
        yield return ScreenCapture.Crop(bmp, 0, 0, w, topH);

        var crW = Math.Clamp((int)Math.Round(w * 0.70), 22, w);
        var crH = Math.Clamp((int)Math.Round(h * 0.34), 12, Math.Min(44, h));
        yield return ScreenCapture.Crop(bmp, 0, 0, crW, crH);
        yield return ScreenCapture.Crop(bmp, Math.Max(0, w - crW), 0, crW, crH);

        if (h <= 80)
        {
            var tallH = Math.Clamp((int)Math.Round(h * 0.62), 20, h);
            yield return ScreenCapture.Crop(bmp, 0, 0, w, tallH);
        }
    }
}

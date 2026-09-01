using System.Drawing;
using Tarkovy.Models;

namespace Tarkovy.Services;

internal sealed record LensHit
{
    public required ItemDefinition Item { get; init; }
    public double Confidence { get; init; }
    public required string Mode { get; init; }
    public int SlotW { get; init; }
    public int SlotH { get; init; }
    public bool Confirmed { get; init; }
}

/// <summary>Stash icon + inspect-title pipeline. AI is not invoked here.</summary>
internal sealed class LensEngine : IDisposable
{
    private readonly IconBank _bank = new();
    private int _builtForCatalog;

    public IconBank Bank => _bank;
    public bool IsReady => _bank.IsReady;

    public async Task EnsureReadyAsync(ItemCatalog catalog, Action<string>? status, CancellationToken ct)
    {
        if (!_bank.IsReady || _builtForCatalog != catalog.Items.Count)
        {
            var progress = status == null ? null : new Progress<string>(status);
            await _bank.BuildAsync(catalog, progress, ct).ConfigureAwait(false);
            _builtForCatalog = catalog.Items.Count;
        }

        await TitleOcr.EnsureReadyAsync(ct).ConfigureAwait(false);
    }

    public void Reset()
    {
        _bank.Reset();
        _builtForCatalog = 0;
    }

    public LensHit? MatchIcon(
        Bitmap iconCrop, int slotW, int slotH, bool highlighted, ItemScanDebugReport? debug,
        bool searchSmaller = false)
    {
        var match = IconLocator.Match(iconCrop, slotW, slotH, _bank, LensConfig.ScanRotatedIcons, searchSmaller);
        if (debug != null)
        {
            foreach (var (item, score, second) in IconLocator.Top(iconCrop, slotW, slotH, _bank, 5))
            {
                debug.Templates.Add(new TemplateDebugLine
                {
                    SlotW = slotW,
                    SlotH = slotH,
                    ItemId = item.Id,
                    Name = ItemDisplayNames.Name(item),
                    ShortName = ItemDisplayNames.ShortName(item),
                    Score = score,
                    SecondScore = second
                });
            }
        }

        if (match.Item == null) return null;

        var confirmed = IconLocator.IsConfirmed(match, highlighted);
        if (!confirmed)
        {
            debug?.Notes.Add(
                $"icon-reject: {ItemDisplayNames.ShortName(match.Item)} conf={match.Confidence:F3} second={match.SecondConfidence:F3}");
            return new LensHit
            {
                Item = match.Item,
                Confidence = match.Confidence,
                Mode = highlighted ? "icon-highlight" : "icon",
                SlotW = match.SlotW,
                SlotH = match.SlotH,
                Confirmed = false
            };
        }

        debug?.Notes.Add(
            $"icon: {ItemDisplayNames.ShortName(match.Item)} conf={match.Confidence:F3} rot={match.Rotated} {match.SlotW}x{match.SlotH} cache-bank={_bank.GameCacheCount}");
        return new LensHit
        {
            Item = match.Item,
            Confidence = match.Confidence,
            Mode = highlighted ? "icon-highlight" : "icon",
            SlotW = match.SlotW,
            SlotH = match.SlotH,
            Confirmed = true
        };
    }

    private static readonly (int W, int H)[] ProbeSizes =
        [(2, 3), (3, 2), (2, 2), (1, 2), (2, 1), (3, 1), (1, 3), (4, 2), (2, 4), (1, 1)];

    /// <summary>
    /// Match the detected cell, then probe larger slot crops around the click.
    /// A 1×1 sliver of a CMS must not win over a confirmed 2×2/2×3 icon.
    /// </summary>
    public LensHit? MatchBestAround(
        Bitmap region, int localX, int localY, StashCell cell, ItemScanDebugReport? debug)
    {
        LensHit? best = MatchIcon(cell.Icon, cell.SlotW, cell.SlotH, cell.Highlighted, debug, searchSmaller: false);
        if (best is { Confirmed: true } && cell.SlotW * cell.SlotH >= 4)
            return best;

        foreach (var (w, h) in ProbeSizes)
        {
            if (w == cell.SlotW && h == cell.SlotH) continue;
            if (best is { Confirmed: true } && w * h < best.SlotW * best.SlotH)
                continue;
            var crops = StashGrid.CropsAtClick(region, localX, localY, w, h);
            try
            {
                foreach (var crop in crops)
                {
                    var hit = MatchIcon(crop, w, h, highlighted: true, debug: null, searchSmaller: false);
                    if (hit == null) continue;
                    if (debug != null && hit.Confirmed)
                        debug.Notes.Add($"icon-probe {w}x{h}: {ItemDisplayNames.ShortName(hit.Item)} conf={hit.Confidence:F3}");
                    best = BetterHit(best, hit);
                }
            }
            finally
            {
                foreach (var crop in crops)
                    crop.Dispose();
            }
        }

        return best;
    }

    private static LensHit? BetterHit(LensHit? current, LensHit candidate)
    {
        if (current == null) return candidate;
        if (candidate.Confirmed != current.Confirmed)
            return candidate.Confirmed ? candidate : current;

        var curSlots = Math.Max(1, current.SlotW * current.SlotH);
        var newSlots = Math.Max(1, candidate.SlotW * candidate.SlotH);
        if (candidate.Confirmed && current.Confirmed && newSlots != curSlots)
        {
            var larger = newSlots > curSlots ? candidate : current;
            var smaller = newSlots > curSlots ? current : candidate;
            if (larger.Confidence + 0.06 >= smaller.Confidence)
                return larger;
        }

        return candidate.Confidence > current.Confidence ? candidate : current;
    }

    public async Task<LensHit?> MatchTitleAsync(
        Bitmap region, int localX, int localY, ItemCatalog catalog, ItemScanDebugReport? debug)
    {
        using var title = InspectFrame.ExtractTitle(region, localX, localY, debug);
        if (title == null) return null;

        var text = await TitleOcr.ReadTitleAsync(title).ConfigureAwait(false);
        debug?.Ocr.Add(new OcrDebugLine
        {
            Crop = "inspect-title",
            RawText = text
        });
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 3)
            return null;

        var item = catalog.MatchByName(text);
        if (debug != null && debug.Ocr.Count > 0)
        {
            debug.Ocr[^1].MatchedItemId = item?.Id;
            debug.Ocr[^1].MatchedShortName = item != null ? ItemDisplayNames.Name(item) : null;
        }

        if (item == null) return null;
        debug?.Notes.Add($"inspect-title: {ItemDisplayNames.Name(item)}");
        return new LensHit
        {
            Item = item,
            Confidence = 0.99,
            Mode = "name",
            SlotW = item.Width,
            SlotH = item.Height,
            Confirmed = true
        };
    }

    public IReadOnlyList<(ItemDefinition Item, double Score, double SecondScore)> TopCandidates(
        Bitmap iconCrop, int slotW, int slotH, int max) =>
        IconLocator.Top(iconCrop, slotW, slotH, _bank, max);

    public void Dispose()
    {
        _bank.Dispose();
        TitleOcr.DisposeEngine();
    }
}

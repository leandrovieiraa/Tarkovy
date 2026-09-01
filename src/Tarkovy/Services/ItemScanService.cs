using System.Drawing;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class ItemScanService : IDisposable
{
    private readonly ItemCatalog _catalog;
    private readonly LensEngine _lens = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private CancellationTokenSource? _scanCts;
    private int _latestScanId;
    private bool _indexReady;

    private static readonly TimeSpan IconCaptureDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan HighlightRetryDelay = TimeSpan.FromMilliseconds(80);

    public ItemScanService(ItemCatalog catalog)
    {
        _catalog = catalog;
        _catalog.ItemsUpdated += OnCatalogUpdated;
    }

    public bool IndexReady => _indexReady && _lens.IsReady;

    public event Action<ItemScanResult>? ScanCompleted;
    public event Action<string>? ScanFailed;
    public event Action<string>? StatusChanged;

    private void OnCatalogUpdated()
    {
        _indexReady = false;
        _lens.Reset();
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        if (!_catalog.IsReady)
            await _catalog.LoadAsync(ct).ConfigureAwait(false);
        if (_indexReady && _lens.IsReady) return;
        StatusChanged?.Invoke(Loc.T("ItemScan.Status.Indexing"));
        await _lens.EnsureReadyAsync(_catalog, msg => StatusChanged?.Invoke(msg), ct).ConfigureAwait(false);
        _indexReady = _lens.IsReady;
        StatusChanged?.Invoke(_indexReady
            ? Loc.T("ItemScan.Status.Ready")
            : Loc.T("ItemScan.Status.IndexFailed"));
    }

    public void ScanIconAt(int x, int y, int scanId) => StartScan(x, y, icon: true, scanId);

    public void ScanNameAt(int x, int y, int scanId) => StartScan(x, y, icon: false, scanId);

    public void CancelPendingScans()
    {
        Interlocked.Increment(ref _latestScanId);
        try { _scanCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void StartScan(int x, int y, bool icon, int scanId)
    {
        Volatile.Write(ref _latestScanId, scanId);
        try { _scanCts?.Cancel(); } catch (ObjectDisposedException) { }

        var cts = new CancellationTokenSource();
        _scanCts = cts;
        _ = RunScanAsync(x, y, icon, scanId, cts.Token);
    }

    private async Task RunScanAsync(int x, int y, bool icon, int scanId, CancellationToken ct)
    {
        StatusChanged?.Invoke(Loc.T("ItemScan.Status.Scanning"));

        var debug = App.Settings.ItemScanDebugEnabled ? new ItemScanDebugReport
        {
            ClickX = x,
            ClickY = y,
            ScanMode = icon ? "shift+icon" : "name"
        } : null;

        Bitmap? scanRegion = null;
        Bitmap? beforeRegion = null;
        StashCell? cell = null;
        Bitmap? nameSnapshot = null;
        string? failMessage = null;
        ItemScanResult? success = null;

        try
        {
            if (debug != null)
            {
                ItemScanDebug.FillEnvironment(debug);
                debug.Catalog.CatalogReady = _catalog.IsReady;
                debug.Catalog.IndexReady = IndexReady;
                debug.Catalog.ItemCount = _catalog.Items.Count;
                debug.Catalog.LastCatalogError = _catalog.LastError;
                debug.Notes.Add(LensConfig.Describe());
                debug.Notes.Add($"icon-bank={_lens.Bank.Count} game-cache={_lens.Bank.GameCacheCount} tess={TitleOcr.IsReady}");
                debug.Notes.Add("Shift+click the icon center in stash/inventory.");
            }

            await ItemLocalizedNames.LoadAsync(ct).ConfigureAwait(false);
            ThrowIfSuperseded(scanId, ct);

            if (!_catalog.IsReady)
                await _catalog.LoadAsync(ct).ConfigureAwait(false);

            if (!IndexReady)
                await EnsureReadyAsync(ct).ConfigureAwait(false);

            ThrowIfSuperseded(scanId, ct);
            await _scanLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                ThrowIfSuperseded(scanId, ct);

                if (icon)
                    success = await ScanIconAsync(x, y, scanId, debug, ct,
                        r => scanRegion = r,
                        b => beforeRegion = b,
                        c => cell = c).ConfigureAwait(false);
                else
                {
                    var cap = ScreenCapture.CaptureIconScanRegion(x, y);
                    scanRegion = cap.Region;
                    nameSnapshot = InspectFrame.ExtractTitle(cap.Region, cap.LocalX, cap.LocalY, debug);
                    success = await ScanNameAsync(cap.Region, cap.LocalX, cap.LocalY, x, y, scanId, debug, ct)
                        .ConfigureAwait(false);
                }

                if (success == null)
                {
                    failMessage = icon && !IndexReady
                        ? Loc.T("ItemScan.Error.Index")
                        : FailMessage(icon, debug);
                }
            }
            finally
            {
                _scanLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            failMessage = ex.Message;
        }
        finally
        {
            var superseded = scanId != Volatile.Read(ref _latestScanId);
            if (debug != null && !superseded)
            {
                debug.Outcome = success != null ? "success" : "failed";
                debug.ErrorMessage = failMessage;
                if (success != null)
                {
                    debug.MatchedItemId = success.Item.Id;
                    debug.MatchedItemName = ItemDisplayNames.Name(success.Item);
                    debug.MatchConfidence = success.Confidence;
                    debug.MatchMode = success.Mode;
                }

                var saveCrops = new List<(int W, int H, Bitmap Bmp, string Label)>();
                if (cell != null)
                {
                    saveCrops.Add((cell.SlotW, cell.SlotH, (Bitmap)cell.Icon.Clone(), "cell-icon"));
                    if (cell.Label != null && !ReferenceEquals(cell.Label, cell.Icon))
                        saveCrops.Add((cell.SlotW, cell.SlotH, (Bitmap)cell.Label.Clone(), "cell-label"));
                }
                if (nameSnapshot != null)
                    saveCrops.Add((0, 0, (Bitmap)nameSnapshot.Clone(), "name"));

                var dir = await ItemScanDebug.SaveAsync(debug, scanRegion, saveCrops).ConfigureAwait(false);
                foreach (var (_, _, bmp, _) in saveCrops)
                    bmp.Dispose();

                if (dir != null && failMessage != null)
                    failMessage = $"{failMessage}\n\nDebug: {dir}";
            }

            scanRegion?.Dispose();
            beforeRegion?.Dispose();
            cell?.Dispose();
            nameSnapshot?.Dispose();
        }

        if (scanId != Volatile.Read(ref _latestScanId)) return;

        if (success != null)
            ScanCompleted?.Invoke(success);
        else if (failMessage != null)
            ScanFailed?.Invoke(failMessage);
    }

    private static string FailMessage(bool icon, ItemScanDebugReport? debug)
    {
        if (!icon) return Loc.T("ItemScan.Error.NoMatch");
        if (debug?.Notes.Any(n => n.StartsWith("icon-reject")) == true)
            return Loc.T("ItemScan.Error.Ambiguous");
        return Loc.T("ItemScan.Error.NoMatch");
    }

    private async Task<ItemScanResult?> ScanIconAsync(
        int x, int y, int scanId, ItemScanDebugReport? debug, CancellationToken ct,
        Action<Bitmap> setRegion, Action<Bitmap> setBefore, Action<StashCell> setCell)
    {
        List<(string Label, Bitmap Bmp)>? hoverStrips = null;
        Bitmap? before = null;
        Bitmap? region = null;
        StashCell? cell = null;
        int localX = 0, localY = 0, originX = 0, originY = 0;

        try
        {
            hoverStrips = TooltipScanner.CaptureNow(x, y);
            var beforeCap = ScreenCapture.CaptureIconScanRegion(x, y);
            before = beforeCap.Region;
            setBefore(before);

            await Task.Delay(IconCaptureDelay, ct).ConfigureAwait(false);

            var after = ScreenCapture.CaptureIconScanRegion(x, y);
            region = after.Region;
            localX = after.LocalX;
            localY = after.LocalY;
            originX = after.OriginX;
            originY = after.OriginY;
            setRegion(region);

            cell = StashGrid.Locate(region, before, localX, localY, debug);
            if (cell == null || !cell.Highlighted)
            {
                await Task.Delay(HighlightRetryDelay, ct).ConfigureAwait(false);
                var retry = ScreenCapture.CaptureIconScanRegion(x, y);
                region.Dispose();
                region = retry.Region;
                localX = retry.LocalX;
                localY = retry.LocalY;
                originX = retry.OriginX;
                originY = retry.OriginY;
                setRegion(region);
                debug?.Notes.Add("highlight: retry capture +80ms.");
                cell?.Dispose();
                cell = StashGrid.Locate(region, before, localX, localY, debug);
            }

            if (cell != null)
                setCell(cell);

            if (debug != null)
            {
                debug.ScanOriginX = originX;
                debug.ScanOriginY = originY;
                debug.LocalClickX = localX;
                debug.LocalClickY = localY;
                debug.Notes.Add($"templates-indexed: {_lens.Bank.Count}");
            }

            var tooltipOcr = new List<string>();
            var tip = await MatchTooltipAsync(hoverStrips, before, region, localX, localY, debug, tooltipOcr)
                .ConfigureAwait(false);
            hoverStrips = null;
            if (tip != null)
                debug?.Notes.Add($"tooltip: {ItemDisplayNames.ShortName(tip)}");

            if (cell == null)
            {
                return await TryAiOnlyAsync(null, tip, tooltipOcr, x, y, scanId, debug, ct)
                    .ConfigureAwait(false);
            }

            return await ResolveIconAsync(cell, region, localX, localY, tip, tooltipOcr, x, y, scanId, debug, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            if (hoverStrips != null)
                TooltipScanner.DisposeStrips(hoverStrips);
        }
    }

    private async Task<ItemScanResult?> ResolveIconAsync(
        StashCell cell,
        Bitmap region,
        int localX, int localY,
        ItemDefinition? tooltip,
        List<string> tooltipOcr,
        int x, int y, int scanId,
        ItemScanDebugReport? debug,
        CancellationToken ct)
    {
        var iconHit = await Task.Run(() =>
            _lens.MatchBestAround(region, localX, localY, cell, debug), ct)
            .ConfigureAwait(false);

        var ocrHits = new List<ItemDefinition>();
        var ocrSlotW = iconHit?.SlotW ?? cell.SlotW;
        var ocrSlotH = iconHit?.SlotH ?? cell.SlotH;
        foreach (var bmp in new[] { cell.Icon, cell.Label })
        {
            if (bmp == null) continue;
            var (lines, hits) = await IconShortNameScanner.TryMatchAllWithDebugAsync(
                bmp, ocrSlotW, ocrSlotH, _catalog, debug != null ? [] : null, cell.Highlighted)
                .ConfigureAwait(false);
            if (debug != null)
                debug.Ocr.AddRange(lines);
            foreach (var item in hits)
            {
                if (_catalog.IsBroadShortLabel(item)) continue;
                if (item.Width != ocrSlotW || item.Height != ocrSlotH) continue;
                if (ocrHits.All(h => h.Id != item.Id))
                    ocrHits.Add(item);
            }
        }

        debug?.Notes.Add(
            $"resolve: icon={(iconHit != null ? ItemDisplayNames.ShortName(iconHit.Item) + (iconHit.Confirmed ? "*" : "?") : "none")} " +
            $"tooltip={(tooltip != null ? ItemDisplayNames.ShortName(tooltip) : "none")} " +
            $"ocr=[{string.Join(",", ocrHits.Select(ItemDisplayNames.ShortName))}]");

        LensHit? local = null;
        if (iconHit is { Confirmed: true })
            local = iconHit;
        else if (ocrHits.Count == 1)
        {
            local = new LensHit
            {
                Item = ocrHits[0],
                Confidence = 0.99,
                Mode = cell.Highlighted ? "shortname-highlight" : "shortname",
                SlotW = ocrHits[0].Width,
                SlotH = ocrHits[0].Height,
                Confirmed = true
            };
        }
        else if (iconHit != null && tooltip != null && iconHit.Item.Id == tooltip.Id)
            local = iconHit with { Confirmed = true, Confidence = Math.Max(iconHit.Confidence, 0.90) };
        else if (iconHit != null && ocrHits.Count == 1 && ocrHits[0].Id == iconHit.Item.Id)
            local = iconHit with { Confirmed = true, Confidence = Math.Max(iconHit.Confidence, 0.90) };

        if (local is { Confirmed: true })
        {
            debug?.Notes.Add("ai: skipped (local confirmed)");
            return Result(local, x, y, scanId);
        }

        debug?.Notes.Add("tooltip/name ignored on icon scan unless they confirm the icon.");
        var ai = await TryAiOnlyAsync(cell, tooltip, tooltipOcr, x, y, scanId, debug, ct, ocrHits, iconHit)
            .ConfigureAwait(false);
        if (ai != null) return ai;

        return null;
    }

    private async Task<ItemScanResult?> ScanNameAsync(
        Bitmap region, int localX, int localY, int x, int y, int scanId,
        ItemScanDebugReport? debug, CancellationToken ct)
    {
        var title = await _lens.MatchTitleAsync(region, localX, localY, _catalog, debug)
            .ConfigureAwait(false);
        if (title is { Confirmed: true })
            return Result(title, x, y, scanId);

        return await TryAiOnlyAsync(null, title?.Item, [], x, y, scanId, debug, ct)
            .ConfigureAwait(false);
    }

    private async Task<ItemDefinition?> MatchTooltipAsync(
        List<(string Label, Bitmap Bmp)>? hoverStrips,
        Bitmap? before, Bitmap? after, int localX, int localY,
        ItemScanDebugReport? debug, List<string> tooltipOcr)
    {
        var strips = hoverStrips ?? [];
        if (before != null && after != null
            && before.Width == after.Width && before.Height == after.Height)
            strips.AddRange(TooltipScanner.IsolateFromFrames(before, after, localX, localY));

        if (strips.Count == 0) return null;

        var (item, lines) = await TooltipScanner.TryMatchCapturedAsync(
            strips, _catalog, []).ConfigureAwait(false);
        debug?.Ocr.AddRange(lines);
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line.RawText))
                tooltipOcr.Add(line.RawText);
        }
        return item;
    }

    private async Task<ItemScanResult?> TryAiOnlyAsync(
        StashCell? cell,
        ItemDefinition? tooltip,
        List<string> tooltipOcr,
        int x, int y, int scanId,
        ItemScanDebugReport? debug,
        CancellationToken ct,
        List<ItemDefinition>? ocrHits = null,
        LensHit? iconHit = null)
    {
        if (!ItemAiIdentifier.IsConfigured)
        {
            debug?.Notes.Add("ai: skipped (disabled or no API key)");
            return null;
        }

        var slotW = cell?.SlotW ?? iconHit?.SlotW ?? 1;
        var slotH = cell?.SlotH ?? iconHit?.SlotH ?? 1;
        var candidates = new List<ItemDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(ItemDefinition? item)
        {
            if (item == null || !seen.Add(item.Id)) return;
            candidates.Add(item);
        }

        Add(tooltip);
        Add(iconHit?.Item);
        if (ocrHits != null)
        {
            foreach (var hit in ocrHits)
                Add(hit);
        }

        if (cell != null)
        {
            foreach (var (item, _, _) in _lens.TopCandidates(cell.Icon, slotW, slotH, 20))
                Add(item);
        }

        var ocrBlob = string.Join(" | ", tooltipOcr.Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        if (ocrBlob.Length >= 2)
        {
            foreach (var item in _catalog.Search(ocrBlob, 20))
                Add(item);
        }

        StatusChanged?.Invoke(Loc.T("ItemScan.Status.Ai"));
        debug?.Notes.Add($"ai: fallback → {App.Settings.ItemScanAiProvider}, {candidates.Count} catalog hints");

        var (itemHit, note, raw) = await ItemAiIdentifier.IdentifyAsync(
            x, y, slotW, slotH, ocrBlob, candidates, _catalog, ct).ConfigureAwait(false);
        debug?.Notes.Add(note);
        if (debug != null)
        {
            debug.AiNote = note;
            debug.AiRaw = string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        if (itemHit == null) return null;
        return Result(itemHit, 0.97, "ai", x, y, scanId, itemHit.Width, itemHit.Height);
    }

    private static ItemScanResult Result(LensHit hit, int x, int y, int scanId) =>
        Result(hit.Item, hit.Confidence, hit.Mode, x, y, scanId, hit.SlotW, hit.SlotH);

    private static ItemScanResult Result(
        ItemDefinition item, double confidence, string mode, int x, int y, int scanId, int slotW, int slotH) => new()
    {
        Item = item,
        Confidence = confidence,
        Mode = mode,
        ScreenX = x,
        ScreenY = y,
        SlotWidth = slotW,
        SlotHeight = slotH,
        ScanId = scanId
    };

    private void ThrowIfSuperseded(int scanId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (scanId != Volatile.Read(ref _latestScanId))
            throw new OperationCanceledException();
    }

    public void Dispose()
    {
        CancelPendingScans();
        try { _scanCts?.Dispose(); } catch { /* ignore */ }
        _scanCts = null;
        _catalog.ItemsUpdated -= OnCatalogUpdated;
        _lens.Dispose();
    }
}

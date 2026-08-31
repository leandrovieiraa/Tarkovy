using System.Drawing;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class ItemScanService : IDisposable
{
    private readonly ItemCatalog _catalog;
    private readonly ItemIconMatcher _matcher = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private CancellationTokenSource? _scanCts;
    private int _latestScanId;
    private bool _indexReady;

    private static readonly (int W, int H)[] IconSizes =
        [(1, 1), (1, 2), (2, 1), (2, 2), (2, 3), (3, 2)];

    private static readonly TimeSpan IconCaptureDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan HighlightRetryDelay = TimeSpan.FromMilliseconds(80);

    public ItemScanService(ItemCatalog catalog)
    {
        _catalog = catalog;
        _catalog.ItemsUpdated += OnCatalogUpdated;
    }

    public bool IndexReady => _indexReady && _matcher.IsReady;

    public event Action<ItemScanResult>? ScanCompleted;
    public event Action<string>? ScanFailed;
    public event Action<string>? StatusChanged;

    private void OnCatalogUpdated()
    {
        _indexReady = false;
        _matcher.ResetIndex();
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        if (!_catalog.IsReady)
            await _catalog.LoadAsync(ct).ConfigureAwait(false);
        if (_indexReady && _matcher.IsReady) return;
        StatusChanged?.Invoke(Loc.T("ItemScan.Status.Indexing"));
        await _matcher.EnsureIndexAsync(_catalog, null, ct).ConfigureAwait(false);
        _indexReady = _matcher.IsReady;
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
        List<IconSnapshot>? iconSnapshots = null;
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
                debug.Notes.Add("Shift+clique no centro do icone no inventario/stash (nao na janela de inspecao).");
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
                        s => iconSnapshots = s).ConfigureAwait(false);
                else
                {
                    nameSnapshot = CaptureNameSnapshot(x, y);
                    success = await TryScanNameFromSnapshotAsync(nameSnapshot, x, y, scanId, debug)
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
                if (iconSnapshots != null)
                {
                    var n = 0;
                    foreach (var snap in iconSnapshots.OrderBy(s => s.W * s.H))
                    {
                        n++;
                        if (n > 8) break;
                        saveCrops.Add((snap.W, snap.H, (Bitmap)snap.Icon.Clone(), $"crop{n}-icon"));
                        if (snap.Label != null && !ReferenceEquals(snap.Label, snap.Icon))
                            saveCrops.Add((snap.W, snap.H, (Bitmap)snap.Label.Clone(), $"crop{n}-label"));
                    }
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
            DisposeSnapshots(iconSnapshots);
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
        if (debug?.Notes.Any(n => n.StartsWith("template-reject")) == true)
            return Loc.T("ItemScan.Error.Ambiguous");
        return Loc.T("ItemScan.Error.NoMatch");
    }

    private static void DisposeSnapshots(List<IconSnapshot>? snapshots)
    {
        if (snapshots == null) return;
        foreach (var snap in snapshots)
        {
            snap.Icon.Dispose();
            if (snap.Label != null && !ReferenceEquals(snap.Label, snap.Icon))
                snap.Label.Dispose();
        }
        snapshots.Clear();
    }

    private async Task<ItemScanResult?> ScanIconAsync(
        int x, int y, int scanId, ItemScanDebugReport? debug, CancellationToken ct,
        Action<Bitmap> setRegion, Action<Bitmap> setBefore, Action<List<IconSnapshot>> setSnapshots)
    {
        List<(string Label, Bitmap Bmp)>? hoverStrips = null;
        Bitmap? before = null;
        Bitmap? region = null;
        List<IconSnapshot>? snapshots = null;
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

            snapshots = BuildIconSnapshots(region, before, originX, originY, x, y, localX, localY, debug);
            if (snapshots.TrueForAll(s => !s.Highlighted))
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
                DisposeSnapshots(snapshots);
                snapshots = BuildIconSnapshots(region, before, originX, originY, x, y, localX, localY, debug);
            }

            setSnapshots(snapshots);

            if (debug != null)
            {
                debug.ScanOriginX = originX;
                debug.ScanOriginY = originY;
                debug.LocalClickX = localX;
                debug.LocalClickY = localY;
                debug.Catalog.CatalogReady = _catalog.IsReady;
                debug.Catalog.IndexReady = IndexReady;
                debug.Catalog.ItemCount = _catalog.Items.Count;
                debug.Catalog.I18nReady = ItemLocalizedNames.IsReady;
                debug.Notes.Add(Loc.IsPortuguese
                    ? ItemLocalizedNames.IsReady
                        ? "Scan PT: OCR pt-BR + nomes json.tarkov.dev/items_pt."
                        : $"Scan PT: nomes indisponiveis — {ItemLocalizedNames.LastError ?? "cache vazio"}."
                    : "Scan EN: OCR en-US + nomes do catalogo.");
                debug.Notes.Add("Icon scan: highlight template (exact size) → unique tooltip → in-cell OCR.");
                debug.Notes.Add($"templates-indexed: {_matcher.IndexedTemplateCount}");
            }

            var tooltipOcr = new List<string>();
            var tip = await MatchTooltipAsync(hoverStrips, before, region, localX, localY, debug, tooltipOcr)
                .ConfigureAwait(false);
            hoverStrips = null;
            if (tip != null)
                debug?.Notes.Add($"tooltip: {ItemDisplayNames.ShortName(tip)}");
            else
            {
                tip = await MatchTightTooltipAsync(x, y, debug, tooltipOcr).ConfigureAwait(false);
                if (tip != null)
                    debug?.Notes.Add($"tooltip-tight: {ItemDisplayNames.ShortName(tip)}");
            }

            if (!IndexReady)
                return tip != null ? Result(tip, 0.99, "tooltip", x, y, scanId) : null;

            return await ScanIconFromSnapshotsAsync(
                snapshots ?? [], x, y, scanId, debug, tip, tooltipOcr, ct).ConfigureAwait(false);
        }
        finally
        {
            if (hoverStrips != null)
                TooltipScanner.DisposeStrips(hoverStrips);
        }
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
        AbsorbTooltipOcr(lines, debug, tooltipOcr);
        return item;
    }

    private async Task<ItemDefinition?> MatchTightTooltipAsync(
        int clickX, int clickY, ItemScanDebugReport? debug, List<string> tooltipOcr)
    {
        var strips = TooltipScanner.CaptureTight(clickX, clickY);
        if (strips.Count == 0) return null;
        var (item, lines) = await TooltipScanner.TryMatchCapturedAsync(
            strips, _catalog, []).ConfigureAwait(false);
        AbsorbTooltipOcr(lines, debug, tooltipOcr);
        return item;
    }

    private static void AbsorbTooltipOcr(
        List<OcrDebugLine> lines, ItemScanDebugReport? debug, List<string> tooltipOcr)
    {
        debug?.Ocr.AddRange(lines);
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line.RawText))
                tooltipOcr.Add(line.RawText);
        }
    }

    private static ItemScanResult Result(
        ItemDefinition item, double confidence, string mode, int x, int y, int scanId) => new()
    {
        Item = item,
        Confidence = confidence,
        Mode = mode,
        ScreenX = x,
        ScreenY = y,
        SlotWidth = item.Width,
        SlotHeight = item.Height,
        ScanId = scanId
    };

    private void ThrowIfSuperseded(int scanId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (scanId != Volatile.Read(ref _latestScanId))
            throw new OperationCanceledException();
    }

    private sealed record IconSnapshot(int W, int H, Bitmap Icon, bool Highlighted, Bitmap? Label);

    private static List<IconSnapshot> BuildIconSnapshots(
        Bitmap region, Bitmap? beforeRegion, int originX, int originY, int clickX, int clickY, int localX, int localY,
        ItemScanDebugReport? debug)
    {
        var list = new List<IconSnapshot>();

        if (HighlightCrop.TryExtract(region, localX, localY, out var hw, out var hh, out var iconBmp, out var labelBmp,
                out var method, beforeRegion)
            && iconBmp != null)
        {
            list.Add(new IconSnapshot(hw, hh, iconBmp, true, labelBmp));
            debug?.Notes.Add(
                $"highlight ({method}): {hw}x{hh} icon {iconBmp.Width}x{iconBmp.Height}px" +
                (labelBmp != null ? $" label {labelBmp.Width}x{labelBmp.Height}px." : "."));
            return list;
        }

        debug?.Notes.Add("highlight: nao detectado (HSV + frame-diff). Fallback grade.");

        foreach (var crop in ExtractSlotCrops(region, originX, originY, clickX, clickY))
            list.Add(new IconSnapshot(crop.W, crop.H, crop.Bmp, false, null));

        return list;
    }

    private static List<(int W, int H, Bitmap Bmp)> ExtractSlotCrops(
        Bitmap region, int originX, int originY, int clickX, int clickY)
    {
        var list = new List<(int, int, Bitmap)>();
        var seen = new HashSet<string>();

        foreach (var (w, h) in IconSizes)
        {
            foreach (var (left, top, bw, bh) in InventoryGrid.SnappedSlotsAt(clickX, clickY, w, h))
            {
                var sx = left - originX;
                var sy = top - originY;
                if (sx < 0 || sy < 0 || sx + bw > region.Width || sy + bh > region.Height)
                    continue;

                var key = $"{sx}:{sy}:{w}x{h}";
                if (!seen.Add(key)) continue;
                list.Add((w, h, ScreenCapture.Crop(region, sx, sy, bw, bh)));
                if (list.Count >= 6) return list;
            }
        }

        return list;
    }

    private static Bitmap CaptureNameSnapshot(int x, int y)
    {
        var scale = ScreenCapture.GameScale();
        var w = (int)Math.Round(520 * scale);
        var h = (int)Math.Round(36 * scale);
        var left = x - (int)Math.Round(10 * scale);
        var top = y - (int)Math.Round(18 * scale);
        return ScreenCapture.CaptureRegion(left, top, w, h);
    }

    private sealed record ScanCandidate(
        ItemDefinition Item,
        double Confidence,
        double SecondConfidence,
        int SlotW,
        int SlotH,
        int Slots);

    private async Task<ItemScanResult?> ScanIconFromSnapshotsAsync(
        List<IconSnapshot> snapshots, int x, int y, int scanId, ItemScanDebugReport? debug,
        ItemDefinition? tooltip, List<string> tooltipOcr, CancellationToken ct)
    {
        var hasHighlight = snapshots.Any(s => s.Highlighted);

        var templateResult = await Task.Run(() => ScanIconFromTemplates(snapshots, x, y, scanId, debug, hasHighlight))
            .ConfigureAwait(false);

        var ocrHits = new List<(ItemDefinition Item, int W, int H, bool Highlighted)>();
        var ocrSnaps = hasHighlight
            ? snapshots.Where(s => s.Highlighted)
            : snapshots.Take(4);

        var ocrAttempts = 0;
        foreach (var snap in ocrSnaps)
        {
            if (++ocrAttempts > 4) break;

            async Task AbsorbSnapAsync(Bitmap bmp)
            {
                var (lines, hits) = await IconShortNameScanner.TryMatchAllWithDebugAsync(
                    bmp, snap.W, snap.H, _catalog, debug != null ? [] : null, snap.Highlighted)
                    .ConfigureAwait(false);
                if (debug != null)
                    debug.Ocr.AddRange(lines);

                foreach (var item in hits)
                    ocrHits.Add((item, snap.W, snap.H, snap.Highlighted));
            }

            await AbsorbSnapAsync(snap.Icon).ConfigureAwait(false);
            if (snap.Label != null && !ReferenceEquals(snap.Label, snap.Icon))
                await AbsorbSnapAsync(snap.Label).ConfigureAwait(false);
        }

        var uniqueOcr = ocrHits
            .GroupBy(h => h.Item.Id)
            .Select(g => g.OrderByDescending(h => h.Highlighted).ThenByDescending(h => h.W * h.H).First())
            .Where(h => !_catalog.IsBroadShortLabel(h.Item))
            .ToList();

        if (uniqueOcr.Count > 1
            && uniqueOcr.Select(h => ItemDisplayNames.Name(h.Item)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
        {
            var ordered = uniqueOcr.OrderBy(h => ItemDisplayNames.ShortName(h.Item).Replace(" ", "").Length).ToList();
            var shortest = ordered[0];
            var longest = ordered[^1];
            var tipSn = tooltip != null ? ItemDisplayNames.ShortName(tooltip) : "";
            var keepLonger = tipSn.Equals(ItemDisplayNames.ShortName(longest.Item), StringComparison.OrdinalIgnoreCase);
            uniqueOcr = [keepLonger ? longest : shortest];
            debug?.Notes.Add($"ocr-variant: {ItemDisplayNames.ShortName(uniqueOcr[0].Item)} (shared name)");
        }

        if (templateResult != null && uniqueOcr.Count > 0
            && uniqueOcr.All(h => h.Item.Id != templateResult.Item.Id))
        {
            debug?.Notes.Add(
                $"template-vs-ocr: dropping {ItemDisplayNames.ShortName(templateResult.Item)}, in-cell label disagrees.");
            templateResult = null;
        }

        if (uniqueOcr.Count > 1)
        {
            if (tooltip != null && uniqueOcr.Any(h => h.Item.Id == tooltip.Id))
            {
                uniqueOcr = uniqueOcr.Where(h => h.Item.Id == tooltip.Id).ToList();
                debug?.Notes.Add($"ocr-agree-tooltip: {ItemDisplayNames.ShortName(tooltip)}");
            }
            else
            {
                var loose = uniqueOcr.Select(h => h.Item).Where(i => IsLooseAmmo(i)).ToList();
                var boxes = uniqueOcr.Select(h => h.Item).Where(IsAmmoBox).ToList();
                var familyOk = tooltip == null
                    || uniqueOcr.Any(h => h.Item.Id == tooltip.Id)
                    || (tooltip != null && IsAmmoFamily(tooltip)
                        && (loose.Any(i => i.Id == tooltip.Id) || boxes.Any(i => i.Id == tooltip.Id)));
                if (familyOk && loose.Count == 1 && boxes.Count >= 1)
                {
                    var pick = PreferAmmoByTooltip(tooltip, loose[0], boxes[0]);
                    debug?.Notes.Add($"ocr-ammo: {ItemDisplayNames.Name(pick)} (loose vs pack)");
                    uniqueOcr = uniqueOcr.Where(h => h.Item.Id == pick.Id).ToList();
                }
                else if (tooltip != null)
                {
                    debug?.Notes.Add(
                        $"ocr-ignored: [{string.Join(",", uniqueOcr.Select(h => ItemDisplayNames.ShortName(h.Item)))}] vs tooltip {ItemDisplayNames.ShortName(tooltip)}");
                    uniqueOcr = [];
                }
            }
        }

        if (debug != null)
        {
            debug.Notes.Add(
                $"resolve: template={(templateResult != null ? ItemDisplayNames.ShortName(templateResult.Item) : "none")} " +
                $"tooltip={(tooltip != null ? ItemDisplayNames.ShortName(tooltip) : "none")} " +
                $"ocr=[{string.Join(",", uniqueOcr.Select(h => ItemDisplayNames.ShortName(h.Item)))}]");
        }

        ItemScanResult? local = null;
        if (templateResult != null)
        {
            if (tooltip?.Id == templateResult.Item.Id || uniqueOcr.Any(h => h.Item.Id == templateResult.Item.Id))
                templateResult.Confidence = Math.Max(templateResult.Confidence, 0.97);
            if (tooltip != null && tooltip.Id != templateResult.Item.Id)
                debug?.Notes.Add(
                    $"tooltip-vs-template: keeping icon {ItemDisplayNames.ShortName(templateResult.Item)} " +
                    $"(tooltip was {ItemDisplayNames.ShortName(tooltip)}).");
            local = templateResult;
        }
        else if (uniqueOcr.Count == 1)
        {
            var ocr = uniqueOcr[0];
            if (tooltip != null && tooltip.Id != ocr.Item.Id)
            {
                if (IsAmmoFamily(tooltip)
                    && (!IsAmmoFamily(ocr.Item) || _catalog.IsBroadShortLabel(ocr.Item)))
                    local = Result(tooltip, 0.99, "tooltip", x, y, scanId);
                else
                {
                    debug?.Notes.Add(
                        $"tooltip-vs-ocr: keeping in-cell {ItemDisplayNames.ShortName(ocr.Item)} " +
                        $"(tooltip was {ItemDisplayNames.ShortName(tooltip)}).");
                    local = Result(ocr.Item, 0.99, ocr.Highlighted ? "shortname-highlight" : "shortname", x, y, scanId);
                }
            }
            else
                local = Result(ocr.Item, 0.99, ocr.Highlighted ? "shortname-highlight" : "shortname", x, y, scanId);
        }
        else if (tooltip != null)
            local = Result(tooltip, 0.99, "tooltip", x, y, scanId);
        else if (uniqueOcr.Count > 1)
            debug?.Notes.Add("ocr-ambiguous: several short names, no template winner.");

        var templateConfirmed = templateResult != null
            && templateResult.Mode is "icon-highlight" or "icon";
        if (ItemAiIdentifier.IsConfigured && !templateConfirmed)
        {
            var ai = await TryAiIdentifyAsync(
                snapshots, tooltip, tooltipOcr, uniqueOcr, x, y, scanId, debug, ct).ConfigureAwait(false);
            if (ai != null)
            {
                if (local != null && local.Item.Id != ai.Item.Id)
                    debug?.Notes.Add(
                        $"ai-override: {ItemDisplayNames.ShortName(local.Item)} → {ItemDisplayNames.ShortName(ai.Item)}");
                return ai;
            }
        }
        else if (debug != null && !ItemAiIdentifier.IsConfigured)
            debug.Notes.Add("ai: skipped (disabled or no API key)");
        else if (debug != null && templateConfirmed)
            debug.Notes.Add("ai: skipped (template confirmed)");

        return local;
    }

    private async Task<ItemScanResult?> TryAiIdentifyAsync(
        List<IconSnapshot> snapshots,
        ItemDefinition? tooltip,
        List<string> tooltipOcr,
        List<(ItemDefinition Item, int W, int H, bool Highlighted)> uniqueOcr,
        int x, int y, int scanId, ItemScanDebugReport? debug, CancellationToken ct)
    {
        if (!ItemAiIdentifier.IsConfigured) return null;

        var snap = snapshots.FirstOrDefault(s => s.Highlighted) ?? snapshots.FirstOrDefault();
        if (snap == null) return null;

        var candidates = new List<ItemDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(ItemDefinition? item)
        {
            if (item == null || !seen.Add(item.Id)) return;
            candidates.Add(item);
        }

        Add(tooltip);
        foreach (var hit in uniqueOcr)
            Add(hit.Item);
        foreach (var (item, _, _) in _matcher.MatchTopCandidates(snap.Icon, snap.W, snap.H, snap.Highlighted, 20))
            Add(item);

        var ocrBits = tooltipOcr.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (debug != null)
        {
            foreach (var line in debug.Ocr)
            {
                if (!string.IsNullOrWhiteSpace(line.RawText))
                    ocrBits.Add(line.RawText);
            }
        }
        var ocrBlob = string.Join(" | ", ocrBits.Distinct(StringComparer.OrdinalIgnoreCase));
        if (ocrBlob.Length >= 2)
        {
            foreach (var item in _catalog.Search(ocrBlob, 20))
                Add(item);
        }
        foreach (var bit in ocrBits)
        {
            var cleaned = ItemCatalog.SanitizeInCellOcr(bit);
            if (cleaned.Length is >= 2 and <= 16)
            {
                Add(_catalog.MatchExactShortLabel(cleaned));
                foreach (var item in _catalog.Search(cleaned, 8))
                    Add(item);
            }
        }

        StatusChanged?.Invoke(Loc.T("ItemScan.Status.Ai"));
        debug?.Notes.Add($"ai: screenshot+click → {App.Settings.ItemScanAiProvider}, {candidates.Count} catalog hints");

        var (itemHit, note, raw) = await ItemAiIdentifier.IdentifyAsync(
            x, y, snap.W, snap.H, ocrBlob, candidates, _catalog, ct).ConfigureAwait(false);
        debug?.Notes.Add(note);
        if (debug != null)
        {
            debug.AiNote = note;
            debug.AiRaw = string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        if (itemHit == null) return null;

        return Result(itemHit, 0.97, "ai", x, y, scanId);
    }

    private static bool IsAmmoBox(ItemDefinition item) =>
        item.Types.Any(t => t.Equals("ammoBox", StringComparison.OrdinalIgnoreCase));

    private static bool IsLooseAmmo(ItemDefinition item) =>
        item.Types.Any(t => t.Equals("ammo", StringComparison.OrdinalIgnoreCase)) && !IsAmmoBox(item);

    private static bool IsAmmoFamily(ItemDefinition item) => IsLooseAmmo(item) || IsAmmoBox(item);

    private static ItemDefinition PreferAmmoByTooltip(
        ItemDefinition? tooltip, ItemDefinition loose, ItemDefinition box)
    {
        if (tooltip?.Id == box.Id) return box;
        if (tooltip?.Id == loose.Id) return loose;
        var name = tooltip != null ? ItemDisplayNames.Name(tooltip) : "";
        if (name.Contains("Pacote", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pack", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Caixa", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Box", StringComparison.OrdinalIgnoreCase))
            return box;
        return loose;
    }

    private ItemScanResult? ScanIconFromTemplates(
        List<IconSnapshot> snapshots, int x, int y, int scanId,
        ItemScanDebugReport? debug, bool highlightOnly)
    {
        var pool = highlightOnly
            ? snapshots.Where(s => s.Highlighted).ToList()
            : snapshots.Take(4).ToList();

        var raw = new List<(ScanCandidate Candidate, bool FromHighlight)>();
        foreach (var snap in pool)
        {
            var w = snap.W;
            var h = snap.H;
            var highlighted = snap.Highlighted;
            // Exact slot crop only — the padded label crop slides 1×1 templates
            // over the neighbor and scores ~0.46 against every ammo pack.
            var search = snap.Icon;
            var (item, conf, second, matchW, matchH) = _matcher.MatchIconInRegion(
                search, w, h, highlighted, searchSmaller: highlighted);
            if (debug != null)
            {
                foreach (var (topItem, score, topSecond) in _matcher.MatchTopCandidates(search, matchW > 0 ? matchW : w, matchH > 0 ? matchH : h, highlighted, 5))
                {
                    debug.Templates.Add(new TemplateDebugLine
                    {
                        SlotW = matchW > 0 ? matchW : w,
                        SlotH = matchH > 0 ? matchH : h,
                        ItemId = topItem.Id,
                        Name = ItemDisplayNames.Name(topItem),
                        ShortName = ItemDisplayNames.ShortName(topItem),
                        Score = score,
                        SecondScore = topSecond
                    });
                }
            }

            if (item == null) continue;

            var slots = matchW * matchH;
            if (conf < PrefilterConfidence(slots, highlighted)) continue;
            raw.Add((new ScanCandidate(item, conf, second, matchW, matchH, slots), highlighted));
        }

        if (raw.Count == 0)
            return null;

        var ranked = raw
            .GroupBy(r => r.Candidate.Item.Id)
            .Select(g =>
            {
                var best = g.OrderByDescending(r => r.Candidate.Confidence)
                    .ThenByDescending(r => r.FromHighlight)
                    .ThenByDescending(r => r.Candidate.Slots)
                    .First();
                var c = best.Candidate;
                return new
                {
                    c.Item,
                    RawConfidence = c.Confidence,
                    InnerMargin = c.Confidence - c.SecondConfidence,
                    FromHighlight = best.FromHighlight,
                    c.SecondConfidence,
                    c.Slots,
                    c.SlotW,
                    c.SlotH,
                    Hits = g.Count()
                };
            })
            .OrderByDescending(t => t.RawConfidence)
            .ThenByDescending(t => t.FromHighlight)
            .ThenByDescending(t => t.Slots)
            .ThenByDescending(t => t.Hits)
            .ToList();

        var top = ranked[0];
        if (ranked.Count > 1 && top.Slots < ranked[1].Slots
            && top.RawConfidence - ranked[1].RawConfidence < 0.06)
        {
            debug?.Notes.Add(
                $"template: {top.Slots}-slot {ItemDisplayNames.ShortName(top.Item)} not far enough " +
                $"ahead of {ranked[1].Slots}-slot {ItemDisplayNames.ShortName(ranked[1].Item)}.");
            top = ranked[1];
        }
        // Compare RAW scores — a highlight bonus used to inflate 78% vs 77%
        // (Gorro UX PRO on Tala) into a "pass".
        var runnerUpRaw = ranked.Count > 1 ? ranked[1].RawConfidence : top.SecondConfidence;
        var margin = top.RawConfidence - runnerUpRaw;

        var minConf = top.FromHighlight ? 0.84 : 0.90;
        var minMargin = top.FromHighlight ? 0.03 : 0.05;
        var minInner = top.FromHighlight ? 0.02 : 0.04;

        var tiedIcon = top.InnerMargin < minInner && top.RawConfidence < 0.91;
        var ambiguous = margin < minMargin || top.RawConfidence < minConf || tiedIcon;

        if (ambiguous)
        {
            if (debug != null)
            {
                debug.Notes.Add(
                    $"template-reject: top={top.Item.Id} conf={top.RawConfidence:F3} margin={margin:F3} inner={top.InnerMargin:F3} slots={top.Slots} (need >={minConf:F2} / margin>={minMargin:F3} / inner>={minInner:F3})");
                if (App.Settings.ItemScanSlotPx == 0)
                    debug.Notes.Add("Dica: se os crops estao desalinhados em 01-scan-region.png, ajuste Tamanho do slot nas Settings (1080p/1440p/4K).");
            }
            return null;
        }

        if (top.RawConfidence < 0.96)
            StatusChanged?.Invoke(Loc.T("ItemScan.Status.LowConfidence"));

        return new ItemScanResult
        {
            Item = top.Item,
            Confidence = top.RawConfidence,
            Mode = top.FromHighlight ? "icon-highlight" : "icon",
            ScreenX = x,
            ScreenY = y,
            SlotWidth = top.SlotW,
            SlotHeight = top.SlotH,
            ScanId = scanId
        };
    }

    private static double PrefilterConfidence(int slots, bool highlighted) =>
        highlighted ? 0.80 : (slots <= 2 ? 0.88 : 0.86);

    private async Task<ItemScanResult?> TryScanNameFromSnapshotAsync(
        Bitmap bmp, int x, int y, int scanId, ItemScanDebugReport? debug)
    {
        var text = await OcrHelper.ReadTextAsync(bmp).ConfigureAwait(false);
        if (debug != null)
        {
            debug.Ocr.Add(new OcrDebugLine
            {
                Crop = "name-bar",
                RawText = text
            });
        }

        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 3)
            return null;

        var item = _catalog.MatchByName(text);
        if (debug != null && debug.Ocr.Count > 0)
        {
            debug.Ocr[^1].MatchedItemId = item?.Id;
            debug.Ocr[^1].MatchedShortName = item != null ? ItemDisplayNames.Name(item) : null;
        }

        if (item == null)
            return null;

        return new ItemScanResult
        {
            Item = item,
            Confidence = 1,
            Mode = "name",
            ScreenX = x,
            ScreenY = y,
            SlotWidth = item.Width,
            SlotHeight = item.Height,
            ScanId = scanId
        };
    }

    public void Dispose()
    {
        CancelPendingScans();
        try { _scanCts?.Dispose(); } catch { /* ignore */ }
        _scanCts = null;
        _catalog.ItemsUpdated -= OnCatalogUpdated;
        _matcher.Dispose();
    }
}

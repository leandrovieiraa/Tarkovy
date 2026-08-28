using System.Drawing;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class ItemScanService : IDisposable
{
    private readonly ItemCatalog _catalog;
    private readonly ItemIconMatcher _matcher = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private bool _indexReady;

    public ItemScanService(ItemCatalog catalog) => _catalog = catalog;

    public bool IndexReady => _indexReady && _matcher.IsReady;

    public event Action<ItemScanResult>? ScanCompleted;
    public event Action<string>? ScanFailed;
    public event Action<string>? StatusChanged;

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

    public void ScanIconAt(int x, int y) => _ = RunScanAsync(x, y, icon: true);

    public void ScanNameAt(int x, int y) => _ = RunScanAsync(x, y, icon: false);

    private async Task RunScanAsync(int x, int y, bool icon)
    {
        if (!await _scanLock.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (!_catalog.IsReady)
                await _catalog.LoadAsync().ConfigureAwait(false);
            if (!IndexReady)
                await EnsureReadyAsync().ConfigureAwait(false);
            if (!IndexReady)
            {
                ScanFailed?.Invoke(Loc.T("ItemScan.Error.Index"));
                return;
            }

            if (icon)
                await ScanIconInternalAsync(x, y).ConfigureAwait(false);
            else
                await ScanNameInternalAsync(x, y).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ScanFailed?.Invoke(ex.Message);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task ScanIconInternalAsync(int x, int y)
    {
        await Task.Yield();
        var scale = ScreenCapture.GameScale();
        var slot = (int)Math.Round(63 * scale);
        var sizes = new[] { (1, 1), (1, 2), (2, 1), (2, 2), (2, 3), (3, 2) };

        ItemDefinition? bestItem = null;
        var bestConf = 0.0;
        foreach (var (w, h) in sizes)
        {
            var cw = slot * w;
            var ch = slot * h;
            using var bmp = ScreenCapture.CaptureAround(x, y, cw, ch);
            var (item, conf) = _matcher.MatchIcon(bmp, w, h);
            if (item != null && conf > bestConf)
            {
                bestConf = conf;
                bestItem = item;
            }
        }

        if (bestItem == null || bestConf < 0.72)
        {
            ScanFailed?.Invoke(Loc.T("ItemScan.Error.NoMatch"));
            return;
        }

        ScanCompleted?.Invoke(new ItemScanResult
        {
            Item = bestItem,
            Confidence = bestConf,
            Mode = "icon",
            ScreenX = x,
            ScreenY = y
        });
    }

    private async Task ScanNameInternalAsync(int x, int y)
    {
        var scale = ScreenCapture.GameScale();
        var w = (int)Math.Round(520 * scale);
        var h = (int)Math.Round(36 * scale);
        var left = x - (int)Math.Round(10 * scale);
        var top = y - (int)Math.Round(18 * scale);
        using var bmp = ScreenCapture.CaptureRegion(left, top, w, h);
        var text = await OcrHelper.ReadTextAsync(bmp).ConfigureAwait(false);
        var item = _catalog.MatchByName(text);
        if (item == null)
        {
            ScanFailed?.Invoke(Loc.T("ItemScan.Error.NoMatch"));
            return;
        }

        ScanCompleted?.Invoke(new ItemScanResult
        {
            Item = item,
            Confidence = 1,
            Mode = "name",
            ScreenX = x,
            ScreenY = y
        });
    }

    public void Dispose() => _matcher.Dispose();
}

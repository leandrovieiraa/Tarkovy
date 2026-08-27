using System.Collections.Concurrent;
using System.IO;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class ScreenshotWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentQueue<string> _deleteQueue = new();
    private readonly System.Timers.Timer _deleteTimer = new(180);
    private readonly System.Timers.Timer _debounceTimer = new(120);
    private PlayerFix? _pending;
    private readonly object _gate = new();
    private bool _disposed;
    private string? _keepLast;

    public string Folder { get; set; } = "";
    public bool DeleteAfterRead { get; set; } = true;
    public bool KeepLast { get; set; }

    public event Action<PlayerFix>? PositionUpdated;
    public event Action<string>? Status;
    public event Action<int>? DeletedCount;

    public int DeletedThisRaid { get; private set; }

    public ScreenshotWatcher()
    {
        _deleteTimer.Elapsed += (_, _) => DrainDeletes();
        _deleteTimer.AutoReset = true;
        _debounceTimer.Elapsed += (_, _) => FlushPending();
        _debounceTimer.AutoReset = false;
    }

    public void Start()
    {
        Stop();
        DeletedThisRaid = 0;
        if (string.IsNullOrWhiteSpace(Folder) || !Directory.Exists(Folder))
        {
            Status?.Invoke(Loc.T("Footer.ShotsMissing"));
            return;
        }

        _watcher = new FileSystemWatcher(Folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
            Filter = "*.*",
            EnableRaisingEvents = true
        };
        _watcher.Created += OnFile;
        _watcher.Renamed += (_, e) => OnFile(this, e);
        _deleteTimer.Start();
        Status?.Invoke(Loc.T("Footer.ShotsWatching"));
    }

    public void Stop()
    {
        _deleteTimer.Stop();
        _debounceTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }

    public void ResetRaidCounters() => DeletedThisRaid = 0;

    public void SweepLeftovers()
    {
        if (string.IsNullOrWhiteSpace(Folder) || !Directory.Exists(Folder) || !DeleteAfterRead)
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(Folder))
            {
                if (KeepLast && string.Equals(file, _keepLast, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ScreenshotParser.LooksLikeEftScreenshot(file))
                    _deleteQueue.Enqueue(file);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void OnFile(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!ScreenshotParser.LooksLikeEftScreenshot(e.FullPath))
                return;
            if (!ScreenshotParser.TryParse(e.Name ?? e.FullPath, out var fix))
                return;

            lock (_gate)
            {
                _pending = fix;
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }

            if (DeleteAfterRead)
            {
                if (KeepLast && _keepLast != null &&
                    !string.Equals(_keepLast, e.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _deleteQueue.Enqueue(_keepLast);
                }

                if (KeepLast)
                    _keepLast = e.FullPath;
                else
                    _deleteQueue.Enqueue(e.FullPath);
            }
        }
        catch (Exception ex)
        {
            Status?.Invoke(Loc.T("Footer.ShotError", ex.Message));
        }
    }

    private void FlushPending()
    {
        PlayerFix? fix;
        lock (_gate)
        {
            fix = _pending;
            _pending = null;
        }

        if (fix != null)
            PositionUpdated?.Invoke(fix);
    }

    private void DrainDeletes()
    {
        var retries = new List<string>();
        while (_deleteQueue.TryDequeue(out var path))
        {
            try
            {
                if (!File.Exists(path)) continue;
                File.Delete(path);
                DeletedThisRaid++;
                DeletedCount?.Invoke(DeletedThisRaid);
            }
            catch
            {
                retries.Add(path);
            }
        }

        foreach (var r in retries)
            _deleteQueue.Enqueue(r);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _deleteTimer.Dispose();
        _debounceTimer.Dispose();
    }
}

using System.IO;
using System.Text;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class LogWatcher : IDisposable
{
    private readonly MapCatalog _maps;
    private FileSystemWatcher? _dirWatcher;
    private FileStream? _stream;
    private StreamReader? _reader;
    private readonly System.Timers.Timer _poll = new(400);
    private string? _currentLog;
    private long _offset;
    private bool _disposed;

    public string LogsFolder { get; set; } = "";

    public event Action<MapDefinition>? MapDetected;
    public event Action? RaidStarted;
    public event Action? RaidEnded;
    public event Action<string>? Status;

    public LogWatcher(MapCatalog maps)
    {
        _maps = maps;
        _poll.Elapsed += (_, _) => Pump();
        _poll.AutoReset = true;
    }

    public void Start()
    {
        StopWatchers();
        if (string.IsNullOrWhiteSpace(LogsFolder) || !Directory.Exists(LogsFolder))
        {
            Status?.Invoke(Loc.T("Footer.LogsMissing"));
            return;
        }

        _dirWatcher = new FileSystemWatcher(LogsFolder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
            Filter = "*.log",
            EnableRaisingEvents = true
        };
        _dirWatcher.Created += (_, _) => AttachLatest(force: false);
        _dirWatcher.Changed += (_, e) =>
        {
            if (string.Equals(e.FullPath, _currentLog, StringComparison.OrdinalIgnoreCase))
                Pump();
        };

        AttachLatest(force: true);
        _poll.Start();
        Status?.Invoke(Loc.T("Footer.LogsWatching"));
    }

    public void Stop()
    {
        _poll.Stop();
        StopWatchers();
    }

    private void AttachLatest(bool force)
    {
        try
        {
            var latest = Directory.EnumerateFiles(LogsFolder, "*application*.log", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest == null) return;
            if (!force && string.Equals(latest.FullName, _currentLog, StringComparison.OrdinalIgnoreCase))
                return;

            _stream?.Dispose();
            _reader?.Dispose();
            _currentLog = latest.FullName;
            _stream = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _reader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            _offset = _stream.Length;
            _stream.Seek(_offset, SeekOrigin.Begin);
            Status?.Invoke(Loc.T("Footer.LogAttached", latest.Name));
        }
        catch (Exception ex)
        {
            Status?.Invoke(Loc.T("Footer.LogError", ex.Message));
        }
    }

    private readonly object _gate = new();

    private void Pump()
    {
        if (_disposed) return;
        lock (_gate)
        {
            try
            {
                if (_stream == null || _reader == null)
                {
                    AttachLatest(force: false);
                    return;
                }

                if (_stream.Length < _offset)
                    _offset = 0;
                if (_stream.Length == _offset) return;
                _stream.Seek(_offset, SeekOrigin.Begin);
                var chunk = _reader.ReadToEnd();
                _offset = _stream.Position;
                if (string.IsNullOrEmpty(chunk)) return;
                foreach (var raw in chunk.Split('\n'))
                    HandleLine(raw.TrimEnd('\r'));
            }
            catch
            {
                AttachLatest(force: true);
            }
        }
    }

    private void HandleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (line.Contains("scene preset path:", StringComparison.OrdinalIgnoreCase))
        {
            var idx = line.IndexOf("scene preset path:", StringComparison.OrdinalIgnoreCase);
            var path = line[(idx + "scene preset path:".Length)..].Trim();
            var space = path.IndexOf(' ');
            if (space > 0) path = path[..space];
            var map = _maps.ResolveFromLog(path);
            if (map != null)
            {
                MapDetected?.Invoke(map);
                Status?.Invoke(Loc.T("Footer.MapDetected", map.Name));
            }
        }

        if (line.Contains("Location:", StringComparison.Ordinal))
        {
            var idx = line.IndexOf("Location:", StringComparison.Ordinal);
            var rest = line[(idx + "Location:".Length)..].Trim();
            var end = rest.IndexOfAny([',', '|']);
            var id = end > 0 ? rest[..end].Trim() : rest.Trim();
            var map = _maps.ResolveFromLog(id);
            if (map != null)
            {
                MapDetected?.Invoke(map);
                Status?.Invoke(Loc.T("Footer.MapDetected", map.Name));
            }
        }

        if (line.Contains("application|GameStarted", StringComparison.Ordinal))
            RaidStarted?.Invoke();

        if (line.Contains("Got notification | UserMatchOver", StringComparison.Ordinal) ||
            line.Contains("Network game matching aborted", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Network game matching cancelled", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("TRACE-NetworkGameLeave", StringComparison.Ordinal) ||
            line.Contains("application|Destroying", StringComparison.Ordinal))
        {
            RaidEnded?.Invoke();
        }
    }

    private void StopWatchers()
    {
        _dirWatcher?.Dispose();
        _dirWatcher = null;
        _reader?.Dispose();
        _reader = null;
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _poll.Dispose();
    }
}

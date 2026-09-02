using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Tarkovy.Services;

public sealed record UpdateInfo(
    string Tag,
    string VersionLabel,
    string DownloadUrl,
    long SizeBytes);

public static class GitHubUpdater
{
    private static readonly HttpClient Http = CreateClient();

    public static bool CanCheck
    {
        get
        {
            if (App.Settings.AutoUpdateEnabled == false) return false;
            var path = Environment.ProcessPath ?? "";
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) return false;
            if (path.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (!CanCheck) return null;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(8));
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{ProductInfo.GitHubOwner}/{ProductInfo.GitHubRepo}/releases/latest");
            using var resp = await Http.SendAsync(req, linked.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return ParseLatest(json);
        }
        catch
        {
            return null;
        }
    }

    public static async Task DownloadAsync(
        UpdateInfo info,
        string destPath,
        IProgress<(long done, long total)>? progress,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        if (File.Exists(destPath))
            File.Delete(destPath);

        using var req = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? info.SizeBytes;
        await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 88 * 1024, true);
        var buffer = new byte[88 * 1024];
        long done = 0;
        while (true)
        {
            var n = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            done += n;
            progress?.Report((done, total));
        }
    }

    public static void ApplyAndRelaunch(string newExePath)
    {
        var current = Environment.ProcessPath
            ?? throw new InvalidOperationException("ProcessPath");
        if (!File.Exists(newExePath))
            throw new InvalidOperationException("update file missing");

        var bat = Path.Combine(Path.GetTempPath(), "tarkovy-apply-update.cmd");
        File.WriteAllText(bat, """
            @echo off
            setlocal
            set "PID=%~1"
            set "SRC=%~2"
            set "DST=%~3"
            :wait
            tasklist /FI "PID eq %PID%" 2>nul | findstr /R /C:" %PID% " >nul
            if not errorlevel 1 (
              timeout /t 1 /nobreak >nul
              goto wait
            )
            copy /Y "%SRC%" "%DST%" >nul
            if errorlevel 1 (
              ping 127.0.0.1 -n 3 >nul
              copy /Y "%SRC%" "%DST%" >nul
            )
            start "" "%DST%"
            del /F /Q "%SRC%" >nul 2>&1
            del /F /Q "%~f0" >nul 2>&1
            """);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\" {Environment.ProcessId} \"{newExePath}\" \"{current}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    public static string StagingPath(UpdateInfo info) =>
        Path.Combine(Path.GetTempPath(), "Tarkovy", $"Tarkovy-{info.Tag}.exe");

    private static UpdateInfo? ParseLatest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var remote = ParseVersion(tag);
        var local = ParseVersion(ProductInfo.AppVersion);
        if (remote == null || local == null || remote <= local) return null;

        string? url = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : "";
                if (!string.Equals(name, "Tarkovy.exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                size = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(url)) return null;
        var label = root.TryGetProperty("name", out var title) ? title.GetString() : null;
        if (string.IsNullOrWhiteSpace(label))
            label = "Dev " + remote;
        return new UpdateInfo(tag.Trim(), label!, url!, size);
    }

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            s = s[1..];
        var i = s.IndexOfAny([' ', '-']);
        if (i > 0) s = s[..i];
        return Version.TryParse(s, out var v) ? v : null;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tarkovy/{ProductInfo.AppVersion}");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }
}

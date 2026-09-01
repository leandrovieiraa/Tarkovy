using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class SquadMate
{
    public string Nick { get; set; } = "";
    public string MapId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Yaw { get; set; }
    public int Hue { get; set; }
}

public sealed class SquadHub : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private CancellationTokenSource? _loop;
    private CancellationTokenSource? _probe;
    private string _roomId = "";
    private List<SquadMate> _mates = [];

    public const int MaxPlayers = 5;

    public bool IsInRoom { get; private set; }
    public bool ProjectOnline { get; private set; }
    public string RoomCode { get; private set; } = "";
    public string Status { get; private set; } = "";
    public IReadOnlyList<SquadMate> Mates
    {
        get { lock (_gate) return _mates; }
    }

    public event Action? Changed;

    public void StartBackgroundWork()
    {
        StartProbeLoop();
        _ = TryRestoreAsync();
    }

    public async Task CreateAsync(string password, string nick, string? roomName = null, CancellationToken ct = default)
    {
        PersistCredsFromSettings();
        var code = string.IsNullOrWhiteSpace(roomName) ? NewRoomName() : roomName.Trim().ToUpperInvariant();
        if (IsInRoom && string.Equals(RoomCode, code, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L("Squad.Error.AlreadyInRoom"));
        if (await IsRoomNameTakenAsync(code, ct).ConfigureAwait(false))
            throw new InvalidOperationException(L("Squad.Error.RoomTaken"));
        if (IsInRoom) await LeaveAsync().ConfigureAwait(false);
        JsonElement json;
        try
        {
            json = await RpcAsync("squad_create", new { p_password = password, p_nick = nick, p_code = code }, ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (IsMissingRpc(ex, "squad_create"))
        {
            json = await RpcAsync("squad_create", new { p_password = password, p_nick = nick }, ct)
                .ConfigureAwait(false);
        }
        Enter(json, password, nick);
        await PublishLastAsync(ct).ConfigureAwait(false);
    }

    public static string NewRoomName()
    {
        string[] words =
        [
            "CUSTOMS", "FACTORY", "DORM", "MALL", "WOODS", "LABS",
            "STREETS", "GROUND", "SHORE", "LIGHTHOUSE", "KORD", "RAID"
        ];
        var word = words[RandomNumberGenerator.GetInt32(words.Length)];
        var tag = RandomNumberGenerator.GetInt32(0x10000).ToString("X4");
        return $"{word}-{tag}";
    }

    public async Task<bool> IsRoomNameTakenAsync(string code, CancellationToken ct = default)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        if (c.Length < 4) return false;
        try
        {
            var json = await RpcAsync("squad_name_taken", new { p_code = c }, ct).ConfigureAwait(false);
            return json.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(json.GetString(), out var b) && b,
                JsonValueKind.Number => json.TryGetInt32(out var n) && n != 0,
                _ => false
            };
        }
        catch (InvalidOperationException ex) when (IsMissingRpc(ex, "squad_name_taken"))
        {
            return false;
        }
    }

    public async Task<string> NextFreeRoomNameAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < 16; i++)
        {
            var name = NewRoomName();
            if (!await IsRoomNameTakenAsync(name, ct).ConfigureAwait(false))
                return name;
        }

        return NewRoomName();
    }

    public static string NewRoomPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    public async Task JoinAsync(string code, string password, string nick, CancellationToken ct = default)
    {
        PersistCredsFromSettings();
        if (IsInRoom) await LeaveAsync().ConfigureAwait(false);
        var json = await RpcAsync("squad_join", new { p_code = code, p_password = password, p_nick = nick }, ct).ConfigureAwait(false);
        Enter(json, password, nick);
        await PublishLastAsync(ct).ConfigureAwait(false);
    }

    public async Task LeaveAsync()
    {
        var s = App.Settings;
        var code = RoomCode;
        var pass = s.SquadPassword;
        var nick = s.SquadNickname;
        StopLoop();
        IsInRoom = false;
        RoomCode = "";
        _roomId = "";
        lock (_gate) _mates = [];
        s.SquadStayInRoom = false;
        SettingsStore.Save(s);
        SetStatus(L("Squad.Status.Idle"));
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(nick))
            return;
        try
        {
            await RpcAsync("squad_leave", new { p_code = code, p_password = pass, p_nick = nick }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }
    }

    public async Task TryRestoreAsync()
    {
        var s = App.Settings;
        if (string.IsNullOrWhiteSpace(s.SquadSupabaseUrl) ||
            string.IsNullOrWhiteSpace(s.SquadSupabaseAnonKey) ||
            string.IsNullOrWhiteSpace(s.SquadRoomCode) ||
            string.IsNullOrWhiteSpace(s.SquadPassword) ||
            string.IsNullOrWhiteSpace(s.SquadNickname) ||
            !s.SquadStayInRoom)
        {
            SetStatus(L("Squad.Status.Idle"));
            return;
        }

        try
        {
            await JoinAsync(s.SquadRoomCode, s.SquadPassword, s.SquadNickname).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetStatus(L("Squad.Status.Error", ShortError(ex)));
        }
    }

    public async Task PublishLastAsync(CancellationToken ct = default)
    {
        if (!IsInRoom) return;
        var s = App.Settings;
        var fix = App.Raid.LastPosition;
        var map = App.Raid.CurrentMap?.Id ?? s.SelectedMapId;
        if (fix == null) return;
        await RpcAsync("squad_publish", new
        {
            p_code = RoomCode,
            p_password = s.SquadPassword,
            p_nick = s.SquadNickname,
            p_map = map,
            p_x = fix.X,
            p_y = fix.Y,
            p_z = fix.Z,
            p_yaw = fix.Yaw
        }, ct).ConfigureAwait(false);
    }

    public async Task ProbeProjectAsync(CancellationToken ct = default)
    {
        var s = App.Settings;
        var url = (s.SquadSupabaseUrl ?? "").Trim().TrimEnd('/');
        var key = (s.SquadSupabaseAnonKey ?? "").Trim();
        var online = false;
        if (Uri.TryCreate(url, UriKind.Absolute, out var baseUri) && !string.IsNullOrWhiteSpace(key))
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(4));
                using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/rest/v1/"));
                req.Headers.TryAddWithoutValidation("apikey", key);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                req.Headers.Accept.ParseAdd("application/json");
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                    .ConfigureAwait(false);
                online = resp.IsSuccessStatusCode;
            }
            catch
            {
                online = false;
            }
        }

        ProjectOnline = online;
        Notify();
    }

    public void Dispose()
    {
        StopLoop();
        StopProbe();
    }

    private void StopProbe()
    {
        try { _probe?.Cancel(); } catch { /* ignore */ }
        _probe?.Dispose();
        _probe = null;
    }

    private void StartProbeLoop()
    {
        StopProbe();
        _probe = new CancellationTokenSource();
        var ct = _probe.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ProbeProjectAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    ProjectOnline = false;
                    Notify();
                }

                try
                {
                    await Task.Delay(15000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);
    }

    private void Enter(JsonElement json, string password, string nick)
    {
        var code = ReadString(json, "code") ?? ReadString(json, "Code");
        var roomId = ReadString(json, "roomId") ?? ReadString(json, "room_id");
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("no room code");

        var s = App.Settings;
        s.SquadNickname = nick.Trim();
        s.SquadPassword = password;
        s.SquadRoomCode = code.ToUpperInvariant();
        s.SquadStayInRoom = true;
        SettingsStore.Save(s);

        _roomId = roomId ?? "";
        RoomCode = s.SquadRoomCode;
        IsInRoom = true;
        SetStatus(L("Squad.Status.InRoom", RoomCode));
        StartLoop();
    }

    private void StartLoop()
    {
        StopLoop();
        _loop = new CancellationTokenSource();
        var ct = _loop.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RefreshListAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("room not found", StringComparison.OrdinalIgnoreCase))
                    {
                        await LeaveAsync().ConfigureAwait(false);
                        break;
                    }
                    SetStatus(L("Squad.Status.Error", ShortError(ex)));
                }

                try
                {
                    await Task.Delay(2500, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);
    }

    private void StopLoop()
    {
        try { _loop?.Cancel(); } catch { /* ignore */ }
        _loop?.Dispose();
        _loop = null;
    }

    private async Task RefreshListAsync(CancellationToken ct)
    {
        if (!IsInRoom) return;
        var s = App.Settings;
        JsonElement json;
        try
        {
            json = await RpcAsync("squad_list", new { p_code = RoomCode, p_password = s.SquadPassword, p_nick = s.SquadNickname }, ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (IsMissingRpc(ex, "squad_list"))
        {
            json = await RpcAsync("squad_list", new { p_code = RoomCode, p_password = s.SquadPassword }, ct)
                .ConfigureAwait(false);
        }
        var list = new List<SquadMate>();
        if (json.ValueKind == JsonValueKind.Array)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-20);
            foreach (var el in json.EnumerateArray())
            {
                var nick = el.GetProperty("nick").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(nick)) continue;
                DateTime updated = DateTime.UtcNow;
                if (el.TryGetProperty("updatedAt", out var ts) && ts.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(ts.GetString(), out var parsed))
                    updated = parsed.ToUniversalTime();
                if (updated < cutoff) continue;
                var mapId = el.TryGetProperty("mapId", out var map) ? map.GetString() ?? "" : "";
                list.Add(new SquadMate
                {
                    Nick = nick,
                    MapId = mapId,
                    X = ReadNum(el, "x"),
                    Y = ReadNum(el, "y"),
                    Z = ReadNum(el, "z"),
                    Yaw = ReadNum(el, "yaw"),
                    Hue = HueFor(nick)
                });
            }
        }

        lock (_gate) _mates = list;
        SetStatus(L("Squad.Status.InRoom", RoomCode));
    }

    private static void PersistCredsFromSettings()
    {
        var s = App.Settings;
        if (string.IsNullOrWhiteSpace(s.SquadSupabaseUrl) || string.IsNullOrWhiteSpace(s.SquadSupabaseAnonKey))
            throw new InvalidOperationException(L("Squad.Error.NeedProject"));
        if (string.IsNullOrWhiteSpace(s.SquadNickname))
            throw new InvalidOperationException(L("Squad.Error.NeedNick"));
    }

    private async Task<JsonElement> RpcAsync(string fn, object body, CancellationToken ct)
    {
        var s = App.Settings;
        var project = (s.SquadSupabaseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(project, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException(L("Squad.Error.NeedProject"));

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"/rest/v1/rpc/{fn}"));
        req.Headers.TryAddWithoutValidation("apikey", s.SquadSupabaseAnonKey.Trim());
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.SquadSupabaseAnonKey.Trim());
        req.Headers.Accept.ParseAdd("application/json");
        req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(ParseRpcError(text) ?? $"{(int)resp.StatusCode}");

        if (string.IsNullOrWhiteSpace(text))
            return JsonDocument.Parse("{}").RootElement.Clone();

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var inner = root.GetString();
            if (string.IsNullOrWhiteSpace(inner))
                return JsonDocument.Parse("{}").RootElement.Clone();
            using var nested = JsonDocument.Parse(inner);
            return nested.RootElement.Clone();
        }

        return root.Clone();
    }

    private static bool IsMissingRpc(Exception ex, string fn) =>
        IsMissingRpc(ex.Message) && ex.Message.Contains(fn, StringComparison.OrdinalIgnoreCase);

    private static bool IsMissingRpc(string msg) =>
        msg.Contains("schema cache", StringComparison.OrdinalIgnoreCase)
        || msg.Contains("could not find the function", StringComparison.OrdinalIgnoreCase);

    private static string? ParseRpcError(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("message", out var m))
                return m.GetString();
        }
        catch
        {
            /* raw */
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? ReadString(JsonElement json, string name)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;
        if (!json.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    private static double ReadNum(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var n)) return 0;
        return n.ValueKind == JsonValueKind.Number ? n.GetDouble() : 0;
    }

    private static int HueFor(string nick)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(nick.ToLowerInvariant()));
        return ((bytes[0] % 11) + 1) * 30;
    }

    private void SetStatus(string text)
    {
        void Apply()
        {
            Status = text;
            Changed?.Invoke();
        }

        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess())
        {
            Apply();
            return;
        }

        d.BeginInvoke(Apply);
    }

    private void Notify()
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess())
        {
            Changed?.Invoke();
            return;
        }

        d.BeginInvoke(() => Changed?.Invoke());
    }

    private static string L(string key, params object[] args)
    {
        var app = Application.Current;
        if (app == null)
            return args.Length == 0 ? key : string.Format(key, args);
        if (app.Dispatcher.CheckAccess())
            return args.Length == 0 ? Loc.T(key) : Loc.T(key, args);
        try
        {
            return app.Dispatcher.Invoke(
                () => args.Length == 0 ? Loc.T(key) : Loc.T(key, args),
                System.Windows.Threading.DispatcherPriority.Background,
                CancellationToken.None,
                TimeSpan.FromMilliseconds(400));
        }
        catch
        {
            return args.Length == 0 ? key : string.Format(key, args);
        }
    }

    public static string FormatError(Exception ex) => ShortError(ex);

    private static string ShortError(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("bad password", StringComparison.OrdinalIgnoreCase))
            return L("Squad.Error.BadPassword");
        if (msg.Contains("room not found", StringComparison.OrdinalIgnoreCase))
            return L("Squad.Error.NoRoom");
        if (msg.Contains("password too short", StringComparison.OrdinalIgnoreCase))
            return L("Squad.Error.ShortPassword");
        if (msg.Contains("invalid nick", StringComparison.OrdinalIgnoreCase))
            return L("Squad.Error.NeedNick");
        if (msg.Contains("room full", StringComparison.OrdinalIgnoreCase))
            return L("Squad.Error.RoomFull");
        if (msg.Contains("room exists", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("unique constraint", StringComparison.OrdinalIgnoreCase))
            return L("Squad.Error.RoomTaken");
        if (IsMissingRpc(msg) || msg.Contains("gen_salt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("pgcrypto", StringComparison.OrdinalIgnoreCase))
            return L("Squad.Error.NeedSql");
        return msg.Length > 80 ? msg[..80] : msg;
    }
}

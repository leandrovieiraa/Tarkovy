using Tarkovy.Models;

namespace Tarkovy.Services;

/// <summary>
/// Built-in squad host plus optional Config override.
/// Official URL/key live in gitignored SquadHost.Official.cs (baked into the published exe).
/// Clone-from-GitHub developers leave Config empty (no host) or paste their own project.
/// </summary>
public static partial class SquadHost
{
    public const string InvitePrefix = "TARKOVY";

    static partial void LoadOfficial(ref string url, ref string anonKey);

    public static (string Url, string Key) Official()
    {
        var url = "";
        var key = "";
        LoadOfficial(ref url, ref key);
        return ((url ?? "").Trim().TrimEnd('/'), (key ?? "").Trim());
    }

    public static bool HasOfficial
    {
        get
        {
            var (url, key) = Official();
            return Uri.TryCreate(url, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(key);
        }
    }

    public static (string Url, string Key) Resolve(AppSettings s)
    {
        var url = (s.SquadSupabaseUrl ?? "").Trim().TrimEnd('/');
        var key = (s.SquadSupabaseAnonKey ?? "").Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(key))
            return (url, key);
        return Official();
    }

    public static bool HasProject(AppSettings s)
    {
        var (url, key) = Resolve(s);
        return Uri.TryCreate(url, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(key);
    }

    public static bool UsingCustomProject(AppSettings s)
    {
        var url = (s.SquadSupabaseUrl ?? "").Trim();
        var key = (s.SquadSupabaseAnonKey ?? "").Trim();
        return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key);
    }

    public static string AppKey(AppSettings s) => (s.SquadAppKey ?? "").Trim();

    public static string FormatInvite(string appKey, string? roomCode = null, string? roomPassword = null)
    {
        var key = (appKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key))
            return "";
        var code = (roomCode ?? "").Trim().ToUpperInvariant();
        var pass = (roomPassword ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(pass))
            return $"{InvitePrefix} {key}";
        return $"{InvitePrefix} {key} {code} {pass}";
    }

    public static bool TryParseInvite(string raw, out string appKey, out string roomCode, out string roomPassword)
    {
        appKey = "";
        roomCode = "";
        roomPassword = "";
        var text = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0].Equals(InvitePrefix, StringComparison.OrdinalIgnoreCase))
        {
            appKey = parts[1];
            if (parts.Length >= 4)
            {
                roomCode = parts[2].ToUpperInvariant();
                roomPassword = string.Join(' ', parts.Skip(3));
            }
            return appKey.Length > 0;
        }

        appKey = text;
        return true;
    }
}

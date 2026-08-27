using System.Globalization;
using System.Text.RegularExpressions;
using Tarkovy.Models;

namespace Tarkovy.Services;

public static partial class ScreenshotParser
{
    [GeneratedRegex(
        @"(?<x>-?\d+(?:\.\d+)?),\s*(?<y>-?\d+(?:\.\d+)?),\s*(?<z>-?\d+(?:\.\d+)?)_(?<r0>-?\d+(?:\.\d+)?),\s*(?<r1>-?\d+(?:\.\d+)?),\s*(?<r2>-?\d+(?:\.\d+)?)(?:,\s*(?<r3>-?\d+(?:\.\d+)?))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}\[", RegexOptions.CultureInvariant)]
    private static partial Regex TarkovShotPrefix();

    public static bool LooksLikeEftScreenshot(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var ext = Path.GetExtension(name);
        if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TarkovShotPrefix().IsMatch(name) || PositionRegex().IsMatch(name);
    }

    public static bool TryParse(string fileName, out PlayerFix fix)
    {
        fix = new PlayerFix { SourceFile = Path.GetFileName(fileName) };
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(name)) return false;

        var match = PositionRegex().Match(name);
        if (!match.Success) return false;

        if (!TryF(match.Groups["x"].Value, out var x) ||
            !TryF(match.Groups["y"].Value, out var y) ||
            !TryF(match.Groups["z"].Value, out var z) ||
            !TryF(match.Groups["r0"].Value, out var r0) ||
            !TryF(match.Groups["r1"].Value, out var r1) ||
            !TryF(match.Groups["r2"].Value, out var r2))
        {
            return false;
        }

        double yaw;
        if (match.Groups["r3"].Success && TryF(match.Groups["r3"].Value, out var r3))
        {
            yaw = QuaternionsToYaw(r0, r1, r2, r3);
        }
        else
        {
            yaw = r1;
        }

        fix.X = x;
        fix.Y = y;
        fix.Z = z;
        fix.Yaw = yaw;
        fix.Utc = DateTime.UtcNow;
        return true;
    }

    private static bool TryF(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>
    /// Same convention as TarkovMonitor: filename stores quaternion x,y,z,w
    /// but yaw uses swapped y/z components from Unity camera rotation.
    /// </summary>
    public static double QuaternionsToYaw(double rx, double ry, double rz, double rw)
    {
        var x = rx;
        var z = ry;
        var y = rz;
        var w = rw;
        var sinyCosp = 2.0 * (w * z + x * y);
        var cosyCosp = 1.0 - 2.0 * (y * y + z * z);
        return Math.Atan2(sinyCosp, cosyCosp) * (180.0 / Math.PI);
    }
}

namespace Tarkovy.Services;

/// <summary>
/// Wiki Ballistics 0–6 ratings (NoFoodAfterMidnight): how well a round
/// deals with armor class N. Threshold is class×10 pen, same rule of thumb
/// as https://escapefromtarkov.fandom.com/wiki/Ballistics
/// </summary>
internal static class AmmoBallistics
{
    public static int ClassRating(int penetration, int armorClass)
    {
        var gap = penetration - armorClass * 10;
        if (gap >= 20) return 6;
        if (gap >= 10) return 5;
        if (gap >= 0) return 4;
        if (gap >= -10) return 3;
        if (gap >= -20) return 2;
        if (gap >= -30) return 1;
        return 0;
    }

    /// <summary>Highest armor class this round is still “effective” (rating ≥ 4) against.</summary>
    public static int BestEffectiveClass(int penetration)
    {
        var best = 0;
        for (var c = 1; c <= 6; c++)
        {
            if (ClassRating(penetration, c) >= 4)
                best = c;
        }
        return best;
    }

    public static string FormatCaliber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.StartsWith("Caliber", StringComparison.OrdinalIgnoreCase)
            ? raw[7..]
            : raw.Trim();
        if (s.Length >= 3 && char.IsDigit(s[0]) && char.IsDigit(s[1]) && char.IsDigit(s[2]) && s[1] != '.')
            s = $"{s[0]}.{s[1..]}";
        foreach (var suffix in new[] { "NATO", "PARA", "Lapua", "Magnum" })
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                s = s[..^suffix.Length];
        }
        return s.Trim();
    }
}

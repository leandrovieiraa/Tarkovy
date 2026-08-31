using Tarkovy.Models;

namespace Tarkovy.Services;

internal static class ItemDisplayNames
{
    public static string Name(ItemDefinition item)
    {
        if (UseLocalized() && ItemLocalizedNames.TryGet(item.Id, out var locName, out _) &&
            !string.IsNullOrWhiteSpace(locName))
            return locName!;
        return Resolve(item.Name, item.Id, item.WikiLink, item.NormalizedName, item.Link, suffix: "Name");
    }

    public static string ShortName(ItemDefinition item)
    {
        if (UseLocalized() && ItemLocalizedNames.TryGet(item.Id, out _, out var locShort) &&
            !string.IsNullOrWhiteSpace(locShort))
            return locShort!;
        return Resolve(item.ShortName, item.Id, item.WikiLink, item.NormalizedName, item.Link, suffix: "ShortName");
    }

    private static bool UseLocalized() =>
        Loc.IsPortuguese && ItemLocalizedNames.IsReady;

    internal static string CatalogName(ItemDefinition item) =>
        Resolve(item.Name, item.Id, item.WikiLink, item.NormalizedName, item.Link, suffix: "Name");

    internal static string CatalogShortName(ItemDefinition item) =>
        Resolve(item.ShortName, item.Id, item.WikiLink, item.NormalizedName, item.Link, suffix: "ShortName");

    private static string Resolve(
        string? raw, string id, string? wikiLink, string normalized, string? devLink, string suffix)
    {
        if (!IsPlaceholder(raw, id, suffix) && !string.IsNullOrWhiteSpace(raw))
            return raw!;

        var fromWiki = FromWikiLink(wikiLink);
        if (!string.IsNullOrWhiteSpace(fromWiki))
            return suffix == "ShortName" ? ShortFromDisplayName(fromWiki) : fromWiki;

        var fromDev = FromDevLink(devLink);
        if (!string.IsNullOrWhiteSpace(fromDev))
            return suffix == "ShortName" ? ShortFromDisplayName(fromDev) : fromDev;

        return suffix == "ShortName"
            ? ShortFromNormalized(normalized, id)
            : TitleFromNormalized(normalized, id);
    }

    internal static bool IsPlaceholder(string? value, string id, string suffix) =>
        string.IsNullOrWhiteSpace(value)
        || value.Contains($"{id} {suffix}", StringComparison.OrdinalIgnoreCase);

    internal static string? FromWikiLink(string? wikiLink)
    {
        if (string.IsNullOrWhiteSpace(wikiLink)) return null;
        var seg = wikiLink.TrimEnd('/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(seg)) return null;
        seg = Uri.UnescapeDataString(seg).Replace('_', ' ').Trim();
        return seg.Length > 0 ? seg : null;
    }

    private static string? FromDevLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        var seg = link.TrimEnd('/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(seg)) return null;
        seg = seg.Replace('-', ' ').Trim();
        return seg.Length > 0 ? TitleFromSlug(seg) : null;
    }

    internal static string TitleFromNormalized(string normalized, string fallbackId)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return fallbackId;
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(static p =>
            p.Length <= 3 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string TitleFromSlug(string slug) =>
        TitleFromNormalized(slug.Replace(' ', '-'), slug);

    internal static string ShortFromDisplayName(string displayName)
    {
        if (displayName.Length <= 16) return displayName;
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts.All(p => p.Length <= 4))
            return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0])));
        return parts[0];
    }

    internal static string ShortFromNormalized(string normalized, string fallbackId)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return fallbackId;
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0].ToUpperInvariant();
        if (parts.All(p => p.Length <= 4))
            return string.Concat(parts.Select(p => p.ToUpperInvariant()));
        return parts[0].ToUpperInvariant();
    }
}

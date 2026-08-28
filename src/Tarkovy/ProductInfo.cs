namespace Tarkovy;

/// <summary>App + target Escape from Tarkov versions (update when revalidating against a new patch).</summary>
public static class ProductInfo
{
    public const string AppVersion = "0.1.10";
    public const string AppVersionLabel = "Dev 0.1.10";

    /// <summary>EFT client / patch this build was validated against.</summary>
    public const string EftPatch = "1.1.0";

    /// <summary>Full client build string when known (optional detail).</summary>
    public const string EftBuild = "1.1.0.1.46699";

    /// <summary>Season / wipe label for humans.</summary>
    public const string EftSeason = "Season 1 — KORD BREACH";

    public const string EftTargetShort = "EFT 1.1.0 · KORD BREACH";
}

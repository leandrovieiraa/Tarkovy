namespace Tarkovy.Models;

public sealed class ItemDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string? IconLink { get; set; }
    public string? GridImageLink { get; set; }
    public string? Link { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public long BasePrice { get; set; }
    public long? Avg24hPrice { get; set; }
    public long? Low24hPrice { get; set; }
    public long? High24hPrice { get; set; }
    public string[] Types { get; set; } = [];
    public ItemTraderPrice[] SellToTrader { get; set; } = [];

    public int Slots => Math.Max(1, Width * Height);

    public long BestTraderRub =>
        SellToTrader.Length == 0 ? 0 : SellToTrader.Max(t => t.PriceRub);

    public bool IsQuestItem => Types.Any(t =>
        t.Contains("quest", StringComparison.OrdinalIgnoreCase));

    public bool IsHideoutItem => Types.Any(t =>
        t.Contains("hideout", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("provisions", StringComparison.OrdinalIgnoreCase));
}

public sealed class ItemTraderPrice
{
    public string Trader { get; set; } = "";
    public long Price { get; set; }
    public string Currency { get; set; } = "RUB";
    public long PriceRub { get; set; }
}

public sealed class ItemScanResult
{
    public ItemDefinition Item { get; set; } = null!;
    public double Confidence { get; set; }
    public string Mode { get; set; } = "icon";
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
}

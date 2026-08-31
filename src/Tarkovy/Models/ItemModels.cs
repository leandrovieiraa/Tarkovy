namespace Tarkovy.Models;

public sealed class ItemDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string? WikiLink { get; set; }
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
    public ItemAmmoStats? Ammo { get; set; }
    public bool IsQuestItem { get; set; }
    public bool IsHideoutItem { get; set; }

    public int Slots => Math.Max(1, Width * Height);

    public long BestTraderRub =>
        SellToTrader.Length == 0 ? 0 : SellToTrader.Max(t => t.PriceRub);
}

public sealed class ItemAmmoStats
{
    public string Caliber { get; set; } = "";
    public int Damage { get; set; }
    public int PenetrationPower { get; set; }
    public int ArmorDamage { get; set; }
    public double FragmentationChance { get; set; }
    public int ProjectileCount { get; set; } = 1;
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
    public int SlotWidth { get; set; } = 1;
    public int SlotHeight { get; set; } = 1;
    public int ScanId { get; set; }
}

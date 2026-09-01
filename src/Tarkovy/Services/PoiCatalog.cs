using Tarkovy.Models;

namespace Tarkovy.Services;

public static class PoiCatalog
{
    public const string CatLoot = "loot";
    public const string CatEnemies = "enemies";
    public const string CatLocations = "locations";

    public static readonly string[] RaidPreset =
        ["boss", "cultist", "black-division", "transit", "btr", "locked-door"];

    public static readonly string[] LootRunPreset =
        ["safe", "weapon-box", "cache", "key", "pc-block", "filing-cabinet"];

    public static IReadOnlyList<PoiTypeDef> Types { get; } =
    [
        T("ammo-box", CatLoot, "Ammo Box", "Caixa de munição", "container_wooden-ammo-box.png"),
        T("cache", CatLoot, "Cache", "Cache", "container_buried-barrel-cache.png", overlay: true),
        T("crate", CatLoot, "Crate", "Caixote", "container_crate.png"),
        T("dead-scav", CatLoot, "Dead Scav", "Scav morto", "container_dead-scav.png"),
        T("duffle-bag", CatLoot, "Duffle Bag", "Mochila", "container_duffle-bag.png"),
        T("filing-cabinet", CatLoot, "Filing Cabinet", "Arquivo", "container_drawer.png", overlay: true),
        T("grenade", CatLoot, "Grenade Box", "Caixa de granadas", "container_grenade-box.png"),
        T("jacket", CatLoot, "Jacket", "Jaqueta", "container_jacket.png"),
        T("loose-loot", CatLoot, "Loose Loot", "Loot solto", "loose_loot.png"),
        T("meds", CatLoot, "Meds", "Meds", "container_medcase.png"),
        T("money", CatLoot, "Money", "Dinheiro", "container_cash-register.png"),
        T("other-loot", CatLoot, "Other Loot", "Outro loot", "container_plastic-suitcase.png"),
        T("pc-block", CatLoot, "PC Block", "PC", "container_pc-block.png", overlay: true),
        T("provisions", CatLoot, "Provisions", "Provisões", "container_crate.png"),
        T("safe", CatLoot, "Safe", "Cofre", "container_safe.png", overlay: true),
        T("toolbox", CatLoot, "Toolbox", "Caixa de ferramentas", "container_toolbox.png"),
        T("weapon-box", CatLoot, "Weapon Box", "Caixa de armas", "container_weapon-box.png", overlay: true),

        T("boss", CatEnemies, "Boss", "Boss", "spawn_boss.png", overlay: true),
        T("cultist", CatEnemies, "Cultists", "Cultistas", "spawn_cultist-priest.png", overlay: true),
        T("black-division", CatEnemies, "Black Division", "Black Division", "spawn_black-div.png", overlay: true),
        T("rogue", CatEnemies, "Rogue", "Rogue", "spawn_rogue.png", overlay: true),
        T("scav", CatEnemies, "Scav", "Scav", "spawn_scav.png"),
        T("scav-sniper", CatEnemies, "Scav Sniper", "Scav sniper", "spawn_sniper_scav.png", overlay: true),

        T("btr", CatLocations, "BTR Stop", "Parada BTR", "btr_stop.png", overlay: true),
        T("locked-door", CatLocations, "Locked Door", "Porta trancada", "lock.png", overlay: true),
        T("transit", CatLocations, "Transit", "Trânsito", "extract_transit.png", overlay: true),
        T("switch", CatLocations, "Switch", "Alavanca", "switch.png", overlay: true),
        T("emplacement", CatLocations, "Emplacement", "Emplacement", "stationarygun.png", overlay: true)
    ];

    private static readonly Dictionary<string, PoiTypeDef> ById =
        Types.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> ContainerTypeById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["578f8778245977358849a9b5"] = "jacket",
        ["578f8782245977354405a1e3"] = "safe",
        ["578f879c24597735401e6bc6"] = "money",
        ["578f87a3245977356274f2cb"] = "duffle-bag",
        ["578f87ad245977356274f2cc"] = "crate",
        ["578f87b7245977356274f2cd"] = "filing-cabinet",
        ["5909d24f86f77466f56e6855"] = "meds",
        ["5909d36d86f774660f0bb900"] = "grenade",
        ["5909d45286f77465a8136dc6"] = "ammo-box",
        ["5909d4c186f7746ad34e805a"] = "meds",
        ["5909d50c86f774659e6aaebe"] = "toolbox",
        ["5909d5ef86f77467974efbd8"] = "weapon-box",
        ["5909d76c86f77471e53d2adf"] = "weapon-box",
        ["5909d7cf86f77470ee57d75a"] = "weapon-box",
        ["5909d89086f77472591234a0"] = "weapon-box",
        ["5909e4b686f7747f5b744fa4"] = "dead-scav",
        ["59139c2186f77411564f8e42"] = "pc-block",
        ["5914944186f774189e5e76c2"] = "jacket",
        ["5937ef2b86f77408a47244b3"] = "money",
        ["59387ac686f77401442ddd61"] = "money",
        ["5c052cea86f7746b2101e8d8"] = "other-loot",
        ["5d07b91b86f7745a077a9432"] = "weapon-box",
        ["5d6d2b5486f774785c2ba8ea"] = "cache",
        ["5d6d2bb386f774785b07a77a"] = "cache",
        ["5d6fd13186f77424ad2a8c69"] = "dead-scav",
        ["5d6fd45b86f774317075ed43"] = "dead-scav",
        ["5d6fe50986f77449d97f7463"] = "dead-scav",
        ["61aa1e9a32a4743c3453d2cf"] = "provisions",
        ["61aa1ead84ea0800645777fd"] = "meds",
        ["64d116f41a9c6143a956127d"] = "crate",
        ["64d11702dd0cd96ab82c3280"] = "crate",
        ["6582e6bb0c3b9823fe6d1840"] = "other-loot",
        ["6582e6c6edf14c4c6023adf2"] = "other-loot",
        ["6582e6d7b14c3f72eb071420"] = "other-loot",
        ["658420d8085fea07e674cdb6"] = "other-loot",
        ["66acff0a1d8e1083b303f5af"] = "other-loot"
    };

    private static readonly Dictionary<string, (string Type, string Name)> MobMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bossTagilla"] = ("boss", "Tagilla"),
        ["bossTagillaAgro"] = ("boss", "Tagilla"),
        ["bossKilla"] = ("boss", "Killa"),
        ["bossGluhar"] = ("boss", "Glukhar"),
        ["bossKojaniy"] = ("boss", "Shturman"),
        ["bossSanitar"] = ("boss", "Sanitar"),
        ["bossBully"] = ("boss", "Reshala"),
        ["bossKnight"] = ("boss", "Knight"),
        ["bossPartisan"] = ("boss", "Partisan"),
        ["bossBoar"] = ("boss", "Kaban"),
        ["bossKolontay"] = ("boss", "Kollontay"),
        ["bossZryachiy"] = ("boss", "Zryachiy"),
        ["bossWedge"] = ("boss", "Wedge"),
        ["bossWedgeLab"] = ("boss", "Wedge"),
        ["sectantPriest"] = ("cultist", "Cultist priest"),
        ["blackDivision"] = ("black-division", "Black Division"),
        ["pmcBotBlackDiv"] = ("black-division", "Black Division"),
        ["bossBullyBlackDiv"] = ("black-division", "Black Division"),
        ["PmcBot"] = ("rogue", "Raider"),
        ["ExUsec"] = ("rogue", "Rogue"),
        ["vsRF"] = ("boss", "BEAR"),
        ["vsRFSniper"] = ("scav-sniper", "Sniper"),
        ["Sentry"] = ("boss", "Sentry")
    };

    public static PoiTypeDef? Find(string? id) =>
        id != null && ById.TryGetValue(id, out var def) ? def : null;

    public static IReadOnlyList<PoiTypeDef> TypesIn(string category) =>
        Types.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)).ToArray();

    public static string TypeLabel(PoiTypeDef def) =>
        Loc.IsPortuguese && !string.IsNullOrWhiteSpace(def.NamePt) ? def.NamePt : def.Name;

    public static HashSet<string> EnabledSet() =>
        new(App.Settings.EnabledPoiTypes ?? [], StringComparer.OrdinalIgnoreCase);

    public static bool IsEnabled(string type) =>
        App.Settings.EnabledPoiTypes.Contains(type, StringComparer.OrdinalIgnoreCase);

    public static bool? CategoryState(string category, IReadOnlyCollection<string>? presentOnMap = null)
    {
        var types = TypesIn(category)
            .Select(t => t.Id)
            .Where(id => presentOnMap == null || presentOnMap.Count == 0 || presentOnMap.Contains(id))
            .ToArray();
        if (types.Length == 0) return false;
        var enabled = types.Count(IsEnabled);
        if (enabled == 0) return false;
        if (enabled == types.Length) return true;
        return null;
    }

    public static void SetType(string type, bool enabled)
    {
        var list = App.Settings.EnabledPoiTypes;
        var has = list.Contains(type, StringComparer.OrdinalIgnoreCase);
        if (enabled && !has) list.Add(type);
        else if (!enabled && has)
            list.RemoveAll(s => string.Equals(s, type, StringComparison.OrdinalIgnoreCase));
    }

    public static void SetCategory(string category, bool enabled, IReadOnlyCollection<string>? presentOnMap = null)
    {
        foreach (var def in TypesIn(category))
        {
            if (presentOnMap is { Count: > 0 } && !presentOnMap.Contains(def.Id))
                continue;
            SetType(def.Id, enabled);
        }
    }

    public static void ToggleCategoryFromOverlay(string category, IReadOnlyCollection<string>? presentOnMap = null)
    {
        var state = CategoryState(category, presentOnMap);
        if (state != false)
        {
            SetCategory(category, false, presentOnMap);
            return;
        }

        var safe = TypesIn(category)
            .Where(t => t.OverlaySafe)
            .Select(t => t.Id)
            .Where(id => presentOnMap == null || presentOnMap.Count == 0 || presentOnMap.Contains(id))
            .ToArray();
        if (safe.Length == 0)
            SetCategory(category, true, presentOnMap);
        else
            foreach (var id in safe)
                SetType(id, true);
    }

    public static void ToggleCategory(string category, IReadOnlyCollection<string>? presentOnMap = null)
    {
        var state = CategoryState(category, presentOnMap);
        SetCategory(category, state == false, presentOnMap);
    }

    public static void ApplyPreset(IReadOnlyList<string> types)
    {
        App.Settings.EnabledPoiTypes = types.ToList();
    }

    public static void ClearAll() => App.Settings.EnabledPoiTypes.Clear();

    public static string? ContainerType(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (ContainerTypeById.TryGetValue(id, out var type)) return type;
        return "other-loot";
    }

    public static (string Type, string Name) ResolveMob(string? mob)
    {
        if (string.IsNullOrWhiteSpace(mob))
            return ("boss", "Boss");
        if (MobMap.TryGetValue(mob, out var mapped))
            return mapped;
        var pretty = mob.StartsWith("boss", StringComparison.OrdinalIgnoreCase)
            ? mob[4..]
            : mob;
        if (pretty.Length > 0)
            pretty = char.ToUpperInvariant(pretty[0]) + pretty[1..];
        return ("boss", pretty);
    }

    public static MapPoi Create(string type, string name, double x, double y, double z, string detail = "")
    {
        var def = Find(type);
        return new MapPoi
        {
            Type = type,
            Category = def?.Category ?? "",
            Name = name,
            Detail = detail,
            Icon = def?.Icon ?? "loose_loot.png",
            X = x,
            Y = y,
            Z = z
        };
    }

    public static List<MapPoi> Cluster(IReadOnlyList<MapPoi> points, double cell)
    {
        if (points.Count <= 1) return points.ToList();
        var buckets = new Dictionary<(string Type, long X, long Z), List<MapPoi>>();
        foreach (var p in points)
        {
            var key = (p.Type, (long)Math.Round(p.X / cell), (long)Math.Round(p.Z / cell));
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }
            list.Add(p);
        }

        var result = new List<MapPoi>(buckets.Count);
        foreach (var pts in buckets.Values)
        {
            var first = pts[0];
            result.Add(Create(
                first.Type,
                first.Name,
                pts.Average(p => p.X),
                pts.Average(p => p.Y),
                pts.Average(p => p.Z),
                first.Detail));
        }
        return result;
    }

    public static IReadOnlyList<string> OverlaySafeIds() =>
        Types.Where(t => t.OverlaySafe).Select(t => t.Id).ToArray();

    private static PoiTypeDef T(string id, string category, string name, string namePt, string icon, bool overlay = false) =>
        new()
        {
            Id = id,
            Category = category,
            Name = name,
            NamePt = namePt,
            Icon = icon,
            OverlaySafe = overlay
        };
}

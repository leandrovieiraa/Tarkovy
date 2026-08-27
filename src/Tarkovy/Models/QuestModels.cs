using System.Text.Json.Serialization;

namespace Tarkovy.Models;

public sealed class QuestDefinition
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Portuguese (game/locale) display name when available.</summary>
    public string NamePt { get; set; } = "";
    public string Trader { get; set; } = "";
    public string TraderPt { get; set; } = "";
    public List<QuestObjective> Objectives { get; set; } = [];
}

public sealed class QuestObjective
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "quest";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class MapWaypoint
{
    public string Kind { get; set; } = "extract"; // extract | quest | custom
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Z { get; set; }
}

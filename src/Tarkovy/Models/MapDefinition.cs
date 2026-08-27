using System.Text.Json.Serialization;

namespace Tarkovy.Models;

public sealed class MapDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string[] LogIds { get; set; } = [];
    public string[] SceneTokens { get; set; } = [];
    public string? SvgPath { get; set; }
    public int CoordinateRotation { get; set; } = 180;
    /// <summary>Leaflet CRS transform [scaleX, marginX, scaleY, marginY] (tarkov.dev / Sayser).</summary>
    public double[]? Transform { get; set; }
    public double[][] Bounds { get; set; } = [[0, 0], [1, 1]];
    /// <summary>Quando presente, usado na projeção no lugar de Bounds (ex.: Reserve).</summary>
    public double[][]? SvgBounds { get; set; }
}

public sealed class ExtractMarker
{
    public string Name { get; set; } = "";
    public string Faction { get; set; } = "any";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class HazardMarker
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "mine";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class PlayerFix
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Yaw { get; set; }
    public string SourceFile { get; set; } = "";
    public DateTime Utc { get; set; } = DateTime.UtcNow;
}

public enum RaidStatus
{
    Idle,
    Watching,
    Loading,
    InRaid
}

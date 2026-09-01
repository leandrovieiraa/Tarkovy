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
    /// <summary>Andares SVG (tarkov.dev). Vazio = mapa plano.</summary>
    public List<MapFloorLayer>? Floors { get; set; }
}

public sealed class MapFloorLayer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string NamePt { get; set; } = "";
    /// <summary>Rótulo curto no botão (G, 2, B…).</summary>
    public string Short { get; set; } = "";
    public string SvgLayer { get; set; } = "";
    public double MinHeight { get; set; } = double.NegativeInfinity;
    public double MaxHeight { get; set; } = double.PositiveInfinity;
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

public sealed class SpawnMarker
{
    public string Name { get; set; } = "PMC Spawn";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class MapPoi
{
    public string Type { get; set; } = "";
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Icon { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class PoiTypeDef
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string NamePt { get; init; } = "";
    public string Icon { get; init; } = "";
    public bool OverlaySafe { get; init; }
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

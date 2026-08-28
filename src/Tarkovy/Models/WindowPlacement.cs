using System.Text.Json.Serialization;

namespace Tarkovy.Models;

public sealed class WindowPlacement
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool IsMaximized { get; set; }

    [JsonIgnore]
    public bool IsValid =>
        Left.HasValue && Top.HasValue &&
        Width is > 0 && Height is > 0;

    public void Clear()
    {
        Left = null;
        Top = null;
        Width = null;
        Height = null;
        IsMaximized = false;
    }
}

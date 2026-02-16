using System.Text.Json.Serialization;

namespace RhythmbulletPrototype.Models;

public sealed class DragNoteEvent
{
    public int TimeMs { get; set; }
    public int DurationMs { get; set; } = 900;
    public float PipeRadius { get; set; } = 26f;
    public string? FillColor { get; set; }
    public string? OutlineColor { get; set; }
    public List<DragPoint> Path { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }
}

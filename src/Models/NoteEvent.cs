using System.Text.Json.Serialization;

namespace RhythmbulletPrototype.Models;

public sealed class NoteEvent
{
    public int TimeMs { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public string? FillColor { get; set; }
    public string? OutlineColor { get; set; }
    public int? Lane { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }
}

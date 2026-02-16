using System.Text.Json.Serialization;

namespace RhythmbulletPrototype.Models;

public sealed class DragPoint
{
    public float X { get; set; }
    public float Y { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }
}

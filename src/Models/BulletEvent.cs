using System.Text.Json.Serialization;

namespace RhythmbulletPrototype.Models;

public sealed class BulletEvent
{
    public int TimeMs { get; set; }
    public string Pattern { get; set; } = "radial";
    public int Count { get; set; } = 1;
    public float Speed { get; set; } = 250f;
    public int IntervalMs { get; set; } = 80;
    public string? BulletType { get; set; }
    public float? BulletSize { get; set; }
    public float? Radius { get; set; }
    public string? Color { get; set; }
    public string? OutlineColor { get; set; }
    public string? GlowColor { get; set; }
    public float? GlowIntensity { get; set; }
    public float? OutlineThickness { get; set; }
    public float? SpreadDeg { get; set; }
    public float? AngleStepDeg { get; set; }
    public float? DirectionDeg { get; set; }
    public float? MovementIntensity { get; set; }
    public float? RingExpandDistance { get; set; }
    public string? MotionPattern { get; set; }
    public int? TelegraphMs { get; set; }
    public int? LaserDurationMs { get; set; }
    public float? LaserWidth { get; set; }
    public float? LaserLength { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }
}

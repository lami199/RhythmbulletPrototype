namespace RhythmbulletPrototype.Models;

public sealed class DifficultyProfile
{
    public float ApproachRate { get; set; } = 8f;
    public float OverallDifficulty { get; set; } = 7f;
    public float CircleSize { get; set; } = 4f;
    public float HealthDrain { get; set; } = 5f;

    public static DifficultyProfile Default => new();

    public static DifficultyProfile Normalize(DifficultyProfile? raw)
    {
        var src = raw ?? Default;
        return new DifficultyProfile
        {
            ApproachRate = Math.Clamp(src.ApproachRate, 0f, 10f),
            OverallDifficulty = Math.Clamp(src.OverallDifficulty, 0f, 10f),
            CircleSize = Math.Clamp(src.CircleSize, 0f, 10f),
            HealthDrain = Math.Clamp(src.HealthDrain, 0f, 10f)
        };
    }
}

namespace RhythmbulletPrototype.Models;

public sealed class GameplaySessionStats
{
    public string? MapPath { get; set; }
    public DateTime TimestampUtc { get; set; }
    public float ClockRate { get; set; }
    public List<GameMod> ActiveMods { get; set; } = new();
    public HitWindows HitWindows { get; set; } = HitWindows.Default;
    public int TotalScore { get; set; }
    public int MaxCombo { get; set; }
    public float Accuracy { get; set; }
    public float HealthRemaining { get; set; }
    public int PerfectCount { get; set; }
    public int GoodCount { get; set; }
    public int OkCount { get; set; }
    public int MissCount { get; set; }
    public int EarlyHitCount { get; set; }
    public int OnTimeHitCount { get; set; }
    public int LateHitCount { get; set; }
}

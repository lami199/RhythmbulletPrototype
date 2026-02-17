namespace RhythmbulletPrototype.Models;

public sealed class HitWindows
{
    public int PerfectMs { get; set; } = 40;
    public int GoodMs { get; set; } = 90;
    public int OkMs { get; set; } = 140;
    public int MissMs { get; set; } = 140;

    public static HitWindows Default => new();

    public static HitWindows Normalize(HitWindows? raw)
    {
        var src = raw ?? Default;
        var perfect = Math.Clamp(src.PerfectMs, 5, 300);
        var good = Math.Clamp(src.GoodMs, perfect, 500);
        var ok = Math.Clamp(src.OkMs, good, 800);
        var miss = Math.Clamp(src.MissMs, ok, 1000);
        return new HitWindows
        {
            PerfectMs = perfect,
            GoodMs = good,
            OkMs = ok,
            MissMs = miss
        };
    }

    public static Judgment ResolveJudgment(int absDeltaMs, HitWindows windows)
    {
        if (absDeltaMs <= windows.PerfectMs) return Judgment.Perfect;
        if (absDeltaMs <= windows.GoodMs) return Judgment.Good;
        if (absDeltaMs <= windows.OkMs) return Judgment.Ok;
        return Judgment.Miss;
    }
}

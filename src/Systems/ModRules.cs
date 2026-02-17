using RhythmbulletPrototype.Models;

namespace RhythmbulletPrototype.Systems;

public static class ModRules
{
    public static float ResolveClockRate(IReadOnlyCollection<GameMod> mods)
    {
        if (mods.Contains(GameMod.DoubleTime)) return 1.5f;
        if (mods.Contains(GameMod.HalfTime)) return 0.75f;
        return 1f;
    }

    public static float ResolveScoreMultiplier(IReadOnlyCollection<GameMod> mods)
    {
        var multiplier = 1f;
        if (mods.Contains(GameMod.Easy)) multiplier *= 0.5f;
        if (mods.Contains(GameMod.HardRock)) multiplier *= 1.06f;
        if (mods.Contains(GameMod.HalfTime)) multiplier *= 0.3f;
        if (mods.Contains(GameMod.DoubleTime)) multiplier *= 1.12f;
        if (mods.Contains(GameMod.NoFail)) multiplier *= 0.5f;
        return multiplier;
    }

    public static DifficultyProfile ResolveDifficultyProfile(DifficultyProfile? baseProfile, IReadOnlyCollection<GameMod> mods)
    {
        var profile = DifficultyProfile.Normalize(baseProfile);

        if (mods.Contains(GameMod.Easy))
        {
            profile.ApproachRate *= 0.5f;
            profile.OverallDifficulty *= 0.5f;
            profile.CircleSize *= 0.5f;
            profile.HealthDrain *= 0.5f;
        }

        if (mods.Contains(GameMod.HardRock))
        {
            profile.ApproachRate *= 1.4f;
            profile.OverallDifficulty *= 1.4f;
            profile.CircleSize *= 1.3f;
            profile.HealthDrain *= 1.4f;
        }

        return DifficultyProfile.Normalize(profile);
    }

    public static HitWindows ResolveHitWindows(HitWindows? baseWindows, DifficultyProfile? profile, IReadOnlyCollection<GameMod> mods)
    {
        var normalized = HitWindows.Normalize(baseWindows);
        var difficulty = ResolveDifficultyProfile(profile, mods);

        var odScale = Math.Clamp(1f + (difficulty.OverallDifficulty - 5f) * 0.06f, 0.45f, 1.55f);
        var modScale = 1f;
        if (mods.Contains(GameMod.Easy)) modScale *= 1.4f;
        if (mods.Contains(GameMod.HardRock)) modScale *= 0.85f;
        if (mods.Contains(GameMod.HalfTime)) modScale *= 1.1f;
        if (mods.Contains(GameMod.DoubleTime)) modScale *= 0.9f;

        var finalScale = Math.Clamp(modScale / odScale, 0.45f, 2f);

        return HitWindows.Normalize(new HitWindows
        {
            PerfectMs = (int)MathF.Round(normalized.PerfectMs * finalScale),
            GoodMs = (int)MathF.Round(normalized.GoodMs * finalScale),
            OkMs = (int)MathF.Round(normalized.OkMs * finalScale),
            MissMs = (int)MathF.Round(normalized.MissMs * finalScale)
        });
    }
}

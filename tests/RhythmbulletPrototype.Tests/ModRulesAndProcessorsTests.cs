using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Systems;
using Xunit;

namespace RhythmbulletPrototype.Tests;

public sealed class ModRulesAndProcessorsTests
{
    [Fact]
    public void ResolveHitWindows_EasyVsHardRock_ProducesExpectedOrdering()
    {
        var baseWindows = new HitWindows { PerfectMs = 40, GoodMs = 90, OkMs = 140, MissMs = 140 };
        var profile = new DifficultyProfile { OverallDifficulty = 7f };

        var easy = ModRules.ResolveHitWindows(baseWindows, profile, new[] { GameMod.Easy });
        var hardRock = ModRules.ResolveHitWindows(baseWindows, profile, new[] { GameMod.HardRock });

        Assert.True(easy.PerfectMs > hardRock.PerfectMs);
        Assert.True(easy.MissMs > hardRock.MissMs);
    }

    [Fact]
    public void ScoreProcessor_TracksComboAndMissReset()
    {
        var processor = new ScoreProcessor();
        processor.Reset(Array.Empty<GameMod>());

        processor.Apply(new NoteJudgmentEvent(Judgment.Perfect, 0, 40, 1000));
        processor.Apply(new NoteJudgmentEvent(Judgment.Good, 22, 90, 1100));
        processor.Apply(new NoteJudgmentEvent(Judgment.Miss, 0, 140, 1200));

        Assert.True(processor.TotalScore > 0);
        Assert.Equal(0, processor.Combo);
        Assert.Equal(2, processor.MaxCombo);
        Assert.Equal(1, processor.MissCount);
    }

    [Fact]
    public void HealthProcessor_BulletAndMissDamage_CanFail()
    {
        var beatmap = new Beatmap
        {
            MaxLives = 30f,
            BulletHitDamage = 12f,
            LifeGainAmount = 3f
        };

        var processor = new HealthProcessor();
        processor.Configure(beatmap, DifficultyProfile.Default, Array.Empty<GameMod>());
        processor.Reset();

        processor.ApplyBulletHit();
        processor.ApplyJudgment(new NoteJudgmentEvent(Judgment.Miss, 0, 140, 1000));
        processor.ApplyBulletHit();
        processor.ApplyBulletHit();

        Assert.True(processor.CurrentHealth <= 0f);
        Assert.True(processor.IsFailed);
    }

    [Fact]
    public void HealthProcessor_SparseMaps_GetHigherRecoveryPerPerfect()
    {
        var sparse = new Beatmap
        {
            MaxLives = 100f,
            LifeGainAmount = 4f,
            Notes = new List<NoteEvent>
            {
                new() { TimeMs = 1000, X = 0.5f, Y = 0.5f },
                new() { TimeMs = 120000, X = 0.5f, Y = 0.5f }
            }
        };

        var dense = new Beatmap
        {
            MaxLives = 100f,
            LifeGainAmount = 4f,
            Notes = Enumerable.Range(0, 120).Select(i => new NoteEvent
            {
                TimeMs = 1000 + i * 500,
                X = 0.5f,
                Y = 0.5f
            }).ToList()
        };

        var sparseProcessor = new HealthProcessor();
        sparseProcessor.Configure(sparse, DifficultyProfile.Default, Array.Empty<GameMod>());
        var denseProcessor = new HealthProcessor();
        denseProcessor.Configure(dense, DifficultyProfile.Default, Array.Empty<GameMod>());

        Assert.True(sparseProcessor.RecoveryPerPerfect > denseProcessor.RecoveryPerPerfect);
    }
}

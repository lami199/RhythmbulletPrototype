using Microsoft.Xna.Framework;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Systems;
using Xunit;

namespace RhythmbulletPrototype.Tests;

public sealed class HitWindowsTests
{
    [Fact]
    public void ResolveJudgment_UsesExpectedBoundaries()
    {
        var windows = HitWindows.Normalize(new HitWindows
        {
            PerfectMs = 30,
            GoodMs = 80,
            OkMs = 120,
            MissMs = 120
        });

        Assert.Equal(Judgment.Perfect, HitWindows.ResolveJudgment(30, windows));
        Assert.Equal(Judgment.Good, HitWindows.ResolveJudgment(31, windows));
        Assert.Equal(Judgment.Good, HitWindows.ResolveJudgment(80, windows));
        Assert.Equal(Judgment.Ok, HitWindows.ResolveJudgment(81, windows));
        Assert.Equal(Judgment.Ok, HitWindows.ResolveJudgment(120, windows));
        Assert.Equal(Judgment.Miss, HitWindows.ResolveJudgment(121, windows));
    }

    [Fact]
    public void NoteSystem_UsesBeatmapHitWindows_ForHitResolution()
    {
        var noteSystem = new NoteSystem();
        var beatmap = new Beatmap
        {
            CircleRadius = 42f,
            HitWindows = new HitWindows
            {
                PerfectMs = 20,
                GoodMs = 45,
                OkMs = 70,
                MissMs = 70
            },
            Notes = new List<NoteEvent>
            {
                new() { TimeMs = 1000, X = 0.5f, Y = 0.5f }
            }
        };

        noteSystem.Reset(beatmap);
        var cursor = new Vector2(640f, 360f);
        var didHit = noteSystem.TryHit(1045, cursor, out var result);

        Assert.True(didHit);
        Assert.Equal(Judgment.Good, result.Judgment);
        Assert.Equal(45, result.WindowMs);
        Assert.Equal(70, noteSystem.HitWindowMs);
    }
}

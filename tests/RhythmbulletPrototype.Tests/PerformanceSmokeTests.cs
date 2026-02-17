using Microsoft.Xna.Framework;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Systems;
using Xunit;

namespace RhythmbulletPrototype.Tests;

public sealed class PerformanceSmokeTests
{
    [Fact]
    public void BulletSystem_Update_DensePattern_CompletesQuickly()
    {
        var beatmap = new Beatmap
        {
            Bullets = new List<BulletEvent>
            {
                new()
                {
                    TimeMs = 0,
                    Pattern = "ring_32",
                    Count = 64,
                    Speed = 260f,
                    MotionPattern = "uniform_outward_drift",
                    X = 0.5f,
                    Y = 0.5f
                }
            }
        };

        var system = new BulletSystem();
        system.Reset(beatmap);
        var cursor = new Vector2(640f, 360f);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1200; i++)
        {
            system.Update(1f / 120f, i * 8, cursor);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5000, $"Dense bullet update too slow: {sw.ElapsedMilliseconds}ms");
    }
}

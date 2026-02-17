using Microsoft.Xna.Framework;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Systems;
using Xunit;

namespace RhythmbulletPrototype.Tests;

public sealed class BulletSystemDeterminismTests
{
    [Fact]
    public void ResetAndReplay_ProducesDeterministicBulletSnapshots()
    {
        var first = RunSimulation();
        var second = RunSimulation();

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
    }

    private static List<string> RunSimulation()
    {
        var beatmap = new Beatmap
        {
            Bullets = new List<BulletEvent>
            {
                new()
                {
                    TimeMs = 200,
                    Pattern = "ring_32",
                    MotionPattern = "shoot_at_mouse",
                    Count = 32,
                    Speed = 240f,
                    X = 0.5f,
                    Y = 0.5f
                }
            }
        };

        var system = new BulletSystem();
        system.Reset(beatmap);

        var frameSignature = new List<string>();
        var drawData = new List<BulletSystem.BulletPreviewDrawData>();
        var cursor = new Vector2(720f, 280f);

        for (var ms = 0; ms <= 2400; ms += 16)
        {
            system.Update(0.016f, ms, cursor);
            system.FillPreviewDrawData(drawData);
            var frame = string.Join('|', drawData.Select(d => $"{d.Position.X:0.###},{d.Position.Y:0.###},{d.Radius:0.##}"));
            frameSignature.Add(frame);
        }

        return frameSignature;
    }
}

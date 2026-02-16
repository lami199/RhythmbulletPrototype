using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Rendering;

namespace RhythmbulletPrototype.Systems;

public sealed class VfxSystem
{
    private readonly List<RingFx> _rings = new();
    private readonly List<ParticleFx> _particles = new();
    private readonly List<TextFx> _texts = new();
    private readonly Random _random = new();

    public void Clear()
    {
        _rings.Clear();
        _particles.Clear();
        _texts.Clear();
    }

    public void SpawnLeftClick(Vector2 position)
    {
        SpawnRing(position, 18f, 100f, 0.28f, new Color(255, 220, 130));
        SpawnParticles(position, 14, 220f, new Color(255, 210, 120));
    }

    public void SpawnRightClick(Vector2 position)
    {
        SpawnRing(position, 16f, 80f, 0.25f, new Color(130, 235, 255));
        SpawnParticles(position, 10, 180f, new Color(140, 230, 255));
    }

    public void SpawnMiddleClick(Vector2 position)
    {
        SpawnRing(position, 10f, 170f, 0.35f, new Color(190, 255, 180));
    }

    public void SpawnJudgment(Judgment judgment, Vector2 position)
    {
        var color = judgment switch
        {
            Judgment.Perfect => new Color(255, 245, 190),
            Judgment.Good => new Color(180, 240, 255),
            Judgment.Ok => new Color(190, 220, 255),
            _ => new Color(255, 140, 140)
        };

        _texts.Add(new TextFx
        {
            Position = position + new Vector2(-24f, -50f),
            Velocity = new Vector2(0f, -22f),
            Life = 0.6f,
            Text = judgment.ToString().ToUpperInvariant(),
            Color = color
        });
    }

    public void SpawnMiss(Vector2 position)
    {
        SpawnJudgment(Judgment.Miss, position);
    }

    public void SpawnFailPulse(Vector2 position)
    {
        SpawnRing(position, 12f, 220f, 0.42f, new Color(255, 70, 90));
        SpawnParticles(position, 20, 280f, new Color(255, 100, 110));
    }

    public void Update(float dt)
    {
        for (var i = _rings.Count - 1; i >= 0; i--)
        {
            var ring = _rings[i];
            ring.Age += dt;
            if (ring.Age >= ring.Life)
            {
                _rings.RemoveAt(i);
            }
            else
            {
                _rings[i] = ring;
            }
        }

        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _particles.RemoveAt(i);
                continue;
            }

            p.Position += p.Velocity * dt;
            p.Velocity *= MathF.Exp(-4f * dt);
            _particles[i] = p;
        }

        for (var i = _texts.Count - 1; i >= 0; i--)
        {
            var t = _texts[i];
            t.Age += dt;
            if (t.Age >= t.Life)
            {
                _texts.RemoveAt(i);
                continue;
            }

            t.Position += t.Velocity * dt;
            _texts[i] = t;
        }
    }

    public void Draw(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        foreach (var ring in _rings)
        {
            var t = ring.Age / ring.Life;
            var radius = MathHelper.Lerp(ring.StartRadius, ring.EndRadius, t);
            var alpha = 1f - t;
            render.DrawCircleOutline(spriteBatch, ring.Position, radius, 2f, ring.Color * alpha);
        }

        foreach (var p in _particles)
        {
            var alpha = 1f - (p.Age / p.Life);
            render.DrawCircleFilled(spriteBatch, p.Position, p.Size, p.Color * alpha);
        }

        foreach (var t in _texts)
        {
            var alpha = 1f - (t.Age / t.Life);
            text.DrawString(spriteBatch, t.Text, t.Position, t.Color * alpha, 2f);
        }
    }

    private void SpawnRing(Vector2 position, float startRadius, float endRadius, float life, Color color)
    {
        _rings.Add(new RingFx
        {
            Position = position,
            StartRadius = startRadius,
            EndRadius = endRadius,
            Life = life,
            Color = color
        });
    }

    private void SpawnParticles(Vector2 position, int count, float speed, Color color)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(_random.NextDouble() * MathHelper.TwoPi);
            var mag = speed * (0.35f + (float)_random.NextDouble() * 0.65f);
            _particles.Add(new ParticleFx
            {
                Position = position,
                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * mag,
                Life = 0.35f + (float)_random.NextDouble() * 0.25f,
                Size = 2.2f + (float)_random.NextDouble() * 2.4f,
                Color = color
            });
        }
    }

    private struct RingFx
    {
        public Vector2 Position;
        public float StartRadius;
        public float EndRadius;
        public float Age;
        public float Life;
        public Color Color;
    }

    private struct ParticleFx
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
        public float Size;
        public Color Color;
    }

    private struct TextFx
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
        public string Text;
        public Color Color;
    }
}

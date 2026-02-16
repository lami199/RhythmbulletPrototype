using Microsoft.Xna.Framework;

namespace RhythmbulletPrototype.Systems;

public sealed class CursorController
{
    private readonly List<TrailPoint> _trail = new();
    private const float TrailLifetimeSeconds = 0.32f;
    private const float SlowMouseSensitivity = 0.10f;

    public CursorController(Vector2 startPosition)
    {
        Position = startPosition;
    }

    public Vector2 Position { get; private set; }

    public IReadOnlyList<TrailPoint> Trail => _trail;

    public float CollisionRadius { get; set; } = 24f;

    public void Reset(Vector2 position)
    {
        Position = position;
        _trail.Clear();
    }

    public void Update(float dt, Vector2 mouseTarget, bool slowMouse)
    {
        for (var i = _trail.Count - 1; i >= 0; i--)
        {
            var p = _trail[i];
            p.Age += dt;
            if (p.Age >= TrailLifetimeSeconds)
            {
                _trail.RemoveAt(i);
            }
            else
            {
                _trail[i] = p;
            }
        }

        if (slowMouse)
        {
            // Hold-to-focus: heavily reduce effective sensitivity for tight dodges.
            var t = 1f - MathF.Pow(1f - SlowMouseSensitivity, Math.Max(1f, dt * 60f));
            Position = Vector2.Lerp(Position, mouseTarget, t);
        }
        else
        {
            Position = mouseTarget;
        }

        Position = new Vector2(
            Math.Clamp(Position.X, 0f, 1280f),
            Math.Clamp(Position.Y, 0f, 720f));

        AddTrailPoint(Position);
    }

    private void AddTrailPoint(Vector2 point)
    {
        if (_trail.Count > 0)
        {
            var last = _trail[^1].Position;
            if (Vector2.DistanceSquared(last, point) < 9f)
            {
                return;
            }
        }

        _trail.Add(new TrailPoint(point, 0f));
        while (_trail.Count > 14)
        {
            _trail.RemoveAt(0);
        }
    }
}

public struct TrailPoint
{
    public TrailPoint(Vector2 position, float age)
    {
        Position = position;
        Age = age;
    }

    public Vector2 Position;
    public float Age;
}

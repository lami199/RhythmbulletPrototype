using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Rendering;

namespace RhythmbulletPrototype.Systems;

public sealed class BulletSystem
{
    private const int ShapeTextureSize = 96;
    private readonly List<BulletEvent> _events = new();
    private readonly List<Bullet> _active = new();
    private readonly List<LaserBeam> _lasers = new();
    private readonly Stack<Bullet> _pool = new();
    private readonly List<SpawnWarning> _warnings = new();
    private readonly List<SpawnCountdown> _countdowns = new();
    private readonly Dictionary<string, Texture2D> _shapeTextureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _rng = new(99);
    private Vector2 _spawnCursorPos = new(640f, 360f);
    private int _nextEventIndex;
    private float _playerRadius = 4f;
    private float _waveSafetyMargin = 8f;
    private bool _autoBalanceWaves = true;
    private float _phaseSeed;

    public float DefaultBulletRadius { get; set; } = 10f;
    public float DefaultOutlineThickness { get; set; } = 2f;
    public float CenterWarningRadius { get; set; } = 120f;
    public int CenterWarningLeadMs { get; set; } = 350;
    public float CenterWarningAlpha { get; set; } = 0.35f;
    public int SpawnCountdownLeadMs { get; set; } = 3000;
    public float RingDensityScale { get; set; } = 1f;
    public int PotentiallyImpossibleEvents { get; private set; }
    public int ActiveBulletCount => _active.Count + _lasers.Count;
    public int ActiveHazardsNearCursor { get; private set; }
    public float MinCursorClearancePx { get; private set; } = float.PositiveInfinity;
    public readonly record struct BulletPreviewDrawData(Vector2 Position, float Radius, float Scale, Color Fill, Color Outline, float OutlineThickness);

    public static readonly string[] MovingPatterns =
    {
        "left_drift",
        "right_drift",
        "left_right_drift",
        "uniform_outward_drift",
        "delayed_acceleration_ramp",
        "rotational_field_spin",
        "counter_rotation_shear",
        "sinusoidal_path_deviation",
        "expanding_spiral_conversion",
        "inward_gravity_pull",
        "elastic_repulsion_field",
        "angular_phase_offset_drift",
        "vertical_compression_field",
        "lateral_sweep_translation",
        "orbit_anchor_conversion",
        "pulsed_velocity_modulation",
        "sector_based_speed_variation",
        "randomized_micro_drift",
        "zigzag_vector_flip",
        "deceleration_freeze",
        "radial_explosion_burst",
        "expanding_radius_lock",
        "rotational_acceleration_ramp",
        "mirror_axis_reflection",
        "expanding_orbital_ringing",
        "delayed_homing_adjustment",
        "staggered_time_offset_release",
        "elliptical_orbit_drift",
        "fountain_arc",
        "mouse_track",
        "shoot_at_mouse"
    };

    public static readonly string[] StaticPatterns =
    {
        "static_single",
        "ring_8","ring_12","ring_16","ring_32","laser_static"
    };

    public void ApplyBeatmapDefaults(Beatmap beatmap)
    {
        _playerRadius = beatmap.CursorHitboxRadius;
        _waveSafetyMargin = beatmap.WaveSafetyMargin;
        _autoBalanceWaves = beatmap.AutoBalanceWaves;
        DefaultOutlineThickness = beatmap.BulletOutlineThickness;
        CenterWarningRadius = beatmap.CenterWarningRadius;
        CenterWarningLeadMs = beatmap.CenterWarningLeadMs;
        CenterWarningAlpha = beatmap.CenterWarningAlpha;
    }

    public void Reset(Beatmap beatmap)
    {
        _events.Clear();
        ApplyBeatmapDefaults(beatmap);
        foreach (var src in beatmap.Bullets.OrderBy(b => b.TimeMs))
        {
            _events.Add(CloneAndBalance(src));
        }

        ClearRuntime();
        PotentiallyImpossibleEvents = 0;
        for (var i = 0; i < _events.Count; i++)
        {
            var evt = _events[i];
            if (!HasEnoughLaneGap(evt)) PotentiallyImpossibleEvents++;
            var origin = ToVirtualPosition(evt);
            _countdowns.Add(new SpawnCountdown
            {
                Position = origin,
                SpawnTimeMs = evt.TimeMs
            });
            if (ShouldWarn(origin))
            {
                _warnings.Add(new SpawnWarning
                {
                    Position = origin,
                    StartTimeMs = evt.TimeMs - CenterWarningLeadMs,
                    SpawnTimeMs = evt.TimeMs
                });
            }
        }
    }

    public void ClearRuntime()
    {
        while (_active.Count > 0) RecycleAt(_active.Count - 1);
        _lasers.Clear();
        _warnings.Clear();
        _countdowns.Clear();
        _nextEventIndex = 0;
        ActiveHazardsNearCursor = 0;
        MinCursorClearancePx = float.PositiveInfinity;
    }

    public void SpawnImmediate(BulletEvent evt, int songTimeMs, Vector2 cursorPos)
    {
        SpawnEvent(CloneAndBalance(evt), cursorPos);
    }

    public void Update(float dt, int songTimeMs, Vector2 cursorPos)
    {
        while (_nextEventIndex < _events.Count && _events[_nextEventIndex].TimeMs <= songTimeMs)
        {
            SpawnEvent(_events[_nextEventIndex], cursorPos);
            _nextEventIndex++;
        }

        UpdateLasers(dt, cursorPos);

        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var b = _active[i];
            b.Age += dt;
            var t = b.Age;
            var basePos = b.Spawn + b.Velocity * t + 0.5f * b.Accel * t * t;
            var w = (t * b.Freq + b.Phase) * MathHelper.TwoPi;
            var perp = new Vector2(-b.Direction.Y, b.Direction.X);
            Vector2? absolutePos = b.Motion switch
            {
                MotionKind.UniformOutwardDrift => b.Spawn + b.OutwardDirection * (b.BaseSpeed * t),
                MotionKind.DelayedAccelerationRamp => b.Spawn + b.OutwardDirection * ComputeDelayedRampDistance(b.BaseSpeed, t),
                MotionKind.RotationalFieldSpin => b.OrbitCenter + Rotate(b.FromCenter, b.Freq * t * MathHelper.TwoPi * 0.25f) + b.OutwardDirection * (b.BaseSpeed * 0.58f * t),
                MotionKind.CounterRotationShear => b.OrbitCenter + Rotate(b.FromCenter, b.SectorSign * b.Freq * t * MathHelper.TwoPi * 0.27f) + b.OutwardDirection * (b.BaseSpeed * 0.52f * t),
                MotionKind.SinusoidalPathDeviation => b.Spawn + b.Direction * (b.BaseSpeed * t) + perp * (MathF.Sin(w) * b.Amp),
                MotionKind.ExpandingSpiralConversion => b.Spawn + Rotate(b.Direction, (0.45f + b.Freq * 0.55f) * t * t) * (b.BaseSpeed * t),
                MotionKind.InwardGravityPull =>
                    b.Spawn + b.Direction * (b.BaseSpeed * t) +
                    (b.OrbitCenter - (b.Spawn + b.Direction * (b.BaseSpeed * t))) *
                    (1f - MathF.Exp(-(0.42f + b.Amp * 0.010f) * t)),
                MotionKind.ElasticRepulsionField => b.Spawn + b.Direction * (b.BaseSpeed * t) + b.OutwardDirection * ComputeElasticRepulsionDisplacement(t, b.Amp),
                MotionKind.AngularPhaseOffsetDrift => b.Spawn + Rotate(b.Direction, MathF.Floor(t / 0.22f) * 0.08f) * (b.BaseSpeed * t),
                MotionKind.VerticalCompressionField =>
                    b.Spawn + b.Direction * (b.BaseSpeed * t) +
                    ComputeVerticalCompressionOffset(t, b.Spawn.Y, b.Direction),
                MotionKind.LateralSweepTranslation =>
                    b.Spawn + b.Direction * (b.BaseSpeed * t) + new Vector2((24f + b.Amp * 0.85f) * t, 0f),
                MotionKind.LateralSweepLeftTranslation =>
                    b.Spawn + b.Direction * (b.BaseSpeed * t) + new Vector2(-(24f + b.Amp * 0.85f) * t, 0f),
                MotionKind.OrbitAnchorConversion =>
                    ComputeAnchorTrajectoryPosition(t, b),
                MotionKind.PulsedVelocityModulation =>
                    b.Spawn + b.Direction * ComputePulsedDistance(b.BaseSpeed, t, b.Freq, b.Phase),
                MotionKind.SectorBasedSpeedVariation =>
                    b.Spawn + b.Direction * (b.BaseSpeed * (b.SectorSign > 0f ? 1.28f : 0.74f) * t),
                MotionKind.RandomizedMicroDrift =>
                    b.Spawn + Rotate(b.Direction, ComputeMicroDriftAngle(t, b.LocalNoise)) * (b.BaseSpeed * t),
                MotionKind.ZigzagVectorFlip =>
                    b.Spawn + ComputeZigZagDirection(t, b.Direction) * (b.BaseSpeed * t),
                MotionKind.DecelerationFreeze =>
                    b.Spawn + b.Direction * ComputeFreezeDistance(b.BaseSpeed, t, b.LocalNoise),
                MotionKind.RadialExplosionBurst =>
                    b.Spawn + b.Direction * (b.BaseSpeed * t) + ComputeRadialBurstOffset(t, b),
                MotionKind.ExpandingRadiusLock =>
                    ComputeRadiusLockPosition(t, b),
                MotionKind.RotationalAccelerationRamp =>
                    ComputeRotationRampPosition(t, b),
                MotionKind.MirrorAxisReflection =>
                    ComputeMirrorReflectionPosition(t, b),
                MotionKind.ExpandingOrbitalRinging =>
                    ComputeOrbitalRingingPosition(t, b),
                MotionKind.DelayedHomingAdjustment =>
                    ComputeDelayedHomingPosition(t, b),
                MotionKind.StaggeredTimeOffsetRelease =>
                    ComputeStaggerReleasePosition(t, b),
                MotionKind.EllipticalOrbitDrift =>
                    ComputeEllipticalDriftPosition(t, b),
                MotionKind.FountainArc =>
                    ComputeFountainArcPosition(t, b),
                MotionKind.MouseTrack =>
                    ComputeMouseTrackPosition(b, cursorPos, dt),
                MotionKind.MouseAimDirection =>
                    ComputeMouseAimAfterExpandPosition(b, dt),
                _ => null
            };

            if (absolutePos.HasValue)
            {
                b.Position = absolutePos.Value;
                b.Scale = 1f;
                b.Rotation += b.RotationSpeed * dt;
                if (b.Position.X < -180 || b.Position.X > 1460 || b.Position.Y < -180 || b.Position.Y > 900)
                {
                    RecycleAt(i);
                }
                continue;
            }

            Vector2 offset = b.Motion switch
            {
                MotionKind.ExpandingSpiralConversion => Rotate(b.Direction, w * 0.16f) * (b.Amp * (0.2f + t * 0.4f)),
                MotionKind.InwardGravityPull => SafeNormalize(b.OrbitCenter - basePos) * (b.Amp * (0.2f + t * 0.6f)),
                MotionKind.ElasticRepulsionField => SafeNormalize(b.OrbitCenter - b.Spawn) * (MathF.Sin(w * 0.75f) * b.Amp * 1.15f),
                MotionKind.AngularPhaseOffsetDrift => Rotate(b.Direction, MathF.Floor(t / 0.22f) * 0.08f) * (b.Amp * 0.65f),
                MotionKind.VerticalCompressionField => new Vector2(0f, b.Amp * 0.3f * t * t),
                MotionKind.Sine => new Vector2(MathF.Sin(w) * b.Amp, 0f),
                MotionKind.Arc => new Vector2(t * b.Amp * 0.35f, -b.Amp * t * (1f - 0.45f * t)),
                MotionKind.Rotate => new Vector2(MathF.Cos(w), MathF.Sin(w)) * b.Amp,
                MotionKind.Spiral => new Vector2(MathF.Cos(w), MathF.Sin(w)) * (b.Amp * (0.2f + t * 0.35f)),
                MotionKind.StaticRotate => new Vector2(MathF.Cos(w), MathF.Sin(w)) * (b.Amp * 0.45f),
                MotionKind.StaticPulse => new Vector2(MathF.Sin(w) * (b.Amp * 0.45f), MathF.Cos(w * 0.5f) * (b.Amp * 0.22f)),
                MotionKind.StaticExpand => new Vector2(MathF.Sin(w) * (b.Amp * 0.3f), 0f),
                _ => Vector2.Zero
            };
            b.Position = basePos + offset;
            b.Scale = 1f;
            b.Rotation += b.RotationSpeed * dt;
            if (b.Position.X < -180 || b.Position.X > 1460 || b.Position.Y < -180 || b.Position.Y > 900)
            {
                RecycleAt(i);
            }
        }

        UpdateDangerBudget(cursorPos);
    }

    public bool CheckCursorHit(Vector2 cursorPos, float cursorRadius)
    {
        for (var i = 0; i < _active.Count; i++)
        {
            var r = _active[i].Radius * _active[i].Scale + cursorRadius;
            if (Vector2.DistanceSquared(_active[i].Position, cursorPos) <= r * r) return true;
        }

        for (var i = 0; i < _lasers.Count; i++)
        {
            var l = _lasers[i];
            if (!l.IsActivePhase)
            {
                continue;
            }

            var r = cursorRadius + l.Width * 0.5f;
            if (DistanceSquaredPointToSegment(cursorPos, l.Start, l.End) <= r * r)
            {
                return true;
            }
        }
        return false;
    }

    public void Draw(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer? text, int songTimeMs, bool showHitboxes)
    {
        EnsureShapeTextures(render.Pixel.GraphicsDevice);
        DrawWarnings(spriteBatch, render, songTimeMs);
        DrawCountdowns(spriteBatch, text, songTimeMs);
        DrawLasers(spriteBatch, render, showHitboxes);
        for (var i = 0; i < _active.Count; i++)
        {
            DrawBullet(spriteBatch, render, _active[i], render.Pixel.GraphicsDevice);
            if (showHitboxes) render.DrawCircleOutline(spriteBatch, _active[i].Position, _active[i].Radius * _active[i].Scale, 1f, Color.OrangeRed);
        }
    }

    public void FillPreviewDrawData(List<BulletPreviewDrawData> target)
    {
        target.Clear();
        for (var i = 0; i < _active.Count; i++)
        {
            var b = _active[i];
            target.Add(new BulletPreviewDrawData(
                b.Position,
                b.Radius,
                b.Scale,
                b.Fill,
                b.Outline,
                b.OutlineThickness));
        }
    }

    private void UpdateLasers(float dt, Vector2 cursorPos)
    {
        for (var i = _lasers.Count - 1; i >= 0; i--)
        {
            var l = _lasers[i];
            l.AgeMs += dt * 1000f;
            var t = l.AgeMs / 1000f;
            var motion = ComputeLaserMotionOffset(l, t);
            l.CurrentOrigin = l.BaseOrigin + motion;
            l.CurrentDirection = ComputeLaserDirection(l, cursorPos, dt);
            l.Start = l.CurrentOrigin;
            l.End = l.CurrentOrigin + l.CurrentDirection * l.Length;
            l.IsActivePhase = l.AgeMs >= l.TelegraphMs && l.AgeMs <= (l.TelegraphMs + l.ActiveMs);

            if (l.AgeMs > l.TelegraphMs + l.ActiveMs)
            {
                _lasers.RemoveAt(i);
                continue;
            }

            _lasers[i] = l;
        }
    }

    private void UpdateDangerBudget(Vector2 cursorPos)
    {
        const float nearHazardPx = 140f;
        var nearCount = 0;
        var minClearance = float.PositiveInfinity;

        for (var i = 0; i < _active.Count; i++)
        {
            var b = _active[i];
            var d = Vector2.Distance(cursorPos, b.Position) - (b.Radius * b.Scale);
            minClearance = MathF.Min(minClearance, d);
            if (d <= nearHazardPx)
            {
                nearCount++;
            }
        }

        for (var i = 0; i < _lasers.Count; i++)
        {
            var l = _lasers[i];
            if (!l.IsActivePhase)
            {
                continue;
            }

            var d = MathF.Sqrt(DistanceSquaredPointToSegment(cursorPos, l.Start, l.End)) - (l.Width * 0.5f);
            minClearance = MathF.Min(minClearance, d);
            if (d <= nearHazardPx)
            {
                nearCount++;
            }
        }

        ActiveHazardsNearCursor = nearCount;
        MinCursorClearancePx = float.IsFinite(minClearance) ? minClearance : 9999f;
    }

    private static Vector2 ComputeLaserMotionOffset(LaserBeam l, float t)
    {
        var dir = l.BaseDirection;
        var perp = new Vector2(-dir.Y, dir.X);
        var w = (t * l.Freq + l.Phase) * MathHelper.TwoPi;

        return l.Motion switch
        {
            MotionKind.LateralSweepTranslation => new Vector2((24f + l.Amp * 0.85f) * t, 0f),
            MotionKind.LateralSweepLeftTranslation => new Vector2(-(24f + l.Amp * 0.85f) * t, 0f),
            MotionKind.SinusoidalPathDeviation => perp * (MathF.Sin(w) * MathF.Max(6f, l.Amp)),
            MotionKind.UniformOutwardDrift => dir * (MathF.Max(8f, l.Amp * 0.9f) * t),
            MotionKind.DelayedAccelerationRamp => dir * ComputeDelayedRampDistance(MathF.Max(8f, l.Amp * 0.9f), t),
            MotionKind.ExpandingSpiralConversion => Rotate(dir, 0.55f * t * t) * (MathF.Max(7f, l.Amp * 0.5f) * t),
            MotionKind.PulsedVelocityModulation => dir * ComputePulsedDistance(MathF.Max(8f, l.Amp), t, l.Freq, l.Phase) * 0.12f,
            _ => Vector2.Zero
        };
    }

    private static Vector2 ComputeLaserDirection(LaserBeam l, Vector2 cursorPos, float dt)
    {
        var direction = l.CurrentDirection.LengthSquared() < 0.0001f ? l.BaseDirection : l.CurrentDirection;
        if (l.Motion != MotionKind.MouseTrack)
        {
            return SafeNormalize(direction);
        }

        var toCursor = cursorPos - l.CurrentOrigin;
        if (toCursor.LengthSquared() < 0.0001f)
        {
            return SafeNormalize(direction);
        }

        var desired = SafeNormalize(toCursor);
        // Keep laser tracking dodgeable: steer with noticeable lag and a low turn cap.
        const float aimBlend = 0.09f;
        const float baseTurnRateRadPerSec = 0.2375f;
        var target = SafeNormalize(Vector2.Lerp(direction, desired, aimBlend));
        var maxTurn = Math.Max(0.01f, (baseTurnRateRadPerSec + l.Amp * 0.004f) * dt);
        return SafeNormalize(RotateTowards(direction, target, maxTurn));
    }

    private static float DistanceSquaredPointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var denom = ab.LengthSquared();
        if (denom < 0.0001f)
        {
            return Vector2.DistanceSquared(p, a);
        }

        var t = Math.Clamp(Vector2.Dot(p - a, ab) / denom, 0f, 1f);
        var c = a + ab * t;
        return Vector2.DistanceSquared(p, c);
    }

    private void DrawLasers(SpriteBatch spriteBatch, RenderHelpers render, bool showHitboxes)
    {
        for (var i = 0; i < _lasers.Count; i++)
        {
            var l = _lasers[i];
            var telegraphT = Math.Clamp(l.AgeMs / Math.Max(1f, l.TelegraphMs), 0f, 1f);
            if (!l.IsActivePhase)
            {
                // Telegraph cadence: pulse once, pulse twice, then fire.
                var pulse1 = Pulse01(telegraphT, 0.33f, 0.085f);
                var pulse2 = Pulse01(telegraphT, 0.67f, 0.085f);
                var pulse = MathF.Max(pulse1, pulse2);
                var alpha = 0.14f + telegraphT * 0.22f + pulse * 0.42f;
                var outlineThickness = l.Width + 4f + pulse * 10f;
                var coreThickness = Math.Max(2f, l.Width * 0.35f + pulse * 3.5f);

                render.DrawLine(spriteBatch, l.Start, l.End, outlineThickness, Color.White * alpha);
                render.DrawLine(spriteBatch, l.Start, l.End, coreThickness, new Color(255, 90, 90) * (alpha * 0.95f));
                render.DrawCircleOutline(spriteBatch, l.Start, 8f + pulse * 6f, 2f, Color.White * (0.55f + pulse * 0.45f));
                continue;
            }

            var activeAgeMs = l.AgeMs - l.TelegraphMs;
            var activeT = Math.Clamp(activeAgeMs / Math.Max(1f, l.ActiveMs), 0f, 1f);
            var fade = 1f - activeT * 0.25f;
            render.DrawLine(spriteBatch, l.Start, l.End, l.Width + 6f, l.Outline * (0.55f * fade));
            render.DrawLine(spriteBatch, l.Start, l.End, l.Width + 2f, l.Fill * (0.55f * fade));
            render.DrawLine(spriteBatch, l.Start, l.End, l.Width, l.Fill * (0.9f * fade));
            render.DrawLine(spriteBatch, l.Start, l.End, Math.Max(2f, l.Width * 0.28f), Color.White * (0.35f * fade));

            if (showHitboxes)
            {
                render.DrawLine(spriteBatch, l.Start, l.End, Math.Max(1f, l.Width * 0.5f), Color.Yellow * 0.8f);
            }
        }
    }

    private static float Pulse01(float t, float center, float halfWidth)
    {
        var d = MathF.Abs(t - center);
        if (d >= halfWidth)
        {
            return 0f;
        }

        return 1f - (d / halfWidth);
    }

    private void SpawnEvent(BulletEvent evt, Vector2 cursorPos)
    {
        _spawnCursorPos = cursorPos;
        var p = evt.Pattern.Trim().ToLowerInvariant().Replace(" ", "_");
        if (p == "rottodriftfan") p = "rot_drift_fan";
        if (p == "static_ring") p = "ring_12";
        var origin = ToVirtualPosition(evt);
        var style = ResolveStyle(evt);
        var count = Math.Max(1, evt.Count);
        var speed = Math.Max(0.1f, evt.Speed);
        var dir = GetDirection(p, origin, cursorPos, evt.DirectionDeg);
        var moving = MovingPatterns.Contains(p);
        var stat = StaticPatterns.Contains(p);

        if (p == "aimed")
        {
            float a;
            if (evt.DirectionDeg.HasValue)
            {
                a = MathHelper.ToRadians(evt.DirectionDeg.Value);
            }
            else
            {
                var toCursor = cursorPos - origin;
                if (toCursor.LengthSquared() < 0.001f) toCursor = new Vector2(0, 1);
                toCursor.Normalize();
                a = MathF.Atan2(toCursor.Y, toCursor.X);
            }

            SpawnFan(origin, a, MathHelper.ToRadians(evt.SpreadDeg ?? 30f), Math.Max(4, count), speed, style, MotionKind.None, 0f, 1f);
            return;
        }

        if (p == "radial")
        {
            SpawnRadial(origin, ScaleRingCount(Math.Max(10, count)), speed, style, MotionKind.None, 0f, 1f, 0f, evt.RingExpandDistance);
            return;
        }

        if (stat)
        {
            var profile = string.IsNullOrWhiteSpace(evt.MotionPattern)
                ? new MotionProfile(MotionKind.None, 0f, 1f)
                : ResolveMotionProfile(evt.MotionPattern, evt.MovementIntensity ?? 1f);
            SpawnStaticPattern(p, origin, dir, count, speed, evt, style, profile);
            return;
        }

        if (moving)
        {
            var motionPattern = string.IsNullOrWhiteSpace(evt.MotionPattern) ? p : evt.MotionPattern!;
            var profile = ResolveMotionProfile(motionPattern, evt.MovementIntensity ?? 1f);
            SpawnByPatternGeometry(p, origin, dir, count, speed, style, evt, profile);
            return;
        }

        // Legacy compatibility.
        if (p.Contains("wall")) SpawnWall(origin, dir, Math.Max(10, count), 900f, speed, style, MotionKind.None, 0f, 1f);
        else if (p.Contains("fan") || p.Contains("arc")) SpawnFan(origin, dir, MathHelper.ToRadians(evt.SpreadDeg ?? 60f), Math.Max(6, count), speed, style, MotionKind.None, 0f, 1f);
        else if (p.Contains("spiral") || p.Contains("helix") || p.Contains("vortex")) SpawnSpiral(origin, Math.Max(16, count), speed, style, MotionKind.Spiral, 22f, 1.35f);
        else SpawnRadial(origin, ScaleRingCount(Math.Max(10, count)), speed, style, MotionKind.None, 0f, 1f, 0f, evt.RingExpandDistance);
    }

    private void SpawnByPatternGeometry(string pattern, Vector2 origin, float direction, int count, float speed, Style style, BulletEvent evt, MotionProfile profile)
    {
        if (pattern.Contains("wall"))
        {
            SpawnWall(origin, direction, Math.Max(10, count), 900f, speed, style, profile.Kind, profile.Amp, profile.Freq);
            return;
        }

        if (pattern.Contains("fan") || pattern.Contains("arc"))
        {
            SpawnFan(
                origin,
                direction,
                MathHelper.ToRadians(evt.SpreadDeg ?? 60f),
                Math.Max(6, count),
                speed,
                style,
                profile.Kind,
                profile.Amp,
                profile.Freq);
            return;
        }

        if (pattern.Contains("spiral") || pattern.Contains("helix") || pattern.Contains("vortex"))
        {
            SpawnSpiral(origin, Math.Max(10, count), speed, style, profile.Kind, profile.Amp, profile.Freq);
            return;
        }

        SpawnRadial(origin, ScaleRingCount(Math.Max(3, count)), speed, style, profile.Kind, profile.Amp, profile.Freq, 0f, evt.RingExpandDistance);
    }

    private void SpawnRadial(Vector2 origin, int count, float speed, Style style, MotionKind motion, float amp, float freq, float life = 0f, float? ringExpandDistance = null)
    {
        for (var i = 0; i < count; i++) SpawnSingle(origin, MathHelper.TwoPi * i / Math.Max(1, count), speed, style, motion, amp, freq, life, origin, ringExpandDistance);
    }

    private void SpawnFan(Vector2 origin, float centerAngle, float spread, int count, float speed, Style style, MotionKind motion, float amp, float freq, float life = 0f)
    {
        for (var i = 0; i < count; i++)
        {
            var t = count <= 1 ? 0.5f : i / (float)(count - 1);
            SpawnSingle(origin, centerAngle - spread * 0.5f + spread * t, speed, style, motion, amp, freq, life, origin);
        }
    }

    private void SpawnWall(Vector2 origin, float angle, int count, float width, float speed, Style style, MotionKind motion, float amp, float freq)
    {
        var d = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var p = new Vector2(-d.Y, d.X);
        for (var i = 0; i < count; i++)
        {
            var t = count <= 1 ? 0.5f : i / (float)(count - 1);
            var pos = origin + p * ((t - 0.5f) * width);
            SpawnSingle(pos, angle, speed, style, motion, amp, freq, 0f, origin);
        }
    }

    private void SpawnSpiral(Vector2 origin, int count, float speed, Style style, MotionKind motion, float amp, float freq)
    {
        var step = MathHelper.ToRadians(11f + (count % 9));
        for (var i = 0; i < count; i++) SpawnSingle(origin, _phaseSeed + i * step, speed, style, motion, amp, freq, 0f, origin);
        _phaseSeed += MathHelper.ToRadians(17f);
    }

    private void SpawnStaticPattern(string pattern, Vector2 origin, float dir, int count, float speed, BulletEvent evt, Style style, MotionProfile profile)
    {
        var c = Math.Max(6, count);
        var motion = profile.Kind;
        var amp = profile.Amp;
        var freq = profile.Freq;

        if (pattern == "laser_static")
        {
            SpawnLaser(origin, dir, evt, style, profile);
            return;
        }

        if (pattern == "ring_8")
        {
            SpawnRadial(origin, ScaleRingCount(8), speed, style, motion, amp, freq, 0f, evt.RingExpandDistance);
            return;
        }

        if (pattern == "ring_16")
        {
            SpawnRadial(origin, ScaleRingCount(16), speed, style, motion, amp, freq, 0f, evt.RingExpandDistance);
            return;
        }

        if (pattern == "ring_12")
        {
            SpawnRadial(origin, ScaleRingCount(12), speed, style, motion, amp, freq, 0f, evt.RingExpandDistance);
            return;
        }

        if (pattern == "ring_32")
        {
            SpawnRadial(origin, ScaleRingCount(32), speed, style, motion, amp, freq, 0f, evt.RingExpandDistance);
            return;
        }

        if (pattern.Contains("single"))
        {
            // Static core pattern: always emitted straight down, movement profile modulates trajectory.
            SpawnSingle(origin, MathHelper.PiOver2, speed, style, motion, amp, freq, 0f, origin);
            return;
        }

        // Fallback for supported static patterns.
        SpawnRadial(origin, ScaleRingCount(Math.Max(10, c)), speed, style, motion, amp, freq, 0f, evt.RingExpandDistance);
    }

    private void SpawnLaser(Vector2 origin, float dir, BulletEvent evt, Style style, MotionProfile profile)
    {
        var laserMotion = profile.Kind == MotionKind.FountainArc ? MotionKind.None : profile.Kind;
        var telegraphMs = Math.Clamp(evt.TelegraphMs ?? 900, 50, 5000);
        var activeMs = Math.Clamp(evt.LaserDurationMs ?? 550, 50, 4000);
        var width = Math.Clamp(evt.LaserWidth ?? 22f, 4f, 220f);
        var length = Math.Clamp(evt.LaserLength ?? 1700f, 100f, 2600f);
        var direction = new Vector2(MathF.Cos(dir), MathF.Sin(dir));
        if (profile.Kind == MotionKind.MouseAimDirection)
        {
            var toCursor = _spawnCursorPos - origin;
            if (toCursor.LengthSquared() > 0.0001f)
            {
                direction = SafeNormalize(toCursor);
            }
        }

        var fill = ParseColor(evt.Color) ?? new Color(235, 40, 50);
        var outline = ParseColor(evt.OutlineColor) ?? Color.Black;
        _lasers.Add(new LaserBeam
        {
            BaseOrigin = origin,
            CurrentOrigin = origin,
            BaseDirection = SafeNormalize(direction),
            CurrentDirection = SafeNormalize(direction),
            Length = length,
            Width = width,
            TelegraphMs = telegraphMs,
            ActiveMs = activeMs,
            Fill = fill,
            Outline = outline,
            Motion = laserMotion,
            Amp = profile.Amp,
            Freq = profile.Freq,
            Phase = (float)_rng.NextDouble() * MathHelper.TwoPi
        });
    }

    private void SpawnSingle(Vector2 origin, float angle, float speed, Style style, MotionKind motion, float amp, float freq, float life = 0f, Vector2? orbitCenter = null, float? ringExpandDistance = null)
    {
        var b = _pool.Count > 0 ? _pool.Pop() : new Bullet();
        var d = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var aimDirection = d;
        if (motion == MotionKind.MouseAimDirection)
        {
            var toCursor = _spawnCursorPos - origin;
            if (toCursor.LengthSquared() > 0.0001f)
            {
                aimDirection = Vector2.Normalize(toCursor);
            }
        }
        var center = orbitCenter ?? origin;
        b.Spawn = origin;
        b.Position = origin;
        b.Velocity = d * speed;
        b.Accel = motion == MotionKind.Accel ? d * 28f : Vector2.Zero;
        b.Direction = d;
        b.BaseSpeed = speed;
        b.OrbitCenter = center;
        b.FromCenter = origin - center;
        b.OutwardDirection = SafeNormalize(b.FromCenter);
        if (b.OutwardDirection.LengthSquared() < 0.0001f) b.OutwardDirection = d;
        b.AimDirection = aimDirection;
        b.HomeDirection = SafeNormalize(new Vector2(640f, 360f) - origin);
        if (b.HomeDirection.LengthSquared() < 0.0001f) b.HomeDirection = d;
        b.SectorSign = ((int)MathF.Floor(((angle + MathHelper.Pi) / MathHelper.TwoPi) * 8f) % 2 == 0) ? 1f : -1f;
        b.LocalNoise = (float)_rng.NextDouble();
        b.Radius = style.Radius;
        b.Fill = style.Fill;
        b.Outline = style.Outline;
        b.Glow = style.Glow;
        b.GlowIntensity = style.GlowIntensity;
        b.OutlineThickness = style.OutlineThickness;
        b.Shape = style.Shape;
        b.Motion = motion;
        b.RingExpandDistance = ringExpandDistance ?? 0f;
        b.Amp = amp;
        b.Freq = freq;
        // Keep left/right oscillation deterministic so bullets always begin from placed spawn.
        b.Phase = motion == MotionKind.SinusoidalPathDeviation
            ? 0f
            : (float)_rng.NextDouble() * MathHelper.TwoPi;
        b.Age = 0f;
        b.Life = life;
        b.TrackMouseLocked = false;
        b.TrackMouseTime = 0f;
        // Fountain bullets should visually face downward regardless of launch direction.
        b.Rotation = motion == MotionKind.FountainArc ? MathHelper.PiOver2 : angle;
        var normalizedShape = NormalizeShape(style.Shape);
        b.RotationSpeed = (motion == MotionKind.Rotate || motion == MotionKind.StaticRotate) && normalizedShape != "kunai" ? 2f : 0f;
        b.Scale = 1f;
        _active.Add(b);
    }

    private void DrawBullet(SpriteBatch sb, RenderHelpers r, Bullet b, GraphicsDevice graphicsDevice)
    {
        var rr = b.Radius * b.Scale;
        var fill = b.Fill;
        DrawActiveGlow(sb, r, b, rr, b.Glow);

        var shape = NormalizeShape(b.Shape);
        var texture = GetShapeTexture(shape, graphicsDevice);
        if (texture == null)
        {
            r.DrawCircleFilled(sb, b.Position, rr, fill);
            r.DrawCircleOutline(sb, b.Position, rr, b.OutlineThickness, b.Outline);
            return;
        }

        var dimensions = GetShapeDimensions(shape, rr * 2.2f);
        var outlineDimensions = dimensions + new Vector2(b.OutlineThickness * 2f);
        var rotation = b.Rotation + MathHelper.PiOver2;
        if (shape == "kunai")
        {
            rotation += MathHelper.Pi;
        }
        var origin = new Vector2(texture.Width * 0.5f, texture.Height * 0.5f);

        sb.Draw(
            texture,
            b.Position,
            null,
            b.Outline,
            rotation,
            origin,
            new Vector2(outlineDimensions.X / texture.Width, outlineDimensions.Y / texture.Height),
            SpriteEffects.None,
            0f);

        sb.Draw(
            texture,
            b.Position,
            null,
            fill,
            rotation,
            origin,
            new Vector2(dimensions.X / texture.Width, dimensions.Y / texture.Height),
            SpriteEffects.None,
            0f);
    }

    private static void DrawActiveGlow(SpriteBatch sb, RenderHelpers r, Bullet b, float rr, Color glow)
    {
        if (b.GlowIntensity <= 0f)
        {
            return;
        }

        var pulse = 0.86f + MathF.Sin((b.Age * 5.5f + b.Phase) * MathHelper.TwoPi) * 0.14f;
        var alpha = Math.Clamp(b.GlowIntensity * (0.08f + pulse * 0.12f), 0f, 0.22f);
        for (var i = 3; i >= 1; i--)
        {
            var t = i / 3f;
            var rad = rr * (1.55f + t * 0.95f * pulse);
            r.DrawCircleFilled(sb, b.Position, rad, glow * (alpha * (0.28f + 0.22f * t)));
        }

        // Keep glow subtle and avoid extra circular overlays on top of shaped bullets.
    }

    private void EnsureShapeTextures(GraphicsDevice graphicsDevice)
    {
        // lazy allocation: only create textures once, on first draw.
        if (_shapeTextureCache.Count > 0)
        {
            return;
        }

        var names = new[]
        {
            "orb", "rice", "kunai", "butterfly", "star", "arrowhead", "droplet", "crystal",
            "diamond", "petal", "flame_shard", "cross_shard", "crescent", "heart_shard", "hex_shard"
        };
        for (var i = 0; i < names.Length; i++)
        {
            _shapeTextureCache[names[i]] = CreateShapeTexture(graphicsDevice, names[i], ShapeTextureSize);
        }
    }

    private Texture2D? GetShapeTexture(string shape, GraphicsDevice graphicsDevice)
    {
        if (_shapeTextureCache.TryGetValue(shape, out var texture))
        {
            return texture;
        }

        if (_shapeTextureCache.TryGetValue("orb", out var fallback))
        {
            return fallback;
        }

        EnsureShapeTextures(graphicsDevice);
        return _shapeTextureCache.TryGetValue(shape, out texture) ? texture : null;
    }

    private static Vector2 GetShapeDimensions(string shape, float baseSize)
    {
        return shape switch
        {
            "rice" => new Vector2(baseSize * 0.8f, baseSize * 2.0f),
            "kunai" => new Vector2(baseSize * 1.06f, baseSize * 2.25f),
            "arrowhead" => new Vector2(baseSize * 1.1f, baseSize * 1.7f),
            "crystal" => new Vector2(baseSize * 1.0f, baseSize * 2.0f),
            "flame_shard" => new Vector2(baseSize * 0.95f, baseSize * 2.1f),
            "droplet" => new Vector2(baseSize * 1.0f, baseSize * 1.85f),
            "petal" => new Vector2(baseSize * 0.9f, baseSize * 1.85f),
            "cross_shard" => new Vector2(baseSize * 1.35f, baseSize * 1.35f),
            "butterfly" => new Vector2(baseSize * 1.7f, baseSize * 1.45f),
            "heart_shard" => new Vector2(baseSize * 1.25f, baseSize * 1.2f),
            "star" => new Vector2(baseSize * 1.2f, baseSize * 1.2f),
            "hex_shard" => new Vector2(baseSize * 1.15f, baseSize * 1.15f),
            "diamond" => new Vector2(baseSize * 1.05f, baseSize * 1.45f),
            "crescent" => new Vector2(baseSize * 1.2f, baseSize * 1.2f),
            _ => new Vector2(baseSize, baseSize)
        };
    }

    private static Texture2D CreateShapeTexture(GraphicsDevice graphicsDevice, string shape, int size)
    {
        var tex = new Texture2D(graphicsDevice, size, size, false, SurfaceFormat.Color);
        var data = new Color[size * size];
        var aaSamples = new[]
        {
            new Vector2(-0.25f, -0.25f),
            new Vector2(0.25f, -0.25f),
            new Vector2(-0.25f, 0.25f),
            new Vector2(0.25f, 0.25f)
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var coverage = 0f;
                for (var s = 0; s < aaSamples.Length; s++)
                {
                    var sx = x + 0.5f + aaSamples[s].X;
                    var sy = y + 0.5f + aaSamples[s].Y;
                    var nx = sx / size * 2f - 1f;
                    var ny = 1f - sy / size * 2f;
                    if (IsInsideShape(shape, nx, ny))
                    {
                        coverage += 1f;
                    }
                }

                var a = (byte)Math.Clamp((coverage / aaSamples.Length) * 255f, 0f, 255f);
                // Premultiplied alpha: avoid sprite quad/square artifacts around shapes.
                data[y * size + x] = new Color(a, a, a, a);
            }
        }

        tex.SetData(data);
        return tex;
    }

    private static bool IsInsideShape(string shape, float x, float y)
    {
        shape = NormalizeShape(shape);
        return shape switch
        {
            "circle" or "orb" => x * x + y * y <= 0.98f,
            "rice" => InEllipse(x, y, 0f, 0f, 0.34f, 0.95f),
            "kunai" => IsKunaiShape(x, y),
            "arrowhead" => IsArrowShape(x, y),
            "butterfly" => IsButterflyShape(x, y),
            "star" => IsStarShape(x, y),
            "droplet" => IsDropletShape(x, y),
            "crystal" => IsCrystalShape(x, y),
            "diamond" => MathF.Abs(x) / 0.58f + MathF.Abs(y) / 0.98f <= 1f,
            "petal" => IsPetalShape(x, y),
            "flame_shard" => IsFlameShape(x, y),
            "cross_shard" => IsCrossShardShape(x, y),
            "crescent" => (x * x + y * y <= 0.95f) && ((x - 0.36f) * (x - 0.36f) + y * y >= 0.55f),
            "heart_shard" => IsHeartShape(x, y),
            "hex_shard" => IsHexShape(x, y),
            _ => x * x + y * y <= 0.98f
        };
    }

    private static bool InEllipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        var dx = (x - cx) / Math.Max(rx, 0.0001f);
        var dy = (y - cy) / Math.Max(ry, 0.0001f);
        return dx * dx + dy * dy <= 1f;
    }

    private static bool IsKunaiShape(float x, float y)
    {
        var blade = MathF.Abs(x) / 0.42f + MathF.Abs(y + 0.18f) / 0.78f <= 1f && y <= 0.42f;
        var handle = MathF.Abs(x) <= 0.13f && y > 0.18f && y <= 0.88f;
        var ringOuter = InEllipse(x, y, 0f, 0.9f, 0.32f, 0.2f);
        var ringInner = InEllipse(x, y, 0f, 0.9f, 0.18f, 0.1f);
        return blade || handle || (ringOuter && !ringInner);
    }

    private static bool IsArrowShape(float x, float y)
    {
        var head = PointInTriangle(new Vector2(x, y), new Vector2(0f, -1f), new Vector2(-0.66f, 0.1f), new Vector2(0.66f, 0.1f));
        var shaft = MathF.Abs(x) <= 0.2f && y > 0.1f && y <= 0.95f;
        var notch = PointInTriangle(new Vector2(x, y), new Vector2(0f, 0.28f), new Vector2(-0.2f, 0.55f), new Vector2(0.2f, 0.55f));
        return (head || shaft) && !notch;
    }

    private static bool IsButterflyShape(float x, float y)
    {
        var leftWing = InEllipse(x, y, -0.46f, -0.15f, 0.38f, 0.45f) || InEllipse(x, y, -0.42f, 0.3f, 0.33f, 0.28f);
        var rightWing = InEllipse(x, y, 0.46f, -0.15f, 0.38f, 0.45f) || InEllipse(x, y, 0.42f, 0.3f, 0.33f, 0.28f);
        var body = MathF.Abs(x) <= 0.1f && y >= -0.72f && y <= 0.78f;
        return leftWing || rightWing || body;
    }

    private static bool IsStarShape(float x, float y)
    {
        var angle = MathF.Atan2(y, x);
        var radius = MathF.Sqrt(x * x + y * y);
        var contour = 0.35f + 0.4f * MathF.Max(0f, MathF.Cos(angle * 5f));
        return radius <= contour;
    }

    private static bool IsDropletShape(float x, float y)
    {
        var bulb = InEllipse(x, y, 0f, -0.2f, 0.52f, 0.52f);
        var tip = PointInTriangle(new Vector2(x, y), new Vector2(0f, 1f), new Vector2(-0.3f, 0.2f), new Vector2(0.3f, 0.2f));
        return bulb || tip;
    }

    private static bool IsCrystalShape(float x, float y)
    {
        var top = PointInTriangle(new Vector2(x, y), new Vector2(0f, -1f), new Vector2(-0.46f, -0.2f), new Vector2(0.46f, -0.2f));
        var core = MathF.Abs(x) / 0.4f + MathF.Abs(y - 0.05f) / 0.8f <= 1f;
        var tail = PointInTriangle(new Vector2(x, y), new Vector2(0f, 1f), new Vector2(-0.3f, 0.2f), new Vector2(0.3f, 0.2f));
        return top || core || tail;
    }

    private static bool IsPetalShape(float x, float y)
    {
        var top = InEllipse(x, y, 0f, -0.22f, 0.42f, 0.52f);
        var bottom = InEllipse(x, y, 0f, 0.22f, 0.42f, 0.52f);
        return top && bottom;
    }

    private static bool IsFlameShape(float x, float y)
    {
        var outer = InEllipse(x, y, 0f, 0.12f, 0.5f, 0.82f);
        var tip = PointInTriangle(new Vector2(x, y), new Vector2(0f, -1f), new Vector2(-0.24f, -0.2f), new Vector2(0.24f, -0.2f));
        var cut = InEllipse(x, y, 0.12f, 0.22f, 0.24f, 0.35f);
        return (outer || tip) && !cut;
    }

    private static bool IsCrossShardShape(float x, float y)
    {
        var vertical = MathF.Abs(x) <= 0.21f && MathF.Abs(y) <= 1f - MathF.Abs(x) * 0.36f;
        var horizontal = MathF.Abs(y) <= 0.21f && MathF.Abs(x) <= 1f - MathF.Abs(y) * 0.36f;
        return vertical || horizontal;
    }

    private static bool IsHeartShape(float x, float y)
    {
        y -= 0.05f;
        var topLeft = InEllipse(x, y, -0.28f, -0.26f, 0.33f, 0.33f);
        var topRight = InEllipse(x, y, 0.28f, -0.26f, 0.33f, 0.33f);
        var bottom = PointInTriangle(new Vector2(x, y), new Vector2(0f, 0.98f), new Vector2(-0.7f, -0.02f), new Vector2(0.7f, -0.02f));
        return topLeft || topRight || bottom;
    }

    private static bool IsHexShape(float x, float y)
    {
        var ax = MathF.Abs(x);
        var ay = MathF.Abs(y);
        if (ay > 0.93f || ax > 0.82f) return false;
        return ay + ax * 0.58f <= 0.93f;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var v0 = c - a;
        var v1 = b - a;
        var v2 = p - a;
        var dot00 = Vector2.Dot(v0, v0);
        var dot01 = Vector2.Dot(v0, v1);
        var dot02 = Vector2.Dot(v0, v2);
        var dot11 = Vector2.Dot(v1, v1);
        var dot12 = Vector2.Dot(v1, v2);
        var invDenom = 1f / (dot00 * dot11 - dot01 * dot01);
        var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        var v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        return (u >= 0f) && (v >= 0f) && (u + v < 1f);
    }

    private void DrawWarnings(SpriteBatch spriteBatch, RenderHelpers render, int songTimeMs)
    {
        for (var i = 0; i < _warnings.Count; i++)
        {
            var warning = _warnings[i];
            if (songTimeMs < warning.StartTimeMs || songTimeMs > warning.SpawnTimeMs + 120) continue;
            float alpha;
            if (songTimeMs <= warning.SpawnTimeMs)
            {
                var t = Math.Clamp((songTimeMs - warning.StartTimeMs) / (float)Math.Max(1, CenterWarningLeadMs), 0f, 1f);
                alpha = CenterWarningAlpha * (0.35f + t * 0.65f);
            }
            else
            {
                var fadeT = Math.Clamp((songTimeMs - warning.SpawnTimeMs) / 120f, 0f, 1f);
                alpha = CenterWarningAlpha * (1f - fadeT);
            }
            var pulse = 1f + 0.08f * MathF.Sin(songTimeMs * 0.015f);
            var radius = 22f * pulse;
            render.DrawCircleFilled(spriteBatch, warning.Position, radius, new Color(255, 90, 90) * (alpha * 0.35f));
            render.DrawCircleOutline(spriteBatch, warning.Position, radius + 8f, 2f, new Color(255, 245, 245) * alpha);
        }
    }

    private void DrawCountdowns(SpriteBatch spriteBatch, BitmapTextRenderer? text, int songTimeMs)
    {
        if (text is null)
        {
            return;
        }

        var leadMs = Math.Max(1000, SpawnCountdownLeadMs);
        for (var i = 0; i < _countdowns.Count; i++)
        {
            var countdown = _countdowns[i];
            if (songTimeMs >= countdown.SpawnTimeMs)
            {
                continue;
            }

            var remainingMs = countdown.SpawnTimeMs - songTimeMs;
            if (remainingMs > leadMs)
            {
                continue;
            }

            var number = (int)MathF.Ceiling(remainingMs / 1000f);
            if (number < 1 || number > 3)
            {
                continue;
            }

            var fade = Math.Clamp(remainingMs / (float)leadMs, 0f, 1f);
            var alpha = 0.16f + fade * 0.36f;
            var pos = countdown.Position + new Vector2(-7f, -13f);
            text.DrawString(spriteBatch, number.ToString(), pos, new Color(255, 38, 38) * alpha, 2.0f);
        }
    }

    private BulletEvent CloneAndBalance(BulletEvent src)
    {
        var evt = new BulletEvent
        {
            TimeMs = src.TimeMs, Pattern = src.Pattern, Count = src.Count, Speed = src.Speed, IntervalMs = src.IntervalMs,
            BulletType = src.BulletType, BulletSize = src.BulletSize, Radius = src.Radius, Color = src.Color, OutlineColor = src.OutlineColor,
            GlowColor = src.GlowColor, GlowIntensity = src.GlowIntensity, OutlineThickness = src.OutlineThickness, SpreadDeg = src.SpreadDeg,
            AngleStepDeg = src.AngleStepDeg, DirectionDeg = src.DirectionDeg, MovementIntensity = src.MovementIntensity, RingExpandDistance = src.RingExpandDistance, MotionPattern = src.MotionPattern,
            TelegraphMs = src.TelegraphMs, LaserDurationMs = src.LaserDurationMs, LaserWidth = src.LaserWidth, LaserLength = src.LaserLength, X = src.X, Y = src.Y
        };
        ApplyReadabilityPolicy(evt);
        if (!_autoBalanceWaves || !IsWavePattern(evt.Pattern)) return evt;
        while (evt.Count > 3 && !HasEnoughLaneGap(evt)) evt.Count--;
        return evt;
    }

    private static void ApplyReadabilityPolicy(BulletEvent evt)
    {
        var p = evt.Pattern.Trim().ToLowerInvariant();
        var motion = (evt.MotionPattern ?? string.Empty).Trim().ToLowerInvariant();

        if (p.Contains("laser"))
        {
            var minTelegraph = 450;
            if (motion.Contains("mouse")) minTelegraph = 700;
            if (evt.Count >= 20) minTelegraph = Math.Max(minTelegraph, 800);
            evt.TelegraphMs = Math.Max(minTelegraph, evt.TelegraphMs ?? 0);
        }

        if ((p.Contains("ring") || p == "radial") && evt.Count > 64)
        {
            evt.Count = 64;
        }
    }

    private bool HasEnoughLaneGap(BulletEvent evt)
    {
        var gap = ComputeLaneGap(evt);
        var required = (_playerRadius * 2f) + _waveSafetyMargin;
        return gap >= required;
    }

    private float ComputeLaneGap(BulletEvent evt)
    {
        var radius = evt.BulletSize ?? evt.Radius ?? DefaultBulletRadius;
        var count = Math.Max(1, evt.Count);
        var p = evt.Pattern.Trim().ToLowerInvariant();
        if (p.Contains("wall")) return (900f / Math.Max(1, count - 1)) - radius * 2f;
        if (p.Contains("fan") || p.Contains("arc")) return (MathHelper.ToRadians(evt.SpreadDeg ?? 60f) * 320f / Math.Max(1, count - 1)) - radius * 2f;
        return (MathHelper.TwoPi * 220f / count) - radius * 2f;
    }

    private static bool IsWavePattern(string pattern)
    {
        var p = pattern.Trim().ToLowerInvariant();
        return p.Contains("ring") || p.Contains("fan") || p.Contains("wall") || p.Contains("spiral") || p.Contains("wave") || p.Contains("grid") || p == "radial";
    }

    private Style ResolveStyle(BulletEvent evt)
    {
        var fill = ParseColor(evt.Color) ?? new Color(235, 40, 50);
        var outline = ParseColor(evt.OutlineColor) ?? Color.Black;
        var glow = ParseColor(evt.GlowColor) ?? Color.Lerp(fill, Color.White, 0.35f);
        return new Style
        {
            Radius = evt.BulletSize ?? evt.Radius ?? DefaultBulletRadius,
            Fill = fill,
            Outline = outline,
            Glow = glow,
            GlowIntensity = evt.GlowIntensity ?? 0.12f,
            OutlineThickness = evt.OutlineThickness ?? DefaultOutlineThickness,
            Shape = evt.BulletType
        };
    }

    private static MotionProfile ResolveMotionProfile(string? movingPattern, float movementIntensity)
    {
        var p = (movingPattern ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_");
        var intensity = Math.Clamp(movementIntensity, 0f, 3f);

        if (string.IsNullOrWhiteSpace(p) || p == "none")
        {
            return new MotionProfile(MotionKind.None, 0f, 1f);
        }

        return p switch
        {
            "left_drift" => new MotionProfile(MotionKind.LateralSweepLeftTranslation, 18f * intensity, 1f),
            "right_drift" => new MotionProfile(MotionKind.LateralSweepTranslation, 18f * intensity, 1f),
            "left_right_drift" => new MotionProfile(MotionKind.SinusoidalPathDeviation, 24f * intensity, 1.05f),
            "uniform_outward_drift" => new MotionProfile(MotionKind.UniformOutwardDrift, 10f * intensity, 1f),
            "delayed_acceleration_ramp" => new MotionProfile(MotionKind.DelayedAccelerationRamp, 10f * intensity, 1f),
            "rotational_field_spin" => new MotionProfile(MotionKind.RotationalFieldSpin, 22f * intensity, 0.9f),
            "counter_rotation_shear" => new MotionProfile(MotionKind.CounterRotationShear, 24f * intensity, 0.9f),
            "sinusoidal_path_deviation" => new MotionProfile(MotionKind.SinusoidalPathDeviation, 20f * intensity, 1.2f),
            "expanding_spiral_conversion" => new MotionProfile(MotionKind.ExpandingSpiralConversion, 18f * intensity, 1.0f),
            "inward_gravity_pull" => new MotionProfile(MotionKind.InwardGravityPull, 26f * intensity, 1.0f),
            "elastic_repulsion_field" => new MotionProfile(MotionKind.ElasticRepulsionField, 22f * intensity, 1.0f),
            "angular_phase_offset_drift" => new MotionProfile(MotionKind.AngularPhaseOffsetDrift, 18f * intensity, 1.0f),
            "vertical_compression_field" => new MotionProfile(MotionKind.VerticalCompressionField, 16f * intensity, 1.0f),
            "lateral_sweep_translation" => new MotionProfile(MotionKind.LateralSweepTranslation, 18f * intensity, 1f),
            "orbit_anchor_conversion" => new MotionProfile(MotionKind.OrbitAnchorConversion, 30f * intensity, 0.85f),
            "pulsed_velocity_modulation" => new MotionProfile(MotionKind.PulsedVelocityModulation, 20f * intensity, 1.2f),
            "sector_based_speed_variation" => new MotionProfile(MotionKind.SectorBasedSpeedVariation, 24f * intensity, 1f),
            "randomized_micro_drift" => new MotionProfile(MotionKind.RandomizedMicroDrift, 10f * intensity, 1.35f),
            "zigzag_vector_flip" => new MotionProfile(MotionKind.ZigzagVectorFlip, 16f * intensity, 1.0f),
            "deceleration_freeze" => new MotionProfile(MotionKind.DecelerationFreeze, 18f * intensity, 1.0f),
            "radial_explosion_burst" => new MotionProfile(MotionKind.RadialExplosionBurst, 34f * intensity, 1.0f),
            "expanding_radius_lock" => new MotionProfile(MotionKind.ExpandingRadiusLock, 24f * intensity, 1.0f),
            "rotational_acceleration_ramp" => new MotionProfile(MotionKind.RotationalAccelerationRamp, 24f * intensity, 1.15f),
            "mirror_axis_reflection" => new MotionProfile(MotionKind.MirrorAxisReflection, 16f * intensity, 1.0f),
            "expanding_orbital_ringing" => new MotionProfile(MotionKind.ExpandingOrbitalRinging, 24f * intensity, 0.95f),
            "delayed_homing_adjustment" => new MotionProfile(MotionKind.DelayedHomingAdjustment, 20f * intensity, 1.0f),
            "staggered_time_offset_release" => new MotionProfile(MotionKind.StaggeredTimeOffsetRelease, 14f * intensity, 1.0f),
            "elliptical_orbit_drift" => new MotionProfile(MotionKind.EllipticalOrbitDrift, 24f * intensity, 0.9f),
            "fountain_arc" => new MotionProfile(MotionKind.FountainArc, 28f * intensity, 1.0f),
            "mouse_track" => new MotionProfile(MotionKind.MouseTrack, 24f * intensity, 1.0f),
            "shoot_at_mouse" => new MotionProfile(MotionKind.MouseAimDirection, 0f, 1.0f),
            _ => new MotionProfile(MotionKind.UniformOutwardDrift, 10f * intensity, 1f)
        };
    }

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static Vector2 SafeNormalize(Vector2 v)
    {
        if (v.LengthSquared() < 0.0001f) return Vector2.Zero;
        v.Normalize();
        return v;
    }

    private static float ComputeDelayedRampDistance(float baseSpeed, float t)
    {
        const float delay = 0.30f;
        const float ramp = 0.70f;
        var holdSpeed = baseSpeed * 0.06f;
        if (t <= delay)
        {
            return holdSpeed * t;
        }

        var u = t - delay;
        if (u <= ramp)
        {
            return holdSpeed * delay + (holdSpeed * u) + ((baseSpeed - holdSpeed) * (u * u) / (2f * ramp));
        }

        return holdSpeed * delay
               + (holdSpeed * ramp)
               + ((baseSpeed - holdSpeed) * ramp * 0.5f)
               + (baseSpeed * (u - ramp));
    }

    private static float ComputeElasticRepulsionDisplacement(float t, float amp)
    {
        var inPhase = SmoothStep01(t / 0.85f);
        var outPhase = SmoothStep01((t - 0.85f) / 0.95f);
        var inward = inPhase * (amp * 1.15f);
        var outward = outPhase * outPhase * (amp * 2.25f);
        return outward - inward;
    }

    private static Vector2 ComputeVerticalCompressionOffset(float t, float spawnY, Vector2 dir)
    {
        var upperFactor = 1.0f + (1f - Math.Clamp(spawnY / 720f, 0f, 1f)) * 1.1f;
        var down = 56f * upperFactor * t * t;
        var sign = MathF.Abs(dir.X) < 0.01f ? 1f : MathF.Sign(dir.X);
        return new Vector2(down * 0.11f * sign, down);
    }

    private static float SmoothStep01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Vector2 ComputeAnchorTrajectoryPosition(float t, Bullet b)
    {
        var amp = 26f + b.Amp * 1.2f;
        var speed = 0.5f + b.Freq * 0.35f;
        var mode = ((int)MathF.Floor(b.LocalNoise * 3f)) % 3;
        var ang = t * MathHelper.TwoPi * speed + b.Phase;

        Vector2 anchor = mode switch
        {
            0 => b.OrbitCenter + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * amp, // circle
            1 => b.OrbitCenter + new Vector2(MathF.Sin(ang) * (amp * 1.5f), 0f),     // horizontal
            _ => b.OrbitCenter + new Vector2(MathF.Sin(ang) * (amp * 1.3f), MathF.Sin(ang * 2f) * (amp * 0.7f)) // figure-eight
        };

        var rel = Rotate(b.FromCenter, ang * 0.65f);
        return anchor + rel;
    }

    private static float ComputePulsedDistance(float baseSpeed, float t, float freq, float phase)
    {
        var pulse = 0.5f + 0.5f * MathF.Sin((t * (0.7f + freq * 0.35f) + phase) * MathHelper.TwoPi);
        var speedMul = 0.62f + pulse * 0.78f;
        return baseSpeed * speedMul * t;
    }

    private static float ComputeMicroDriftAngle(float t, float noiseSeed)
    {
        var n1 = MathF.Sin((t * 3.7f + noiseSeed * 11.3f) * MathHelper.TwoPi);
        var n2 = MathF.Sin((t * 6.1f + noiseSeed * 17.9f) * MathHelper.TwoPi) * 0.5f;
        var bounded = (n1 + n2) * 0.5f;
        return bounded * 0.22f;
    }

    private static Vector2 ComputeZigZagDirection(float t, Vector2 direction)
    {
        const float interval = 0.20f;
        const float flipAngle = 0.44f;
        var step = (int)MathF.Floor(t / interval);
        var sign = (step & 1) == 0 ? -1f : 1f;
        return Rotate(direction, sign * flipAngle);
    }

    private static float ComputeFreezeDistance(float baseSpeed, float t, float seed)
    {
        const float freezeStart = 0.0f;
        const float freezeEnd = 1.55f;
        var jitter = 1f + (seed - 0.5f) * 0.08f;
        var slowT = Math.Clamp((t - freezeStart) / Math.Max(0.001f, freezeEnd - freezeStart), 0f, 1f);
        var speedScale = 1f - slowT;
        var freezeDistance = baseSpeed * jitter * (t - 0.5f * (slowT * t));

        // Optional micro-resume far later (very subtle, keeps "stacking" behavior by default).
        if (t > 3.8f)
        {
            freezeDistance += baseSpeed * 0.08f * (t - 3.8f);
        }

        return Math.Max(0f, freezeDistance);
    }

    private static Vector2 ComputeRadialBurstOffset(float t, Bullet b)
    {
        const float burstDelay = 0.62f;
        if (t <= burstDelay) return Vector2.Zero;
        var dt = t - burstDelay;
        var burstDir = SafeNormalize(b.Direction + Rotate(b.Direction, (b.LocalNoise - 0.5f) * 0.46f));
        var burstSpeed = b.BaseSpeed * (1.85f + b.Amp * 0.03f);
        return burstDir * (burstSpeed * dt);
    }

    private static Vector2 ComputeRadiusLockPosition(float t, Bullet b)
    {
        var dir = b.OutwardDirection.LengthSquared() > 0.0001f ? b.OutwardDirection : b.Direction;
        var radius = b.FromCenter.Length() + (b.BaseSpeed * 0.35f * t) + (8f + b.Amp * 0.22f) * t * t;
        return b.OrbitCenter + dir * radius;
    }

    private static Vector2 ComputeRotationRampPosition(float t, Bullet b)
    {
        var baseAngle = MathF.Atan2(b.FromCenter.Y, b.FromCenter.X);
        var radius = Math.Max(24f, b.FromCenter.Length() + b.BaseSpeed * 0.08f * t);
        var omega0 = 0.35f;
        var alpha = 1.45f + b.Freq * 0.55f;
        var angle = baseAngle + omega0 * t + 0.5f * alpha * t * t;
        return b.OrbitCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static Vector2 ComputeMirrorReflectionPosition(float t, Bullet b)
    {
        const float axisX = 640f;
        var xRaw = b.Spawn.X + b.Direction.X * b.BaseSpeed * t;
        var reflectedX = axisX + MathF.Abs(xRaw - axisX);
        if (b.Spawn.X < axisX) reflectedX = axisX - MathF.Abs(xRaw - axisX);
        var y = b.Spawn.Y + b.Direction.Y * b.BaseSpeed * t;
        return new Vector2(reflectedX, y);
    }

    private static Vector2 ComputeOrbitalRingingPosition(float t, Bullet b)
    {
        var baseRadius = Math.Max(20f, b.FromCenter.Length());
        var step = MathF.Floor(t / 0.34f);
        var radius = baseRadius + step * (8f + b.Amp * 0.32f);
        var baseAngle = MathF.Atan2(b.FromCenter.Y, b.FromCenter.X);
        var omega = (0.65f + b.Freq * 0.25f) * MathHelper.TwoPi;
        var angle = baseAngle + omega * t;
        return b.OrbitCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static Vector2 ComputeDelayedHomingPosition(float t, Bullet b)
    {
        const float delay = 0.62f;
        var pre = b.Spawn + b.Direction * (b.BaseSpeed * MathF.Min(t, delay));
        if (t <= delay)
        {
            return pre;
        }

        var dt = t - delay;
        var dot = Math.Clamp(Vector2.Dot(b.Direction, b.HomeDirection), -1f, 1f);
        var cross = b.Direction.X * b.HomeDirection.Y - b.Direction.Y * b.HomeDirection.X;
        var ang = MathF.Acos(dot) * 0.22f;
        var signed = cross < 0f ? -ang : ang;
        var adjusted = Rotate(b.Direction, signed);
        return pre + adjusted * (b.BaseSpeed * dt);
    }

    private static Vector2 ComputeStaggerReleasePosition(float t, Bullet b)
    {
        var delayBand = (MathF.Floor(b.LocalNoise * 4f) / 4f) * 0.36f;
        var sectorDelay = (b.SectorSign > 0f ? 0.06f : 0.18f);
        var delay = delayBand + sectorDelay;
        if (t <= delay) return b.Spawn;
        return b.Spawn + b.Direction * (b.BaseSpeed * (t - delay));
    }

    private static Vector2 ComputeEllipticalDriftPosition(float t, Bullet b)
    {
        var baseAngle = MathF.Atan2(b.FromCenter.Y, b.FromCenter.X);
        var theta = baseAngle + (0.7f + b.Freq * 0.2f) * MathHelper.TwoPi * t;
        var rx = Math.Max(20f, b.FromCenter.Length() * 0.9f) + t * (12f + b.Amp * 0.35f);
        var ry = Math.Max(12f, b.FromCenter.Length() * 0.55f) + t * (7f + b.Amp * 0.22f);
        var local = new Vector2(MathF.Cos(theta) * rx, MathF.Sin(theta) * ry);
        var majorAxisRot = 0.22f * t;
        return b.OrbitCenter + Rotate(local, majorAxisRot);
    }

    private static Vector2 ComputeFountainArcPosition(float t, Bullet b)
    {
        const float defaultExpandPhaseSec = 0.22f;
        var outward = b.Direction.LengthSquared() < 0.0001f ? new Vector2(0f, 1f) : SafeNormalize(b.Direction);
        var expandSpeed = MathF.Max(120f + b.Amp * 1.2f, b.BaseSpeed * 0.45f);
        var defaultExpandDistance = expandSpeed * defaultExpandPhaseSec;
        var configuredExpandDistance = Math.Max(0f, b.RingExpandDistance);
        var expandDistance = configuredExpandDistance > 0.01f ? configuredExpandDistance : defaultExpandDistance;
        var expandPhaseSec = Math.Max(0.01f, expandDistance / Math.Max(1f, expandSpeed));

        if (t <= expandPhaseSec)
        {
            // Early spread preserves ring shape before vertical fountain motion begins.
            return b.Spawn + outward * (expandSpeed * t);
        }

        var u = t - expandPhaseSec;
        var spreadBase = b.Spawn + outward * expandDistance;
        var keepSpread = outward * (b.BaseSpeed * 0.55f * u);
        var launchUp = -(240f + b.Amp * 1.8f);
        var gravity = 520f + b.Amp * 2.4f;
        var vertical = launchUp * u + 0.5f * gravity * u * u;
        return spreadBase + keepSpread + new Vector2(0f, vertical);
    }

    private static Vector2 ComputeMouseTrackPosition(Bullet b, Vector2 cursorPos, float dt)
    {
        // Tunables for "initial seek then straight" feel.
        const float trackStartDelaySec = 0.30f;
        const float minTrackingTimeSec = 0.22f;
        const float maxTrackingDurationSec = 0.9f;
        const float baseTurnRateRadPerSec = 3.2f;

        if (b.TrackMouseLocked)
        {
            b.Velocity = b.Direction * b.BaseSpeed;
            b.Position += b.Velocity * dt;
            return b.Position;
        }

        // Let patterns (like ring_32) establish their formation before tracking begins.
        if (b.Age < trackStartDelaySec)
        {
            b.Velocity = b.Direction * b.BaseSpeed;
            b.Position += b.Velocity * dt;
            return b.Position;
        }

        b.TrackMouseTime += dt;

        var toCursor = cursorPos - b.Position;

        // Once passed once (dot <= 0), lock tracking permanently and continue straight.
        var passedAlongHeading = Vector2.Dot(toCursor, b.Direction) <= 0f;
        if ((b.TrackMouseTime >= minTrackingTimeSec && passedAlongHeading) ||
            b.TrackMouseTime >= maxTrackingDurationSec)
        {
            b.TrackMouseLocked = true;
            b.Velocity = b.Direction * b.BaseSpeed;
            b.Position += b.Velocity * dt;
            return b.Position;
        }

        var desired = SafeNormalize(toCursor);
        if (desired.LengthSquared() < 0.0001f)
        {
            desired = b.Direction;
        }

        // Gradual steering toward current mouse position.
        var target = SafeNormalize(Vector2.Lerp(b.Direction, desired, 0.28f));
        var maxTurnRadians = Math.Max(0.04f, (baseTurnRateRadPerSec + b.Amp * 0.02f) * dt);
        var turned = RotateTowards(b.Direction, target, maxTurnRadians);

        if (turned.LengthSquared() < 0.0001f)
        {
            turned = b.Direction;
        }

        b.Direction = turned;
        b.Velocity = b.Direction * b.BaseSpeed;
        b.Position += b.Velocity * dt;
        return b.Position;
    }

    private static Vector2 ComputeMouseAimAfterExpandPosition(Bullet b, float dt)
    {
        // Keep ring spread first, then lock in a straight shot toward cursor.
        const float expandPhaseSec = 0.30f;

        if (!b.TrackMouseLocked && b.Age >= expandPhaseSec)
        {
            var aim = b.AimDirection.LengthSquared() > 0.0001f ? b.AimDirection : b.Direction;
            b.Direction = SafeNormalize(aim);
            b.TrackMouseLocked = true;
        }

        b.Velocity = b.Direction * b.BaseSpeed;
        b.Position += b.Velocity * dt;
        return b.Position;
    }

    private static Vector2 RotateTowards(Vector2 current, Vector2 target, float maxRadians)
    {
        if (current.LengthSquared() < 0.0001f) return target;
        if (target.LengthSquared() < 0.0001f) return current;

        current.Normalize();
        target.Normalize();

        var currentAngle = MathF.Atan2(current.Y, current.X);
        var targetAngle = MathF.Atan2(target.Y, target.X);
        var delta = MathHelper.WrapAngle(targetAngle - currentAngle);
        var clamped = Math.Clamp(delta, -maxRadians, maxRadians);
        var next = currentAngle + clamped;
        return new Vector2(MathF.Cos(next), MathF.Sin(next));
    }

    private static string NormalizeShape(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "orb";
        var t = raw.Trim().ToLowerInvariant();
        return t switch
        {
            "circle" or "orb" or "rice" or "kunai" or "star" or "butterfly" or "droplet" or "diamond" or "petal" or "crescent" => t,
            "arrow" => "arrowhead",
            "crystal_shard" => "crystal",
            _ => t
        };
    }

    private static float GetDirection(string pattern, Vector2 origin, Vector2 cursorPos, float? overrideDeg)
    {
        if (overrideDeg.HasValue) return MathHelper.ToRadians(overrideDeg.Value);
        if (pattern.EndsWith("_left")) return MathHelper.Pi;
        if (pattern.EndsWith("_right")) return 0f;
        if (pattern.StartsWith("aimed"))
        {
            var to = cursorPos - origin;
            if (to.LengthSquared() > 0.001f) return MathF.Atan2(to.Y, to.X);
        }
        return MathHelper.PiOver2;
    }

    private static Vector2 ToVirtualPosition(BulletEvent evt) => new((evt.X ?? 0.5f) * 1280f, (evt.Y ?? 0.5f) * 720f);

    private int ScaleRingCount(int baseCount)
    {
        var scale = Math.Clamp(RingDensityScale, 0.25f, 3f);
        return Math.Max(1, (int)MathF.Round(baseCount * scale));
    }

    private bool ShouldWarn(Vector2 origin)
    {
        var center = new Vector2(640f, 360f);
        if (Vector2.DistanceSquared(origin, center) < 0.0001f) return true;
        return Vector2.Distance(origin, center) <= CenterWarningRadius;
    }

    private static Color? ParseColor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith("#")) s = s[1..];
        try
        {
            if (s.Length == 6) return new Color(Convert.ToByte(s[0..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16));
            if (s.Length == 8) return new Color(Convert.ToByte(s[0..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16), Convert.ToByte(s[6..8], 16));
        }
        catch { }
        return null;
    }

    private void RecycleAt(int i)
    {
        var b = _active[i];
        _active[i] = _active[^1];
        _active.RemoveAt(_active.Count - 1);
        _pool.Push(b);
    }

    private sealed class Bullet
    {
        public Vector2 Spawn;
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Accel;
        public Vector2 Direction;
        public Vector2 OutwardDirection;
        public Vector2 AimDirection;
        public Vector2 HomeDirection;
        public float BaseSpeed;
        public Vector2 OrbitCenter;
        public Vector2 FromCenter;
        public float SectorSign;
        public float LocalNoise;
        public float Radius;
        public Color Fill;
        public Color Outline;
        public Color Glow;
        public float GlowIntensity;
        public float OutlineThickness;
        public string? Shape;
        public MotionKind Motion;
        public float RingExpandDistance;
        public float Amp;
        public float Freq;
        public float Phase;
        public float Age;
        public float Life;
        public bool TrackMouseLocked;
        public float TrackMouseTime;
        public float Rotation;
        public float RotationSpeed;
        public float Scale = 1f;
    }

    private struct LaserBeam
    {
        public Vector2 BaseOrigin;
        public Vector2 CurrentOrigin;
        public Vector2 BaseDirection;
        public Vector2 CurrentDirection;
        public Vector2 Start;
        public Vector2 End;
        public float Length;
        public float Width;
        public float TelegraphMs;
        public float ActiveMs;
        public float AgeMs;
        public bool IsActivePhase;
        public Color Fill;
        public Color Outline;
        public MotionKind Motion;
        public float Amp;
        public float Freq;
        public float Phase;
    }

    private struct Style
    {
        public float Radius;
        public Color Fill;
        public Color Outline;
        public Color Glow;
        public float GlowIntensity;
        public float OutlineThickness;
        public string? Shape;
    }

    private struct SpawnWarning
    {
        public Vector2 Position;
        public int StartTimeMs;
        public int SpawnTimeMs;
    }

    private struct SpawnCountdown
    {
        public Vector2 Position;
        public int SpawnTimeMs;
    }

    private enum MotionKind
    {
        None,
        UniformOutwardDrift,
        DelayedAccelerationRamp,
        RotationalFieldSpin,
        CounterRotationShear,
        SinusoidalPathDeviation,
        ExpandingSpiralConversion,
        InwardGravityPull,
        ElasticRepulsionField,
        AngularPhaseOffsetDrift,
        VerticalCompressionField,
        LateralSweepTranslation,
        LateralSweepLeftTranslation,
        OrbitAnchorConversion,
        PulsedVelocityModulation,
        SectorBasedSpeedVariation,
        RandomizedMicroDrift,
        ZigzagVectorFlip,
        DecelerationFreeze,
        RadialExplosionBurst,
        ExpandingRadiusLock,
        RotationalAccelerationRamp,
        MirrorAxisReflection,
        ExpandingOrbitalRinging,
        DelayedHomingAdjustment,
        StaggeredTimeOffsetRelease,
        EllipticalOrbitDrift,
        FountainArc,
        MouseTrack,
        MouseAimDirection,
        Sine,
        Arc,
        Rotate,
        Spiral,
        Accel,
        StaticRotate,
        StaticPulse,
        StaticExpand
    }

    private readonly record struct MotionProfile(MotionKind Kind, float Amp, float Freq);

}

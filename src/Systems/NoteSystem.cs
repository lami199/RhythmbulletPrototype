using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Rendering;

namespace RhythmbulletPrototype.Systems;

public sealed class NoteSystem
{
    private const int TimingHistogramBucketCount = 9;
    private readonly List<RuntimeTapNote> _tapNotes = new();
    private readonly List<RuntimeDragNote> _dragNotes = new();
    private readonly Queue<AutoMissFeedback> _autoMisses = new();
    private readonly Queue<NoteJudgmentEvent> _judgments = new();
    private readonly List<HitCircleVfx> _hitCircleVfx = new();
    private readonly int[] _timingHistogram = new int[TimingHistogramBucketCount];
    private HitWindows _hitWindows = HitWindows.Default;

    public float CircleRadius { get; private set; } = 42f;
    public int ApproachMs { get; private set; } = 900;
    public int HitWindowMs => _hitWindows.MissMs;
    public HitWindows CurrentHitWindows => _hitWindows;
    public int NumberCycle { get; private set; } = 4;
    public bool ShowNumbers { get; private set; } = true;

    public int Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int PerfectCount { get; private set; }
    public int GoodCount { get; private set; }
    public int OkCount { get; private set; }
    public int MissCount { get; private set; }
    public float LastHitDeltaMs { get; private set; }
    public int EarlyHitCount { get; private set; }
    public int LateHitCount { get; private set; }
    public int OnTimeHitCount { get; private set; }
    public IReadOnlyList<int> TimingHistogram => _timingHistogram;

    public int NextObjectIndex
    {
        get
        {
            var nextTap = _tapNotes.Where(n => !n.Resolved).Select(n => n.TimeMs).DefaultIfEmpty(int.MaxValue).Min();
            var nextDrag = _dragNotes.Where(n => !n.Resolved).Select(n => n.TimeMs).DefaultIfEmpty(int.MaxValue).Min();
            var target = Math.Min(nextTap, nextDrag);
            if (target == int.MaxValue)
            {
                return _tapNotes.Count + _dragNotes.Count;
            }

            return _tapNotes.Count(n => n.TimeMs < target) + _dragNotes.Count(n => n.TimeMs < target);
        }
    }

    public float Accuracy
    {
        get
        {
            var judged = PerfectCount + GoodCount + OkCount + MissCount;
            if (judged <= 0)
            {
                return 100f;
            }

            var total = PerfectCount * 300 + GoodCount * 100 + OkCount * 50;
            return (total / (judged * 300f)) * 100f;
        }
    }

    public void Reset(Beatmap beatmap)
    {
        _tapNotes.Clear();
        _dragNotes.Clear();
        _autoMisses.Clear();
        _judgments.Clear();
        _hitCircleVfx.Clear();

        CircleRadius = beatmap.CircleRadius;
        ApproachMs = beatmap.ApproachMs;
        NumberCycle = Math.Max(1, beatmap.NumberCycle);
        ShowNumbers = beatmap.ShowNumbers;
        _hitWindows = HitWindows.Normalize(beatmap.HitWindows);

        foreach (var n in beatmap.Notes)
        {
            _tapNotes.Add(new RuntimeTapNote
            {
                TimeMs = n.TimeMs,
                Position = new Vector2(n.X * 1280f, n.Y * 720f),
                FillColor = ParseColor(n.FillColor, new Color(70, 160, 255, 200)),
                OutlineColor = ParseColor(n.OutlineColor, Color.White)
            });
        }

        // Drag/roller notes are temporarily disabled.
        _dragNotes.Clear();

        AssignDisplayNumbers();

        Score = 0;
        Combo = 0;
        MaxCombo = 0;
        PerfectCount = 0;
        GoodCount = 0;
        OkCount = 0;
        MissCount = 0;
        LastHitDeltaMs = 0f;
        EarlyHitCount = 0;
        LateHitCount = 0;
        OnTimeHitCount = 0;
        Array.Clear(_timingHistogram, 0, _timingHistogram.Length);
    }

    public void Update(float dt, int songTimeMs, Vector2 cursorPos, bool leftDown, bool leftReleased)
    {
        for (var i = 0; i < _tapNotes.Count; i++)
        {
            if (_tapNotes[i].Resolved)
            {
                continue;
            }

            if (songTimeMs > _tapNotes[i].TimeMs + HitWindowMs)
            {
                var note = _tapNotes[i];
                note.Resolved = true;
                _tapNotes[i] = note;
                RegisterJudgment(Judgment.Miss, 0, songTimeMs);
                _autoMisses.Enqueue(new AutoMissFeedback(note.Position));
            }
        }

        for (var i = 0; i < _dragNotes.Count; i++)
        {
            var drag = _dragNotes[i];
            if (drag.Resolved)
            {
                continue;
            }

            if (songTimeMs > drag.EndMs + HitWindowMs)
            {
                RegisterJudgment(Judgment.Miss, 0, songTimeMs);
                _autoMisses.Enqueue(new AutoMissFeedback(drag.Points[^1]));
                drag.Resolved = true;
                drag.Dragging = false;
                _dragNotes[i] = drag;
                continue;
            }

            if (!drag.Dragging)
            {
                continue;
            }

            if (!leftDown)
            {
                if (leftReleased && drag.Progress >= 0.98f)
                {
                    var deltaMs = songTimeMs - drag.EndMs;
                    var judgment = ResolveJudgmentFromDelta(Math.Abs(deltaMs));
                    if (judgment == Judgment.Miss)
                    {
                        judgment = Judgment.Ok;
                    }

                    RegisterJudgment(judgment, deltaMs, songTimeMs);
                    QueueHitVfx(drag.Points[^1], drag.PipeRadius * 0.7f);
                }
                else
                {
                    RegisterJudgment(Judgment.Miss, 0, songTimeMs);
                    _autoMisses.Enqueue(new AutoMissFeedback(drag.Points[^1]));
                }

                drag.Resolved = true;
                drag.Dragging = false;
                _dragNotes[i] = drag;
                continue;
            }

            var nearest = ProjectOnPolyline(drag.Points, cursorPos);
            if (nearest.Distance <= drag.PipeRadius * 1.9f)
            {
                drag.Progress = Math.Max(drag.Progress, nearest.T);
                drag.LastValidDragMs = songTimeMs;

                var expectedT = Math.Clamp((songTimeMs - drag.TimeMs) / (float)Math.Max(1, drag.EndMs - drag.TimeMs), 0f, 1f);
                var expectedPos = PointOnPolyline(drag.Points, expectedT);
                var inRollingBall = Vector2.DistanceSquared(cursorPos, expectedPos) <= ((drag.PipeRadius * 1.55f) * (drag.PipeRadius * 1.55f));
                _ = inRollingBall;
            }
            else if (songTimeMs - drag.LastValidDragMs > 420)
            {
                RegisterJudgment(Judgment.Miss, 0, songTimeMs);
                _autoMisses.Enqueue(new AutoMissFeedback(drag.Points[^1]));
                drag.Resolved = true;
                drag.Dragging = false;
            }

            _dragNotes[i] = drag;
        }

        if (leftReleased)
        {
            for (var i = 0; i < _hitCircleVfx.Count; i++)
            {
                var fx = _hitCircleVfx[i];
                if (fx.WaitingRelease)
                {
                    fx.WaitingRelease = false;
                    fx.Age = 0f;
                    _hitCircleVfx[i] = fx;
                }
            }
        }

        for (var i = _hitCircleVfx.Count - 1; i >= 0; i--)
        {
            var fx = _hitCircleVfx[i];
            fx.Age += dt;
            if (fx.WaitingRelease && fx.Age > 0.25f)
            {
                fx.WaitingRelease = false;
                fx.Age = 0f;
            }

            if (!fx.WaitingRelease && fx.Age >= fx.Life)
            {
                _hitCircleVfx.RemoveAt(i);
            }
            else
            {
                _hitCircleVfx[i] = fx;
            }
        }
    }

    public bool TryHit(int songTimeMs, Vector2 cursorPos, out HitAttemptResult result)
    {
        RuntimeTapNote? candidate = null;
        var candidateIndex = -1;
        var candidateAbsDt = int.MaxValue;

        for (var i = 0; i < _tapNotes.Count; i++)
        {
            var note = _tapNotes[i];
            if (note.Resolved)
            {
                continue;
            }

            var dt = songTimeMs - note.TimeMs;
            var absDt = Math.Abs(dt);
            if (absDt > HitWindowMs)
            {
                continue;
            }

            if (Vector2.DistanceSquared(note.Position, cursorPos) > CircleRadius * CircleRadius)
            {
                continue;
            }

            if (absDt < candidateAbsDt)
            {
                candidate = note;
                candidateIndex = i;
                candidateAbsDt = absDt;
            }
        }

        if (candidate is not null)
        {
            var chosen = candidate.Value;
            var deltaMs = songTimeMs - chosen.TimeMs;
            var judgment = ResolveJudgmentFromDelta(Math.Abs(deltaMs));

            chosen.Resolved = true;
            _tapNotes[candidateIndex] = chosen;

            RegisterJudgment(judgment, deltaMs, songTimeMs);
            QueueHitVfx(chosen.Position, CircleRadius);
            result = new HitAttemptResult(judgment, deltaMs, GetJudgmentWindowMs(judgment), chosen.Position);
            return true;
        }

        for (var i = 0; i < _dragNotes.Count; i++)
        {
            var drag = _dragNotes[i];
            if (drag.Resolved || drag.Dragging)
            {
                continue;
            }

            var dt = songTimeMs - drag.TimeMs;
            if (Math.Abs(dt) > HitWindowMs)
            {
                continue;
            }

            var start = drag.Points[0];
            if (Vector2.DistanceSquared(start, cursorPos) > (drag.PipeRadius * 1.5f) * (drag.PipeRadius * 1.5f))
            {
                continue;
            }

            drag.Dragging = true;
            drag.Progress = 0f;
            drag.LastValidDragMs = songTimeMs;
            _dragNotes[i] = drag;
            result = HitAttemptResult.None;
            return true;
        }

        result = HitAttemptResult.None;
        return false;
    }

    public bool TryDequeueAutoMiss(out AutoMissFeedback miss)
    {
        return _autoMisses.TryDequeue(out miss);
    }

    public bool TryDequeueJudgment(out NoteJudgmentEvent judgmentEvent)
    {
        return _judgments.TryDequeue(out judgmentEvent);
    }

    public void Draw(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, int songTimeMs, bool showHitboxes)
    {
        DrawTapNotes(spriteBatch, render, text, songTimeMs, showHitboxes);
        DrawDragNotes(spriteBatch, render, text, songTimeMs, showHitboxes);

        for (var i = 0; i < _hitCircleVfx.Count; i++)
        {
            var fx = _hitCircleVfx[i];
            if (fx.WaitingRelease)
            {
                render.DrawCircleFilled(spriteBatch, fx.Position, fx.BaseRadius, new Color(140, 220, 255, 80));
                render.DrawCircleOutline(spriteBatch, fx.Position, fx.BaseRadius, 2f, Color.White * 0.5f);
                continue;
            }

            var t = fx.Age / fx.Life;
            var scale = MathHelper.Lerp(1f, 1.3f, t);
            var alpha = 1f - t;
            var radius = fx.BaseRadius * scale;
            render.DrawCircleFilled(spriteBatch, fx.Position, radius, new Color(130, 215, 255) * (0.45f * alpha));
            render.DrawCircleOutline(spriteBatch, fx.Position, radius, 2.5f, Color.White * alpha);
        }
    }

    private void DrawTapNotes(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, int songTimeMs, bool showHitboxes)
    {
        for (var i = 0; i < _tapNotes.Count; i++)
        {
            var note = _tapNotes[i];
            if (note.Resolved)
            {
                continue;
            }

            var appearMs = note.TimeMs - ApproachMs;
            var disappearMs = note.TimeMs + HitWindowMs;
            if (songTimeMs < appearMs || songTimeMs > disappearMs)
            {
                continue;
            }

            var t = Math.Clamp((songTimeMs - appearMs) / (float)ApproachMs, 0f, 1f);
            var approachRadius = MathHelper.Lerp(CircleRadius * 1.8f, CircleRadius, t);

            render.DrawCircleOutline(spriteBatch, note.Position, approachRadius, 2f, new Color(110, 200, 255, 180));
            render.DrawCircleFilled(spriteBatch, note.Position, CircleRadius, note.FillColor);
            render.DrawCircleOutline(spriteBatch, note.Position, CircleRadius, 3f, note.OutlineColor);

            if (ShowNumbers)
            {
                DrawCenteredNumber(spriteBatch, text, note.DisplayNumber, note.Position, CircleRadius);
            }

            if (showHitboxes)
            {
                render.DrawCircleOutline(spriteBatch, note.Position, CircleRadius, 1f, Color.Yellow);
            }
        }
    }

    private void DrawDragNotes(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, int songTimeMs, bool showHitboxes)
    {
        for (var i = 0; i < _dragNotes.Count; i++)
        {
            var drag = _dragNotes[i];
            if (drag.Resolved)
            {
                continue;
            }

            var appearMs = drag.TimeMs - ApproachMs;
            var disappearMs = drag.EndMs + HitWindowMs;
            if (songTimeMs < appearMs || songTimeMs > disappearMs)
            {
                continue;
            }

            var alpha = drag.Dragging ? 0.95f : 0.75f;
            var pipeColor = drag.FillColor * alpha;
            var edgeColor = drag.OutlineColor * alpha;

            for (var p = 0; p < drag.Points.Count - 1; p++)
            {
                var a = drag.Points[p];
                var b = drag.Points[p + 1];
                render.DrawLine(spriteBatch, a, b, drag.PipeRadius * 2f, pipeColor * 0.6f);
                render.DrawLine(spriteBatch, a, b, 2.2f, edgeColor);
                render.DrawCircleFilled(spriteBatch, a, drag.PipeRadius, pipeColor * 0.85f);
                render.DrawCircleOutline(spriteBatch, a, drag.PipeRadius, 2f, edgeColor);
            }

            var end = drag.Points[^1];
            render.DrawCircleFilled(spriteBatch, end, drag.PipeRadius, pipeColor * 0.85f);
            render.DrawCircleOutline(spriteBatch, end, drag.PipeRadius, 2f, edgeColor);

            var start = drag.Points[0];
            if (ShowNumbers)
            {
                DrawCenteredNumber(spriteBatch, text, drag.DisplayNumber, start, drag.PipeRadius * 0.9f);
            }

            var expectedT = Math.Clamp((songTimeMs - drag.TimeMs) / (float)Math.Max(1, drag.EndMs - drag.TimeMs), 0f, 1f);
            var rollingPos = PointOnPolyline(drag.Points, expectedT);
            render.DrawCircleFilled(spriteBatch, rollingPos, drag.PipeRadius * 0.82f, new Color(255, 245, 160));
            render.DrawCircleOutline(spriteBatch, rollingPos, drag.PipeRadius * 0.82f, 2.5f, Color.Black * 0.85f);

            if (showHitboxes)
            {
                for (var p = 0; p < drag.Points.Count; p++)
                {
                    render.DrawCircleOutline(spriteBatch, drag.Points[p], drag.PipeRadius, 1f, Color.Yellow);
                }
            }
        }
    }

    private void AssignDisplayNumbers()
    {
        var items = new List<(int TimeMs, bool IsDrag, int Index)>();
        for (var i = 0; i < _tapNotes.Count; i++) items.Add((_tapNotes[i].TimeMs, false, i));
        for (var i = 0; i < _dragNotes.Count; i++) items.Add((_dragNotes[i].TimeMs, true, i));
        items.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

        for (var i = 0; i < items.Count; i++)
        {
            var number = (i % NumberCycle) + 1;
            var item = items[i];
            if (item.IsDrag)
            {
                var drag = _dragNotes[item.Index];
                drag.DisplayNumber = number;
                _dragNotes[item.Index] = drag;
            }
            else
            {
                var tap = _tapNotes[item.Index];
                tap.DisplayNumber = number;
                _tapNotes[item.Index] = tap;
            }
        }
    }

    private void QueueHitVfx(Vector2 position, float radius)
    {
        _hitCircleVfx.Add(new HitCircleVfx
        {
            Position = position,
            BaseRadius = radius,
            Life = 0.4f,
            Age = 0f,
            WaitingRelease = true
        });
    }

    private void RegisterJudgment(Judgment judgment, int deltaMs, int songTimeMs)
    {
        LastHitDeltaMs = deltaMs;
        if (judgment is Judgment.Perfect or Judgment.Good or Judgment.Ok)
        {
            RecordTimingTelemetry(deltaMs);
        }

        switch (judgment)
        {
            case Judgment.Perfect:
                PerfectCount++;
                Combo++;
                break;
            case Judgment.Good:
                GoodCount++;
                Combo++;
                break;
            case Judgment.Ok:
                OkCount++;
                Combo++;
                break;
            case Judgment.Miss:
                MissCount++;
                Combo = 0;
                break;
        }

        MaxCombo = Math.Max(MaxCombo, Combo);

        var baseScore = JudgmentValues.ToBaseScore(judgment);
        var multiplier = Math.Clamp(1f + Combo / 25f, 1f, 4f);
        Score += (int)MathF.Round(baseScore * multiplier);
        _judgments.Enqueue(new NoteJudgmentEvent(
            judgment,
            deltaMs,
            GetJudgmentWindowMs(judgment),
            songTimeMs));
    }

    private Judgment ResolveJudgmentFromDelta(int absDtMs)
    {
        return Models.HitWindows.ResolveJudgment(absDtMs, _hitWindows);
    }

    private int GetJudgmentWindowMs(Judgment judgment)
    {
        return judgment switch
        {
            Judgment.Perfect => _hitWindows.PerfectMs,
            Judgment.Good => _hitWindows.GoodMs,
            Judgment.Ok => _hitWindows.OkMs,
            _ => _hitWindows.MissMs
        };
    }

    private void RecordTimingTelemetry(int deltaMs)
    {
        if (deltaMs < -12) EarlyHitCount++;
        else if (deltaMs > 12) LateHitCount++;
        else OnTimeHitCount++;

        var clamped = Math.Clamp(deltaMs, -HitWindowMs, HitWindowMs);
        var t = (clamped + HitWindowMs) / (float)Math.Max(1, HitWindowMs * 2);
        var idx = Math.Clamp((int)MathF.Floor(t * TimingHistogramBucketCount), 0, TimingHistogramBucketCount - 1);
        _timingHistogram[idx]++;
    }

    private static void DrawCenteredNumber(SpriteBatch spriteBatch, BitmapTextRenderer text, int number, Vector2 center, float noteRadius)
    {
        var str = number.ToString();
        var scale = Math.Clamp(noteRadius / 12f, 1.8f, 5f);
        var size = text.MeasureString(str, scale);
        var pos = center - (size * 0.5f);

        text.DrawString(spriteBatch, str, pos + new Vector2(1f, 1f), Color.Black * 0.8f, scale);
        text.DrawString(spriteBatch, str, pos, Color.White, scale);
    }

    private static Color ParseColor(string? raw, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var s = raw.Trim();
        if (s.StartsWith("#"))
        {
            s = s[1..];
        }

        try
        {
            if (s.Length == 6)
            {
                return new Color(
                    Convert.ToByte(s[0..2], 16),
                    Convert.ToByte(s[2..4], 16),
                    Convert.ToByte(s[4..6], 16));
            }

            if (s.Length == 8)
            {
                return new Color(
                    Convert.ToByte(s[0..2], 16),
                    Convert.ToByte(s[2..4], 16),
                    Convert.ToByte(s[4..6], 16),
                    Convert.ToByte(s[6..8], 16));
            }
        }
        catch
        {
            return fallback;
        }

        return fallback;
    }

    private static (float T, float Distance) ProjectOnPolyline(List<Vector2> points, Vector2 p)
    {
        var totalLen = 0f;
        var lengths = new float[points.Count - 1];
        for (var i = 0; i < points.Count - 1; i++)
        {
            lengths[i] = Vector2.Distance(points[i], points[i + 1]);
            totalLen += lengths[i];
        }

        if (totalLen <= 0.001f)
        {
            return (0f, Vector2.Distance(points[0], p));
        }

        var bestT = 0f;
        var bestDist = float.MaxValue;
        var traversed = 0f;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var ab = b - a;
            var denom = ab.LengthSquared();
            var localT = denom > 0.0001f ? Math.Clamp(Vector2.Dot(p - a, ab) / denom, 0f, 1f) : 0f;
            var proj = a + ab * localT;
            var dist = Vector2.Distance(proj, p);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestT = (traversed + lengths[i] * localT) / totalLen;
            }

            traversed += lengths[i];
        }

        return (bestT, bestDist);
    }

    private static Vector2 PointOnPolyline(List<Vector2> points, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var totalLen = 0f;
        var lengths = new float[points.Count - 1];
        for (var i = 0; i < points.Count - 1; i++)
        {
            lengths[i] = Vector2.Distance(points[i], points[i + 1]);
            totalLen += lengths[i];
        }

        if (totalLen <= 0.001f)
        {
            return points[0];
        }

        var target = t * totalLen;
        var traversed = 0f;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var segLen = lengths[i];
            if (traversed + segLen >= target)
            {
                var local = (target - traversed) / Math.Max(segLen, 0.0001f);
                return Vector2.Lerp(points[i], points[i + 1], local);
            }

            traversed += segLen;
        }

        return points[^1];
    }

    private struct RuntimeTapNote
    {
        public int TimeMs;
        public Vector2 Position;
        public bool Resolved;
        public int DisplayNumber;
        public Color FillColor;
        public Color OutlineColor;
    }

    private struct RuntimeDragNote
    {
        public int TimeMs;
        public int EndMs;
        public float PipeRadius;
        public List<Vector2> Points;
        public bool Resolved;
        public bool Dragging;
        public float Progress;
        public int LastValidDragMs;
        public int DisplayNumber;
        public Color FillColor;
        public Color OutlineColor;
    }

    private struct HitCircleVfx
    {
        public Vector2 Position;
        public float BaseRadius;
        public float Age;
        public float Life;
        public bool WaitingRelease;
    }
}

public readonly record struct HitAttemptResult(Judgment Judgment, int DeltaMs, int WindowMs, Vector2 Position)
{
    public static readonly HitAttemptResult None = new(Judgment.None, 0, 0, Vector2.Zero);
}

public readonly record struct NoteJudgmentEvent(
    Judgment Judgment,
    int DeltaMs,
    int WindowMs,
    int SongTimeMs);

public readonly record struct AutoMissFeedback(Vector2 Position);

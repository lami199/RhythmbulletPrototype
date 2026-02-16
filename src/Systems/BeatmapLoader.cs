using System.Text.Json;
using RhythmbulletPrototype.Models;

namespace RhythmbulletPrototype.Systems;

public sealed class BeatmapLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public Beatmap LoadFromPath(string path)
    {
        if (!File.Exists(path))
        {
            throw new BeatmapLoadException($"Beatmap file not found: {path}");
        }

        Beatmap? beatmap;
        try
        {
            var json = File.ReadAllText(path);
            beatmap = JsonSerializer.Deserialize<Beatmap>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BeatmapLoadException($"Invalid JSON in beatmap: {ex.Message}");
        }

        if (beatmap is null)
        {
            throw new BeatmapLoadException("Beatmap JSON produced null data.");
        }

        Validate(beatmap);
        beatmap.Notes = beatmap.Notes.OrderBy(n => n.TimeMs).ToList();
        beatmap.DragNotes = beatmap.DragNotes.OrderBy(n => n.TimeMs).ToList();
        beatmap.Bullets = beatmap.Bullets.OrderBy(b => b.TimeMs).ToList();
        return beatmap;
    }

    private static void Validate(Beatmap beatmap)
    {
        beatmap.ApproachMs = Math.Clamp(beatmap.ApproachMs, 250, 2500);
        beatmap.CircleRadius = Math.Clamp(beatmap.CircleRadius, 12f, 96f);
        beatmap.TargetFps = Math.Clamp(beatmap.TargetFps, 30, 500);
        beatmap.KeyboardMoveSpeed = Math.Clamp(beatmap.KeyboardMoveSpeed, 60f, 900f);
        beatmap.CursorHitboxRadius = Math.Clamp(beatmap.CursorHitboxRadius, 2f, 12f);
        beatmap.NumberCycle = Math.Clamp(beatmap.NumberCycle, 1, 99);
        beatmap.BulletOutlineThickness = Math.Clamp(beatmap.BulletOutlineThickness, 0.5f, 6f);
        beatmap.CenterWarningRadius = Math.Clamp(beatmap.CenterWarningRadius, 0f, 500f);
        beatmap.CenterWarningLeadMs = Math.Clamp(beatmap.CenterWarningLeadMs, 0, 2000);
        beatmap.CenterWarningAlpha = Math.Clamp(beatmap.CenterWarningAlpha, 0.05f, 1f);
        beatmap.WaveSafetyMargin = Math.Clamp(beatmap.WaveSafetyMargin, 0f, 40f);
        beatmap.MaxLives = Math.Clamp(beatmap.MaxLives, 1f, 999f);
        beatmap.BulletHitDamage = Math.Clamp(beatmap.BulletHitDamage, 1f, 200f);
        beatmap.LifeGainStepScore = Math.Clamp(beatmap.LifeGainStepScore, 1, 1000000);
        beatmap.LifeGainAmount = Math.Clamp(beatmap.LifeGainAmount, 0f, 200f);
        beatmap.BackgroundGridAlpha = Math.Clamp(beatmap.BackgroundGridAlpha, 0f, 1f);
        beatmap.BackgroundImageAlpha = Math.Clamp(beatmap.BackgroundImageAlpha, 0f, 1f);
        beatmap.BackgroundOverlayAlpha = Math.Clamp(beatmap.BackgroundOverlayAlpha, 0f, 1f);

        if (beatmap.Notes is null)
        {
            throw new BeatmapLoadException("Beatmap is missing required field: notes");
        }

        for (var i = 0; i < beatmap.Notes.Count; i++)
        {
            var note = beatmap.Notes[i];
            if (note.TimeMs < 0)
            {
                throw new BeatmapLoadException($"notes[{i}].timeMs must be >= 0");
            }

            note.X = Math.Clamp(note.X, 0f, 1f);
            note.Y = Math.Clamp(note.Y, 0f, 1f);
        }

        if (beatmap.Bullets is null)
        {
            beatmap.Bullets = new List<BulletEvent>();
        }

        if (beatmap.DragNotes is null)
        {
            beatmap.DragNotes = new List<DragNoteEvent>();
        }

        for (var i = 0; i < beatmap.DragNotes.Count; i++)
        {
            var drag = beatmap.DragNotes[i];
            if (drag.TimeMs < 0)
            {
                throw new BeatmapLoadException($"dragNotes[{i}].timeMs must be >= 0");
            }

            drag.DurationMs = Math.Clamp(drag.DurationMs, 200, 10000);
            drag.PipeRadius = Math.Clamp(drag.PipeRadius, 10f, 80f);

            if (drag.Path is null || drag.Path.Count < 2)
            {
                throw new BeatmapLoadException($"dragNotes[{i}] requires at least 2 path points");
            }

            for (var p = 0; p < drag.Path.Count; p++)
            {
                drag.Path[p].X = Math.Clamp(drag.Path[p].X, 0f, 1f);
                drag.Path[p].Y = Math.Clamp(drag.Path[p].Y, 0f, 1f);
            }
        }

        for (var i = 0; i < beatmap.Bullets.Count; i++)
        {
            var bullet = beatmap.Bullets[i];
            if (bullet.TimeMs < 0)
            {
                throw new BeatmapLoadException($"bullets[{i}].timeMs must be >= 0");
            }

            if (bullet.Count <= 0)
            {
                bullet.Count = 1;
            }

            if (bullet.Speed < 0.1f)
            {
                bullet.Speed = 0.1f;
            }

            bullet.IntervalMs = Math.Clamp(bullet.IntervalMs, 10, 1000);
            bullet.BulletSize = bullet.BulletSize.HasValue ? Math.Clamp(bullet.BulletSize.Value, 2f, 64f) : null;
            bullet.Radius = bullet.Radius.HasValue ? Math.Clamp(bullet.Radius.Value, 2f, 40f) : null;
            bullet.OutlineThickness = bullet.OutlineThickness.HasValue ? Math.Clamp(bullet.OutlineThickness.Value, 0.5f, 8f) : null;
            bullet.SpreadDeg = bullet.SpreadDeg.HasValue ? Math.Clamp(bullet.SpreadDeg.Value, 1f, 360f) : null;
            bullet.AngleStepDeg = bullet.AngleStepDeg.HasValue ? Math.Clamp(bullet.AngleStepDeg.Value, -180f, 180f) : null;
            bullet.GlowIntensity = bullet.GlowIntensity.HasValue ? Math.Clamp(bullet.GlowIntensity.Value, 0f, 2f) : null;
            bullet.DirectionDeg = bullet.DirectionDeg.HasValue ? Math.Clamp(bullet.DirectionDeg.Value, -720f, 720f) : null;
            bullet.MovementIntensity = bullet.MovementIntensity.HasValue ? Math.Clamp(bullet.MovementIntensity.Value, 0f, 3f) : null;
            bullet.TelegraphMs = bullet.TelegraphMs.HasValue ? Math.Clamp(bullet.TelegraphMs.Value, 50, 5000) : null;
            bullet.LaserDurationMs = bullet.LaserDurationMs.HasValue ? Math.Clamp(bullet.LaserDurationMs.Value, 50, 4000) : null;
            bullet.LaserWidth = bullet.LaserWidth.HasValue ? Math.Clamp(bullet.LaserWidth.Value, 4f, 220f) : null;
            bullet.LaserLength = bullet.LaserLength.HasValue ? Math.Clamp(bullet.LaserLength.Value, 100f, 2600f) : null;
            bullet.X = bullet.X.HasValue ? Math.Clamp(bullet.X.Value, 0f, 1f) : null;
            bullet.Y = bullet.Y.HasValue ? Math.Clamp(bullet.Y.Value, 0f, 1f) : null;

            if (string.IsNullOrWhiteSpace(bullet.Pattern))
            {
                bullet.Pattern = "radial";
            }

            bullet.Pattern = bullet.Pattern.Trim().ToLowerInvariant();
        }
    }
}

public sealed class BeatmapLoadException : Exception
{
    public BeatmapLoadException(string message) : base(message)
    {
    }
}

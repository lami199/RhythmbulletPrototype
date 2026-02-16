using System.Text.Json;

namespace RhythmbulletPrototype.Editor;

public sealed class LevelSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public LevelDocument LoadFromPath(string path)
    {
        if (!File.Exists(path))
        {
            throw new LevelSerializationException($"Level file not found: {path}");
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<LevelDocument>(json, JsonOptions);
            if (doc is null)
            {
                throw new LevelSerializationException("Level JSON produced null data.");
            }

            Normalize(doc);
            return doc;
        }
        catch (JsonException ex)
        {
            throw new LevelSerializationException($"Invalid level JSON: {ex.Message}");
        }
    }

    public void SaveToPath(LevelDocument doc, string path)
    {
        Normalize(doc);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(doc, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void Normalize(LevelDocument doc)
    {
        if (doc.SchemaVersion <= 0)
        {
            doc.SchemaVersion = LevelEditorConstants.SchemaVersion;
        }

        if (doc.SchemaVersion > LevelEditorConstants.SchemaVersion)
        {
            throw new LevelSerializationException($"Unsupported schemaVersion {doc.SchemaVersion}. Current is {LevelEditorConstants.SchemaVersion}.");
        }

        if (doc.Bpm.HasValue && doc.Bpm.Value <= 0)
        {
            doc.Bpm = null;
        }

        doc.LevelLengthMs = Math.Max(0, doc.LevelLengthMs);

        if (doc.Notes is null)
        {
            doc.Notes = new List<LevelNoteEvent>();
        }

        if (doc.Bullets is null)
        {
            doc.Bullets = new List<LevelBulletEvent>();
        }

        var nextId = Math.Max(1L, doc.NextEventId);

        foreach (var note in doc.Notes)
        {
            note.TimeMs = Math.Max(0, note.TimeMs);
            note.Lane = Math.Max(0, note.Lane);
            note.NoteType = note.NoteType.Equals(LevelEditorConstants.NoteTypeHold, StringComparison.OrdinalIgnoreCase)
                ? LevelEditorConstants.NoteTypeHold
                : LevelEditorConstants.NoteTypeTap;

            if (note.NoteType == LevelEditorConstants.NoteTypeTap)
            {
                note.DurationMs = 0;
            }
            else
            {
                note.DurationMs = Math.Max(1, note.DurationMs);
            }

            note.X = note.X.HasValue ? Math.Clamp(note.X.Value, 0f, 1f) : null;
            note.Y = note.Y.HasValue ? Math.Clamp(note.Y.Value, 0f, 1f) : null;

            if (note.EventId <= 0)
            {
                note.EventId = nextId++;
            }
            else
            {
                nextId = Math.Max(nextId, note.EventId + 1);
            }
        }

        foreach (var bullet in doc.Bullets)
        {
            bullet.TimeMs = Math.Max(0, bullet.TimeMs);
            bullet.PatternId = string.IsNullOrWhiteSpace(bullet.PatternId) ? "radial" : bullet.PatternId.Trim();
            bullet.X = bullet.X.HasValue ? Math.Clamp(bullet.X.Value, 0f, 1f) : null;
            bullet.Y = bullet.Y.HasValue ? Math.Clamp(bullet.Y.Value, 0f, 1f) : null;
            bullet.Parameters ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (bullet.EventId <= 0)
            {
                bullet.EventId = nextId++;
            }
            else
            {
                nextId = Math.Max(nextId, bullet.EventId + 1);
            }
        }

        doc.Notes = doc.Notes
            .OrderBy(n => n.TimeMs)
            .ThenBy(n => n.EventId)
            .ToList();

        doc.Bullets = doc.Bullets
            .OrderBy(b => b.TimeMs)
            .ThenBy(b => b.EventId)
            .ToList();

        doc.NextEventId = nextId;
    }
}

public sealed class LevelSerializationException : Exception
{
    public LevelSerializationException(string message) : base(message)
    {
    }
}

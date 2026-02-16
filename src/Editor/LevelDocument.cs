using System.Text.Json;
using System.Text.Json.Serialization;

namespace RhythmbulletPrototype.Editor;

public sealed class LevelDocument
{
    public int SchemaVersion { get; set; } = LevelEditorConstants.SchemaVersion;
    public string? AudioPath { get; set; }
    public double? Bpm { get; set; }
    public int LevelLengthMs { get; set; } = 30000;
    public long NextEventId { get; set; } = 1;
    public List<LevelNoteEvent> Notes { get; set; } = new();
    public List<LevelBulletEvent> Bullets { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LevelNoteEvent
{
    public long EventId { get; set; }
    public int TimeMs { get; set; }
    public int Lane { get; set; }
    public string NoteType { get; set; } = LevelEditorConstants.NoteTypeTap;
    public int DurationMs { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LevelBulletEvent
{
    public long EventId { get; set; }
    public int TimeMs { get; set; }

    [JsonPropertyName("pattern")]
    public string PatternId { get; set; } = "radial";

    public string? Anchor { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public readonly record struct EditorSelection(string Kind, int Index)
{
    public static readonly EditorSelection None = new(string.Empty, -1);

    public bool IsNone => string.IsNullOrEmpty(Kind) || Index < 0;
}

public sealed class TimelineRow
{
    public required string Kind { get; init; }
    public required int Index { get; init; }
    public required long EventId { get; init; }
    public required int TimeMs { get; init; }
    public required string Label { get; init; }
}

public sealed class EditorViewModel
{
    public required string ActivePath { get; init; }
    public required bool IsPlaying { get; init; }
    public required int CurrentTimeMs { get; init; }
    public required int SongDurationMs { get; init; }
    public required int TimelineEndMs { get; init; }
    public required int LastEntityEndMs { get; init; }
    public required bool SnapEnabled { get; init; }
    public required int SnapMs { get; init; }
    public required int CurrentLane { get; init; }
    public required string CurrentPattern { get; init; }
    public required string CurrentAudioPath { get; init; }
    public required IReadOnlyList<string> SongOptions { get; init; }
    public required EditorSelection Selection { get; init; }
    public required IReadOnlyList<TimelineRow> TimelineRows { get; init; }
    public required IReadOnlyList<PreviewMark> PreviewMarks { get; init; }
    public required TimingAnalysisSnapshot TimingAnalysis { get; init; }
    public required string StatusMessage { get; init; }
}

public sealed class PreviewMark
{
    public required long EventId { get; init; }
    public required string Kind { get; init; }
    public required int StartMs { get; init; }
    public required int EndMs { get; init; }
    public required float X { get; init; }
    public required float Y { get; init; }
    public required string Label { get; init; }
}

public sealed class TimingAnalysisSnapshot
{
    public static readonly TimingAnalysisSnapshot Empty = new();

    public string Source { get; init; } = "OnsetProxy";
    public int SectionLengthMs { get; init; } = 4000;
    public double TempoInstabilityPercent { get; init; }
    public double EstimatedBpm { get; init; }
    public IReadOnlyList<TimingSectionDrift> SectionDrift { get; init; } = Array.Empty<TimingSectionDrift>();
    public IReadOnlyList<RedlineSuggestion> RedlineSuggestions { get; init; } = Array.Empty<RedlineSuggestion>();
    public IReadOnlyList<HumanizationHeatBin> HumanizationHeatmap { get; init; } = Array.Empty<HumanizationHeatBin>();
}

public readonly record struct TimingSectionDrift(int StartMs, int EndMs, double MeanOffsetMs, double SpreadMs, int Samples);

public readonly record struct RedlineSuggestion(int TimeMs, double SuggestedBpm, string Reason);

public readonly record struct HumanizationHeatBin(int StartMs, int EndMs, int EarlyCount, int LateCount, double MeanOffsetMs);

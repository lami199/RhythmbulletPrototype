namespace RhythmbulletPrototype.Editor;

public enum EditorCommandType
{
    TogglePlayPause,
    Stop,
    SeekToMs,
    SeekDeltaMs,
    ToggleSnap,
    SetSnapMs,
    SetCurrentLane,
    SetCurrentPattern,
    AddTapNoteAtCurrent,
    AddHoldNoteAtCurrent,
    AddBulletAtCurrent,
    SelectNext,
    SelectPrevious,
    SelectByKey,
    DeleteByEventId,
    DeleteSelected,
    NudgeSelectedMs,
    SetAudioPath,
    SetPlaybackRate,
    Save,
    Load,
    PublishJson
}

public sealed class EditorCommand
{
    public required EditorCommandType Type { get; init; }
    public int IntValue { get; init; }
    public float FloatValue { get; init; }
    public string? StringValue { get; init; }
    public float? X { get; init; }
    public float? Y { get; init; }
    public int DurationMs { get; init; }
    public IReadOnlyDictionary<string, double>? Parameters { get; init; }

    public static EditorCommand TogglePlayPause() => new() { Type = EditorCommandType.TogglePlayPause };
    public static EditorCommand Stop() => new() { Type = EditorCommandType.Stop };
    public static EditorCommand SeekDelta(int deltaMs) => new() { Type = EditorCommandType.SeekDeltaMs, IntValue = deltaMs };
    public static EditorCommand SeekTo(int timeMs) => new() { Type = EditorCommandType.SeekToMs, IntValue = timeMs };
    public static EditorCommand ToggleSnap() => new() { Type = EditorCommandType.ToggleSnap };
    public static EditorCommand SetSnap(int snapMs) => new() { Type = EditorCommandType.SetSnapMs, IntValue = snapMs };
    public static EditorCommand SetLane(int lane) => new() { Type = EditorCommandType.SetCurrentLane, IntValue = lane };
    public static EditorCommand SetPattern(string patternId) => new() { Type = EditorCommandType.SetCurrentPattern, StringValue = patternId };
    public static EditorCommand AddTap(float? x = null, float? y = null) => new() { Type = EditorCommandType.AddTapNoteAtCurrent, X = x, Y = y };
    public static EditorCommand AddHold(int durationMs, float? x = null, float? y = null) => new() { Type = EditorCommandType.AddHoldNoteAtCurrent, DurationMs = durationMs, X = x, Y = y };
    public static EditorCommand AddBullet(float? x = null, float? y = null, IReadOnlyDictionary<string, double>? parameters = null) => new() { Type = EditorCommandType.AddBulletAtCurrent, X = x, Y = y, Parameters = parameters };
    public static EditorCommand SelectNext() => new() { Type = EditorCommandType.SelectNext };
    public static EditorCommand SelectPrevious() => new() { Type = EditorCommandType.SelectPrevious };
    public static EditorCommand SelectByKey(string key) => new() { Type = EditorCommandType.SelectByKey, StringValue = key };
    public static EditorCommand DeleteByEventId(string kind, int eventId) => new() { Type = EditorCommandType.DeleteByEventId, StringValue = kind, IntValue = eventId };
    public static EditorCommand DeleteSelected() => new() { Type = EditorCommandType.DeleteSelected };
    public static EditorCommand NudgeSelected(int deltaMs) => new() { Type = EditorCommandType.NudgeSelectedMs, IntValue = deltaMs };
    public static EditorCommand SetAudioPath(string audioPath) => new() { Type = EditorCommandType.SetAudioPath, StringValue = audioPath };
    public static EditorCommand SetPlaybackRate(float rate) => new() { Type = EditorCommandType.SetPlaybackRate, FloatValue = rate };
    public static EditorCommand Save() => new() { Type = EditorCommandType.Save };
    public static EditorCommand Load() => new() { Type = EditorCommandType.Load };
    public static EditorCommand PublishJson() => new() { Type = EditorCommandType.PublishJson };
}

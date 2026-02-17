using System.Text.Json.Serialization;

namespace RhythmbulletPrototype.Models;

public sealed class Beatmap
{
    public string? AudioPath { get; set; }
    public int ApproachMs { get; set; } = 900;
    public float CircleRadius { get; set; } = 42f;
    public int GlobalOffsetMs { get; set; } = 0;
    public int TargetFps { get; set; } = 120;
    public float KeyboardMoveSpeed { get; set; } = 260f;
    public float CursorHitboxRadius { get; set; } = 4f;
    public int NumberCycle { get; set; } = 4;
    public bool ShowNumbers { get; set; } = true;
    public HitWindows? HitWindows { get; set; }
    public DifficultyProfile? DifficultyProfile { get; set; }
    public float BulletOutlineThickness { get; set; } = 2f;
    public float CenterWarningRadius { get; set; } = 120f;
    public int CenterWarningLeadMs { get; set; } = 350;
    public float CenterWarningAlpha { get; set; } = 0.35f;
    public bool AutoBalanceWaves { get; set; } = true;
    public float WaveSafetyMargin { get; set; } = 8f;
    public float MaxLives { get; set; } = 100f;
    public float BulletHitDamage { get; set; } = 22f;
    public int LifeGainStepScore { get; set; } = 350;
    public float LifeGainAmount { get; set; } = 4f;
    public string BackgroundTopColor { get; set; } = "#121A28";
    public string BackgroundBottomColor { get; set; } = "#0A0F18";
    public string BackgroundAccentColor { get; set; } = "#1E324F";
    public float BackgroundGridAlpha { get; set; } = 0.18f;
    public string? BackgroundImagePath { get; set; }
    public float BackgroundImageAlpha { get; set; } = 1f;
    public string BackgroundImageMode { get; set; } = "cover";
    public string? BackgroundOverlayPath { get; set; }
    public float BackgroundOverlayAlpha { get; set; } = 0f;
    public List<NoteEvent> Notes { get; set; } = new();
    public List<DragNoteEvent> DragNotes { get; set; } = new();
    public List<BulletEvent> Bullets { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }
}

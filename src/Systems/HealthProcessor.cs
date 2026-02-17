using RhythmbulletPrototype.Models;

namespace RhythmbulletPrototype.Systems;

public sealed class HealthProcessor
{
    private float _maxHealth = 100f;
    private float _bulletDamage = 22f;
    private float _missDamage = 4f;
    private float _drainPerSecond;
    private float _recoveryPerPerfect = 4f;
    private bool _noFail;

    public float CurrentHealth { get; private set; } = 100f;
    public float MaxHealth => _maxHealth;
    public float RecoveryPerPerfect => _recoveryPerPerfect;
    public bool IsFailed => !_noFail && CurrentHealth <= 0f;

    public void Configure(Beatmap beatmap, DifficultyProfile? difficultyProfile, IReadOnlyCollection<GameMod> mods)
    {
        var profile = ModRules.ResolveDifficultyProfile(difficultyProfile, mods);
        var objectCount = Math.Max(0, (beatmap.Notes?.Count ?? 0) + (beatmap.DragNotes?.Count ?? 0));
        var mapLengthMs = EstimateMapLengthMs(beatmap);
        var minutes = Math.Max(0.5f, mapLengthMs / 60000f);
        var objectsPerMinute = objectCount / minutes;

        _maxHealth = Math.Clamp(beatmap.MaxLives, 1f, 999f);
        _bulletDamage = Math.Clamp(beatmap.BulletHitDamage, 1f, 200f);
        _missDamage = Math.Clamp(beatmap.LifeGainAmount * 0.9f, 0.5f, 64f);
        _drainPerSecond = Math.Clamp(profile.HealthDrain * 0.22f, 0f, 4f);
        _recoveryPerPerfect = ResolveAdaptiveRecovery(beatmap, objectCount, mapLengthMs);
        _noFail = mods.Contains(GameMod.NoFail);

        var densityScale = Math.Clamp(objectsPerMinute / 90f, 0.35f, 1.35f);
        _drainPerSecond *= densityScale;
        _missDamage *= Math.Clamp(densityScale, 0.5f, 1.2f);

        if (mods.Contains(GameMod.Easy))
        {
            _bulletDamage *= 0.7f;
            _drainPerSecond *= 0.75f;
        }

        if (mods.Contains(GameMod.HardRock))
        {
            _bulletDamage *= 1.15f;
            _drainPerSecond *= 1.15f;
        }
    }

    public void Reset()
    {
        CurrentHealth = _maxHealth;
    }

    public void Update(float dtSeconds)
    {
        if (dtSeconds <= 0f)
        {
            return;
        }

        CurrentHealth = Math.Clamp(CurrentHealth - _drainPerSecond * dtSeconds, 0f, _maxHealth);
    }

    public void ApplyBulletHit()
    {
        CurrentHealth = Math.Clamp(CurrentHealth - _bulletDamage, 0f, _maxHealth);
    }

    public void ApplyJudgment(NoteJudgmentEvent judgmentEvent)
    {
        switch (judgmentEvent.Judgment)
        {
            case Judgment.Perfect:
                CurrentHealth = Math.Clamp(CurrentHealth + _recoveryPerPerfect, 0f, _maxHealth);
                break;
            case Judgment.Good:
                CurrentHealth = Math.Clamp(CurrentHealth + _recoveryPerPerfect * 0.65f, 0f, _maxHealth);
                break;
            case Judgment.Ok:
                CurrentHealth = Math.Clamp(CurrentHealth + _recoveryPerPerfect * 0.3f, 0f, _maxHealth);
                break;
            case Judgment.Miss:
                CurrentHealth = Math.Clamp(CurrentHealth - _missDamage, 0f, _maxHealth);
                break;
        }
    }

    private float ResolveAdaptiveRecovery(Beatmap beatmap, int objectCount, int mapLengthMs)
    {
        var baseRecovery = Math.Clamp(beatmap.LifeGainAmount, 0f, _maxHealth);
        if (objectCount <= 0)
        {
            return baseRecovery;
        }

        var mapSeconds = Math.Max(30f, mapLengthMs / 1000f);
        var passiveDrainBudget = _drainPerSecond * mapSeconds;
        var targetRecoveryBudget = (_maxHealth * 0.85f) + (passiveDrainBudget * 0.55f);
        var adaptiveRecovery = targetRecoveryBudget / objectCount;

        var minRecovery = Math.Max(baseRecovery, _maxHealth * 0.01f);
        var maxRecovery = Math.Max(minRecovery, _maxHealth * 0.35f);
        return Math.Clamp(adaptiveRecovery, minRecovery, maxRecovery);
    }

    private static int EstimateMapLengthMs(Beatmap beatmap)
    {
        var noteMs = beatmap.Notes.Count > 0 ? beatmap.Notes.Max(n => n.TimeMs) : 0;
        var dragMs = beatmap.DragNotes.Count > 0 ? beatmap.DragNotes.Max(n => n.TimeMs + n.DurationMs) : 0;
        var bulletMs = beatmap.Bullets.Count > 0 ? beatmap.Bullets.Max(b => b.TimeMs) : 0;
        return Math.Max(30000, Math.Max(noteMs, Math.Max(dragMs, bulletMs)));
    }
}

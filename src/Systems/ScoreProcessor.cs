using RhythmbulletPrototype.Models;

namespace RhythmbulletPrototype.Systems;

public sealed class ScoreProcessor
{
    private float _scoreMultiplier = 1f;

    public int TotalScore { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int PerfectCount { get; private set; }
    public int GoodCount { get; private set; }
    public int OkCount { get; private set; }
    public int MissCount { get; private set; }
    public float LastDeltaMs { get; private set; }

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

    public void Reset(IReadOnlyCollection<GameMod> mods)
    {
        _scoreMultiplier = ModRules.ResolveScoreMultiplier(mods);
        TotalScore = 0;
        Combo = 0;
        MaxCombo = 0;
        PerfectCount = 0;
        GoodCount = 0;
        OkCount = 0;
        MissCount = 0;
        LastDeltaMs = 0f;
    }

    public void Apply(NoteJudgmentEvent judgmentEvent)
    {
        var judgment = judgmentEvent.Judgment;
        LastDeltaMs = judgmentEvent.DeltaMs;

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
        var comboMultiplier = Math.Clamp(1f + Combo / 25f, 1f, 4f);
        var scored = JudgmentValues.ToBaseScore(judgment) * comboMultiplier * _scoreMultiplier;
        TotalScore += (int)MathF.Round(scored);
    }
}

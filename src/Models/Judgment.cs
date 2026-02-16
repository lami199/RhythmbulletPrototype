namespace RhythmbulletPrototype.Models;

public enum Judgment
{
    None,
    Perfect,
    Good,
    Ok,
    Miss
}

public static class JudgmentValues
{
    public const int PerfectMs = 40;
    public const int GoodMs = 90;
    public const int OkMs = 140;

    public static int ToBaseScore(Judgment j)
    {
        return j switch
        {
            Judgment.Perfect => 300,
            Judgment.Good => 100,
            Judgment.Ok => 50,
            _ => 0
        };
    }
}

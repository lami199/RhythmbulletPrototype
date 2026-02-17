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

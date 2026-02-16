namespace RhythmbulletPrototype.Editor;

public sealed class NoOpNoteReceiver : INoteReceiver
{
    public void OnNote(LevelNoteEvent note)
    {
    }
}

public sealed class NoOpBulletPatternSpawner : IBulletPatternSpawner
{
    public void SpawnPattern(LevelBulletEvent bulletEvent)
    {
    }
}


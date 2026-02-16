namespace RhythmbulletPrototype.Editor;

public interface IAudioTransport
{
    int CurrentTimeMs { get; }
    int SongDurationMs { get; }
    bool IsPlaying { get; }
    bool TryLoadAudio(string? audioPath, out string? error);
    void SetPlaybackRate(float rate);
    void Tick(float dtSeconds);
    void Play();
    void Pause();
    void Stop();
    void Seek(int timeMs);
}

public interface INoteReceiver
{
    void OnNote(LevelNoteEvent note);
}

public interface IBulletPatternSpawner
{
    void SpawnPattern(LevelBulletEvent bulletEvent);
}

public interface IEditorView
{
    void Render(EditorViewModel model);
    bool TryDequeueCommand(out EditorCommand command);
    void PushMessage(string message);
}

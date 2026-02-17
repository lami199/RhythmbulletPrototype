namespace RhythmbulletPrototype.Systems;

public interface IGameClock
{
    bool IsRunning { get; }
    int SongDurationMs { get; }
    int CurrentTimeMs { get; }
    float Rate { get; }
    void PlayFromStart();
    void Pause();
    void Resume();
    void Stop();
    void SetTimeMs(int timeMs);
    void SetPlaybackRate(float rate);
    void Tick(float dtSeconds);
}

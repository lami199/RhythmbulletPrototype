using RhythmbulletPrototype.Systems;

namespace RhythmbulletPrototype.Editor;

public sealed class SongClockAudioTransport : IAudioTransport
{
    private readonly SongClock _songClock;
    private readonly string _baseDirectory;

    public SongClockAudioTransport(SongClock songClock, string baseDirectory)
    {
        _songClock = songClock;
        _baseDirectory = baseDirectory;
    }

    public int CurrentTimeMs => _songClock.CurrentTimeMs;
    public int SongDurationMs => _songClock.SongDurationMs;
    public bool IsPlaying => _songClock.IsRunning;

    public bool TryLoadAudio(string? audioPath, out string? error)
    {
        var ok = _songClock.TryLoadSong(audioPath, _baseDirectory, out var loadedError);
        error = string.IsNullOrWhiteSpace(loadedError) ? null : loadedError;
        return ok;
    }

    public void SetPlaybackRate(float rate)
    {
        _songClock.SetPlaybackRate(rate);
    }

    public void Tick(float dtSeconds)
    {
        _songClock.Tick(dtSeconds);
    }

    public void Play()
    {
        if (_songClock.CurrentTimeMs <= 0)
        {
            _songClock.PlayFromStart();
            return;
        }

        _songClock.Resume();
    }

    public void Pause()
    {
        _songClock.Pause();
    }

    public void Stop()
    {
        _songClock.Stop();
    }

    public void Seek(int timeMs)
    {
        _songClock.SetTimeMs(timeMs);
    }
}

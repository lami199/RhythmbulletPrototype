using System.Diagnostics;
using Microsoft.Xna.Framework.Media;

namespace RhythmbulletPrototype.Systems;

public sealed class SongClock : IDisposable
{
    private readonly Stopwatch _wallClock = Stopwatch.StartNew();
    private long _anchorWallMs;
    private long _timeAtAnchorMs;
    private float _timeScale = 1f;
    private bool _isRunning;
    private Song? _baseSong;
    private Song? _activeSong;
    private readonly Dictionary<float, Song> _slowSongs = new();
    private string? _loadedSongPath;

    public bool HasAudioSong => _activeSong is not null;
    public bool IsRunning => _isRunning;
    public int SongDurationMs => _activeSong is null ? 0 : (int)Math.Max(0, _activeSong.Duration.TotalMilliseconds);

    public int CurrentTimeMs
    {
        get
        {
            if (!_isRunning)
            {
                return (int)_timeAtAnchorMs;
            }

            var delta = _wallClock.ElapsedMilliseconds - _anchorWallMs;
            return (int)(_timeAtAnchorMs + delta * _timeScale);
        }
    }

    public bool TryLoadSong(string? audioPath, string baseDirectory, out string error)
    {
        error = string.Empty;
        UnloadSong();

        if (string.IsNullOrWhiteSpace(audioPath))
        {
            return false;
        }

        var fullPath = ResolveAudioPath(audioPath, baseDirectory);
        if (fullPath is null)
        {
            error = $"Audio missing: {audioPath}. Running with silent clock.";
            return false;
        }

        try
        {
            _loadedSongPath = fullPath;
            _baseSong = Song.FromUri(Path.GetFileNameWithoutExtension(fullPath), new Uri(fullPath));
            _activeSong = _baseSong;
            LoadSlowVariants(fullPath);
            MediaPlayer.IsRepeating = false;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load audio '{audioPath}': {ex.Message}. Running with silent clock.";
            _baseSong = null;
            _activeSong = null;
            return false;
        }
    }

    public void PlayFromStart()
    {
        _timeAtAnchorMs = 0;
        _anchorWallMs = _wallClock.ElapsedMilliseconds;
        _isRunning = true;

        if (_activeSong is not null)
        {
            try
            {
                MediaPlayer.Play(_activeSong, TimeSpan.Zero);
            }
            catch
            {
            }
        }
    }

    public void Pause()
    {
        if (!_isRunning)
        {
            return;
        }

        _timeAtAnchorMs = CurrentTimeMs;
        _isRunning = false;

        if (_activeSong is not null)
        {
            try
            {
                // Stop guarantees no background playback on backends where Pause can drift.
                MediaPlayer.Stop();
            }
            catch
            {
            }
        }
    }

    public void Resume()
    {
        if (_isRunning)
        {
            return;
        }

        _anchorWallMs = _wallClock.ElapsedMilliseconds;
        _isRunning = true;

        if (_activeSong is not null)
        {
            try
            {
                MediaPlayer.Play(_activeSong, TimeSpan.FromMilliseconds(_timeAtAnchorMs));
            }
            catch
            {
                try
                {
                    MediaPlayer.Resume();
                }
                catch
                {
                }
            }
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _timeAtAnchorMs = 0;
        _anchorWallMs = _wallClock.ElapsedMilliseconds;

        if (_activeSong is not null)
        {
            try
            {
                MediaPlayer.Stop();
            }
            catch
            {
            }
        }
    }

    public void SetTimeMs(int timeMs)
    {
        _timeAtAnchorMs = Math.Max(0, timeMs);
        _anchorWallMs = _wallClock.ElapsedMilliseconds;

        if (_activeSong is null)
        {
            return;
        }

        var wasRunning = _isRunning;
        try
        {
            if (wasRunning)
            {
                MediaPlayer.Play(_activeSong, TimeSpan.FromMilliseconds(_timeAtAnchorMs));
            }
        }
        catch
        {
            // Fallback to internal clock only when platform/media backend does not support seek.
        }
    }

    public void SetPlaybackRate(float rate)
    {
        rate = Math.Clamp(rate, 0.1f, 2f);
        if (Math.Abs(rate - _timeScale) < 0.0001f)
        {
            return;
        }

        _timeAtAnchorMs = CurrentTimeMs;
        _anchorWallMs = _wallClock.ElapsedMilliseconds;
        _timeScale = rate;

        var selected = SelectSongForRate(rate);
        if (!ReferenceEquals(selected, _activeSong))
        {
            _activeSong = selected;
            if (_isRunning && _activeSong is not null)
            {
                try
                {
                    MediaPlayer.Play(_activeSong, TimeSpan.FromMilliseconds(_timeAtAnchorMs));
                }
                catch
                {
                }
            }
        }
    }

    public void Tick(float dtSeconds)
    {
        if (!_isRunning || _activeSong is null)
        {
            return;
        }

        if (MediaPlayer.State != MediaState.Playing)
        {
            try
            {
                MediaPlayer.Play(_activeSong, TimeSpan.FromMilliseconds(CurrentTimeMs));
            }
            catch
            {
            }
        }
    }

    private Song? SelectSongForRate(float rate)
    {
        if (_baseSong is null)
        {
            return null;
        }

        // Prefer pre-generated slowed assets for smooth slow edit:
        // song_slow50.(ogg/wav/mp3), song_0p5.*
        if (rate <= 0.26f && _slowSongs.TryGetValue(0.25f, out var slow25))
        {
            return slow25;
        }

        if (rate <= 0.55f && _slowSongs.TryGetValue(0.5f, out var slow50))
        {
            return slow50;
        }

        return _baseSong;
    }

    private void LoadSlowVariants(string basePath)
    {
        _slowSongs.Clear();
        foreach (var rate in new[] { 0.5f, 0.25f })
        {
            var path = FindSlowVariant(basePath, rate);
            if (path is null)
            {
                continue;
            }

            try
            {
                var key = rate;
                var song = Song.FromUri(Path.GetFileNameWithoutExtension(path), new Uri(path));
                _slowSongs[key] = song;
            }
            catch
            {
            }
        }
    }

    private static string? FindSlowVariant(string basePath, float rate)
    {
        var dir = Path.GetDirectoryName(basePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        string[] suffixes = rate switch
        {
            0.5f => new[] { "_slow50", "_0p5", "_half" },
            0.25f => new[] { "_slow25", "_0p25", "_quarter" },
            _ => Array.Empty<string>()
        };

        foreach (var suffix in suffixes)
        {
            var candidate = Path.Combine(dir, $"{name}{suffix}{ext}");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveAudioPath(string audioPath, string baseDirectory)
    {
        if (Path.IsPathRooted(audioPath) && File.Exists(audioPath))
        {
            return audioPath;
        }

        var candidates = new[]
        {
            Path.Combine(baseDirectory, audioPath),
            Path.Combine(Directory.GetCurrentDirectory(), audioPath),
            Path.Combine(AppContext.BaseDirectory, audioPath)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void UnloadSong()
    {
        if (_baseSong is null && _slowSongs.Count == 0)
        {
            return;
        }

        try
        {
            MediaPlayer.Stop();
        }
        catch
        {
        }

        if (_baseSong is not null)
        {
            _baseSong.Dispose();
            _baseSong = null;
        }

        foreach (var pair in _slowSongs)
        {
            pair.Value.Dispose();
        }
        _slowSongs.Clear();
        _activeSong = null;
        _loadedSongPath = null;
    }

    public void Dispose()
    {
        UnloadSong();
    }
}

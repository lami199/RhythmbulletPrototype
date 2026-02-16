namespace RhythmbulletPrototype.Editor;

public sealed class LevelPlayer
{
    private readonly INoteReceiver _noteReceiver;
    private readonly IBulletPatternSpawner _bulletSpawner;

    private readonly List<LevelNoteEvent> _notes = new();
    private readonly List<LevelBulletEvent> _bullets = new();

    private int _nextNoteIndex;
    private int _nextBulletIndex;
    private int _lastTimeMs;

    public LevelPlayer(INoteReceiver noteReceiver, IBulletPatternSpawner bulletSpawner)
    {
        _noteReceiver = noteReceiver;
        _bulletSpawner = bulletSpawner;
    }

    public void Load(LevelDocument level)
    {
        _notes.Clear();
        _bullets.Clear();
        _notes.AddRange(level.Notes.OrderBy(n => n.TimeMs).ThenBy(n => n.EventId));
        _bullets.AddRange(level.Bullets.OrderBy(b => b.TimeMs).ThenBy(b => b.EventId));
        Reset();
    }

    public void Reset()
    {
        _nextNoteIndex = 0;
        _nextBulletIndex = 0;
        _lastTimeMs = 0;
    }

    public void Seek(int timeMs)
    {
        var clamped = Math.Max(0, timeMs);
        _nextNoteIndex = FindFirstAfter(_notes, clamped);
        _nextBulletIndex = FindFirstAfter(_bullets, clamped);
        _lastTimeMs = clamped;
    }

    public void Update(int songTimeMs)
    {
        var now = Math.Max(0, songTimeMs);
        if (now < _lastTimeMs)
        {
            Seek(now);
            return;
        }

        while (_nextNoteIndex < _notes.Count && _notes[_nextNoteIndex].TimeMs <= now)
        {
            _noteReceiver.OnNote(_notes[_nextNoteIndex]);
            _nextNoteIndex++;
        }

        while (_nextBulletIndex < _bullets.Count && _bullets[_nextBulletIndex].TimeMs <= now)
        {
            _bulletSpawner.SpawnPattern(_bullets[_nextBulletIndex]);
            _nextBulletIndex++;
        }

        _lastTimeMs = now;
    }

    private static int FindFirstAfter<TEvent>(IReadOnlyList<TEvent> events, int timeMs) where TEvent : class
    {
        var lo = 0;
        var hi = events.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            var eventTime = events[mid] switch
            {
                LevelNoteEvent note => note.TimeMs,
                LevelBulletEvent bullet => bullet.TimeMs,
                _ => 0
            };

            if (eventTime <= timeMs)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}


using Microsoft.Xna.Framework;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Systems;

namespace RhythmbulletPrototype.Editor;

public sealed class LevelEditorController
{
    private const int BulletDeleteWindowMs = 100;
    private const int SimulatedBulletMaxMs = 600000;
    private const int SimulatedBulletStepMs = 16;
    private const int MinTimelineMsWithSong = 30000;
    private const int MinTimelineMsNoSong = 480000;
    private readonly IAudioTransport _transport;
    private readonly IEditorView _view;
    private readonly LevelSerializer _serializer;
    private readonly string _contentAudioDirectory;

    private string _activePath = string.Empty;
    private LevelDocument _level = new();
    private EditorSelection _selection = EditorSelection.None;
    private bool _snapEnabled;
    private int _snapMs = 10;
    private int _currentLane = 0;
    private string _currentPattern = "radial";
    private string _statusMessage = "Editor ready";
    private int _revision;
    private int _cachedRevision = -1;
    private List<TimelineRow> _cachedRows = new();
    private List<PreviewMark> _cachedMarks = new();
    private readonly Dictionary<long, BulletDurationCacheEntry> _bulletDurationByEventId = new();
    private TimingAnalysisSnapshot _cachedTimingAnalysis = TimingAnalysisSnapshot.Empty;
    private int _cachedLastEntityEndMs;
    private int _cachedMaxEventTimeMs;
    private List<string> _cachedSongs = new();
    private long _nextSongRefreshTickMs;

    public LevelEditorController(IAudioTransport transport, IEditorView view, LevelSerializer serializer, string contentAudioDirectory)
    {
        _transport = transport;
        _view = view;
        _serializer = serializer;
        _contentAudioDirectory = contentAudioDirectory;
    }

    public LevelDocument CurrentLevel => _level;
    public string ActivePath => _activePath;
    public int Revision => _revision;

    public void LoadOrCreate(string path, string? defaultAudioPath = null)
    {
        _activePath = path;
        if (File.Exists(path))
        {
            _level = _serializer.LoadFromPath(path);
            _statusMessage = $"Loaded level: {Path.GetFileName(path)}";
        }
        else
        {
            _level = new LevelDocument { AudioPath = defaultAudioPath };
            _serializer.Normalize(_level);
            _statusMessage = $"Created new level: {Path.GetFileName(path)}";
        }

        _selection = EditorSelection.None;
        _revision++;
        _transport.SetPlaybackRate(1f);
        LoadAudioForCurrentLevel();
    }

    public void Save()
    {
        EnsureActivePath();
        _level.LevelLengthMs = ComputeLastEntityVisibleMs();
        _serializer.SaveToPath(_level, _activePath);
        _statusMessage = $"Saved level: {Path.GetFileName(_activePath)}";
    }

    public void Reload()
    {
        EnsureActivePath();
        _level = _serializer.LoadFromPath(_activePath);
        _selection = EditorSelection.None;
        _statusMessage = $"Reloaded level: {Path.GetFileName(_activePath)}";
        _revision++;
        LoadAudioForCurrentLevel();
    }

    public void Update(float dtSeconds = 1f / 60f)
    {
        _transport.Tick(dtSeconds);
        var model = BuildViewModel();
        _view.Render(model);

        while (_view.TryDequeueCommand(out var command))
        {
            Execute(command);
        }
    }

    private EditorViewModel BuildViewModel()
    {
        EnsureCachesUpToDate();
        var songDuration = Math.Max(0, _transport.SongDurationMs);
        var minTimelineMs = songDuration > 0 ? MinTimelineMsWithSong : MinTimelineMsNoSong;
        var timelineEnd = Math.Max(
            Math.Max(minTimelineMs, _level.LevelLengthMs),
            Math.Max(songDuration, Math.Max(_cachedMaxEventTimeMs + 2000, _transport.CurrentTimeMs + 2000)));
        var songs = GetSongOptionsCached();
        return new EditorViewModel
        {
            ActivePath = _activePath,
            IsPlaying = _transport.IsPlaying,
            CurrentTimeMs = _transport.CurrentTimeMs,
            SongDurationMs = songDuration,
            TimelineEndMs = timelineEnd,
            LastEntityEndMs = _cachedLastEntityEndMs,
            SnapEnabled = _snapEnabled,
            SnapMs = _snapMs,
            CurrentLane = _currentLane,
            CurrentPattern = _currentPattern,
            CurrentAudioPath = _level.AudioPath ?? string.Empty,
            SongOptions = songs,
            Selection = _selection,
            TimelineRows = _cachedRows,
            PreviewMarks = _cachedMarks,
            TimingAnalysis = _cachedTimingAnalysis,
            StatusMessage = _statusMessage
        };
    }

    private IReadOnlyList<TimelineRow> BuildTimelineRows()
    {
        var rows = new List<TimelineRow>(_level.Notes.Count + _level.Bullets.Count);
        for (var i = 0; i < _level.Notes.Count; i++)
        {
            var note = _level.Notes[i];
            var hold = note.NoteType == LevelEditorConstants.NoteTypeHold ? $" hold:{note.DurationMs}" : string.Empty;
            rows.Add(new TimelineRow
            {
                Kind = LevelEditorConstants.EventKindNote,
                Index = i,
                EventId = note.EventId,
                TimeMs = note.TimeMs,
                Label = $"NOTE lane:{note.Lane}{hold}"
            });
        }

        for (var i = 0; i < _level.Bullets.Count; i++)
        {
            var bullet = _level.Bullets[i];
            rows.Add(new TimelineRow
            {
                Kind = LevelEditorConstants.EventKindBullet,
                Index = i,
                EventId = bullet.EventId,
                TimeMs = bullet.TimeMs,
                Label = $"BULLET {GetBulletDisplayLabel(bullet)}"
            });
        }

        return rows
            .OrderBy(r => r.TimeMs)
            .ThenBy(r => r.EventId)
            .ToList();
    }

    private IReadOnlyList<PreviewMark> BuildPreviewMarks()
    {
        var marks = new List<PreviewMark>(_level.Notes.Count + _level.Bullets.Count);
        var seenBulletIds = new HashSet<long>();

        foreach (var note in _level.Notes)
        {
            var x = note.X ?? LaneToNormalizedX(note.Lane);
            var y = note.Y ?? 0.56f;
            // Allow pre-roll before 0ms so early notes keep full approach timing in editor preview.
            var start = note.TimeMs - 900;
            var end = note.NoteType == LevelEditorConstants.NoteTypeHold
                ? note.TimeMs + Math.Max(1, note.DurationMs) + 240
                : note.TimeMs + 180;

            marks.Add(new PreviewMark
            {
                EventId = note.EventId,
                Kind = LevelEditorConstants.EventKindNote,
                StartMs = start,
                EndMs = end,
                X = Math.Clamp(x, 0f, 1f),
                Y = Math.Clamp(y, 0f, 1f),
                Label = note.NoteType == LevelEditorConstants.NoteTypeHold ? "Hold" : "Tap"
            });
        }

        foreach (var bullet in _level.Bullets)
        {
            var x = bullet.X ?? 0.5f;
            var y = bullet.Y ?? 0.5f;
            var offscreenMs = EstimateBulletVisibleDurationMsCached(bullet, x, y);
            seenBulletIds.Add(bullet.EventId);

            marks.Add(new PreviewMark
            {
                EventId = bullet.EventId,
                Kind = LevelEditorConstants.EventKindBullet,
                StartMs = bullet.TimeMs,
                EndMs = bullet.TimeMs + offscreenMs,
                X = Math.Clamp((float)x, 0f, 1f),
                Y = Math.Clamp((float)y, 0f, 1f),
                Label = GetBulletDisplayLabel(bullet)
            });
        }

        if (_bulletDurationByEventId.Count > 0)
        {
            var staleIds = _bulletDurationByEventId.Keys.Where(id => !seenBulletIds.Contains(id)).ToArray();
            for (var i = 0; i < staleIds.Length; i++)
            {
                _bulletDurationByEventId.Remove(staleIds[i]);
            }
        }

        return marks;
    }

    private int EstimateBulletVisibleDurationMsCached(LevelBulletEvent bullet, double x, double y)
    {
        var signature = BuildBulletDurationSignature(bullet, x, y);
        if (_bulletDurationByEventId.TryGetValue(bullet.EventId, out var cached) &&
            string.Equals(cached.Signature, signature, StringComparison.Ordinal))
        {
            return cached.DurationMs;
        }

        var evt = BuildRuntimeBulletEventForEstimate(bullet, x, y);
        var durationMs = SimulateBulletOffscreenDurationMs(evt);
        _bulletDurationByEventId[bullet.EventId] = new BulletDurationCacheEntry(signature, durationMs);
        return durationMs;
    }

    private static string BuildBulletDurationSignature(LevelBulletEvent bullet, double x, double y)
    {
        var parts = new List<string>(24)
        {
            $"p:{bullet.PatternId}",
            $"x:{Math.Round(x, 4):0.0000}",
            $"y:{Math.Round(y, 4):0.0000}"
        };

        foreach (var pair in bullet.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            parts.Add($"{pair.Key}:{Math.Round(pair.Value, 4):0.0000}");
        }

        return string.Join('|', parts);
    }

    private void Execute(EditorCommand command)
    {
        switch (command.Type)
        {
            case EditorCommandType.TogglePlayPause:
                if (_transport.IsPlaying)
                {
                    _transport.Pause();
                }
                else
                {
                    _transport.Play();
                }

                break;

            case EditorCommandType.Stop:
                _transport.Stop();
                break;

            case EditorCommandType.SeekToMs:
                _transport.Seek(command.IntValue);
                break;

            case EditorCommandType.SeekDeltaMs:
                _transport.Seek(_transport.CurrentTimeMs + command.IntValue);
                break;

            case EditorCommandType.ToggleSnap:
                _snapEnabled = !_snapEnabled;
                _statusMessage = $"Snap {(_snapEnabled ? "ON" : "OFF")}";
                break;

            case EditorCommandType.SetSnapMs:
                _snapMs = Math.Clamp(command.IntValue, 1, 2000);
                _statusMessage = $"Snap {_snapMs}ms";
                break;

            case EditorCommandType.SetCurrentLane:
                _currentLane = Math.Max(0, command.IntValue);
                break;

            case EditorCommandType.SetCurrentPattern:
                if (!string.IsNullOrWhiteSpace(command.StringValue))
                {
                    _currentPattern = command.StringValue.Trim();
                }

                break;

            case EditorCommandType.AddTapNoteAtCurrent:
                AddNote(LevelEditorConstants.NoteTypeTap, 0, command.X, command.Y);
                break;

            case EditorCommandType.AddHoldNoteAtCurrent:
                AddNote(LevelEditorConstants.NoteTypeHold, Math.Max(1, command.DurationMs), command.X, command.Y);
                break;

            case EditorCommandType.AddBulletAtCurrent:
                AddBullet(command.X, command.Y, command.Parameters);
                break;

            case EditorCommandType.SelectNext:
                SelectAdjacent(+1);
                break;

            case EditorCommandType.SelectPrevious:
                SelectAdjacent(-1);
                break;

            case EditorCommandType.SelectByKey:
                SelectByKey(command.StringValue);
                break;

            case EditorCommandType.DeleteByEventId:
                DeleteByEventId(command.StringValue, command.IntValue);
                break;

            case EditorCommandType.DeleteSelected:
                DeleteSelection();
                break;

            case EditorCommandType.NudgeSelectedMs:
                NudgeSelection(command.IntValue);
                break;

            case EditorCommandType.SetAudioPath:
                SetAudioPath(command.StringValue);
                break;

            case EditorCommandType.SetPlaybackRate:
                _transport.SetPlaybackRate(Math.Clamp(command.FloatValue, 0.1f, 2f));
                break;

            case EditorCommandType.Save:
                Save();
                break;

            case EditorCommandType.Load:
                Reload();
                break;

            case EditorCommandType.PublishJson:
                PublishRuntimeMapJson();
                break;
        }
    }

    private void AddNote(string noteType, int durationMs, float? x, float? y)
    {
        var timeMs = GetPlacementTimeMs();
        var placedX = ClampNullable01(x) ?? LaneToNormalizedX(_currentLane);
        var placedY = ClampNullable01(y) ?? 0.56f;
        var note = new LevelNoteEvent
        {
            EventId = AllocateEventId(),
            TimeMs = timeMs,
            Lane = _currentLane,
            NoteType = noteType,
            DurationMs = durationMs,
            // Persist explicit coordinates so hit center matches authored placement exactly.
            X = placedX,
            Y = placedY
        };

        _level.Notes.Add(note);
        NormalizeAndKeepSelection(note.EventId, LevelEditorConstants.EventKindNote);
        _revision++;
        _statusMessage = $"Added {note.NoteType} note at {note.TimeMs}ms";
    }

    private void AddBullet(float? x, float? y, IReadOnlyDictionary<string, double>? parameters)
    {
        var timeMs = GetPlacementTimeMs();
        var resolvedParameters = parameters is null
            ? BuildDefaultBulletParameters(_currentPattern)
            : new Dictionary<string, double>(parameters, StringComparer.OrdinalIgnoreCase);

        if (resolvedParameters.Count == 0)
        {
            resolvedParameters = BuildDefaultBulletParameters(_currentPattern);
        }

        if (!resolvedParameters.ContainsKey("directionDeg"))
        {
            resolvedParameters["directionDeg"] = 90d;
        }

        var noMotion = resolvedParameters.TryGetValue("noMotion", out var noMotionRaw) && noMotionRaw >= 0.5d;
        if (noMotion)
        {
            resolvedParameters.Remove("motionPatternId");
        }
        else if (resolvedParameters.TryGetValue("motionPatternId", out var motionIdRaw))
        {
            var maxMotion = Math.Max(0, BulletSystem.MovingPatterns.Length - 1);
            resolvedParameters["motionPatternId"] = Math.Clamp(Math.Round(motionIdRaw), 0d, maxMotion);
        }
        else
        {
            resolvedParameters["noMotion"] = 1d;
            resolvedParameters.Remove("motionPatternId");
        }

        if (resolvedParameters.TryGetValue("bulletSize", out var bulletSize))
        {
            resolvedParameters["bulletSize"] = SnapBulletSize(bulletSize);
        }
        else if (resolvedParameters.TryGetValue("radius", out var radius))
        {
            resolvedParameters["bulletSize"] = SnapBulletSize(radius);
        }
        else
        {
            resolvedParameters["bulletSize"] = 8d;
        }

        var bullet = new LevelBulletEvent
        {
            EventId = AllocateEventId(),
            TimeMs = timeMs,
            PatternId = _currentPattern,
            X = ClampNullable01(x),
            Y = ClampNullable01(y),
            Parameters = resolvedParameters
        };

        _level.Bullets.Add(bullet);
        NormalizeAndKeepSelection(bullet.EventId, LevelEditorConstants.EventKindBullet);
        _revision++;
        _statusMessage = $"Added bullet '{bullet.PatternId}' at {bullet.TimeMs}ms";
    }

    private static Dictionary<string, double> BuildDefaultBulletParameters(string? patternId)
    {
        var pattern = string.IsNullOrWhiteSpace(patternId) ? "radial" : patternId.Trim().ToLowerInvariant();
        var p = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["speed"] = 240,
            ["count"] = 12,
            ["directionDeg"] = 90,
            ["noMotion"] = 1,
            ["bulletSize"] = 8,
            ["shapeId"] = 0,
            ["primaryR"] = 235,
            ["primaryG"] = 40,
            ["primaryB"] = 50,
            ["outlineR"] = 0,
            ["outlineG"] = 0,
            ["outlineB"] = 0,
            ["glowR"] = 255,
            ["glowG"] = 120,
            ["glowB"] = 120,
            ["glowIntensity"] = 0.12,
            ["movementIntensity"] = 1.0
        };

        switch (pattern)
        {
            case "static_single":
                p["count"] = 1;
                break;
            case "static_5":
                p["count"] = 5;
                break;
            case "static_10":
                p["count"] = 10;
                break;
            case "static_15":
                p["count"] = 15;
                break;
            case "static_20":
                p["count"] = 20;
                break;
            case "static_25":
                p["count"] = 25;
                break;
            case "static_50":
                p["count"] = 50;
                break;
            case "laser_static":
                p["count"] = 1;
                p["speed"] = 0;
                p["telegraphMs"] = 900;
                p["laserDurationMs"] = 550;
                p["laserWidth"] = 22;
                p["laserLength"] = 1700;
                break;
            case var _ when BulletSystem.MovingPatterns.Contains(pattern) || BulletSystem.StaticPatterns.Contains(pattern):
                p["count"] = BulletSystem.MovingPatterns.Contains(pattern) ? 18 : 16;
                break;
            case "aimed":
                p["count"] = 6;
                p["intervalMs"] = 80;
                break;
            case "spiral":
                p["count"] = 18;
                p["intervalMs"] = 60;
                p["angleStepDeg"] = 12;
                break;
            case "fan":
                p["count"] = 7;
                p["spreadDeg"] = 60;
                break;
            case "radial":
                p["count"] = 12;
                break;
            case "shotgun":
                p["count"] = 12;
                p["spreadDeg"] = 40;
                break;
            case "aimed_fan":
                p["count"] = 8;
                p["spreadDeg"] = 58;
                break;
            case "aimed_line":
                p["count"] = 6;
                p["intervalMs"] = 70;
                break;
            default:
                break;
        }

        return p;
    }

    private static double SnapBulletSize(double raw)
    {
        var clamped = Math.Clamp(raw, 2d, 64d);
        return Math.Round(clamped / 2d) * 2d;
    }

    private void SelectAdjacent(int direction)
    {
        var timeline = BuildTimelineRows();
        if (timeline.Count == 0)
        {
            _selection = EditorSelection.None;
            return;
        }

        var currentIdx = -1;
        if (!_selection.IsNone)
        {
            currentIdx = timeline
                .Select((row, idx) => (row, idx))
                .FirstOrDefault(pair => pair.row.Kind == _selection.Kind && pair.row.Index == _selection.Index)
                .idx;
        }

        if (currentIdx < 0)
        {
            currentIdx = direction >= 0 ? 0 : timeline.Count - 1;
        }
        else
        {
            currentIdx = Math.Clamp(currentIdx + direction, 0, timeline.Count - 1);
        }

        var row = timeline[currentIdx];
        _selection = new EditorSelection(row.Kind, row.Index);
    }

    private void SelectByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var tokens = key.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2)
        {
            return;
        }

        if (!int.TryParse(tokens[1], out var index))
        {
            return;
        }

        var kind = tokens[0].ToLowerInvariant();
        if (kind != LevelEditorConstants.EventKindNote && kind != LevelEditorConstants.EventKindBullet)
        {
            return;
        }

        _selection = new EditorSelection(kind, index);
    }

    private void DeleteSelection()
    {
        if (_selection.IsNone)
        {
            return;
        }

        var removed = false;
        if (_selection.Kind == LevelEditorConstants.EventKindNote)
        {
            if (_selection.Index >= 0 && _selection.Index < _level.Notes.Count)
            {
                _level.Notes.RemoveAt(_selection.Index);
                _statusMessage = "Deleted selected note";
                removed = true;
            }
        }
        else if (_selection.Kind == LevelEditorConstants.EventKindBullet)
        {
            if (_selection.Index >= 0 && _selection.Index < _level.Bullets.Count)
            {
                var bullet = _level.Bullets[_selection.Index];
                if (!CanDeleteBulletNow(bullet, out var reason))
                {
                    _statusMessage = reason;
                    return;
                }

                _level.Bullets.RemoveAt(_selection.Index);
                _statusMessage = "Deleted selected bullet event";
                removed = true;
            }
        }

        if (!removed)
        {
            return;
        }

        _selection = EditorSelection.None;
        _serializer.Normalize(_level);
        _revision++;
    }

    private void NudgeSelection(int deltaMs)
    {
        if (_selection.IsNone)
        {
            return;
        }

        if (_selection.Kind == LevelEditorConstants.EventKindNote &&
            _selection.Index >= 0 &&
            _selection.Index < _level.Notes.Count)
        {
            _level.Notes[_selection.Index].TimeMs = Math.Max(0, _level.Notes[_selection.Index].TimeMs + deltaMs);
            var eventId = _level.Notes[_selection.Index].EventId;
            NormalizeAndKeepSelection(eventId, LevelEditorConstants.EventKindNote);
            _revision++;
            return;
        }

        if (_selection.Kind == LevelEditorConstants.EventKindBullet &&
            _selection.Index >= 0 &&
            _selection.Index < _level.Bullets.Count)
        {
            _level.Bullets[_selection.Index].TimeMs = Math.Max(0, _level.Bullets[_selection.Index].TimeMs + deltaMs);
            var eventId = _level.Bullets[_selection.Index].EventId;
            NormalizeAndKeepSelection(eventId, LevelEditorConstants.EventKindBullet);
            _revision++;
        }
    }

    private void NormalizeAndKeepSelection(long eventId, string kind)
    {
        _serializer.Normalize(_level);
        if (kind == LevelEditorConstants.EventKindNote)
        {
            var idx = _level.Notes.FindIndex(n => n.EventId == eventId);
            _selection = idx >= 0 ? new EditorSelection(kind, idx) : EditorSelection.None;
            return;
        }

        var bulletIdx = _level.Bullets.FindIndex(b => b.EventId == eventId);
        _selection = bulletIdx >= 0 ? new EditorSelection(kind, bulletIdx) : EditorSelection.None;
    }

    private int GetPlacementTimeMs()
    {
        var t = Math.Max(0, _transport.CurrentTimeMs);
        if (_snapEnabled)
        {
            t = (int)(Math.Round(t / (double)_snapMs) * _snapMs);
        }

        return t;
    }

    private long AllocateEventId()
    {
        var id = Math.Max(1, _level.NextEventId);
        _level.NextEventId = id + 1;
        return id;
    }

    private static float? ClampNullable01(float? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return Math.Clamp(value.Value, 0f, 1f);
    }

    private static float LaneToNormalizedX(int lane)
    {
        var clampedLane = Math.Clamp(lane, 0, 7);
        return 0.1f + (clampedLane / 7f) * 0.8f;
    }

    private static double GetParameterOrDefault(LevelBulletEvent bullet, string key, double fallback)
    {
        if (bullet.Parameters.TryGetValue(key, out var value) && value > 0)
        {
            return value;
        }

        return fallback;
    }

    private static double FarthestCornerDistancePixels(double x01, double y01)
    {
        const double width = 1280d;
        const double height = 720d;
        var px = x01 * width;
        var py = y01 * height;
        var corners = new[]
        {
            (0d, 0d),
            (width, 0d),
            (0d, height),
            (width, height)
        };

        var maxDist = 0d;
        foreach (var (cx, cy) in corners)
        {
            var dx = px - cx;
            var dy = py - cy;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d > maxDist)
            {
                maxDist = d;
            }
        }

        return maxDist;
    }

    private static BulletEvent BuildRuntimeBulletEventForEstimate(LevelBulletEvent bullet, double x, double y)
    {
        var evt = new BulletEvent
        {
            TimeMs = 0,
            Pattern = string.IsNullOrWhiteSpace(bullet.PatternId) ? "radial" : bullet.PatternId,
            X = (float)Math.Clamp(x, 0d, 1d),
            Y = (float)Math.Clamp(y, 0d, 1d)
        };

        if (bullet.Parameters.TryGetValue("count", out var count)) evt.Count = Math.Max(1, (int)Math.Round(count));
        if (bullet.Parameters.TryGetValue("speed", out var speed)) evt.Speed = (float)Math.Max(0.1, speed);
        if (bullet.Parameters.TryGetValue("intervalMs", out var interval)) evt.IntervalMs = Math.Max(10, (int)Math.Round(interval));
        if (bullet.Parameters.TryGetValue("spreadDeg", out var spread)) evt.SpreadDeg = (float)spread;
        if (bullet.Parameters.TryGetValue("angleStepDeg", out var step)) evt.AngleStepDeg = (float)step;
        if (bullet.Parameters.TryGetValue("directionDeg", out var direction)) evt.DirectionDeg = (float)direction;
        if (bullet.Parameters.TryGetValue("movementIntensity", out var movement)) evt.MovementIntensity = (float)movement;
        if (bullet.Parameters.TryGetValue("ringExpandDistance", out var ringExpandDistance)) evt.RingExpandDistance = (float)Math.Max(0, ringExpandDistance);
        if (!evt.RingExpandDistance.HasValue && bullet.Parameters.TryGetValue("expansionDistance", out var expansionDistance)) evt.RingExpandDistance = (float)Math.Max(0, expansionDistance);
        if (bullet.Parameters.TryGetValue("radius", out var radius)) evt.Radius = (float)radius;
        if (bullet.Parameters.TryGetValue("bulletSize", out var bsize)) evt.BulletSize = (float)bsize;
        if (bullet.Parameters.TryGetValue("outlineThickness", out var outT)) evt.OutlineThickness = (float)outT;
        if (bullet.Parameters.TryGetValue("telegraphMs", out var telegraphMs)) evt.TelegraphMs = Math.Max(50, (int)Math.Round(telegraphMs));
        if (bullet.Parameters.TryGetValue("laserDurationMs", out var laserDurationMs)) evt.LaserDurationMs = Math.Max(50, (int)Math.Round(laserDurationMs));
        if (bullet.Parameters.TryGetValue("laserWidth", out var laserWidth)) evt.LaserWidth = (float)laserWidth;
        if (bullet.Parameters.TryGetValue("laserLength", out var laserLength)) evt.LaserLength = (float)laserLength;
        if (bullet.Parameters.TryGetValue("shapeId", out var shapeId)) evt.BulletType = ShapeIdToType((int)Math.Round(shapeId));
        if (bullet.Parameters.TryGetValue("motionPatternId", out var motionPatternId)) evt.MotionPattern = MotionPatternIdToName((int)Math.Round(motionPatternId));
        return evt;
    }

    private static int SimulateBulletOffscreenDurationMs(BulletEvent evt)
    {
        var beatmap = new Beatmap
        {
            Bullets = new List<BulletEvent> { evt },
            Notes = new List<NoteEvent>(),
            DragNotes = new List<DragNoteEvent>()
        };

        var system = new BulletSystem();
        system.Reset(beatmap);

        var cursor = new Vector2(640f, 360f);
        var sawAny = false;

        for (var t = 0; t <= SimulatedBulletMaxMs; t += SimulatedBulletStepMs)
        {
            system.Update(SimulatedBulletStepMs / 1000f, t, cursor);
            if (system.ActiveBulletCount > 0)
            {
                sawAny = true;
            }
            else if (sawAny)
            {
                return t;
            }
        }

        return SimulatedBulletMaxMs;
    }

    private readonly record struct BulletDurationCacheEntry(string Signature, int DurationMs);

    private static string ShapeIdToType(int shapeId)
    {
        string[] shapes =
        {
            "orb", "circle", "rice", "kunai", "butterfly", "star", "arrowhead",
            "droplet", "crystal", "diamond", "petal", "flame_shard", "cross_shard",
            "crescent", "heart_shard", "hex_shard"
        };
        return (shapeId >= 0 && shapeId < shapes.Length) ? shapes[shapeId] : "orb";
    }

    private static string MotionPatternIdToName(int motionPatternId)
    {
        if (motionPatternId < 0)
        {
            return string.Empty;
        }

        var patterns = BulletSystem.MovingPatterns;
        if (motionPatternId >= 0 && motionPatternId < patterns.Length)
        {
            return patterns[motionPatternId];
        }

        return patterns.Length > 0 ? patterns[0] : string.Empty;
    }

    private static bool TryColorFromParams(IReadOnlyDictionary<string, double> parameters, string prefix, out string hex)
    {
        hex = string.Empty;
        if (!parameters.TryGetValue(prefix + "R", out var rv) ||
            !parameters.TryGetValue(prefix + "G", out var gv) ||
            !parameters.TryGetValue(prefix + "B", out var bv))
        {
            return false;
        }

        var r = Math.Clamp((int)Math.Round(rv), 0, 255);
        var g = Math.Clamp((int)Math.Round(gv), 0, 255);
        var b = Math.Clamp((int)Math.Round(bv), 0, 255);
        hex = $"#{r:X2}{g:X2}{b:X2}";
        return true;
    }

    private void EnsureActivePath()
    {
        if (string.IsNullOrWhiteSpace(_activePath))
        {
            throw new InvalidOperationException("Editor has no active path. Call LoadOrCreate first.");
        }
    }

    private void LoadAudioForCurrentLevel()
    {
        if (_transport.TryLoadAudio(_level.AudioPath, out var error) && string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            _view.PushMessage(error);
            _statusMessage = error;
        }
    }

    private void SetAudioPath(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
        {
            return;
        }

        _level.AudioPath = audioPath.Trim();
        _revision++;
        _transport.Stop();
        _transport.TryLoadAudio(_level.AudioPath, out var error);
        _transport.Play();
        if (!string.IsNullOrWhiteSpace(error))
        {
            _statusMessage = error;
            _view.PushMessage(error);
        }
        else
        {
            _statusMessage = $"Audio: {_level.AudioPath}";
        }
    }

    private void PublishRuntimeMapJson()
    {
        EnsureActivePath();
        var cutoffMs = ComputeLastEntityVisibleMs();
        _level.LevelLengthMs = cutoffMs;
        var outPath = Path.Combine(
            Path.GetDirectoryName(_activePath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(_activePath) + ".published.map.json");

        var beatmap = BuildRuntimeBeatmap(cutoffMs);
        var json = System.Text.Json.JsonSerializer.Serialize(beatmap, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(outPath, json);
        _statusMessage = $"Published runtime map: {Path.GetFileName(outPath)}";
        _view.PushMessage(_statusMessage);
    }

    private RhythmbulletPrototype.Models.Beatmap BuildRuntimeBeatmap(int cutoffMs)
    {
        var beatmap = new RhythmbulletPrototype.Models.Beatmap
        {
            AudioPath = _level.AudioPath,
            Notes = new List<RhythmbulletPrototype.Models.NoteEvent>(),
            Bullets = new List<RhythmbulletPrototype.Models.BulletEvent>()
        };

        foreach (var n in _level.Notes.Where(n => n.TimeMs <= cutoffMs).OrderBy(n => n.TimeMs).ThenBy(n => n.EventId))
        {
            beatmap.Notes.Add(new RhythmbulletPrototype.Models.NoteEvent
            {
                TimeMs = Math.Max(0, n.TimeMs),
                X = n.X ?? LaneToNormalizedX(n.Lane),
                Y = n.Y ?? 0.56f,
                Lane = n.Lane
            });
        }

        foreach (var b in _level.Bullets.Where(b => b.TimeMs <= cutoffMs).OrderBy(b => b.TimeMs).ThenBy(b => b.EventId))
        {
            var evt = new RhythmbulletPrototype.Models.BulletEvent
            {
                TimeMs = Math.Max(0, b.TimeMs),
                Pattern = string.IsNullOrWhiteSpace(b.PatternId) ? "radial" : b.PatternId,
                X = b.X ?? 0.5f,
                Y = b.Y ?? 0.5f
            };

            if (b.Parameters.TryGetValue("count", out var count)) evt.Count = Math.Max(1, (int)Math.Round(count));
            if (b.Parameters.TryGetValue("speed", out var speed)) evt.Speed = (float)Math.Max(0.1, speed);
            if (b.Parameters.TryGetValue("intervalMs", out var interval)) evt.IntervalMs = Math.Max(10, (int)Math.Round(interval));
            if (b.Parameters.TryGetValue("spreadDeg", out var spread)) evt.SpreadDeg = (float)spread;
            if (b.Parameters.TryGetValue("angleStepDeg", out var step)) evt.AngleStepDeg = (float)step;
            if (b.Parameters.TryGetValue("directionDeg", out var direction)) evt.DirectionDeg = (float)direction;
            if (b.Parameters.TryGetValue("movementIntensity", out var movement)) evt.MovementIntensity = (float)movement;
            if (b.Parameters.TryGetValue("ringExpandDistance", out var ringExpandDistance)) evt.RingExpandDistance = (float)Math.Max(0, ringExpandDistance);
            if (!evt.RingExpandDistance.HasValue && b.Parameters.TryGetValue("expansionDistance", out var expansionDistance)) evt.RingExpandDistance = (float)Math.Max(0, expansionDistance);
            if (b.Parameters.TryGetValue("radius", out var radius)) evt.Radius = (float)radius;
            if (b.Parameters.TryGetValue("bulletSize", out var bsize)) evt.BulletSize = (float)bsize;
            if (b.Parameters.TryGetValue("outlineThickness", out var outT)) evt.OutlineThickness = (float)outT;
            if (b.Parameters.TryGetValue("glowIntensity", out var glowI)) evt.GlowIntensity = (float)glowI;
            if (b.Parameters.TryGetValue("telegraphMs", out var telegraphMs)) evt.TelegraphMs = Math.Max(50, (int)Math.Round(telegraphMs));
            if (b.Parameters.TryGetValue("laserDurationMs", out var laserDurationMs)) evt.LaserDurationMs = Math.Max(50, (int)Math.Round(laserDurationMs));
            if (b.Parameters.TryGetValue("laserWidth", out var laserWidth)) evt.LaserWidth = (float)laserWidth;
            if (b.Parameters.TryGetValue("laserLength", out var laserLength)) evt.LaserLength = (float)laserLength;
            if (b.Parameters.TryGetValue("shapeId", out var shapeId)) evt.BulletType = ShapeIdToType((int)Math.Round(shapeId));
            if (b.Parameters.TryGetValue("motionPatternId", out var motionPatternId)) evt.MotionPattern = MotionPatternIdToName((int)Math.Round(motionPatternId));
            if (TryColorFromParams(b.Parameters, "primary", out var primaryHex)) evt.Color = primaryHex;
            if (TryColorFromParams(b.Parameters, "outline", out var outlineHex)) evt.OutlineColor = outlineHex;
            if (TryColorFromParams(b.Parameters, "glow", out var glowHex)) evt.GlowColor = glowHex;

            beatmap.Bullets.Add(evt);
        }

        return beatmap;
    }

    private void DeleteByEventId(string? kind, int eventId)
    {
        if (string.IsNullOrWhiteSpace(kind) || eventId <= 0)
        {
            return;
        }

        if (kind.Equals(LevelEditorConstants.EventKindNote, StringComparison.OrdinalIgnoreCase))
        {
            var idx = _level.Notes.FindIndex(n => n.EventId == eventId);
            if (idx >= 0)
            {
                _level.Notes.RemoveAt(idx);
                _selection = EditorSelection.None;
                _serializer.Normalize(_level);
                _revision++;
                _statusMessage = $"Deleted note #{eventId}";
            }

            return;
        }

        if (kind.Equals(LevelEditorConstants.EventKindBullet, StringComparison.OrdinalIgnoreCase))
        {
            var idx = _level.Bullets.FindIndex(b => b.EventId == eventId);
            if (idx >= 0)
            {
                var bullet = _level.Bullets[idx];
                if (!CanDeleteBulletNow(bullet, out var reason))
                {
                    _statusMessage = reason;
                    return;
                }

                _level.Bullets.RemoveAt(idx);
                _selection = EditorSelection.None;
                _serializer.Normalize(_level);
                _revision++;
                _statusMessage = $"Deleted bullet #{eventId}";
            }
        }
    }

    private static string GetBulletDisplayLabel(LevelBulletEvent bullet)
    {
        var movement = "none";
        if (!(bullet.Parameters.TryGetValue("noMotion", out var noMotion) && noMotion >= 0.5d) &&
            bullet.Parameters.TryGetValue("motionPatternId", out var motionId))
        {
            movement = MotionPatternIdToName((int)Math.Round(motionId));
        }

        return $"pattern:{bullet.PatternId} move:{movement}";
    }

    private bool CanDeleteBulletNow(LevelBulletEvent bullet, out string reason)
    {
        var deltaMs = _transport.CurrentTimeMs - bullet.TimeMs;
        if (deltaMs >= 0 && deltaMs <= BulletDeleteWindowMs)
        {
            reason = string.Empty;
            return true;
        }

        reason = $"Bullet delete allowed only within {BulletDeleteWindowMs}ms after placement";
        return false;
    }

    private int ComputeLastEntityVisibleMs()
    {
        EnsureCachesUpToDate();
        var maxEnd = _cachedLastEntityEndMs;
        return Math.Max(0, maxEnd);
    }

    private IReadOnlyList<string> DiscoverSongs()
    {
        var songs = new List<string>();
        try
        {
            if (Directory.Exists(_contentAudioDirectory))
            {
                var files = Directory.GetFiles(_contentAudioDirectory)
                    .Where(f => f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    songs.Add($"Content/Audio/{name}");
                }
            }
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(_level.AudioPath) &&
            !songs.Contains(_level.AudioPath, StringComparer.OrdinalIgnoreCase))
        {
            songs.Insert(0, _level.AudioPath);
        }

        return songs;
    }

    private IReadOnlyList<string> GetSongOptionsCached()
    {
        var now = Environment.TickCount64;
        if (_cachedSongs.Count == 0 || now >= _nextSongRefreshTickMs)
        {
            _cachedSongs = DiscoverSongs().ToList();
            _nextSongRefreshTickMs = now + 1500;
        }

        return _cachedSongs;
    }

    private void EnsureCachesUpToDate()
    {
        if (_cachedRevision == _revision)
        {
            return;
        }

        _cachedRows = BuildTimelineRows().ToList();
        _cachedMarks = BuildPreviewMarks().ToList();
        _cachedMaxEventTimeMs = _cachedRows.Count > 0 ? _cachedRows.Max(r => r.TimeMs) : 0;
        _cachedLastEntityEndMs = _cachedMarks.Count > 0 ? _cachedMarks.Max(m => m.EndMs) : 0;
        var analysisTimelineEnd = Math.Max(_level.LevelLengthMs, Math.Max(_cachedLastEntityEndMs, _cachedMaxEventTimeMs + 4000));
        _cachedTimingAnalysis = TimingAnalysisEngine.Build(_level, analysisTimelineEnd);
        _cachedRevision = _revision;
    }
}

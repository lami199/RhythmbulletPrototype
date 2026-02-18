# Rhythm Prototype

Small 2D rhythm + bullet prototype inspired by osu!, built with C# + MonoGame DesktopGL.

## Run

1. Install .NET 8 SDK.
2. From this folder run:

```bash
dotnet run
```

## Beatmap Dev Mode

- Beatmap path: `Content/Maps/map.json`
- Press `F5` in-game to hot-reload JSON and restart.
- If `audioPath` is missing or invalid, the game continues with a silent deterministic clock.

### JSON format

```json
{
  "audioPath": "Content/Audio/song.ogg",
  "approachMs": 900,
  "circleRadius": 42,
  "globalOffsetMs": 0,
  "notes": [
    { "timeMs": 1000, "x": 0.5, "y": 0.4 }
  ],
  "bullets": [
    { "timeMs": 1200, "pattern": "radial", "count": 12, "speed": 260, "x": 0.5, "y": 0.5 },
    { "timeMs": 2000, "pattern": "aimed", "count": 6, "speed": 320, "intervalMs": 80, "x": 0.5, "y": 0.2 },
    { "timeMs": 2600, "pattern": "spiral", "count": 16, "speed": 250 }
  ]
}
```

## Controls

- Mouse move: cursor target (smoothed)
- `WASD`: micro-adjust velocity
- Left click: hit notes + click VFX/SFX
- Right click: alt click VFX/SFX
- Middle click: pulse click VFX/SFX
- `F1`: debug HUD
- `F2`: hitbox visualization
- `Space`: pause/unpause
- `R`: restart map
- `+/-`: timing offset adjust
- `F5`: reload map JSON and restart
- `F7`: toggle in-game Editor Mode

## Bundled Sample Levels

- `shooting_star` is bundled for main play flow.
- Source files live in `Content/Maps/Projects/`:
  - `shooting_star.editor.json`
  - `shooting_star.editor.published.map.json`
- Route fallback can target the published file via `Content/Maps/course_routes.json`.
- To ship custom editor levels through GitHub, keep their published JSON in `Content/Maps/Projects/` (not only under `bin/...` output).

## In-Game Level Editor (MVP)

The project now includes a framework-agnostic editor core under `src/Editor` with a MonoGame adapter.

- Editor file path: `Content/Maps/editor_level.json`
- Schema uses `schemaVersion` and stores notes + bullet triggers on the same timeline.
- Runtime/editor share the same data model (`LevelDocument`).

### Editor hotkeys

- `F7`: toggle editor mode
- `Space`: play/pause transport
- `K`: stop transport (seek to 0)
- `[` / `]`: seek -50ms / +50ms
- `Left` / `Right`: seek -500ms / +500ms
- `N`: add tap note at current time (uses mouse normalized pos)
- `H`: add hold note at current time (default 400ms)
- `B`: add bullet event at current time (uses current pattern)
- `1..9`: set lane
- `Up` / `Down`: select previous/next event
- `Delete`: delete selected event
- `Q` / `E`: nudge selected -10ms / +10ms (`Shift` for 50ms)
- `G`: toggle time snap
- `Ctrl+S`: save editor level JSON
- `Ctrl+L`: reload editor level JSON

### Integration points

Core interfaces:
- `IAudioTransport`: transport abstraction (play/pause/stop/seek/time)
- `INoteReceiver`: runtime note callback
- `IBulletPatternSpawner`: runtime bullet trigger callback
- `IEditorView`: UI/input abstraction for editor commands + model rendering

Main classes:
- `LevelSerializer`: JSON load/save + validation + stable sorting
- `LevelEditorController`: editor state + command handling
- `LevelPlayer`: runtime timeline playback of authored events
- `SongClockAudioTransport`: adapter from existing `SongClock` to `IAudioTransport`
- `MonoGameEditorView`: lightweight in-game keyboard/mouse editor UI

`Game1` wiring:
- Initializes editor with `Content/Maps/editor_level.json`
- Shares `SongClock` through `SongClockAudioTransport`
- Uses `F7` to toggle editor mode
- In editor mode, gameplay update/render is skipped and editor overlay is shown

### Example JSON produced by editor

```json
{
  "schemaVersion": 1,
  "audioPath": "Content/Audio/song.ogg",
  "bpm": 120.0,
  "nextEventId": 4,
  "notes": [
    { "eventId": 1, "timeMs": 1000, "lane": 1, "noteType": "tap", "durationMs": 0, "x": 0.45, "y": 0.42 },
    { "eventId": 2, "timeMs": 1400, "lane": 2, "noteType": "hold", "durationMs": 400, "x": 0.62, "y": 0.50 }
  ],
  "bullets": [
    {
      "eventId": 3,
      "timeMs": 1200,
      "pattern": "radial",
      "x": 0.5,
      "y": 0.1,
      "parameters": { "count": 12, "speed": 240 }
    }
  ]
}
```

## Notes

- Gameplay runs in virtual `1280x720` space with window scaling.
- Missing textures and SFX are handled via runtime-generated placeholders.
- Extra JSON fields are tolerated; required fields are validated with friendly errors.

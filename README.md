# RhythmbulletPrototype

RhythmbulletPrototype is a rhythm + bullet-hell prototype built with C# and MonoGame DesktopGL.

## Quick Start

### Prerequisites
- .NET 8 SDK
- DesktopGL native dependencies required by MonoGame on your OS (OpenGL/audio libs)

### Run
From the project root (`prototype3`):

```bash
dotnet run
```

### Fast Reload During Gameplay
- Edit `Content/Maps/map.json`
- Press `F5` in-game to reload map JSON and restart immediately

## Project Layout
- `Game1.cs`: main app loop, menus, gameplay/editor wiring
- `src/Systems/`: gameplay systems (notes, bullets, VFX, scoring, health, clock)
- `src/Editor/`: in-game editor view/controller/serialization
- `Content/Maps/map.json`: default runtime gameplay map
- `Content/Maps/editor_level.json`: editor working level file
- `Content/Maps/Projects/*.editor.json`: saved editor projects
- `Content/Maps/Projects/*.published.map.json`: published runtime maps from editor projects
- `Program.cs`: startup entrypoint with optional route/course argument

## Gameplay Controls
- Mouse move: cursor target
- `Z` / `X` / Left click: hit input
- Hold `C`: low-sensitivity focus movement
- Right click: alt VFX/SFX pulse
- Middle click: pulse VFX/SFX
- `F1`: debug HUD
- `F2`: hitboxes
- `F3`: toggle mouse smoothing
- `F5`: reload `map.json` and restart
- `F6`: cycle target FPS
- `Space`: pause/unpause (gameplay)
- `R`: restart current map
- `+` / `-`: adjust timing offset
- `Esc`: pause/escape menu

## Beatmap Authoring (`Content/Maps/map.json`)
Use `Content/Maps/map.json` as the primary runtime map for quick iteration.

Minimal example:

```json
{
  "audioPath": "Content/Audio/song.ogg",
  "approachMs": 900,
  "circleRadius": 42,
  "globalOffsetMs": 0,
  "notes": [
    { "timeMs": 1000, "x": 0.50, "y": 0.40 }
  ],
  "bullets": [
    { "timeMs": 1200, "pattern": "radial", "count": 12, "speed": 240, "x": 0.5, "y": 0.5 }
  ]
}
```

Notes:
- If `audioPath` is missing/invalid, gameplay still runs using a silent deterministic clock.
- New static scatter patterns are supported:
  - `static_scatter_5`
  - `static_scatter_10`
  - `static_scatter_20`
- Static scatter emits bullets one-by-one quickly, then releases movement together; selected movement patterns still apply.

## In-Game Editor Workflow
Open editor from **Main Menu -> Level Editor**.

High-value hotkeys:
- `P`: play/pause timeline
- `K`: stop timeline
- `Left` / `Right`: seek -500ms / +500ms
- `[` / `]`: seek -50ms / +50ms
- `;` / `'`: seek -1ms / +1ms
- `O`: cycle slow edit speed
- `N`: place tap note at mouse position
- `B`: place bullet event at mouse position (uses current static pattern + movement settings)
- `Up` / `Down`: select previous/next event
- `Delete`: delete selected event
- `Q` / `E`: nudge selected event (-10/+10ms, hold `Shift` for -50/+50ms)
- `G`: toggle snap
- `1..9`: set lane
- `Ctrl+S`: save editor project
- `Ctrl+L`: load editor project
- `Ctrl+P`: publish runtime JSON

Publishing flow:
1. Edit in project files (`*.editor.json`)
2. Publish to runtime JSON (`*.published.map.json`)
3. Play via route/level selection or direct map load flow

## Route / Course Argument
`Program.cs` accepts an optional argument and passes it to `Game1`:

```bash
dotnet run -- course_2
```

At runtime, `Game1` checks `Content/Maps/course_routes.json` to resolve the active/selected course map path. If routing fails, it falls back to the default map flow.

## Troubleshooting
- Game launches but no music:
  - Verify `audioPath` in map JSON and that the file exists.
  - If missing, silent deterministic timing is expected behavior.
- Build/runtime issues on DesktopGL:
  - Confirm OS-level MonoGame DesktopGL dependencies are installed.
  - Re-run `dotnet restore` then `dotnet run`.
- Wrong map loaded:
  - Check `Content/Maps/course_routes.json`
  - Check optional route argument passed to `dotnet run -- <route>`
  - Verify files exist under `Content/Maps/` and `Content/Maps/Projects/`

## License
This project is licensed under the terms in `LICENSE`.

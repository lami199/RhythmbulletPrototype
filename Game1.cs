/*
Rhythm + Bullet Hell Prototype (MonoGame DesktopGL)

How to run:
1) Install .NET 8 SDK and desktop dependencies required by MonoGame DesktopGL.
2) From this folder run: dotnet run
3) Edit beatmap at Content/Maps/map.json and press F5 in-game to reload + restart.
4) Place a song file and point audioPath in map.json. If missing, game runs with a silent deterministic clock.

Controls:
- Mouse move: main cursor target
- Z / X / Left click: hit input
- C (hold): low-sensitivity mouse focus for tight spaces
- Left click: hit notes + left VFX/SFX
- Right click: alt VFX/SFX
- Middle click: pulse VFX/SFX
- F1: debug HUD
- F2: hitboxes
- F5: reload map.json + restart
- Space: pause/unpause
- R: restart map
- +/-: timing offset adjust
*/

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RhythmbulletPrototype.Editor;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Rendering;
using RhythmbulletPrototype.Systems;
using RhythmbulletPrototype.Utils;
using System.Text.Json;

namespace RhythmbulletPrototype;

public sealed class Game1 : Game
{
    private enum AppMode
    {
        MainMenu,
        LevelSelect,
        SongSelect,
        Settings,
        Gameplay,
        Editor
    }

    private const int VirtualWidth = 1280;
    private const int VirtualHeight = 720;
    private const int EditorPreviewMaxBackSimMs = 30000;

    private readonly GraphicsDeviceManager _graphics;
    private readonly InputState _input = new();
    private readonly BeatmapLoader _beatmapLoader = new();
    private readonly LevelSerializer _levelSerializer = new();
    private readonly SongClock _songClock = new();
    private readonly string? _routeArg;

    private SpriteBatch? _spriteBatch;
    private RenderHelpers? _render;
    private BitmapTextRenderer? _text;
    private VirtualViewport? _viewport;

    private CursorController _cursor = new(new Vector2(VirtualWidth * 0.5f, VirtualHeight * 0.5f));
    private NoteSystem _noteSystem = new();
    private BulletSystem _bulletSystem = new();
    private VfxSystem _vfx = new();
    private readonly NoteSystem _editorPreviewNotes = new();
    private readonly BulletSystem _editorPreviewBullets = new();
    private MonoGameEditorView? _editorView;
    private LevelEditorController? _editorController;
    private SongClockAudioTransport? _editorTransport;

    private Beatmap _beatmap = new();
    private AppMode _appMode = AppMode.MainMenu;
    private readonly string[] _mainMenuOptions = { "Play", "Level Editor", "Settings", "Exit" };
    private readonly (string Label, int ApproachMs)[] _difficultyOptions =
    {
        ("AR 5", 1200),
        ("AR 8", 750),
        ("AR 10", 450)
    };
    private int _mainMenuIndex;
    private int _levelSelectIndex;
    private int _songSelectIndex;
    private readonly List<string> _levelSelectOptions = new();
    private string? _playMapOverridePath;
    private bool _songSelectFromLevelPlayer;
    private readonly List<string> _songSelectOptions = new();
    private string? _playAudioOverridePath;
    private int _difficultyIndex = 1;
    private string _activeMapPath = string.Empty;
    private string _editorLevelPath = string.Empty;
    private string _currentEditorProjectPath = string.Empty;
    private readonly List<string> _editorProjectPaths = new();
    private int _editorPreviewRevision = -1;
    private int _editorPreviewLastMs;
    private int _editorPreviewLastBulletEventCount;
    private float _editorPendingPatternSpawnTimer;
    private string _editorPendingPatternId = string.Empty;
    private int _editorSuppressPendingPreviewUntilMs;

    private bool _showDebugHud = true;
    private bool _showHitboxes;
    private bool _paused;
    private bool _failed;
    private int _timingOffsetMs;
    private int _lastLifeAwardScore;
    private int _nextDamageAllowedMs;
    private float _lives;
    private float _maxLives = 100f;
    private float _bulletHitDamage = 22f;
    private int _lifeGainStepScore = 350;
    private float _lifeGainAmount = 4f;
    private string _statusMessage = "";
    private Color _bgTop = new Color(18, 26, 40);
    private Color _bgBottom = new Color(10, 14, 22);
    private Color _bgAccent = new Color(30, 50, 79);
    private float _bgGridAlpha = 0.18f;
    private Texture2D? _backgroundImage;
    private Texture2D? _backgroundOverlayImage;
    private float _backgroundImageAlpha = 1f;
    private float _backgroundOverlayAlpha = 0f;
    private string _backgroundImageMode = "cover";
    private readonly int[] _fpsOptions = { 60, 120, 240, 360 };
    private int _targetFps = 120;
    private float _smoothedFps = 120f;
    private bool _perfWarning;
    private Vector2 _projectUiPos = new(16f, 520f);
    private bool _dragProjectUi;
    private Vector2 _projectUiDragOffset;
    private bool _projectDropdownOpen;
    private int _projectDropdownScroll;
    private bool _projectUiCollapsed;
    private bool _projectNameEditing;
    private string _projectNameInput = "";
    private const int DeletedProjectRetentionDays = 30;
    private float _projectNameDeleteRepeatTimer;
    private const float ProjectNameDeleteInitialDelay = 0.30f;
    private const float ProjectNameDeleteRepeatInterval = 0.045f;
    private float _saveToastTimer;
    private const float SaveToastDurationSec = 1f;
    private readonly string[] _escMenuOptions = { "Main Menu", "Exit" };
    private bool _showEscMenu;
    private int _escMenuIndex;
    private bool _resumeAfterEscMenu;
    private bool _wasInvulnerable;
    private int _vulnerableReturnAnimStartMs = int.MinValue;
    private const int VulnerableReturnAnimDurationMs = 260;
    private const int BulletInvulnerabilityMs = 2000;

    public Game1(string? routeArg = null)
    {
        _routeArg = routeArg;
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";

        _graphics.PreferredBackBufferWidth = 1600;
        _graphics.PreferredBackBufferHeight = 900;
        _graphics.HardwareModeSwitch = false;
        _graphics.IsFullScreen = false;
        IsMouseVisible = false;
        IsFixedTimeStep = true;
        ApplyTargetFps(_targetFps);

        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (_, _) => _viewport?.Refresh();
    }

    protected override void Initialize()
    {
        Window.IsBorderless = true;
        var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        _graphics.PreferredBackBufferWidth = dm.Width;
        _graphics.PreferredBackBufferHeight = dm.Height;
        _graphics.ApplyChanges();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _render = new RenderHelpers(GraphicsDevice);
        _text = new BitmapTextRenderer(_render.Pixel);
        _viewport = new VirtualViewport(GraphicsDevice, VirtualWidth, VirtualHeight);
        _editorLevelPath = Path.Combine(AppContext.BaseDirectory, "Content", "Maps", "editor_level.json");

        ReloadBeatmapAndRestart();
    }

    protected override void Update(GameTime gameTime)
    {
        if (_spriteBatch is null || _viewport is null)
        {
            return;
        }

        _input.Update();
        IsMouseVisible = _appMode != AppMode.Gameplay || _showEscMenu;

        if (_showEscMenu)
        {
            UpdateEscMenu();
            return;
        }

        if (_input.IsKeyPressed(Keys.Escape))
        {
            OpenEscMenu();
            return;
        }

        if (_appMode == AppMode.MainMenu)
        {
            UpdateMainMenu();
            base.Update(gameTime);
            return;
        }

        if (_appMode == AppMode.Settings)
        {
            UpdateSettingsMenu();
            base.Update(gameTime);
            return;
        }

        if (_appMode == AppMode.SongSelect)
        {
            UpdateSongSelectMenu();
            base.Update(gameTime);
            return;
        }

        if (_appMode == AppMode.LevelSelect)
        {
            UpdateLevelSelectMenu();
            base.Update(gameTime);
            return;
        }

        if (_input.IsKeyPressed(Keys.F1)) _showDebugHud = !_showDebugHud;
        if (_input.IsKeyPressed(Keys.F2)) _showHitboxes = !_showHitboxes;
        if (_input.IsKeyPressed(Keys.F5)) ReloadBeatmapAndRestart();
        if (_input.IsKeyPressed(Keys.F6)) CycleTargetFps();
        if (_appMode == AppMode.Gameplay && _input.IsKeyPressed(Keys.Space))
        {
            _paused = !_paused;
            if (_paused)
            {
                _songClock.Pause();
            }
            else
            {
                _songClock.Resume();
            }
        }

        if (_input.IsKeyPressed(Keys.R))
        {
            RestartCurrentMap();
        }

        if (_input.IsKeyPressed(Keys.OemPlus) || _input.IsKeyPressed(Keys.Add)) _timingOffsetMs += 5;
        if (_input.IsKeyPressed(Keys.OemMinus) || _input.IsKeyPressed(Keys.Subtract)) _timingOffsetMs -= 5;

        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _saveToastTimer = MathF.Max(0f, _saveToastTimer - dt);
        var mouseVirtual = _viewport.ScreenToVirtual(_input.MousePosition);
        var slowMouse = _input.IsKeyDown(Keys.C);
        _cursor.Update(dt, mouseVirtual, slowMouse);
        var mouseNormalized = new Vector2(
            Math.Clamp(mouseVirtual.X / VirtualWidth, 0f, 1f),
            Math.Clamp(mouseVirtual.Y / VirtualHeight, 0f, 1f));

        if (_appMode == AppMode.Editor && _editorView is not null && _editorController is not null)
        {
            var ctrlDown = _input.IsKeyDown(Keys.LeftControl) || _input.IsKeyDown(Keys.RightControl);
            if (ctrlDown && _input.IsKeyPressed(Keys.S))
            {
                TriggerSaveToast();
            }

            var consumedByProjectUi = HandleEditorProjectGui(mouseVirtual, dt);
            if (!consumedByProjectUi)
            {
                _editorView.CaptureHotkeys(_input, mouseVirtual, mouseNormalized, dt);
            }
            _editorController.Update(dt);
            if (!consumedByProjectUi)
            {
                HandleEditorProjectHotkeys();
            }
            UpdateEditorPreview(dt);
            base.Update(gameTime);
            return;
        }

        var hitPressed = _input.LeftPressed || _input.IsKeyPressed(Keys.Z) || _input.IsKeyPressed(Keys.X);

        if (hitPressed)
        {
            _vfx.SpawnLeftClick(_cursor.Position);
        }

        if (_input.RightPressed)
        {
            _vfx.SpawnRightClick(_cursor.Position);
        }

        if (_input.MiddlePressed)
        {
            _vfx.SpawnMiddleClick(_cursor.Position);
        }

        _vfx.Update(dt);
        UpdatePerformanceMetrics(dt);

        if (_paused)
        {
            base.Update(gameTime);
            return;
        }

        var songMs = _songClock.CurrentTimeMs + _timingOffsetMs + _beatmap.GlobalOffsetMs;

        if (!_failed)
        {
            _noteSystem.Update(dt, songMs, _cursor.Position, _input.LeftDown, _input.LeftReleased);
            _bulletSystem.Update(dt, songMs, _cursor.Position);

            while (_noteSystem.TryDequeueAutoMiss(out var miss))
            {
                _vfx.SpawnMiss(miss.Position);
            }

            if (hitPressed)
            {
                if (_noteSystem.TryHit(songMs, _cursor.Position, out var hitResult) && hitResult.Judgment != Judgment.None)
                {
                    _vfx.SpawnJudgment(hitResult.Judgment, hitResult.Position);
                }
            }

            if (_bulletSystem.CheckCursorHit(_cursor.Position, _cursor.CollisionRadius))
            {
                if (songMs >= _nextDamageAllowedMs)
                {
                    _lives = Math.Max(0f, _lives - _bulletHitDamage);
                    _nextDamageAllowedMs = songMs + BulletInvulnerabilityMs;
                    _vfx.SpawnFailPulse(_cursor.Position);
                    if (_lives <= 0f)
                    {
                        _failed = true;
                        _statusMessage = "FAILED - PRESS R TO RESTART";
                    }
                }
            }

            AwardLivesFromScore();
        }

        if (_appMode == AppMode.Gameplay)
        {
            var invulnerableNow = songMs < _nextDamageAllowedMs;
            if (_wasInvulnerable && !invulnerableNow)
            {
                _vulnerableReturnAnimStartMs = songMs;
            }

            _wasInvulnerable = invulnerableNow;
        }
        else
        {
            _wasInvulnerable = false;
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_spriteBatch is null || _render is null || _text is null || _viewport is null)
        {
            return;
        }

        GraphicsDevice.Clear(new Color(10, 14, 22));

        _spriteBatch.Begin(
            samplerState: SamplerState.PointClamp,
            blendState: BlendState.AlphaBlend,
            transformMatrix: _viewport.Matrix);

        var songMs = _songClock.CurrentTimeMs + _timingOffsetMs + _beatmap.GlobalOffsetMs;
        DrawBackground(_spriteBatch, _render, songMs);
        if (_appMode == AppMode.Gameplay)
        {
            _noteSystem.Draw(_spriteBatch, _render, _text, songMs, _showHitboxes);
            _bulletSystem.Draw(_spriteBatch, _render, _text, songMs, _showHitboxes);
        }
        else if (_appMode == AppMode.Editor)
        {
            _editorPreviewNotes.Draw(_spriteBatch, _render, _text, songMs, _showHitboxes);
            _editorPreviewBullets.Draw(_spriteBatch, _render, _text, songMs, _showHitboxes);
        }
        DrawCursor(_spriteBatch, _render, songMs);
        if (_appMode == AppMode.Gameplay)
        {
            _vfx.Draw(_spriteBatch, _render, _text);
            DrawLivesOverlay(_spriteBatch, _text);
        }
        else if (_appMode == AppMode.Editor)
        {
            _editorView?.DrawOverlay(_spriteBatch, _render, _text, VirtualWidth, VirtualHeight);
            DrawEditorProjectUi(_spriteBatch, _render, _text);
            DrawSaveToast(_spriteBatch, _text);
        }
        if (_appMode == AppMode.MainMenu)
        {
            DrawMainMenu(_spriteBatch, _render, _text);
        }
        else if (_appMode == AppMode.LevelSelect)
        {
            DrawLevelSelectMenu(_spriteBatch, _render, _text);
        }
        else if (_appMode == AppMode.SongSelect)
        {
            DrawSongSelectMenu(_spriteBatch, _render, _text);
        }
        else if (_appMode == AppMode.Settings)
        {
            DrawSettingsMenu(_spriteBatch, _render, _text);
        }

        if (_paused)
        {
            _render.DrawRect(_spriteBatch, new Rectangle(0, 0, VirtualWidth, VirtualHeight), new Color(0, 0, 0, 110));
            _text.DrawString(_spriteBatch, "PAUSED", new Vector2(590f, 340f), Color.White, 3f);
        }

        if (_failed)
        {
            _render.DrawRect(_spriteBatch, new Rectangle(0, 0, VirtualWidth, VirtualHeight), new Color(35, 0, 0, 110));
            _text.DrawString(_spriteBatch, "FAILED", new Vector2(585f, 332f), new Color(255, 120, 120), 3f);
            _text.DrawString(_spriteBatch, "PRESS R TO RESTART", new Vector2(510f, 370f), Color.White, 2f);
        }

        if (_showDebugHud && _appMode == AppMode.Gameplay)
        {
            DrawDebugHud(_spriteBatch, _text, gameTime, songMs);
        }

        if (_showEscMenu)
        {
            DrawEscMenu(_spriteBatch, _render, _text);
        }

        _spriteBatch.End();

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _songClock.Dispose();
        _backgroundImage?.Dispose();
        _backgroundOverlayImage?.Dispose();
        _render?.Dispose();
        base.UnloadContent();
    }

    private void ReloadBeatmapAndRestart()
    {
        var mapPath = ResolveBeatmapPathFromRoutes();
        _activeMapPath = mapPath;

        try
        {
            _beatmap = _beatmapLoader.LoadFromPath(mapPath);
            _statusMessage = "MAP LOADED";
        }
        catch (BeatmapLoadException ex)
        {
            _beatmap = CreateFallbackBeatmap();
            _statusMessage = ex.Message;
        }

        EnsureEditorInitialized();
        RestartCurrentMap();
    }

    private void OpenEscMenu()
    {
        _showEscMenu = true;
        _escMenuIndex = 0;
        _resumeAfterEscMenu = _songClock.IsRunning && !_paused;
        if (_resumeAfterEscMenu)
        {
            _songClock.Pause();
        }
    }

    private void CloseEscMenu(bool resumeClock)
    {
        _showEscMenu = false;
        if (resumeClock && _resumeAfterEscMenu && _appMode != AppMode.MainMenu)
        {
            _songClock.Resume();
        }

        _resumeAfterEscMenu = false;
    }

    private void UpdateEscMenu()
    {
        if (_viewport is not null)
        {
            var mouseVirtual = _viewport.ScreenToVirtual(_input.MousePosition);
            for (var i = 0; i < _escMenuOptions.Length; i++)
            {
                if (!GetEscMenuItemRect(i).Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
                {
                    continue;
                }

                _escMenuIndex = i;
                if (_input.LeftPressed)
                {
                    ActivateEscMenuSelection(i);
                    return;
                }
            }
        }

        if (_input.IsKeyPressed(Keys.Up))
        {
            _escMenuIndex = (_escMenuIndex + _escMenuOptions.Length - 1) % _escMenuOptions.Length;
        }

        if (_input.IsKeyPressed(Keys.Down))
        {
            _escMenuIndex = (_escMenuIndex + 1) % _escMenuOptions.Length;
        }

        if (_input.IsKeyPressed(Keys.Escape))
        {
            CloseEscMenu(resumeClock: true);
            return;
        }

        if (_input.IsKeyPressed(Keys.Enter))
        {
            ActivateEscMenuSelection(_escMenuIndex);
        }
    }

    private void ActivateEscMenuSelection(int index)
    {
        switch (index)
        {
            case 0:
                _songClock.Stop();
                _paused = false;
                _failed = false;
                _appMode = AppMode.MainMenu;
                _statusMessage = "MAIN MENU";
                CloseEscMenu(resumeClock: false);
                break;
            case 1:
                Exit();
                break;
        }
    }

    private static Rectangle GetEscMenuItemRect(int index)
    {
        var panelX = 470;
        var panelY = 250;
        var y = panelY + 86 + index * 42;
        return new Rectangle(panelX + 86, y - 4, 190, 34);
    }

    private void DrawEscMenu(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        render.DrawRect(spriteBatch, new Rectangle(0, 0, VirtualWidth, VirtualHeight), new Color(0, 0, 0, 170));
        var panel = new Rectangle(470, 250, 340, 220);
        render.DrawRect(spriteBatch, panel, new Color(18, 22, 32, 240));
        render.DrawRect(spriteBatch, new Rectangle(panel.X, panel.Y, panel.Width, 1), new Color(150, 170, 205, 240));
        render.DrawRect(spriteBatch, new Rectangle(panel.X, panel.Bottom - 1, panel.Width, 1), new Color(150, 170, 205, 240));

        text.DrawString(spriteBatch, "PAUSE MENU", new Vector2(panel.X + 72f, panel.Y + 24f), Color.White, 2.1f);
        for (var i = 0; i < _escMenuOptions.Length; i++)
        {
            var selected = i == _escMenuIndex;
            var color = selected ? new Color(120, 240, 255) : Color.White;
            text.DrawString(spriteBatch, _escMenuOptions[i], new Vector2(panel.X + 95f, panel.Y + 86f + i * 42f), color, 2f);
        }

        text.DrawString(spriteBatch, "UP/DOWN: SELECT  ENTER: CONFIRM  ESC: CLOSE", new Vector2(panel.X - 34f, panel.Bottom - 34f), new Color(200, 215, 235), 1.35f);
    }

    private void RestartCurrentMap()
    {
        _failed = false;
        _paused = false;
        _vfx.Clear();
        _cursor.Reset(new Vector2(VirtualWidth * 0.5f, VirtualHeight * 0.5f));

        ApplyDifficultyApproachOverride();
        _noteSystem.Reset(_beatmap);
        _bulletSystem.Reset(_beatmap);
        _cursor.CollisionRadius = _beatmap.CursorHitboxRadius;
        _bgTop = ParseHexColor(_beatmap.BackgroundTopColor, new Color(18, 26, 40));
        _bgBottom = ParseHexColor(_beatmap.BackgroundBottomColor, new Color(10, 14, 22));
        _bgAccent = ParseHexColor(_beatmap.BackgroundAccentColor, new Color(30, 50, 79));
        _bgGridAlpha = Math.Clamp(_beatmap.BackgroundGridAlpha, 0f, 1f);
        _backgroundImageAlpha = Math.Clamp(_beatmap.BackgroundImageAlpha, 0f, 1f);
        _backgroundOverlayAlpha = Math.Clamp(_beatmap.BackgroundOverlayAlpha, 0f, 1f);
        _backgroundImageMode = string.IsNullOrWhiteSpace(_beatmap.BackgroundImageMode) ? "cover" : _beatmap.BackgroundImageMode.Trim().ToLowerInvariant();
        ReloadBackgroundTextures();
        ApplyTargetFps(_beatmap.TargetFps);
        _maxLives = _beatmap.MaxLives;
        _bulletHitDamage = _beatmap.BulletHitDamage;
        _lifeGainStepScore = _beatmap.LifeGainStepScore;
        _lifeGainAmount = _beatmap.LifeGainAmount;
        _lives = _maxLives;
        _lastLifeAwardScore = 0;
        _nextDamageAllowedMs = 0;
        _wasInvulnerable = false;
        _vulnerableReturnAnimStartMs = int.MinValue;

        var contentBase = AppContext.BaseDirectory;
        _songClock.TryLoadSong(_beatmap.AudioPath, contentBase, out var audioError);
        if (!string.IsNullOrWhiteSpace(audioError))
        {
            _statusMessage = audioError;
        }

        _songClock.PlayFromStart();
    }

    private void DrawBackground(SpriteBatch spriteBatch, RenderHelpers render, int songMs)
    {
        var stripeHeight = 6;
        for (var y = 0; y < VirtualHeight; y += stripeHeight)
        {
            var t = y / (float)VirtualHeight;
            var c = Color.Lerp(_bgTop, _bgBottom, t);
            render.DrawRect(spriteBatch, new Rectangle(0, y, VirtualWidth, stripeHeight + 1), c);
        }

        var pulse = 0.35f + 0.1f * MathF.Sin(songMs * 0.003f);
        render.DrawCircleFilled(spriteBatch, new Vector2(640, 360), 300f, _bgAccent * pulse);

        var gridColor = Color.White * _bgGridAlpha;
        for (var x = 0; x <= VirtualWidth; x += 64)
        {
            render.DrawLine(spriteBatch, new Vector2(x, 0), new Vector2(x, VirtualHeight), 1f, gridColor);
        }

        for (var y = 0; y <= VirtualHeight; y += 64)
        {
            render.DrawLine(spriteBatch, new Vector2(0, y), new Vector2(VirtualWidth, y), 1f, gridColor);
        }

        if (_backgroundImage is not null && _backgroundImageAlpha > 0f)
        {
            DrawBackgroundImage(spriteBatch, _backgroundImage, _backgroundImageAlpha);
        }

        if (_backgroundOverlayImage is not null && _backgroundOverlayAlpha > 0f)
        {
            DrawBackgroundImage(spriteBatch, _backgroundOverlayImage, _backgroundOverlayAlpha);
        }
    }

    private void DrawCursor(SpriteBatch spriteBatch, RenderHelpers render, int songMs)
    {
        var invulnerable = _appMode == AppMode.Gameplay && !_failed && songMs < _nextDamageAllowedMs;
        var invulPulse = 0.72f + 0.28f * MathF.Sin(songMs * 0.045f);
        var alphaScale = invulnerable ? (0.38f + invulPulse * 0.24f) : 1f;

        var trail = _cursor.Trail;
        for (var i = 0; i < trail.Count; i++)
        {
            var t = i / (float)Math.Max(1, trail.Count - 1);
            var lifeT = Math.Clamp(1f - (trail[i].Age / 0.32f), 0f, 1f);
            var alpha = (0.12f + t * 0.42f) * lifeT * alphaScale;
            render.DrawCircleFilled(spriteBatch, trail[i].Position, 7f + t * 7f, new Color(120, 200, 255) * alpha);
        }

        var bodyRadius = Math.Max(13f, _cursor.CollisionRadius * 4.5f);
        render.DrawCircleFilled(spriteBatch, _cursor.Position, bodyRadius, new Color(125, 225, 255, 95) * alphaScale);
        render.DrawCircleOutline(spriteBatch, _cursor.Position, bodyRadius, 1.5f, new Color(205, 245, 255, 170) * alphaScale);

        render.DrawCircleFilled(spriteBatch, _cursor.Position, _cursor.CollisionRadius, new Color(15, 18, 24) * alphaScale);
        render.DrawCircleOutline(spriteBatch, _cursor.Position, _cursor.CollisionRadius + 1.3f, 2.3f, Color.White * alphaScale);

        if (!invulnerable)
        {
            var recoverElapsed = songMs - _vulnerableReturnAnimStartMs;
            if (recoverElapsed >= 0 && recoverElapsed <= VulnerableReturnAnimDurationMs)
            {
                var t = recoverElapsed / (float)VulnerableReturnAnimDurationMs;
                var ease = 1f - MathF.Pow(1f - t, 2f);
                var ringR = _cursor.CollisionRadius + 3.5f + ease * 16f;
                var ringA = (1f - t) * (1f - t);
                render.DrawCircleOutline(spriteBatch, _cursor.Position, ringR, 2f, new Color(240, 245, 255) * (0.7f * ringA));
                render.DrawCircleFilled(spriteBatch, _cursor.Position, _cursor.CollisionRadius + 0.5f, Color.White * (0.12f * ringA));
            }
        }

        if (_showHitboxes)
        {
            render.DrawCircleOutline(spriteBatch, _cursor.Position, _cursor.CollisionRadius + 2.5f, 1f, Color.LawnGreen);
        }
    }

    private void DrawDebugHud(SpriteBatch spriteBatch, BitmapTextRenderer text, GameTime gameTime, int songMs)
    {
        var fps = 1f / Math.Max((float)gameTime.ElapsedGameTime.TotalSeconds, 0.0001f);

        text.DrawString(spriteBatch, $"FPS: {fps:0}", new Vector2(16f, 16f), Color.White, 2f);
        text.DrawString(spriteBatch, $"TARGET_FPS: {_targetFps}", new Vector2(16f, 232f), Color.White, 2f);
        text.DrawString(spriteBatch, $"PERF: {(_perfWarning ? "LAGGING" : "OK")}", new Vector2(16f, 250f), _perfWarning ? new Color(255, 140, 140) : new Color(170, 245, 170), 2f);
        text.DrawString(spriteBatch, $"SONG_MS: {songMs}", new Vector2(16f, 34f), Color.White, 2f);
        text.DrawString(spriteBatch, $"CURSOR: {_cursor.Position.X:0},{_cursor.Position.Y:0}", new Vector2(16f, 52f), Color.White, 2f);
        text.DrawString(spriteBatch, $"NEXT_NOTE: {_noteSystem.NextObjectIndex}", new Vector2(16f, 70f), Color.White, 2f);
        text.DrawString(spriteBatch, $"BULLETS: {_bulletSystem.ActiveBulletCount}", new Vector2(16f, 88f), Color.White, 2f);
        text.DrawString(spriteBatch, $"OFFSET_MS: {_timingOffsetMs}", new Vector2(16f, 106f), Color.White, 2f);
        text.DrawString(spriteBatch, $"HITBOX_R: {_cursor.CollisionRadius:0.0}", new Vector2(16f, 124f), Color.White, 2f);
        text.DrawString(spriteBatch, $"LAST_DT: {_noteSystem.LastHitDeltaMs:0}", new Vector2(16f, 142f), Color.White, 2f);
        text.DrawString(spriteBatch, $"SCORE: {_noteSystem.Score}", new Vector2(16f, 160f), Color.White, 2f);
        text.DrawString(spriteBatch, $"COMBO: {_noteSystem.Combo}", new Vector2(16f, 178f), Color.White, 2f);
        text.DrawString(spriteBatch, $"ACC: {_noteSystem.Accuracy:0.00}%", new Vector2(16f, 196f), Color.White, 2f);
        text.DrawString(spriteBatch, $"WAVE_RISK: {_bulletSystem.PotentiallyImpossibleEvents}", new Vector2(16f, 214f), _bulletSystem.PotentiallyImpossibleEvents > 0 ? new Color(255, 170, 170) : new Color(170, 245, 170), 2f);
        text.DrawString(spriteBatch, "F1 HUD F2 HITBOX F5 RELOAD F6 FPS SPACE PAUSE R RESTART +/- OFFSET", new Vector2(16f, 700f), new Color(200, 210, 230), 1.5f);

        if (!string.IsNullOrWhiteSpace(_statusMessage))
        {
            text.DrawString(spriteBatch, _statusMessage, new Vector2(16f, 248f), new Color(255, 210, 150), 2f);
        }
    }

    private void DrawLivesBar(SpriteBatch spriteBatch)
    {
        if (_render is null)
        {
            return;
        }

        var barX = 16;
        var barY = 232;
        var barWidth = 280;
        var barHeight = 16;
        var fill = (int)MathF.Round(barWidth * Math.Clamp(_lives / Math.Max(1f, _maxLives), 0f, 1f));

        _render.DrawRect(spriteBatch, new Rectangle(barX - 2, barY - 2, barWidth + 4, barHeight + 4), Color.White * 0.85f);
        _render.DrawRect(spriteBatch, new Rectangle(barX, barY, barWidth, barHeight), new Color(35, 35, 40));
        _render.DrawRect(spriteBatch, new Rectangle(barX, barY, fill, barHeight), new Color(90, 235, 120));
    }

    private void DrawLivesOverlay(SpriteBatch spriteBatch, BitmapTextRenderer text)
    {
        text.DrawString(spriteBatch, $"LIVES: {_lives:0}/{_maxLives:0}", new Vector2(960f, 16f), Color.White, 2f);
        DrawLivesBarAt(spriteBatch, 960, 36, 300, 16);
    }

    private void DrawLivesBarAt(SpriteBatch spriteBatch, int barX, int barY, int barWidth, int barHeight)
    {
        if (_render is null)
        {
            return;
        }

        var fill = (int)MathF.Round(barWidth * Math.Clamp(_lives / Math.Max(1f, _maxLives), 0f, 1f));
        _render.DrawRect(spriteBatch, new Rectangle(barX - 2, barY - 2, barWidth + 4, barHeight + 4), Color.White * 0.85f);
        _render.DrawRect(spriteBatch, new Rectangle(barX, barY, barWidth, barHeight), new Color(35, 35, 40));
        _render.DrawRect(spriteBatch, new Rectangle(barX, barY, fill, barHeight), new Color(90, 235, 120));
    }

    private void AwardLivesFromScore()
    {
        var totalScore = _noteSystem.Score;

        while (totalScore - _lastLifeAwardScore >= _lifeGainStepScore)
        {
            _lastLifeAwardScore += _lifeGainStepScore;
            _lives = Math.Min(_maxLives, _lives + _lifeGainAmount);
        }
    }

    private void UpdatePerformanceMetrics(float dt)
    {
        var inst = 1f / Math.Max(dt, 0.0001f);
        _smoothedFps = MathHelper.Lerp(_smoothedFps, inst, 0.08f);
        _perfWarning = _smoothedFps < _targetFps * 0.9f;
    }

    private void ApplyTargetFps(int fps)
    {
        _targetFps = Math.Clamp(fps, 30, 500);
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / _targetFps);
        _statusMessage = $"TARGET FPS {_targetFps}";
    }

    private void CycleTargetFps()
    {
        var idx = Array.FindIndex(_fpsOptions, f => f == _targetFps);
        idx = idx < 0 ? 0 : (idx + 1) % _fpsOptions.Length;
        ApplyTargetFps(_fpsOptions[idx]);
    }

    private string ResolveBeatmapPathFromRoutes()
    {
        var defaultMap = Path.Combine(AppContext.BaseDirectory, "Content", "Maps", "map.json");
        var routesPath = Path.Combine(AppContext.BaseDirectory, "Content", "Maps", "course_routes.json");
        if (!File.Exists(routesPath))
        {
            return defaultMap;
        }

        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(routesPath));
            var root = doc.RootElement;
            var routeKey = _routeArg;
            if (string.IsNullOrWhiteSpace(routeKey) && root.TryGetProperty("activeCourse", out var activeCourse))
            {
                routeKey = activeCourse.GetString();
            }

            if (!string.IsNullOrWhiteSpace(routeKey) &&
                root.TryGetProperty("courses", out var courses) &&
                courses.ValueKind == JsonValueKind.Object &&
                courses.TryGetProperty(routeKey, out var mapped) &&
                mapped.ValueKind == JsonValueKind.String)
            {
                var mappedPath = mapped.GetString();
                if (!string.IsNullOrWhiteSpace(mappedPath))
                {
                    var resolved = ResolveAssetPath(mappedPath);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        _statusMessage = $"COURSE {routeKey}";
                        return resolved;
                    }
                }
            }
        }
        catch
        {
            _statusMessage = "Invalid course_routes.json, using default map";
        }

        return defaultMap;
    }

    private void EnsureEditorInitialized()
    {
        _editorView ??= new MonoGameEditorView();
        _editorTransport ??= new SongClockAudioTransport(_songClock, AppContext.BaseDirectory);
        var audioDir = Path.Combine(AppContext.BaseDirectory, "Content", "Audio");
        _editorController ??= new LevelEditorController(_editorTransport, _editorView, _levelSerializer, audioDir);

        _currentEditorProjectPath = ResolveMostRecentEditorProjectPath();
        var existed = File.Exists(_currentEditorProjectPath);
        _editorController.LoadOrCreate(_currentEditorProjectPath, _beatmap.AudioPath);
        if (!existed)
        {
            _editorController.Save();
        }
        _projectNameInput = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(_currentEditorProjectPath));
        _editorPreviewRevision = -1;
        _editorPreviewLastMs = 0;
        _editorPendingPatternSpawnTimer = 0f;
        _editorPendingPatternId = string.Empty;
        _editorPreviewLastBulletEventCount = 0;
        _editorSuppressPendingPreviewUntilMs = 0;
        _statusMessage = $"EDITOR PROJECT: {Path.GetFileName(_currentEditorProjectPath)}";
    }

    private void HandleEditorProjectHotkeys()
    {
        if (_editorController is null)
        {
            return;
        }

        if (_input.IsKeyPressed(Keys.F8))
        {
            _editorController.Save();
            _currentEditorProjectPath = CreateNewEditorProjectPath(_projectNameInput);
            _editorController.LoadOrCreate(_currentEditorProjectPath, _beatmap.AudioPath);
            _editorController.Save();
            RefreshEditorProjectPaths();
            ResetEditorPreviewState();
            _statusMessage = $"NEW PROJECT: {Path.GetFileName(_currentEditorProjectPath)}";
            return;
        }

        var ctrlDown = _input.IsKeyDown(Keys.LeftControl) || _input.IsKeyDown(Keys.RightControl);
        if (ctrlDown && _input.IsKeyPressed(Keys.Delete))
        {
            DeleteCurrentEditorProject();
        }
    }

    private bool HandleEditorProjectGui(Vector2 mouseVirtual, float dt)
    {
        if (_editorController is null)
        {
            return false;
        }
        RefreshEditorProjectPaths();

        var panelHeight = _projectUiCollapsed ? 22 : 96;
        var panelRect = new Rectangle((int)_projectUiPos.X, (int)_projectUiPos.Y, 500, panelHeight);
        var headerRect = new Rectangle(panelRect.X, panelRect.Y, panelRect.Width, 20);
        var toggleRect = new Rectangle(headerRect.Right - 18, headerRect.Y + 2, 14, 14);
        var labelWidth = 108;
        var fieldX = panelRect.X + 8 + labelWidth;
        var fieldWidth = 188;
        var nameRect = new Rectangle(fieldX, panelRect.Y + 30, fieldWidth, 20);
        var dropdownRect = new Rectangle(fieldX, panelRect.Y + 54, fieldWidth, 20);
        var newRect = new Rectangle(panelRect.X + 314, panelRect.Y + 30, 74, 20);
        var saveRect = new Rectangle(panelRect.X + 314, panelRect.Y + 54, 74, 20);
        var deleteRect = new Rectangle(panelRect.X + 392, panelRect.Y + 30, 96, 44);
        var listRect = new Rectangle(dropdownRect.X, dropdownRect.Bottom + 2, dropdownRect.Width, 8 * 12 + 4);
        var dropdownVisible = _projectDropdownOpen && _editorProjectPaths.Count > 0;
        var mousePoint = new Point((int)mouseVirtual.X, (int)mouseVirtual.Y);

        var overUi = panelRect.Contains(mousePoint) || (dropdownVisible && listRect.Contains(mousePoint)) || _dragProjectUi || _projectNameEditing;

        if (_dragProjectUi)
        {
            if (_input.LeftDown)
            {
                _projectUiPos = mouseVirtual - _projectUiDragOffset;
                _projectUiPos = ClampProjectPanelToViewport(_projectUiPos, _projectUiCollapsed ? 22f : 96f);
            }
            else
            {
                _dragProjectUi = false;
                _projectUiPos = ClampProjectPanelToViewport(_projectUiPos, _projectUiCollapsed ? 22f : 96f);
            }

            return true;
        }

        if (_projectNameEditing)
        {
            HandleProjectNameTyping(dt);
        }

        if (!_projectUiCollapsed && _input.MouseWheelDelta != 0 && dropdownVisible && listRect.Contains(mousePoint))
        {
            var steps = _input.MouseWheelDelta / 120;
            _projectDropdownScroll -= steps;
            _projectDropdownScroll = Math.Clamp(_projectDropdownScroll, 0, Math.Max(0, _editorProjectPaths.Count - 8));
            return true;
        }

        if (_input.LeftPressed)
        {
            if (toggleRect.Contains(mousePoint))
            {
                _projectUiCollapsed = !_projectUiCollapsed;
                _projectDropdownOpen = false;
                _projectNameEditing = false;
                _projectNameDeleteRepeatTimer = 0f;
                _projectUiPos = ClampProjectPanelToViewport(_projectUiPos, _projectUiCollapsed ? 22f : 96f);
                return true;
            }

            if (headerRect.Contains(mousePoint))
            {
                _dragProjectUi = true;
                _projectUiDragOffset = mouseVirtual - _projectUiPos;
                return true;
            }

            if (_projectUiCollapsed)
            {
                return panelRect.Contains(mousePoint);
            }

            if (nameRect.Contains(mousePoint))
            {
                _projectNameEditing = true;
                _projectNameDeleteRepeatTimer = 0f;
                return true;
            }

            if (dropdownRect.Contains(mousePoint))
            {
                _projectDropdownOpen = !_projectDropdownOpen;
                _projectNameEditing = false;
                _projectNameDeleteRepeatTimer = 0f;
                return true;
            }

            if (newRect.Contains(mousePoint))
            {
                _editorController.Save();
                _currentEditorProjectPath = CreateNewEditorProjectPath(_projectNameInput);
                _editorController.LoadOrCreate(_currentEditorProjectPath, _beatmap.AudioPath);
                _editorController.Save();
                RefreshEditorProjectPaths();
                ResetEditorPreviewState();
                TriggerSaveToast();
                _projectDropdownOpen = false;
                _projectNameEditing = false;
                _projectNameDeleteRepeatTimer = 0f;
                _statusMessage = $"NEW PROJECT: {Path.GetFileName(_currentEditorProjectPath)}";
                return true;
            }

            if (saveRect.Contains(mousePoint))
            {
                _editorController.Save();
                RefreshEditorProjectPaths();
                TriggerSaveToast();
                _projectDropdownOpen = false;
                _projectNameEditing = false;
                _projectNameDeleteRepeatTimer = 0f;
                _statusMessage = $"SAVED: {Path.GetFileName(_currentEditorProjectPath)}";
                return true;
            }

            if (deleteRect.Contains(mousePoint))
            {
                DeleteCurrentEditorProject();
                _projectDropdownOpen = false;
                _projectNameEditing = false;
                _projectNameDeleteRepeatTimer = 0f;
                return true;
            }

            if (dropdownVisible && listRect.Contains(mousePoint))
            {
                var start = Math.Clamp(_projectDropdownScroll, 0, Math.Max(0, _editorProjectPaths.Count - 8));
                var visible = Math.Min(8, _editorProjectPaths.Count - start);
                for (var i = 0; i < visible; i++)
                {
                    var itemRect = new Rectangle(listRect.X + 1, listRect.Y + 1 + i * 12, listRect.Width - 2, 12);
                    if (!itemRect.Contains(mousePoint))
                    {
                        continue;
                    }

                    OpenEditorProject(_editorProjectPaths[start + i]);
                    _projectDropdownOpen = false;
                    _projectNameEditing = false;
                    _projectNameDeleteRepeatTimer = 0f;
                    return true;
                }
            }
            else if (!panelRect.Contains(mousePoint))
            {
                _projectDropdownOpen = false;
                _projectNameEditing = false;
                _projectNameDeleteRepeatTimer = 0f;
            }
        }

        return overUi;
    }

    private static Vector2 ClampProjectPanelToViewport(Vector2 pos)
    {
        var clampedX = Math.Clamp(pos.X, 0f, VirtualWidth - 500f);
        var clampedY = Math.Clamp(pos.Y, 0f, VirtualHeight - 96f);
        return new Vector2(clampedX, clampedY);
    }

    private static Vector2 ClampProjectPanelToViewport(Vector2 pos, float panelHeight)
    {
        var clampedX = Math.Clamp(pos.X, 0f, VirtualWidth - 500f);
        var clampedY = Math.Clamp(pos.Y, 0f, VirtualHeight - panelHeight);
        return new Vector2(clampedX, clampedY);
    }

    private static void DrawUiButton(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, Rectangle rect, string label)
    {
        render.DrawRect(spriteBatch, rect, new Color(30, 36, 50, 230));
        render.DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(170, 185, 215, 220));
        render.DrawRect(spriteBatch, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(170, 185, 215, 220));
        text.DrawString(spriteBatch, label, new Vector2(rect.X + 6f, rect.Y + 4f), Color.White, 1.1f);
    }

    private void DrawEditorProjectUi(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        RefreshEditorProjectPaths();

        var panelHeight = _projectUiCollapsed ? 22 : 96;
        var panelRect = new Rectangle((int)_projectUiPos.X, (int)_projectUiPos.Y, 500, panelHeight);
        var headerRect = new Rectangle(panelRect.X, panelRect.Y, panelRect.Width, 20);
        var toggleRect = new Rectangle(headerRect.Right - 18, headerRect.Y + 2, 14, 14);
        var labelWidth = 108;
        var labelX = panelRect.X + 8;
        var fieldX = labelX + labelWidth;
        var fieldWidth = 188;
        var nameRect = new Rectangle(fieldX, panelRect.Y + 30, fieldWidth, 20);
        var dropdownRect = new Rectangle(fieldX, panelRect.Y + 54, fieldWidth, 20);
        var newRect = new Rectangle(panelRect.X + 314, panelRect.Y + 30, 74, 20);
        var saveRect = new Rectangle(panelRect.X + 314, panelRect.Y + 54, 74, 20);
        var deleteRect = new Rectangle(panelRect.X + 392, panelRect.Y + 30, 96, 44);
        var listRect = new Rectangle(dropdownRect.X, dropdownRect.Bottom + 2, dropdownRect.Width, 8 * 12 + 4);

        render.DrawRect(spriteBatch, panelRect, new Color(10, 12, 18, 180));
        render.DrawRect(spriteBatch, headerRect, new Color(14, 18, 26, 210));
        text.DrawString(spriteBatch, "Projects", new Vector2(headerRect.X + 8f, headerRect.Y + 4f), new Color(225, 235, 245), 1.15f);
        render.DrawRect(spriteBatch, toggleRect, new Color(28, 34, 46, 235));
        text.DrawString(spriteBatch, _projectUiCollapsed ? "+" : "-", new Vector2(toggleRect.X + 4f, toggleRect.Y + 2f), Color.White, 1.2f);

        if (_projectUiCollapsed)
        {
            return;
        }

        render.DrawRect(spriteBatch, nameRect, _projectNameEditing ? new Color(55, 88, 130, 235) : new Color(30, 36, 50, 230));
        render.DrawRect(spriteBatch, new Rectangle(nameRect.X, nameRect.Y, nameRect.Width, 1), new Color(170, 185, 215, 220));
        render.DrawRect(spriteBatch, new Rectangle(nameRect.X, nameRect.Bottom - 1, nameRect.Width, 1), new Color(170, 185, 215, 220));

        render.DrawRect(spriteBatch, dropdownRect, new Color(30, 36, 50, 230));
        render.DrawRect(spriteBatch, new Rectangle(dropdownRect.X, dropdownRect.Y, dropdownRect.Width, 1), new Color(170, 185, 215, 220));
        render.DrawRect(spriteBatch, new Rectangle(dropdownRect.X, dropdownRect.Bottom - 1, dropdownRect.Width, 1), new Color(170, 185, 215, 220));
        DrawUiButton(spriteBatch, render, text, newRect, "NEW");
        DrawUiButton(spriteBatch, render, text, saveRect, "SAVE");
        DrawUiButton(spriteBatch, render, text, deleteRect, "DELETE");

        text.DrawString(spriteBatch, "Project Name", new Vector2(labelX, nameRect.Y + 4f), new Color(190, 205, 225), 0.95f);
        var nameInputText = string.IsNullOrWhiteSpace(_projectNameInput) ? "(type name)" : _projectNameInput;
        text.DrawString(spriteBatch, nameInputText, new Vector2(nameRect.X + 8f, nameRect.Y + 4f), _projectNameEditing ? new Color(240, 250, 255) : new Color(205, 215, 230), 1.05f);

        text.DrawString(spriteBatch, "Project Selector", new Vector2(labelX, dropdownRect.Y + 4f), new Color(190, 205, 225), 0.95f);
        var name = string.IsNullOrWhiteSpace(_currentEditorProjectPath) ? "(none)" : Path.GetFileName(_currentEditorProjectPath);
        text.DrawString(spriteBatch, name, new Vector2(dropdownRect.X + 8f, dropdownRect.Y + 4f), new Color(235, 240, 250), 1.05f);
        text.DrawString(spriteBatch, _projectDropdownOpen ? "v" : ">", new Vector2(dropdownRect.Right - 14f, dropdownRect.Y + 4f), Color.White, 1.2f);

        if (!_projectDropdownOpen || _editorProjectPaths.Count == 0)
        {
            return;
        }

        render.DrawRect(spriteBatch, listRect, new Color(16, 20, 28, 235));
        var start = Math.Clamp(_projectDropdownScroll, 0, Math.Max(0, _editorProjectPaths.Count - 8));
        var visible = Math.Min(8, _editorProjectPaths.Count - start);
        for (var i = 0; i < visible; i++)
        {
            var path = _editorProjectPaths[start + i];
            var itemRect = new Rectangle(listRect.X + 1, listRect.Y + 1 + i * 12, listRect.Width - 2, 12);
            var selected = string.Equals(path, _currentEditorProjectPath, StringComparison.OrdinalIgnoreCase);
            if (selected)
            {
                render.DrawRect(spriteBatch, itemRect, new Color(55, 88, 130, 210));
            }

            text.DrawString(spriteBatch, Path.GetFileName(path), new Vector2(itemRect.X + 4f, itemRect.Y + 1f), Color.White, 1.0f);
        }
    }

    private void HandleProjectNameTyping(float dt)
    {
        if (!_projectNameEditing)
        {
            return;
        }

        if (_input.IsKeyPressed(Keys.Enter) || _input.IsKeyPressed(Keys.Escape))
        {
            _projectNameEditing = false;
            _projectNameDeleteRepeatTimer = 0f;
            return;
        }

        if (_input.IsKeyPressed(Keys.Back) || _input.IsKeyPressed(Keys.Delete))
        {
            if (_projectNameInput.Length > 0)
            {
                _projectNameInput = _projectNameInput[..^1];
            }
            _projectNameDeleteRepeatTimer = ProjectNameDeleteInitialDelay;
        }
        else if (_input.IsKeyDown(Keys.Back) || _input.IsKeyDown(Keys.Delete))
        {
            _projectNameDeleteRepeatTimer -= dt;
            while (_projectNameDeleteRepeatTimer <= 0f)
            {
                if (_projectNameInput.Length > 0)
                {
                    _projectNameInput = _projectNameInput[..^1];
                }
                _projectNameDeleteRepeatTimer += ProjectNameDeleteRepeatInterval;
            }
        }
        else
        {
            _projectNameDeleteRepeatTimer = 0f;
        }

        var shift = _input.IsKeyDown(Keys.LeftShift) || _input.IsKeyDown(Keys.RightShift);
        for (var i = 0; i < 26; i++)
        {
            var key = Keys.A + i;
            if (!_input.IsKeyPressed(key))
            {
                continue;
            }

            var c = (char)('a' + i);
            if (shift)
            {
                c = char.ToUpperInvariant(c);
            }
            AppendProjectNameChar(c);
        }

        for (var i = 0; i <= 9; i++)
        {
            var key = Keys.D0 + i;
            if (_input.IsKeyPressed(key))
            {
                AppendProjectNameChar((char)('0' + i));
            }

            var numKey = Keys.NumPad0 + i;
            if (_input.IsKeyPressed(numKey))
            {
                AppendProjectNameChar((char)('0' + i));
            }
        }

        if (_input.IsKeyPressed(Keys.Space))
        {
            AppendProjectNameChar('_');
        }
        if (_input.IsKeyPressed(Keys.OemMinus) || _input.IsKeyPressed(Keys.Subtract))
        {
            AppendProjectNameChar('-');
        }
        if (_input.IsKeyPressed(Keys.OemPeriod) || _input.IsKeyPressed(Keys.Decimal))
        {
            AppendProjectNameChar('.');
        }
    }

    private void AppendProjectNameChar(char c)
    {
        if (_projectNameInput.Length >= 40)
        {
            return;
        }
        _projectNameInput += c;
    }

    private void TriggerSaveToast()
    {
        _saveToastTimer = SaveToastDurationSec;
    }

    private void DrawSaveToast(SpriteBatch spriteBatch, BitmapTextRenderer text)
    {
        if (_saveToastTimer <= 0f)
        {
            return;
        }

        var t = Math.Clamp(_saveToastTimer / SaveToastDurationSec, 0f, 1f);
        var alpha = t * t;
        var main = new Color(160, 255, 200) * alpha;
        var shadow = new Color(10, 20, 10) * (alpha * 0.85f);
        var scale = 6.2f;
        var size = text.MeasureString("SAVED", scale);
        var pos = new Vector2((VirtualWidth - size.X) * 0.5f, (VirtualHeight - size.Y) * 0.5f);
        text.DrawString(spriteBatch, "SAVED", pos + new Vector2(5f, 5f), shadow, 6.2f);
        text.DrawString(spriteBatch, "SAVED", pos, main, scale);
    }

    private void OpenAdjacentEditorProject(int delta)
    {
        if (_editorController is null)
        {
            return;
        }

        RefreshEditorProjectPaths();
        if (_editorProjectPaths.Count == 0)
        {
            _statusMessage = "NO PROJECTS FOUND";
            return;
        }

        var currentIndex = _editorProjectPaths.FindIndex(p => string.Equals(p, _currentEditorProjectPath, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = _editorProjectPaths.Count - 1;
        }

        var nextIndex = (currentIndex + delta + _editorProjectPaths.Count) % _editorProjectPaths.Count;
        OpenEditorProject(_editorProjectPaths[nextIndex]);
    }

    private void OpenEditorProject(string projectPath)
    {
        if (_editorController is null)
        {
            return;
        }

        _currentEditorProjectPath = projectPath;
        _editorController.LoadOrCreate(_currentEditorProjectPath, _beatmap.AudioPath);
        _projectNameInput = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(_currentEditorProjectPath));
        ResetEditorPreviewState();
        _statusMessage = $"OPEN PROJECT: {Path.GetFileName(_currentEditorProjectPath)}";
    }

    private void DeleteCurrentEditorProject()
    {
        if (_editorController is null || string.IsNullOrWhiteSpace(_currentEditorProjectPath))
        {
            return;
        }

        try
        {
            _editorController.Save();
            SoftDeleteProjectFile(_currentEditorProjectPath);

            var publishedPath = Path.Combine(
                Path.GetDirectoryName(_currentEditorProjectPath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(_currentEditorProjectPath) + ".published.map.json");
            SoftDeleteProjectFile(publishedPath);

            _currentEditorProjectPath = ResolveMostRecentEditorProjectPath();
            _editorController.LoadOrCreate(_currentEditorProjectPath, _beatmap.AudioPath);
            ResetEditorPreviewState();
            _statusMessage = $"DELETED PROJECT, OPENED: {Path.GetFileName(_currentEditorProjectPath)}";
        }
        catch
        {
            _statusMessage = "FAILED TO DELETE PROJECT";
        }
    }

    private void ResetEditorPreviewState()
    {
        _editorPreviewRevision = -1;
        _editorPreviewLastMs = 0;
        _editorPendingPatternSpawnTimer = 0f;
        _editorPendingPatternId = string.Empty;
    }

    private string ResolveMostRecentEditorProjectPath()
    {
        RefreshEditorProjectPaths();
        if (_editorProjectPaths.Count > 0)
        {
            return _editorProjectPaths[^1];
        }

        return CreateNewEditorProjectPath();
    }

    private void RefreshEditorProjectPaths()
    {
        _editorProjectPaths.Clear();
        var dir = GetProjectsDirectory();
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        CleanupDeletedProjects();

        var files = Directory
            .GetFiles(dir, "*.editor.json")
            .OrderBy(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        _editorProjectPaths.AddRange(files);
    }

    private string CreateNewEditorProjectPath(string? preferredName = null)
    {
        var dir = GetProjectsDirectory();
        Directory.CreateDirectory(dir);
        var baseName = SanitizeProjectName(preferredName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"project_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        }

        var basePath = Path.Combine(dir, $"{baseName}.editor.json");
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        var i = 1;
        while (true)
        {
            var candidate = Path.Combine(dir, $"{baseName}_{i}.editor.json");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            i++;
        }
    }

    private string GetProjectsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Content", "Maps", "Projects");
    }

    private string GetDeletedProjectsDirectory()
    {
        return Path.Combine(GetProjectsDirectory(), "Deleted");
    }

    private static string SanitizeProjectName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Trim()
            .Where(c => !invalid.Contains(c))
            .Select(c => char.IsWhiteSpace(c) ? '_' : c)
            .ToArray();
        var cleaned = new string(chars);
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }

    private void SoftDeleteProjectFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var deletedDir = GetDeletedProjectsDirectory();
        Directory.CreateDirectory(deletedDir);

        var fileName = Path.GetFileName(path);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var destination = Path.Combine(deletedDir, $"{stamp}_{fileName}");
        File.Move(path, destination, overwrite: true);
    }

    private void CleanupDeletedProjects()
    {
        var deletedDir = GetDeletedProjectsDirectory();
        if (!Directory.Exists(deletedDir))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-DeletedProjectRetentionDays);
        foreach (var file in Directory.GetFiles(deletedDir))
        {
            var writeTime = File.GetLastWriteTimeUtc(file);
            if (writeTime < cutoff)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }
    }

    private void UpdateEditorPreview(float dt)
    {
        if (_editorController is null)
        {
            return;
        }

        var songMs = _songClock.CurrentTimeMs;
        var songDeltaMs = Math.Max(0, songMs - _editorPreviewLastMs);
        var songDt = songDeltaMs / 1000f;
        var timelineStartMs = 0;
        var timelineEndMs = Math.Max(songMs, songMs + 30000);
        _editorView?.GetTimelineViewRange(out timelineStartMs, out timelineEndMs);
        if (_editorPreviewRevision != _editorController.Revision || songMs < _editorPreviewLastMs)
        {
            var currentBulletEventCount = _editorController.CurrentLevel.Bullets.Count;
            if (currentBulletEventCount < _editorPreviewLastBulletEventCount)
            {
                _editorSuppressPendingPreviewUntilMs = songMs + 400;
                _editorPendingPatternSpawnTimer = 0f;
                _editorPendingPatternId = string.Empty;
            }

            _editorPreviewBullets.ClearRuntime();
            var previewMap = BuildPreviewBeatmapFromEditor(_editorController.CurrentLevel, out var previewSimStartMs, timelineStartMs, timelineEndMs, songMs);
            _editorPreviewNotes.Reset(previewMap);
            _editorPreviewBullets.Reset(previewMap);
            SimulateEditorPreviewToTime(songMs, previewSimStartMs);
            _editorPreviewRevision = _editorController.Revision;
            _editorPreviewLastBulletEventCount = currentBulletEventCount;
            songDt = 0f;
        }

        _editorPreviewNotes.Update(songDt, songMs, _cursor.Position, false, false);
        _editorPreviewBullets.Update(songDt, songMs, _cursor.Position);
        while (_editorPreviewNotes.TryDequeueAutoMiss(out _))
        {
        }

        if (songMs < _editorSuppressPendingPreviewUntilMs)
        {
            _editorPendingPatternSpawnTimer = 0f;
            _editorPendingPatternId = string.Empty;
        }
        else if (_editorView is not null && _editorView.TryGetPendingBulletPreview(out var pending))
        {
            var pendingKey = $"{pending.Pattern}|{pending.Shape}|{pending.X:0.000}|{pending.Y:0.000}";
            if (!string.Equals(_editorPendingPatternId, pendingKey, StringComparison.OrdinalIgnoreCase))
            {
                _editorPendingPatternId = pendingKey;
                _editorPendingPatternSpawnTimer = 0f;
            }

            if (_editorPreviewBullets.ActiveBulletCount > 0)
            {
                // Hold spawn while any preview entities are still active.
                _editorPendingPatternSpawnTimer = 0.2f;
            }
            else
            {
                _editorPendingPatternSpawnTimer -= songDt;
                if (_editorPendingPatternSpawnTimer <= 0f)
                {
                    var evt = BuildPendingPreviewBulletEvent(pending, songMs);
                    _editorPreviewBullets.SpawnImmediate(evt, songMs, _cursor.Position);
                    _editorPendingPatternSpawnTimer = 0.2f;
                }
            }
        }
        else
        {
            _editorPendingPatternSpawnTimer = 0f;
            _editorPendingPatternId = string.Empty;
        }

        _editorPreviewLastMs = songMs;
    }

    private void SimulateEditorPreviewToTime(int songMs, int timelineStartMs)
    {
        var simStartMs = Math.Max(0, Math.Min(songMs, timelineStartMs));
        if (songMs <= simStartMs)
        {
            // Ensure events authored exactly at current time still spawn while paused.
            _editorPreviewNotes.Update(0f, songMs, _cursor.Position, false, false);
            _editorPreviewBullets.Update(0f, songMs, _cursor.Position);
            while (_editorPreviewNotes.TryDequeueAutoMiss(out _))
            {
            }
            return;
        }

        var simMs = simStartMs;
        while (simMs < songMs)
        {
            var nextMs = Math.Min(songMs, simMs + 16);
            var simDt = (nextMs - simMs) / 1000f;
            _editorPreviewNotes.Update(simDt, nextMs, _cursor.Position, false, false);
            _editorPreviewBullets.Update(simDt, nextMs, _cursor.Position);
            while (_editorPreviewNotes.TryDequeueAutoMiss(out _))
            {
            }

            simMs = nextMs;
        }
    }

    private static BulletEvent BuildPendingPreviewBulletEvent(PendingBulletPreview pending, int songMs)
    {
        var pattern = string.IsNullOrWhiteSpace(pending.Pattern) ? "radial" : pending.Pattern;
        var shape = string.IsNullOrWhiteSpace(pending.Shape) ? "orb" : pending.Shape;
        var evt = new BulletEvent
        {
            TimeMs = songMs,
            Pattern = pattern,
            X = Math.Clamp(pending.X, 0f, 1f),
            Y = Math.Clamp(pending.Y, 0f, 1f),
            DirectionDeg = 90f,
            Count = 5,
            Speed = 240f,
            BulletType = shape,
            BulletSize = 8f
        };

        if (pattern.Contains("fan") || pattern.Contains("arc"))
        {
            evt.Count = 5;
            evt.SpreadDeg = 70f;
            evt.Speed = 260f;
        }
        else if (pattern.Contains("spiral") || pattern.Contains("helix") || pattern.Contains("vortex"))
        {
            evt.Count = 7;
            evt.Speed = 230f;
            evt.IntervalMs = 50;
            evt.AngleStepDeg = 12f;
        }
        else if (pattern.Contains("wall"))
        {
            evt.Count = 8;
            evt.Speed = 190f;
        }

        return evt;
    }

    private Beatmap BuildPreviewBeatmapFromEditor(LevelDocument level, out int previewSimStartMs, int? timelineStartMs = null, int? timelineEndMs = null, int? currentSongMs = null)
    {
        var useTimelineFilter = timelineStartMs.HasValue && timelineEndMs.HasValue;
        var rangeStart = useTimelineFilter ? Math.Max(0, timelineStartMs!.Value) : 0;
        var rangeEnd = useTimelineFilter ? Math.Max(rangeStart, timelineEndMs!.Value) : int.MaxValue;
        var simFloorMs = currentSongMs.HasValue
            ? Math.Max(0, currentSongMs.Value - EditorPreviewMaxBackSimMs)
            : 0;
        const int noteLookbackMs = 5000;
        var includeStart = 0;
        var minRelevantTimeMs = int.MaxValue;

        var beatmap = new Beatmap
        {
            AudioPath = level.AudioPath,
            ApproachMs = _beatmap.ApproachMs,
            CircleRadius = _beatmap.CircleRadius,
            CursorHitboxRadius = _beatmap.CursorHitboxRadius,
            BulletOutlineThickness = _beatmap.BulletOutlineThickness,
            CenterWarningRadius = _beatmap.CenterWarningRadius,
            CenterWarningLeadMs = _beatmap.CenterWarningLeadMs,
            CenterWarningAlpha = _beatmap.CenterWarningAlpha,
            Notes = new List<NoteEvent>(),
            DragNotes = new List<DragNoteEvent>(),
            Bullets = new List<BulletEvent>()
        };

        foreach (var note in level.Notes.OrderBy(n => n.TimeMs).ThenBy(n => n.EventId))
        {
            var noteAppearMs = note.TimeMs - beatmap.ApproachMs;
            var noteExpireMs = note.TimeMs + 200;
            if (useTimelineFilter && (noteExpireMs < (rangeStart - noteLookbackMs) || noteAppearMs > rangeEnd))
            {
                continue;
            }

            minRelevantTimeMs = Math.Min(minRelevantTimeMs, noteAppearMs);

            beatmap.Notes.Add(new NoteEvent
            {
                TimeMs = Math.Max(0, note.TimeMs),
                X = note.X ?? (0.1f + (Math.Clamp(note.Lane, 0, 7) / 7f) * 0.8f),
                Y = note.Y ?? 0.56f,
                Lane = note.Lane
            });
        }

        foreach (var bullet in level.Bullets.OrderBy(b => b.TimeMs).ThenBy(b => b.EventId))
        {
            var evt = new BulletEvent
            {
                TimeMs = Math.Max(0, bullet.TimeMs),
                Pattern = string.IsNullOrWhiteSpace(bullet.PatternId) ? "radial" : bullet.PatternId,
                X = bullet.X ?? 0.5f,
                Y = bullet.Y ?? 0.5f
            };

            if (bullet.Parameters.TryGetValue("count", out var count)) evt.Count = Math.Max(1, (int)Math.Round(count));
            if (bullet.Parameters.TryGetValue("speed", out var speed)) evt.Speed = (float)Math.Max(1, speed);
            if (bullet.Parameters.TryGetValue("intervalMs", out var interval)) evt.IntervalMs = Math.Max(10, (int)Math.Round(interval));
            if (bullet.Parameters.TryGetValue("spreadDeg", out var spread)) evt.SpreadDeg = (float)spread;
            if (bullet.Parameters.TryGetValue("angleStepDeg", out var step)) evt.AngleStepDeg = (float)step;
            if (bullet.Parameters.TryGetValue("directionDeg", out var direction)) evt.DirectionDeg = (float)direction;
            if (bullet.Parameters.TryGetValue("movementIntensity", out var movement)) evt.MovementIntensity = (float)movement;
            if (bullet.Parameters.TryGetValue("radius", out var radius)) evt.Radius = (float)radius;
            if (bullet.Parameters.TryGetValue("bulletSize", out var bsize)) evt.BulletSize = (float)bsize;
            if (bullet.Parameters.TryGetValue("outlineThickness", out var outT)) evt.OutlineThickness = (float)outT;
            if (bullet.Parameters.TryGetValue("glowIntensity", out var glowI)) evt.GlowIntensity = (float)glowI;
            if (bullet.Parameters.TryGetValue("telegraphMs", out var telegraphMs)) evt.TelegraphMs = Math.Max(50, (int)Math.Round(telegraphMs));
            if (bullet.Parameters.TryGetValue("laserDurationMs", out var laserDurationMs)) evt.LaserDurationMs = Math.Max(50, (int)Math.Round(laserDurationMs));
            if (bullet.Parameters.TryGetValue("laserWidth", out var laserWidth)) evt.LaserWidth = (float)laserWidth;
            if (bullet.Parameters.TryGetValue("laserLength", out var laserLength)) evt.LaserLength = (float)laserLength;
            if (bullet.Parameters.TryGetValue("shapeId", out var shapeId)) evt.BulletType = PreviewShapeIdToType((int)Math.Round(shapeId));
            if (bullet.Parameters.TryGetValue("motionPatternId", out var motionPatternId)) evt.MotionPattern = PreviewMotionPatternIdToName((int)Math.Round(motionPatternId));
            if (TryPreviewColorFromParams(bullet.Parameters, "primary", out var primaryHex)) evt.Color = primaryHex;
            if (TryPreviewColorFromParams(bullet.Parameters, "outline", out var outlineHex)) evt.OutlineColor = outlineHex;
            if (TryPreviewColorFromParams(bullet.Parameters, "glow", out var glowHex)) evt.GlowColor = glowHex;

            if (useTimelineFilter)
            {
                var bulletExpireMs = EstimateBulletExpireMs(evt);
                if (evt.TimeMs > rangeEnd || bulletExpireMs < rangeStart)
                {
                    continue;
                }

                // Cap editor back-simulation range. If a bullet started before the floor but should still be
                // visible after the floor, spawn it at the floor as an approximation to avoid full-history sim.
                if (evt.TimeMs < simFloorMs && bulletExpireMs >= simFloorMs)
                {
                    evt.TimeMs = simFloorMs;
                }
            }

            minRelevantTimeMs = Math.Min(minRelevantTimeMs, evt.TimeMs);
            beatmap.Bullets.Add(evt);
        }

        previewSimStartMs = minRelevantTimeMs == int.MaxValue
            ? Math.Max(includeStart, simFloorMs)
            : Math.Max(simFloorMs, Math.Max(0, minRelevantTimeMs));
        return beatmap;
    }

    private static int EstimateBulletExpireMs(BulletEvent evt)
    {
        var x = Math.Clamp(evt.X ?? 0.5f, 0f, 1f) * VirtualWidth;
        var y = Math.Clamp(evt.Y ?? 0.5f, 0f, 1f) * VirtualHeight;
        var d0 = Vector2.Distance(new Vector2(x, y), Vector2.Zero);
        var d1 = Vector2.Distance(new Vector2(x, y), new Vector2(VirtualWidth, 0f));
        var d2 = Vector2.Distance(new Vector2(x, y), new Vector2(0f, VirtualHeight));
        var d3 = Vector2.Distance(new Vector2(x, y), new Vector2(VirtualWidth, VirtualHeight));
        var farthest = MathF.Max(MathF.Max(d0, d1), MathF.Max(d2, d3)) + 360f;
        var speed = Math.Max(1f, evt.Speed);
        var count = Math.Max(1, evt.Count);
        var staggerMs = Math.Max(0, evt.IntervalMs) * Math.Max(0, count - 1);
        var moveIntensity = MathF.Max(0f, evt.MovementIntensity ?? 1f);
        var motionSlackMs = (int)MathF.Ceiling(((140f + moveIntensity * 220f) / speed) * 1000f);

        var p = evt.Pattern.Trim().ToLowerInvariant();
        var patternExtraMs = 0;
        if (BulletSystem.StaticPatterns.Contains(p))
        {
            patternExtraMs += (int)MathF.Round(14000f + moveIntensity * 3000f);
        }
        else if (BulletSystem.MovingPatterns.Contains(p))
        {
            patternExtraMs += (int)MathF.Round(5000f + moveIntensity * 4000f);
        }
        else
        {
            if (p.Contains("spiral") || p.Contains("helix") || p.Contains("vortex")) patternExtraMs += 3500;
            if (p.Contains("fan") || p.Contains("arc")) patternExtraMs += 1600;
            if (p.Contains("wall")) patternExtraMs += 1200;
            if (p.Contains("aimed")) patternExtraMs += 900;
        }

        var travelMs = (int)MathF.Ceiling((farthest / speed) * 1000f) + staggerMs + motionSlackMs + patternExtraMs + 900;
        travelMs = Math.Clamp(travelMs, 400, 600000);
        return evt.TimeMs + travelMs;
    }

    private static string PreviewShapeIdToType(int shapeId)
    {
        string[] shapes =
        {
            "orb", "circle", "rice", "kunai", "butterfly", "star", "arrowhead",
            "droplet", "crystal", "diamond", "petal", "flame_shard", "cross_shard",
            "crescent", "heart_shard", "hex_shard"
        };
        return (shapeId >= 0 && shapeId < shapes.Length) ? shapes[shapeId] : "orb";
    }

    private static string PreviewMotionPatternIdToName(int motionPatternId)
    {
        var patterns = BulletSystem.MovingPatterns;
        if (motionPatternId >= 0 && motionPatternId < patterns.Length)
        {
            return patterns[motionPatternId];
        }

        return patterns.Length > 0 ? patterns[0] : "uniform_outward_drift";
    }

    private static bool TryPreviewColorFromParams(IReadOnlyDictionary<string, double> parameters, string prefix, out string hex)
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

    private void UpdateMainMenu()
    {
        if (_songClock.IsRunning)
        {
            _songClock.Stop();
        }

        if (_viewport is not null)
        {
            var mouseVirtual = _viewport.ScreenToVirtual(_input.MousePosition);
            for (var i = 0; i < _mainMenuOptions.Length; i++)
            {
                if (!GetMainMenuItemRect(i).Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
                {
                    continue;
                }

                _mainMenuIndex = i;
                if (_input.LeftPressed)
                {
                    ActivateMainMenuSelection(i);
                    return;
                }
            }
        }

        if (_input.IsKeyPressed(Keys.Up))
        {
            _mainMenuIndex = (_mainMenuIndex + _mainMenuOptions.Length - 1) % _mainMenuOptions.Length;
        }

        if (_input.IsKeyPressed(Keys.Down))
        {
            _mainMenuIndex = (_mainMenuIndex + 1) % _mainMenuOptions.Length;
        }

        if (_input.IsKeyPressed(Keys.Enter))
        {
            ActivateMainMenuSelection(_mainMenuIndex);
        }
    }

    private void ActivateMainMenuSelection(int index)
    {
        switch (index)
        {
            case 0:
                _playMapOverridePath = null;
                _playAudioOverridePath = null;
                _songSelectFromLevelPlayer = false;
                _appMode = AppMode.Gameplay;
                LoadPlayBeatmapAndRestart(_playMapOverridePath, _playAudioOverridePath);
                break;
            case 1:
                EnsureEditorInitialized();
                _songClock.Stop();
                _songClock.PlayFromStart();
                _appMode = AppMode.Editor;
                _statusMessage = "EDITOR MODE";
                break;
            case 2:
                _appMode = AppMode.Settings;
                _statusMessage = "SETTINGS";
                break;
            case 3:
                Exit();
                break;
        }
    }

    private static Rectangle GetMainMenuItemRect(int index)
    {
        var y = 300f + index * 46f;
        return new Rectangle(540, (int)y - 4, 280, 40);
    }

    private void UpdateSettingsMenu()
    {
        if (_input.IsKeyPressed(Keys.Up))
        {
            _difficultyIndex = (_difficultyIndex + _difficultyOptions.Length - 1) % _difficultyOptions.Length;
        }

        if (_input.IsKeyPressed(Keys.Down))
        {
            _difficultyIndex = (_difficultyIndex + 1) % _difficultyOptions.Length;
        }

        if (_input.IsKeyPressed(Keys.Enter))
        {
            var selected = _difficultyOptions[_difficultyIndex];
            _statusMessage = $"DIFFICULTY {selected.Label} ({selected.ApproachMs}ms)";
            _appMode = AppMode.MainMenu;
        }

        if (_input.IsKeyPressed(Keys.Back))
        {
            _appMode = AppMode.MainMenu;
        }
    }

    private void PrepareLevelSelectMenu()
    {
        _levelSelectOptions.Clear();
        _levelSelectOptions.Add("__AUTO_RECENT_OR_ROUTE__");
        _levelSelectOptions.AddRange(DiscoverPlayableLevels());
        _levelSelectIndex = 0;
        _playMapOverridePath = null;
        _statusMessage = "SELECT LEVEL";
    }

    private List<string> DiscoverPlayableLevels()
    {
        var results = new List<string>();
        try
        {
            var dir = GetProjectsDirectory();
            if (!Directory.Exists(dir))
            {
                return results;
            }

            var files = Directory
                .GetFiles(dir, "*.editor.json")
                .OrderBy(path => File.GetLastWriteTimeUtc(path));
            results.AddRange(files);
        }
        catch
        {
        }

        return results;
    }

    private void UpdateLevelSelectMenu()
    {
        if (_songClock.IsRunning)
        {
            _songClock.Stop();
        }

        if (_levelSelectOptions.Count == 0)
        {
            PrepareLevelSelectMenu();
        }

        if (_input.IsKeyPressed(Keys.Up))
        {
            _levelSelectIndex = (_levelSelectIndex + _levelSelectOptions.Count - 1) % _levelSelectOptions.Count;
        }

        if (_input.IsKeyPressed(Keys.Down))
        {
            _levelSelectIndex = (_levelSelectIndex + 1) % _levelSelectOptions.Count;
        }

        if (_input.IsKeyPressed(Keys.Enter))
        {
            var selected = _levelSelectOptions[Math.Clamp(_levelSelectIndex, 0, _levelSelectOptions.Count - 1)];
            _playMapOverridePath = selected == "__AUTO_RECENT_OR_ROUTE__" ? null : selected;
            _songSelectFromLevelPlayer = true;
            PrepareSongSelectMenu();
            _appMode = AppMode.SongSelect;
        }

        if (_input.IsKeyPressed(Keys.Back))
        {
            _appMode = AppMode.MainMenu;
            _statusMessage = "MAIN MENU";
        }
    }

    private void PrepareSongSelectMenu()
    {
        _songSelectOptions.Clear();
        _songSelectOptions.Add("__MAP_DEFAULT__");
        _songSelectOptions.AddRange(DiscoverPlayableSongs());
        _songSelectIndex = 0;
        _playAudioOverridePath = null;
        _statusMessage = "SELECT SONG";
    }

    private List<string> DiscoverPlayableSongs()
    {
        var results = new List<string>();
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Content", "Audio");
            if (!Directory.Exists(dir))
            {
                return results;
            }

            var files = Directory
                .GetFiles(dir)
                .Where(f =>
                    f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                results.Add($"Content/Audio/{Path.GetFileName(file)}");
            }
        }
        catch
        {
        }

        return results;
    }

    private void UpdateSongSelectMenu()
    {
        if (_songClock.IsRunning)
        {
            _songClock.Stop();
        }

        if (_songSelectOptions.Count == 0)
        {
            PrepareSongSelectMenu();
        }

        if (_input.IsKeyPressed(Keys.Up))
        {
            _songSelectIndex = (_songSelectIndex + _songSelectOptions.Count - 1) % _songSelectOptions.Count;
        }

        if (_input.IsKeyPressed(Keys.Down))
        {
            _songSelectIndex = (_songSelectIndex + 1) % _songSelectOptions.Count;
        }

        if (_input.IsKeyPressed(Keys.Enter))
        {
            var selected = _songSelectOptions[Math.Clamp(_songSelectIndex, 0, _songSelectOptions.Count - 1)];
            _playAudioOverridePath = selected == "__MAP_DEFAULT__" ? null : selected;
            _appMode = AppMode.Gameplay;
            LoadPlayBeatmapAndRestart(_playMapOverridePath, _playAudioOverridePath);
        }

        if (_input.IsKeyPressed(Keys.Back))
        {
            if (_songSelectFromLevelPlayer)
            {
                _appMode = AppMode.LevelSelect;
                _statusMessage = "SELECT LEVEL";
            }
            else
            {
                _appMode = AppMode.MainMenu;
                _statusMessage = "MAIN MENU";
            }
        }
    }

    private void DrawMainMenu(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        render.DrawRect(spriteBatch, new Rectangle(0, 0, VirtualWidth, VirtualHeight), new Color(0, 0, 0, 170));
        text.DrawString(spriteBatch, "RHYTHM HELL PROTOTYPE", new Vector2(380f, 150f), Color.White, 3f);
        text.DrawString(spriteBatch, "MAIN MENU", new Vector2(560f, 205f), new Color(200, 215, 240), 2f);

        var y = 300f;
        for (var i = 0; i < _mainMenuOptions.Length; i++)
        {
            var selected = i == _mainMenuIndex;
            var c = selected ? new Color(120, 240, 255) : Color.White;
            text.DrawString(spriteBatch, _mainMenuOptions[i], new Vector2(550f, y), c, 2.4f);
            y += 46f;
        }

        var diff = _difficultyOptions[_difficultyIndex];
        text.DrawString(spriteBatch, $"Difficulty: {diff.Label} ({diff.ApproachMs}ms)", new Vector2(430f, 600f), new Color(220, 235, 255), 1.8f);
        text.DrawString(spriteBatch, "UP/DOWN: SELECT  ENTER: CONFIRM  ESC: EXIT", new Vector2(400f, 640f), new Color(205, 215, 230), 1.8f);
    }

    private void DrawSettingsMenu(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        render.DrawRect(spriteBatch, new Rectangle(0, 0, VirtualWidth, VirtualHeight), new Color(0, 0, 0, 185));
        text.DrawString(spriteBatch, "SETTINGS - DIFFICULTY", new Vector2(430f, 150f), Color.White, 2.6f);
        text.DrawString(spriteBatch, "Affects note approach speed", new Vector2(470f, 190f), new Color(190, 210, 235), 1.8f);

        var y = 280f;
        for (var i = 0; i < _difficultyOptions.Length; i++)
        {
            var selected = i == _difficultyIndex;
            var c = selected ? new Color(120, 240, 255) : Color.White;
            if (selected)
            {
                text.DrawString(spriteBatch, ">", new Vector2(470f, y), c, 2f);
            }

            var (label, approachMs) = _difficultyOptions[i];
            text.DrawString(spriteBatch, $"{label} ({approachMs} ms)", new Vector2(510f, y), c, 2.3f);
            y += 46f;
        }

        text.DrawString(spriteBatch, "AR 5 = 1.2s  |  AR 8 = 0.75s  |  AR 10 = 0.45s", new Vector2(350f, 460f), new Color(220, 230, 245), 1.7f);
        text.DrawString(spriteBatch, "UP/DOWN: CHANGE  ENTER: APPLY  BACKSPACE: BACK", new Vector2(340f, 640f), new Color(205, 215, 230), 1.8f);
    }

    private void DrawSongSelectMenu(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        render.DrawRect(spriteBatch, new Rectangle(0, 0, VirtualWidth, VirtualHeight), new Color(0, 0, 0, 180));
        text.DrawString(spriteBatch, "SONG SELECT", new Vector2(520f, 120f), Color.White, 2.8f);
        text.DrawString(spriteBatch, "Choose track before gameplay", new Vector2(430f, 160f), new Color(190, 210, 235), 1.8f);

        var visibleCount = 12;
        var start = Math.Max(0, _songSelectIndex - (visibleCount / 2));
        if (_songSelectOptions.Count > visibleCount)
        {
            start = Math.Clamp(start, 0, _songSelectOptions.Count - visibleCount);
        }

        var y = 240f;
        var end = Math.Min(_songSelectOptions.Count, start + visibleCount);
        for (var i = start; i < end; i++)
        {
            var selected = i == _songSelectIndex;
            var c = selected ? new Color(120, 240, 255) : Color.White;
            var label = _songSelectOptions[i] == "__MAP_DEFAULT__"
                ? "Map Default"
                : Path.GetFileName(_songSelectOptions[i]);
            text.DrawString(spriteBatch, label, new Vector2(390f, y), c, 2.0f);
            y += 34f;
        }

        var backLabel = _songSelectFromLevelPlayer ? "LEVEL SELECT" : "MAIN MENU";
        text.DrawString(spriteBatch, $"UP/DOWN: SELECT  ENTER: PLAY  BACKSPACE: {backLabel}", new Vector2(250f, 660f), new Color(205, 215, 230), 1.7f);
    }

    private void DrawLevelSelectMenu(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        render.DrawRect(spriteBatch, new Rectangle(0, 0, VirtualWidth, VirtualHeight), new Color(0, 0, 0, 180));
        text.DrawString(spriteBatch, "LEVEL SELECT", new Vector2(500f, 120f), Color.White, 2.8f);
        text.DrawString(spriteBatch, "Choose project level from editor", new Vector2(400f, 160f), new Color(190, 210, 235), 1.8f);

        var visibleCount = 12;
        var start = Math.Max(0, _levelSelectIndex - (visibleCount / 2));
        if (_levelSelectOptions.Count > visibleCount)
        {
            start = Math.Clamp(start, 0, _levelSelectOptions.Count - visibleCount);
        }

        var y = 240f;
        var end = Math.Min(_levelSelectOptions.Count, start + visibleCount);
        for (var i = start; i < end; i++)
        {
            var selected = i == _levelSelectIndex;
            var c = selected ? new Color(120, 240, 255) : Color.White;
            var option = _levelSelectOptions[i];
            var label = option == "__AUTO_RECENT_OR_ROUTE__"
                ? "Auto: Most Recent Published / Route Map"
                : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(option));
            text.DrawString(spriteBatch, label, new Vector2(320f, y), c, 2.0f);
            y += 34f;
        }

        text.DrawString(spriteBatch, "UP/DOWN: SELECT  ENTER: NEXT (SONG)  BACKSPACE: MAIN MENU", new Vector2(225f, 660f), new Color(205, 215, 230), 1.7f);
    }

    private void LoadPlayBeatmapAndRestart(string? playMapOverridePath = null, string? playAudioOverridePath = null)
    {
        var editorPath = GetMostRecentEditorProjectMapPath();
        var publishedPath = GetMostRecentPublishedProjectMapPath();
        var fallbackPath = ResolveBeatmapPathFromRoutes();
        var selectedPath = !string.IsNullOrWhiteSpace(playMapOverridePath)
            ? playMapOverridePath!
            : !string.IsNullOrWhiteSpace(editorPath)
                ? editorPath
                : !string.IsNullOrWhiteSpace(publishedPath)
                ? publishedPath
                : fallbackPath;

        try
        {
            if (selectedPath.EndsWith(".editor.json", StringComparison.OrdinalIgnoreCase))
            {
                var level = _levelSerializer.LoadFromPath(selectedPath);
                _beatmap = BuildPreviewBeatmapFromEditor(level, out _, null, null);
            }
            else
            {
                _beatmap = _beatmapLoader.LoadFromPath(selectedPath);
            }

            _activeMapPath = selectedPath;
            _statusMessage = !string.IsNullOrWhiteSpace(playMapOverridePath)
                ? $"GAMEPLAY - LEVEL {Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(selectedPath))}"
                : !string.IsNullOrWhiteSpace(editorPath)
                    ? "GAMEPLAY - RECENT EDITOR MAP"
                : !string.IsNullOrWhiteSpace(publishedPath)
                    ? "GAMEPLAY - PUBLISHED MAP"
                    : "GAMEPLAY - ROUTE MAP";
        }
        catch (Exception ex) when (ex is BeatmapLoadException or LevelSerializationException)
        {
            _beatmap = CreateFallbackBeatmap();
            _statusMessage = ex.Message;
        }

        if (!string.IsNullOrWhiteSpace(playAudioOverridePath))
        {
            _beatmap.AudioPath = playAudioOverridePath;
            _statusMessage = $"GAMEPLAY - SONG {Path.GetFileName(playAudioOverridePath)}";
        }

        RestartCurrentMap();
    }

    private string? GetMostRecentEditorProjectMapPath()
    {
        var dir = GetProjectsDirectory();
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var editorFiles = Directory
            .GetFiles(dir, "*.editor.json")
            .OrderBy(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        if (editorFiles.Count == 0)
        {
            return null;
        }

        return editorFiles[^1];
    }

    private string? GetMostRecentPublishedProjectMapPath()
    {
        var dir = GetProjectsDirectory();
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var publishedFiles = Directory
            .GetFiles(dir, "*.published.map.json")
            .OrderBy(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        if (publishedFiles.Count == 0)
        {
            return null;
        }

        return publishedFiles[^1];
    }

    private void ApplyDifficultyApproachOverride()
    {
        var targetApproach = _difficultyOptions[Math.Clamp(_difficultyIndex, 0, _difficultyOptions.Length - 1)].ApproachMs;
        _beatmap.ApproachMs = targetApproach;
    }

    private static Beatmap CreateFallbackBeatmap()
    {
        return new Beatmap
        {
            AudioPath = null,
            ApproachMs = 900,
            CircleRadius = 42f,
            GlobalOffsetMs = 0,
            TargetFps = 120,
            KeyboardMoveSpeed = 260f,
            CursorHitboxRadius = 4f,
            NumberCycle = 4,
            ShowNumbers = true,
            BulletOutlineThickness = 2f,
            CenterWarningRadius = 120f,
            CenterWarningLeadMs = 350,
            CenterWarningAlpha = 0.35f,
            BackgroundTopColor = "#121A28",
            BackgroundBottomColor = "#0A0F18",
            BackgroundAccentColor = "#1E324F",
            BackgroundGridAlpha = 0.18f,
            AutoBalanceWaves = true,
            WaveSafetyMargin = 8f,
            MaxLives = 100f,
            BulletHitDamage = 22f,
            LifeGainStepScore = 350,
            LifeGainAmount = 4f,
            BackgroundImagePath = null,
            BackgroundImageAlpha = 1f,
            BackgroundImageMode = "cover",
            BackgroundOverlayPath = null,
            BackgroundOverlayAlpha = 0f,
            Notes = new List<NoteEvent>
            {
                new() { TimeMs = 1000, X = 0.45f, Y = 0.45f },
                new() { TimeMs = 1400, X = 0.57f, Y = 0.45f },
                new() { TimeMs = 1800, X = 0.65f, Y = 0.56f }
            },
            Bullets = new List<BulletEvent>
            {
                new() { TimeMs = 1200, Pattern = "radial", Count = 10, Speed = 240f, X = 0.5f, Y = 0.5f }
            },
            DragNotes = new List<DragNoteEvent>()
        };
    }

    private static Color ParseHexColor(string? raw, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var s = raw.Trim();
        if (s.StartsWith("#"))
        {
            s = s[1..];
        }

        try
        {
            if (s.Length == 6)
            {
                var r = Convert.ToByte(s[0..2], 16);
                var g = Convert.ToByte(s[2..4], 16);
                var b = Convert.ToByte(s[4..6], 16);
                return new Color(r, g, b);
            }

            if (s.Length == 8)
            {
                var r = Convert.ToByte(s[0..2], 16);
                var g = Convert.ToByte(s[2..4], 16);
                var b = Convert.ToByte(s[4..6], 16);
                var a = Convert.ToByte(s[6..8], 16);
                return new Color(r, g, b, a);
            }
        }
        catch
        {
            return fallback;
        }

        return fallback;
    }

    private void DrawBackgroundImage(SpriteBatch spriteBatch, Texture2D texture, float alpha)
    {
        if (_backgroundImageMode == "stretch")
        {
            spriteBatch.Draw(texture, new Rectangle(0, 0, VirtualWidth, VirtualHeight), Color.White * alpha);
            return;
        }

        var texW = texture.Width;
        var texH = texture.Height;
        if (texW <= 0 || texH <= 0)
        {
            return;
        }

        var scale = MathF.Max(VirtualWidth / (float)texW, VirtualHeight / (float)texH);
        var drawW = (int)MathF.Round(texW * scale);
        var drawH = (int)MathF.Round(texH * scale);
        var drawX = (VirtualWidth - drawW) / 2;
        var drawY = (VirtualHeight - drawH) / 2;
        spriteBatch.Draw(texture, new Rectangle(drawX, drawY, drawW, drawH), Color.White * alpha);
    }

    private void ReloadBackgroundTextures()
    {
        _backgroundImage?.Dispose();
        _backgroundOverlayImage?.Dispose();
        _backgroundImage = null;
        _backgroundOverlayImage = null;

        if (!string.IsNullOrWhiteSpace(_beatmap.BackgroundImagePath))
        {
            _backgroundImage = TryLoadTextureFromPath(_beatmap.BackgroundImagePath!);
        }

        if (!string.IsNullOrWhiteSpace(_beatmap.BackgroundOverlayPath))
        {
            _backgroundOverlayImage = TryLoadTextureFromPath(_beatmap.BackgroundOverlayPath!);
        }
    }

    private Texture2D? TryLoadTextureFromPath(string path)
    {
        try
        {
            var fullPath = ResolveAssetPath(path);
            if (fullPath is null || !File.Exists(fullPath))
            {
                return null;
            }

            using var stream = File.OpenRead(fullPath);
            return Texture2D.FromStream(GraphicsDevice, stream);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveAssetPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, path),
            Path.Combine(Directory.GetCurrentDirectory(), path)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

}

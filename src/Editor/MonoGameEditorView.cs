using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RhythmbulletPrototype.Models;
using RhythmbulletPrototype.Rendering;
using RhythmbulletPrototype.Systems;
using System.Globalization;

namespace RhythmbulletPrototype.Editor;

public sealed class MonoGameEditorView : IEditorView
{
    private const float TimelineX = 140f;
    private const float TimelineTopY = 8f;
    private const float TimelineBottomY = 648f;
    private const float VirtualWidth = 1280f;
    private const float VirtualHeight = 720f;
    private const float TimelineW = 1000f;
    private const float TimelineH = 14f;
    private const float TimelineZoomTrackW = 240f;
    private const int MinTimelineWindowMs = 1000;
    private const int MaxTimelineWindowMs = 7200000;
    private const int MinTimelineTotalMsNoSong = 480000;
    private const int HeavyPreviewMarkThreshold = 700;
    private const int MaxActiveWorldMarkersDrawn = 220;
    private const int MaxActiveLabelDrawn = 36;
    private const int BulletPlacementMarkerLifetimeMs = 200;
    private const int PreviewTimelineBins = 200;

    private readonly Queue<EditorCommand> _commands = new();
    private readonly Queue<string> _messages = new();
    private EditorViewModel? _model;
    private bool _movingPatternDropdownOpen;
    private bool _staticPatternDropdownOpen;
    private bool _shapeDropdownOpen;
    private bool _songDropdownOpen;
    private bool _toolPanelCollapsed;
    private bool _patternPanelCollapsed;
    private bool _songPanelCollapsed;
    private string _selectedPattern = "static_single";
    private string _selectedMovingPattern = "lateral_sweep_translation";
    private string _selectedStaticPattern = "static_single";
    private string _selectedMovementOption = "left_to_right";
    private string _selectedBulletShape = "orb";
    private string _selectedSong = string.Empty;
    private PlacementTool _placementTool = PlacementTool.NoteTap;
    private int _movingPatternScroll;
    private int _staticPatternScroll;
    private int _shapeScroll;
    private int _songScroll;
    private int _timelineViewStartMs;
    private bool _timelineAtBottom;
    private bool _dragUiPanel;
    private Vector2 _uiPanelPos = new(16f, 94f);
    private Vector2 _uiPanelDragOffset;
    private bool _dragTimeline;
    private float _timelineDragOffsetY;
    private float _timelineDragY;
    private bool _dragTimelineWindowSlider;
    private float _timelineWindowSliderGrabOffsetX;
    private bool _dragTimelineZoomSlider;
    private float _timelineZoomSliderGrabOffsetX;
    private bool _timelineZoomTouched;
    private string _lastModelPath = string.Empty;
    private int _timelineWindowMs = 30000;
    private bool _editingTimelineWindowMs;
    private string _timelineWindowInput = "30000";
    private const int PatternVisibleCount = 16;
    private const int SongVisibleCount = 10;
    private bool _hasMouseSample;
    private Vector2 _lastMouseVirtual;
    private float _mouseStillSec;
    private Vector2 _pendingBulletOriginNorm = new(0.5f, 0.5f);
    private bool _showPendingBulletPreview;
    private readonly List<PreviewBullet> _previewBullets = new();
    private float _previewSpawnTimer;
    private float _previewSpiralAngle;
    private int _slowMoIndex;
    private byte _primaryR = 235;
    private byte _primaryG = 40;
    private byte _primaryB = 50;
    private byte _outlineR = 0;
    private byte _outlineG = 0;
    private byte _outlineB = 0;
    private byte _glowR = 255;
    private byte _glowG = 120;
    private byte _glowB = 120;
    private float _glowIntensity = 0.12f;
    private float _bulletSpeed = 240f;
    private bool _editingBulletSpeed;
    private bool _editingGlowIntensity;
    private bool _editingBulletSize;
    private bool _editingMovementIntensity;
    private string _bulletSpeedInput = "240";
    private string _glowIntensityInput = "0.12";
    private string _bulletSizeInput = "8";
    private string _movementIntensityInput = "1.00";
    private string _trajectoryInput = "90";
    private float _bulletSize = 8f;
    private float _movementIntensity = 1f;
    private float _trajectoryDeg = 90f;
    private bool _editingTrajectoryDeg;
    private bool _trajectorySnapEnabled = true;
    private const float TrajectorySnapStepDeg = 15f;
    private float _pauseToggleBlockSec;
    private float _wheelHue;
    private float _wheelSat = 1f;
    private bool _colorWheelPopupOpen;
    private bool _editingHexColor;
    private string _hexColorInput = "EB2832";
    private bool _dragMovementIntensitySlider;
    private bool _dragBulletSizeSlider;
    private string _hoverPatternName = string.Empty;
    private bool _hoverPatternIsMoving;
    private bool _hoverPatternActive;
    private string _hoverShapeOverride = string.Empty;
    private readonly BulletSystem _hoverPreviewSystem = new();
    private readonly List<BulletSystem.BulletPreviewDrawData> _hoverPreviewDrawData = new();
    private readonly List<PreviewMark> _cachedVisiblePreviewMarks = new(2048);
    private readonly byte[] _cachedPreviewBulletBins = new byte[PreviewTimelineBins];
    private readonly byte[] _cachedPreviewNoteBins = new byte[PreviewTimelineBins];
    private IReadOnlyList<PreviewMark>? _cachedPreviewMarksSource;
    private int _cachedPreviewStartMs = int.MinValue;
    private int _cachedPreviewWindowMs = -1;
    private bool _cachedPreviewHeavyMode;
    private string _hoverPreviewSignature = string.Empty;
    private int _hoverPreviewSongMs;
    private bool _hoverPreviewHadAny;
    private long _hoverPreviewLastTickMs;
    private const int ColorWheelHueSnapSteps = 36;
    private const int ColorWheelSatSnapSteps = 10;
    private static readonly float[] SlowMoScales = { 1f, 0.5f, 0.25f };

    private enum PlacementTool
    {
        NoteTap,
        Bullet
    }

    private static readonly string[] MovingPatterns = BulletSystem.MovingPatterns;
    private static readonly string[] StaticPatterns = BulletSystem.StaticPatterns;
    private static readonly string[] BulletShapes =
    {
        "orb", "circle", "rice", "kunai", "butterfly", "star", "arrowhead",
        "droplet", "crystal", "diamond", "petal", "flame_shard", "cross_shard",
        "crescent", "heart_shard", "hex_shard"
    };
    private static readonly string[] MovementOptions =
    {
        "none",
        "fountain",
        "fountain_bounce",
        "left_drift",
        "right_drift",
        "left_to_right",
        "track_mouse",
        "shoot_at_mouse"
    };

    public void CaptureHotkeys(InputState input, Vector2 mouseVirtual, Vector2 mouseVirtualNormalized, float dt)
    {
        if (_model is null)
        {
            return;
        }

        _selectedMovingPattern = MapMovementOptionToPattern(_selectedMovementOption);
        _selectedSong = string.IsNullOrWhiteSpace(_model.CurrentAudioPath) ? _selectedSong : _model.CurrentAudioPath;
        _timelineWindowMs = Math.Clamp(_timelineWindowMs, MinTimelineWindowMs, MaxTimelineWindowMs);

        if (!string.Equals(_lastModelPath, _model.ActivePath, StringComparison.OrdinalIgnoreCase))
        {
            _lastModelPath = _model.ActivePath;
            _timelineZoomTouched = false;
        }

        var timelineTotalMs = GetTimelineTotalMs();

        if (!_timelineZoomTouched)
        {
            _timelineWindowMs = Math.Clamp(timelineTotalMs, MinTimelineWindowMs, MaxTimelineWindowMs);
            _timelineWindowInput = _timelineWindowMs.ToString();
        }

        var maxTimelineStart = Math.Max(0, timelineTotalMs - _timelineWindowMs);
        _timelineViewStartMs = Math.Clamp(_timelineViewStartMs, 0, maxTimelineStart);

        _lastMouseVirtual = mouseVirtual;
        _showPendingBulletPreview = false;

        var panelX = _uiPanelPos.X;
        var panelY = _uiPanelPos.Y;
        var panelW = 500f;
        var panelHeaderH = 20f;
        var panelHeaderRect = new Rectangle((int)panelX - 6, (int)panelY - 24, 420, (int)panelHeaderH);
        var panelToggleRect = new Rectangle(panelHeaderRect.Right - 18, panelHeaderRect.Y + 2, 14, 14);
        var movementHeaderY = panelY + 48f;
        var movementHeader = new Rectangle((int)panelX, (int)movementHeaderY, 360, 20);
        var staticHeaderY = movementHeaderY + 24f + (_movingPatternDropdownOpen ? PatternVisibleCount * 12 + 4 : 0f);
        var staticHeader = new Rectangle((int)panelX, (int)staticHeaderY, 360, 20);
        var shapeHeaderY = staticHeaderY + 24f +
            (_staticPatternDropdownOpen ? PatternVisibleCount * 12 + 4 : 0f);
        var shapeHeader = new Rectangle((int)panelX, (int)shapeHeaderY, 360, 20);
        var songHeaderY = shapeHeaderY + 24f + (_shapeDropdownOpen ? PatternVisibleCount * 12 + 4 : 0f);
        var songHeader = new Rectangle((int)panelX, (int)songHeaderY, 360, 20);
        var noteTapBtn = new Rectangle((int)panelX, (int)panelY, 120, 20);
        var bulletBtn = new Rectangle((int)panelX, (int)(panelY + 24f), 120, 20);
        var publishBtn = new Rectangle((int)(panelX + 128f), (int)(panelY + 24f), 120, 20);
        var patternHeight = _patternPanelCollapsed ? 0 :
            48 +
            (_movingPatternDropdownOpen ? (PatternVisibleCount * 12 + 4) : 0) +
            (_staticPatternDropdownOpen ? (PatternVisibleCount * 12 + 4) : 0) +
            (_shapeDropdownOpen ? (PatternVisibleCount * 12 + 4) : 0);
        var songHeight = _songPanelCollapsed ? 0 : (_songDropdownOpen ? (24 + SongVisibleCount * 12) : 24);
        var colorHeight = 270;
        var panelHeight = _toolPanelCollapsed ? 0 : (96 + patternHeight + songHeight + colorHeight);
        var toolPanelBounds = new Rectangle((int)panelX, (int)panelY, (int)panelW, panelHeight);
        var movementListTop = movementHeaderY + 22f;
        var movementListRect = new Rectangle((int)panelX, (int)movementListTop, 360, PatternVisibleCount * 12 + 2);
        var staticListTop = staticHeaderY + 22f;
        var staticListRect = new Rectangle((int)panelX, (int)staticListTop, 360, PatternVisibleCount * 12 + 2);
        var shapeListTop = shapeHeaderY + 22f;
        var shapeListRect = new Rectangle((int)panelX, (int)shapeListTop, 360, PatternVisibleCount * 12 + 2);
        var songListTop = songHeader.Y + 22f;
        var songListRect = new Rectangle((int)panelX, (int)songListTop, 360, SongVisibleCount * 12 + 2);
        var songToggleRect = new Rectangle(songHeader.Right - 18, songHeader.Y + 2, 14, 14);
        var colorY = songHeader.Y + 24f + (_songDropdownOpen && !_songPanelCollapsed ? SongVisibleCount * 12 + 10 : 10f);
        var colorButtonRect = new Rectangle((int)(panelX + 18f), (int)colorY + 24, 130, 22);
        var speedInputRect = new Rectangle((int)(panelX + 190f), (int)colorY + 32, 160, 22);
        var glowInputRect = new Rectangle((int)(panelX + 190f), (int)colorY + 72, 160, 22);
        var sizeSliderRect = new Rectangle((int)(panelX + 190f), (int)colorY + 120, 120, 10);
        var sizeSliderHitRect = new Rectangle(sizeSliderRect.X - 2, sizeSliderRect.Y - 6, sizeSliderRect.Width + 4, 22);
        var sizeInputRect = new Rectangle(sizeSliderRect.Right + 8, (int)colorY + 114, 42, 22);
        var movementSliderRect = new Rectangle((int)(panelX + 190f), (int)colorY + 162, 120, 10);
        var movementSliderHitRect = new Rectangle(movementSliderRect.X - 2, movementSliderRect.Y - 6, movementSliderRect.Width + 4, 22);
        var movementInputRect = new Rectangle(movementSliderRect.Right + 8, (int)colorY + 156, 42, 22);
        var trajectoryInputRect = new Rectangle((int)(panelX + 190f), (int)colorY + 196, 86, 22);
        var trajectoryMinusRect = new Rectangle(trajectoryInputRect.Right + 6, trajectoryInputRect.Y, 22, 22);
        var trajectoryPlusRect = new Rectangle(trajectoryMinusRect.Right + 4, trajectoryInputRect.Y, 22, 22);
        var trajectorySnapRect = new Rectangle(trajectoryPlusRect.Right + 6, trajectoryInputRect.Y, 68, 22);
        var popupRect = GetColorPopupRect();
        var popupWheelCenter = new Vector2(popupRect.X + 120f, popupRect.Y + 118f);
        const float popupWheelOuterR = 88f;
        const float popupWheelInnerR = 40f;
        var popupWheelBounds = new Rectangle((int)(popupWheelCenter.X - popupWheelOuterR), (int)(popupWheelCenter.Y - popupWheelOuterR), (int)(popupWheelOuterR * 2f), (int)(popupWheelOuterR * 2f));
        var popupHexRect = new Rectangle(popupRect.X + 236, popupRect.Y + 82, 132, 24);
        var popupApplyRect = new Rectangle(popupRect.X + 236, popupRect.Y + 112, 64, 22);
        var popupCloseRect = new Rectangle(popupRect.X + 306, popupRect.Y + 112, 62, 22);
        var timelineY = GetTimelineY();
        var swapTimelineRect = new Rectangle((int)(TimelineX + TimelineW + 28), (int)(timelineY - 2), 26, 18);
        var minusWindowRect = new Rectangle((int)(TimelineX + TimelineW + 60), (int)(timelineY - 2), 16, 18);
        var plusWindowRect = new Rectangle((int)(TimelineX + TimelineW + 78), (int)(timelineY - 2), 16, 18);
        var windowInputRect = new Rectangle((int)(TimelineX + TimelineW + 96), (int)(timelineY - 2), 72, 18);
        var windowSetRect = new Rectangle((int)(TimelineX + TimelineW + 170), (int)(timelineY - 2), 30, 18);
        var timelineNavTrackRect = GetTimelineNavTrackRect(timelineY);
        var timelineNavThumbRect = GetTimelineNavThumbRect(timelineNavTrackRect, timelineTotalMs);
        var timelineZoomTrackRect = GetTimelineZoomTrackRect(timelineY);
        var timelineZoomThumbRect = GetTimelineZoomThumbRect(timelineZoomTrackRect, timelineTotalMs);
        var zoomOutRect = new Rectangle(timelineZoomTrackRect.X - 18, timelineZoomTrackRect.Y - 2, 16, 12);
        var zoomInRect = new Rectangle(timelineZoomTrackRect.Right + 2, timelineZoomTrackRect.Y - 2, 16, 12);
        var timelineDragRect = new Rectangle((int)(TimelineX - 130), (int)(timelineY - 12), 120, 10);
        var timelineRect = new Rectangle((int)TimelineX, (int)timelineY, (int)TimelineW, (int)TimelineH);
        var panelDragRect = panelHeaderRect;
        var hoverPoint = new Point((int)mouseVirtual.X, (int)mouseVirtual.Y);

        _hoverPatternActive = false;
        _hoverShapeOverride = string.Empty;
        if (_shapeDropdownOpen && shapeListRect.Contains(hoverPoint))
        {
            var start = Math.Clamp(_shapeScroll, 0, Math.Max(0, BulletShapes.Length - PatternVisibleCount));
            var visible = Math.Min(PatternVisibleCount, BulletShapes.Length - start);
            for (var i = 0; i < visible; i++)
            {
                var rowRect = new Rectangle((int)panelX, (int)(shapeListTop + 1 + i * 12), 360, 12);
                if (!rowRect.Contains(hoverPoint))
                {
                    continue;
                }

                _hoverPatternName = "static_single";
                _hoverPatternIsMoving = false;
                _hoverPatternActive = true;
                _hoverShapeOverride = BulletShapes[start + i];
                break;
            }
        }

        if (input.LeftPressed && panelToggleRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            _toolPanelCollapsed = !_toolPanelCollapsed;
            if (_toolPanelCollapsed)
            {
                _movingPatternDropdownOpen = false;
                _staticPatternDropdownOpen = false;
                _shapeDropdownOpen = false;
                _songDropdownOpen = false;
            }
            return;
        }

        if (input.LeftPressed && panelDragRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            _dragUiPanel = true;
            _uiPanelDragOffset = mouseVirtual - _uiPanelPos;
            return;
        }

        if (_dragUiPanel)
        {
            if (input.LeftDown)
            {
                _uiPanelPos = mouseVirtual - _uiPanelDragOffset;
                _uiPanelPos = ClampPanelToViewport(_uiPanelPos, panelHeight);
            }
            else
            {
                _dragUiPanel = false;
                _uiPanelPos = ClampPanelToViewport(_uiPanelPos, panelHeight);
            }

            return;
        }

        if (input.LeftPressed && timelineDragRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            _dragTimeline = true;
            _timelineDragY = timelineY;
            _timelineDragOffsetY = mouseVirtual.Y - timelineY;
            return;
        }

        if (_dragTimeline)
        {
            if (input.LeftDown)
            {
                _timelineDragY = Math.Clamp(mouseVirtual.Y - _timelineDragOffsetY, TimelineTopY, TimelineBottomY);
            }
            else
            {
                _dragTimeline = false;
                var mid = (TimelineTopY + TimelineBottomY) * 0.5f;
                _timelineAtBottom = _timelineDragY >= mid;
            }

            return;
        }

        if (input.LeftPressed && timelineRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            var t = (mouseVirtual.X - TimelineX) / TimelineW;
            var seek = _timelineViewStartMs + (int)MathF.Round(Math.Clamp(t, 0f, 1f) * _timelineWindowMs);
            _commands.Enqueue(EditorCommand.SeekTo(seek));
            return;
        }

        if (input.LeftPressed && timelineNavThumbRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            _dragTimelineWindowSlider = true;
            _timelineWindowSliderGrabOffsetX = mouseVirtual.X - timelineNavThumbRect.X;
            return;
        }

        if (_dragTimelineWindowSlider)
        {
            if (input.LeftDown)
            {
                var thumbWidth = Math.Max(12, timelineNavThumbRect.Width);
                SetTimelineStartFromSliderPosition(
                    mouseVirtual.X - _timelineWindowSliderGrabOffsetX,
                    timelineNavTrackRect,
                    thumbWidth,
                    timelineTotalMs);
            }
            else
            {
                _dragTimelineWindowSlider = false;
            }

            return;
        }

        if (input.LeftPressed && timelineNavTrackRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            var thumbWidth = Math.Max(12, timelineNavThumbRect.Width);
            SetTimelineStartFromSliderPosition(
                mouseVirtual.X - thumbWidth * 0.5f,
                timelineNavTrackRect,
                thumbWidth,
                timelineTotalMs);
            return;
        }

        if (input.LeftPressed && timelineZoomThumbRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            _dragTimelineZoomSlider = true;
            _timelineZoomSliderGrabOffsetX = mouseVirtual.X - timelineZoomThumbRect.X;
            _timelineZoomTouched = true;
            return;
        }

        if (_dragTimelineZoomSlider)
        {
            if (input.LeftDown)
            {
                var thumbWidth = Math.Max(12, timelineZoomThumbRect.Width);
                SetTimelineWindowFromZoomSliderPosition(
                    mouseVirtual.X - _timelineZoomSliderGrabOffsetX,
                    timelineZoomTrackRect,
                    thumbWidth,
                    timelineTotalMs);
            }
            else
            {
                _dragTimelineZoomSlider = false;
            }

            return;
        }

        if (input.LeftPressed && timelineZoomTrackRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            var thumbWidth = Math.Max(12, timelineZoomThumbRect.Width);
            SetTimelineWindowFromZoomSliderPosition(
                mouseVirtual.X - thumbWidth * 0.5f,
                timelineZoomTrackRect,
                thumbWidth,
                timelineTotalMs);
            _timelineZoomTouched = true;
            return;
        }

        if (input.LeftPressed && zoomInRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            AdjustTimelineZoom(zoomIn: true, timelineTotalMs);
            return;
        }

        if (input.LeftPressed && zoomOutRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            AdjustTimelineZoom(zoomIn: false, timelineTotalMs);
            return;
        }

        if (input.LeftPressed && swapTimelineRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            _timelineAtBottom = !_timelineAtBottom;
            return;
        }

        if (input.LeftPressed && minusWindowRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            var newWindow = Math.Max(MinTimelineWindowMs, _timelineWindowMs - 1000);
            ApplyTimelineWindowAroundFocus(newWindow, timelineTotalMs);
            _editingTimelineWindowMs = false;
            _timelineZoomTouched = true;
            return;
        }

        if (input.LeftPressed && plusWindowRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            var newWindow = Math.Min(Math.Min(MaxTimelineWindowMs, timelineTotalMs), _timelineWindowMs + 1000);
            ApplyTimelineWindowAroundFocus(newWindow, timelineTotalMs);
            _editingTimelineWindowMs = false;
            _timelineZoomTouched = true;
            return;
        }

        if (input.LeftPressed && windowInputRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            _editingTimelineWindowMs = true;
            _timelineWindowInput = _timelineWindowMs.ToString();
            return;
        }

        if (input.LeftPressed && windowSetRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            if (int.TryParse(_timelineWindowInput, out var parsed))
            {
                var newWindow = Math.Clamp(parsed, MinTimelineWindowMs, Math.Min(MaxTimelineWindowMs, timelineTotalMs));
                ApplyTimelineWindowAroundFocus(newWindow, timelineTotalMs);
            }

            _timelineWindowInput = _timelineWindowMs.ToString();
            _editingTimelineWindowMs = false;
            _timelineZoomTouched = true;
            return;
        }

        if (input.LeftPressed &&
            !windowInputRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !windowSetRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            _editingTimelineWindowMs = false;
        }

        if (input.LeftPressed &&
            !speedInputRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !glowInputRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !sizeInputRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !sizeSliderHitRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !movementInputRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !trajectoryInputRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !trajectoryMinusRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !trajectoryPlusRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) &&
            !trajectorySnapRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y))
        {
            CommitNumericFieldEdits();
            _editingBulletSpeed = false;
            _editingGlowIntensity = false;
            _editingBulletSize = false;
            _editingMovementIntensity = false;
            _editingTrajectoryDeg = false;
        }

        if (_dragBulletSizeSlider)
        {
            if (input.LeftDown)
            {
                var t = Math.Clamp((mouseVirtual.X - sizeSliderRect.X) / Math.Max(1f, sizeSliderRect.Width), 0f, 1f);
                _bulletSize = SnapBulletSize(2f + t * 62f);
                _bulletSizeInput = ((int)MathF.Round(_bulletSize)).ToString();
            }
            else
            {
                _dragBulletSizeSlider = false;
            }

            return;
        }

        if (_dragMovementIntensitySlider)
        {
            if (input.LeftDown)
            {
                var t = Math.Clamp((mouseVirtual.X - movementSliderRect.X) / Math.Max(1f, movementSliderRect.Width), 0f, 1f);
                _movementIntensity = 0f + t * 2f;
                _movementIntensityInput = _movementIntensity.ToString("0.00");
            }
            else
            {
                _dragMovementIntensitySlider = false;
            }

            return;
        }

        if (_colorWheelPopupOpen)
        {
            if (input.LeftPressed)
            {
                var p = new Point((int)mouseVirtual.X, (int)mouseVirtual.Y);
                if (popupWheelBounds.Contains(p))
                {
                    var delta = mouseVirtual - popupWheelCenter;
                    var dist = delta.Length();
                    if (dist <= popupWheelOuterR && dist >= popupWheelInnerR)
                    {
                        _wheelHue = (MathF.Atan2(delta.Y, delta.X) / MathHelper.TwoPi + 1f) % 1f;
                        _wheelSat = Math.Clamp((dist - popupWheelInnerR) / Math.Max(1f, popupWheelOuterR - popupWheelInnerR), 0f, 1f);
                        SnapWheelSelection(ref _wheelHue, ref _wheelSat);
                        var c = HsvToColor(_wheelHue, _wheelSat, 1f);
                        _primaryR = c.R;
                        _primaryG = c.G;
                        _primaryB = c.B;
                        _hexColorInput = $"{_primaryR:X2}{_primaryG:X2}{_primaryB:X2}";
                        _glowR = (byte)Math.Clamp((int)MathF.Round(Color.Lerp(c, Color.White, 0.35f).R), 0, 255);
                        _glowG = (byte)Math.Clamp((int)MathF.Round(Color.Lerp(c, Color.White, 0.35f).G), 0, 255);
                        _glowB = (byte)Math.Clamp((int)MathF.Round(Color.Lerp(c, Color.White, 0.35f).B), 0, 255);
                    }
                    return;
                }

                if (popupHexRect.Contains(p))
                {
                    _editingHexColor = true;
                    return;
                }

                if (popupApplyRect.Contains(p))
                {
                    ApplyHexColorInput();
                    _editingHexColor = false;
                    return;
                }

                if (popupCloseRect.Contains(p))
                {
                    _colorWheelPopupOpen = false;
                    _editingHexColor = false;
                    return;
                }

                if (!popupRect.Contains(p))
                {
                    _colorWheelPopupOpen = false;
                    _editingHexColor = false;
                    return;
                }
            }

            if (_editingHexColor)
            {
                HandleHexColorTyping(input);
                return;
            }
        }

        if (input.LeftPressed)
        {
            var p = new Point((int)mouseVirtual.X, (int)mouseVirtual.Y);

            if (!_toolPanelCollapsed)
            {
                if (noteTapBtn.Contains(p))
                {
                    _placementTool = PlacementTool.NoteTap;
                    _movingPatternDropdownOpen = false;
                    _staticPatternDropdownOpen = false;
                    _shapeDropdownOpen = false;
                    return;
                }

                if (bulletBtn.Contains(p))
                {
                    _placementTool = PlacementTool.Bullet;
                    _movingPatternDropdownOpen = false;
                    _staticPatternDropdownOpen = false;
                    _shapeDropdownOpen = false;
                    return;
                }

                if (publishBtn.Contains(p))
                {
                    _commands.Enqueue(EditorCommand.PublishJson());
                    return;
                }

                if (colorButtonRect.Contains(p))
                {
                    _colorWheelPopupOpen = !_colorWheelPopupOpen;
                    _editingHexColor = false;
                    _hexColorInput = $"{_primaryR:X2}{_primaryG:X2}{_primaryB:X2}";
                    return;
                }

                if (movementHeader.Contains(p))
                {
                    _movingPatternDropdownOpen = !_movingPatternDropdownOpen;
                    _staticPatternDropdownOpen = false;
                    _shapeDropdownOpen = false;
                    _songDropdownOpen = false;
                    return;
                }

                if (staticHeader.Contains(p))
                {
                    _placementTool = PlacementTool.Bullet;
                    _staticPatternDropdownOpen = !_staticPatternDropdownOpen;
                    _movingPatternDropdownOpen = false;
                    _shapeDropdownOpen = false;
                    _songDropdownOpen = false;
                    return;
                }

                if (shapeHeader.Contains(p))
                {
                    _shapeDropdownOpen = !_shapeDropdownOpen;
                    _movingPatternDropdownOpen = false;
                    _staticPatternDropdownOpen = false;
                    _songDropdownOpen = false;
                    return;
                }

                if (songHeader.Contains(p))
                {
                    _songDropdownOpen = !_songDropdownOpen;
                    _movingPatternDropdownOpen = false;
                    _staticPatternDropdownOpen = false;
                    _shapeDropdownOpen = false;
                    return;
                }

                if (songToggleRect.Contains(p))
                {
                    _songPanelCollapsed = !_songPanelCollapsed;
                    if (_songPanelCollapsed)
                    {
                        _songDropdownOpen = false;
                    }
                    return;
                }

                if (!_patternPanelCollapsed && _movingPatternDropdownOpen && movementListRect.Contains(p))
                {
                    var start = Math.Clamp(_movingPatternScroll, 0, Math.Max(0, MovementOptions.Length - PatternVisibleCount));
                    var visible = Math.Min(PatternVisibleCount, MovementOptions.Length - start);
                    for (var i = 0; i < visible; i++)
                    {
                        var itemRect = new Rectangle((int)panelX, (int)(movementListTop + 1 + i * 12), 360, 12);
                        if (!itemRect.Contains(p))
                        {
                            continue;
                        }

                        _selectedMovementOption = MovementOptions[start + i];
                        _selectedMovingPattern = MapMovementOptionToPattern(_selectedMovementOption);
                        _movingPatternDropdownOpen = false;
                        return;
                    }
                }

                if (!_patternPanelCollapsed && _staticPatternDropdownOpen && staticListRect.Contains(p))
                {
                    var start = Math.Clamp(_staticPatternScroll, 0, Math.Max(0, StaticPatterns.Length - PatternVisibleCount));
                    var visible = Math.Min(PatternVisibleCount, StaticPatterns.Length - start);
                    for (var i = 0; i < visible; i++)
                    {
                        var itemRect = new Rectangle((int)panelX, (int)(staticListTop + 1 + i * 12), 360, 12);
                        if (!itemRect.Contains(p))
                        {
                            continue;
                        }

                        _selectedStaticPattern = StaticPatterns[start + i];
                        _selectedPattern = _selectedStaticPattern;
                        _commands.Enqueue(EditorCommand.SetPattern(_selectedPattern));
                        _staticPatternDropdownOpen = false;
                        return;
                    }
                }

                if (!_patternPanelCollapsed && _shapeDropdownOpen && shapeListRect.Contains(p))
                {
                    var start = Math.Clamp(_shapeScroll, 0, Math.Max(0, BulletShapes.Length - PatternVisibleCount));
                    var visible = Math.Min(PatternVisibleCount, BulletShapes.Length - start);
                    for (var i = 0; i < visible; i++)
                    {
                        var itemRect = new Rectangle((int)panelX, (int)(shapeListTop + 1 + i * 12), 360, 12);
                        if (itemRect.Contains(p))
                        {
                            _selectedBulletShape = BulletShapes[start + i];
                            _shapeDropdownOpen = false;
                            return;
                        }
                    }
                }

                if (!_songPanelCollapsed && _songDropdownOpen && songListRect.Contains(p))
                {
                    var start = Math.Clamp(_songScroll, 0, Math.Max(0, _model.SongOptions.Count - SongVisibleCount));
                    var visible = Math.Min(SongVisibleCount, _model.SongOptions.Count - start);
                    for (var i = 0; i < visible; i++)
                    {
                        var itemRect = new Rectangle((int)panelX, (int)(songListTop + 1 + i * 12), 360, 12);
                        if (itemRect.Contains(p))
                        {
                            _selectedSong = _model.SongOptions[start + i];
                            _commands.Enqueue(EditorCommand.SetAudioPath(_selectedSong));
                            _songDropdownOpen = false;
                            return;
                        }
                    }
                }

                if (speedInputRect.Contains(p))
                {
                    _editingBulletSpeed = true;
                    _editingGlowIntensity = false;
                    _editingBulletSize = false;
                    _editingMovementIntensity = false;
                    _editingTrajectoryDeg = false;
                    _bulletSpeedInput = _bulletSpeed.ToString("0.##", CultureInfo.InvariantCulture);
                    return;
                }

                if (glowInputRect.Contains(p))
                {
                    _editingGlowIntensity = true;
                    _editingBulletSpeed = false;
                    _editingBulletSize = false;
                    _editingMovementIntensity = false;
                    _editingTrajectoryDeg = false;
                    _glowIntensityInput = _glowIntensity.ToString("0.##");
                    return;
                }

                if (sizeInputRect.Contains(p))
                {
                    _editingBulletSize = true;
                    _editingBulletSpeed = false;
                    _editingGlowIntensity = false;
                    _editingMovementIntensity = false;
                    _bulletSizeInput = ((int)MathF.Round(_bulletSize)).ToString();
                    return;
                }

                if (sizeSliderHitRect.Contains(p))
                {
                    var t = Math.Clamp((mouseVirtual.X - sizeSliderRect.X) / Math.Max(1f, sizeSliderRect.Width), 0f, 1f);
                    _bulletSize = SnapBulletSize(2f + t * 62f);
                    _bulletSizeInput = ((int)MathF.Round(_bulletSize)).ToString();
                    _editingBulletSize = false;
                    _dragBulletSizeSlider = true;
                    return;
                }

                if (movementInputRect.Contains(p))
                {
                    _editingMovementIntensity = true;
                    _editingBulletSpeed = false;
                    _editingGlowIntensity = false;
                    _editingBulletSize = false;
                    _editingTrajectoryDeg = false;
                    _movementIntensityInput = _movementIntensity.ToString("0.00");
                    return;
                }

                if (movementSliderHitRect.Contains(p))
                {
                    var t = Math.Clamp((mouseVirtual.X - movementSliderRect.X) / Math.Max(1f, movementSliderRect.Width), 0f, 1f);
                    _movementIntensity = 0f + t * 2f;
                    _movementIntensityInput = _movementIntensity.ToString("0.00");
                    _editingMovementIntensity = false;
                    _dragMovementIntensitySlider = true;
                    return;
                }

                if (trajectoryInputRect.Contains(p))
                {
                    _editingTrajectoryDeg = true;
                    _editingBulletSpeed = false;
                    _editingGlowIntensity = false;
                    _editingBulletSize = false;
                    _editingMovementIntensity = false;
                    _trajectoryInput = _trajectoryDeg.ToString("0.##", CultureInfo.InvariantCulture);
                    return;
                }

                if (trajectoryMinusRect.Contains(p))
                {
                    NudgeTrajectory(-1f);
                    return;
                }

                if (trajectoryPlusRect.Contains(p))
                {
                    NudgeTrajectory(1f);
                    return;
                }

                if (trajectorySnapRect.Contains(p))
                {
                    _trajectorySnapEnabled = !_trajectorySnapEnabled;
                    CommitNumericFieldEdits();
                    return;
                }
            }

            // Placement click in playfield (outside tool panel and timeline)
            var overTimeline = timelineRect.Contains(p) ||
                               swapTimelineRect.Contains(p) || minusWindowRect.Contains(p) ||
                               plusWindowRect.Contains(p) || windowInputRect.Contains(p) || windowSetRect.Contains(p) ||
                               timelineNavTrackRect.Contains(p) || timelineNavThumbRect.Contains(p) ||
                               timelineZoomTrackRect.Contains(p) || timelineZoomThumbRect.Contains(p) ||
                               zoomInRect.Contains(p) || zoomOutRect.Contains(p);

            var inPlayfieldBounds =
                mouseVirtual.X >= 0f && mouseVirtual.X <= VirtualWidth &&
                mouseVirtual.Y >= 0f && mouseVirtual.Y <= VirtualHeight;
            if (!toolPanelBounds.Contains(p) && !overTimeline && mouseVirtual.Y > 30f && inPlayfieldBounds)
            {
                var placed = false;
                switch (_placementTool)
                {
                    case PlacementTool.NoteTap:
                        _commands.Enqueue(EditorCommand.AddTap(mouseVirtualNormalized.X, mouseVirtualNormalized.Y));
                        placed = true;
                        break;
                    case PlacementTool.Bullet:
                        _pendingBulletOriginNorm = mouseVirtualNormalized;
                        _selectedPattern = string.IsNullOrWhiteSpace(_selectedStaticPattern) ? "static_single" : _selectedStaticPattern;
                        _commands.Enqueue(EditorCommand.SetPattern(_selectedPattern));
                        _commands.Enqueue(EditorCommand.AddBullet(mouseVirtualNormalized.X, mouseVirtualNormalized.Y, BuildBulletParameterPayload()));
                        _showPendingBulletPreview = false;
                        _previewBullets.Clear();
                        placed = true;
                        break;
                }

                _pauseToggleBlockSec = 0.14f;
                if (placed && _model.IsPlaying)
                {
                    _commands.Enqueue(EditorCommand.TogglePlayPause());
                }
            }
        }

        if (input.RightPressed)
        {
            var now = _model.CurrentTimeMs;
            var preferredKind = _placementTool == PlacementTool.NoteTap
                ? LevelEditorConstants.EventKindNote
                : LevelEditorConstants.EventKindBullet;
            var noteThresholdSq = 32f * 32f;
            var bulletThresholdSq = 22f * 22f;
            PreviewMark? bestPreferred = null;
            PreviewMark? bestOther = null;
            var bestPreferredDistSq = float.MaxValue;
            var bestOtherDistSq = float.MaxValue;
            foreach (var mark in _model.PreviewMarks)
            {
                if (now < mark.StartMs || now > mark.EndMs)
                {
                    continue;
                }

                var px = mark.X * 1280f;
                var py = mark.Y * 720f;
                var dx = mouseVirtual.X - px;
                var dy = mouseVirtual.Y - py;
                var d2 = dx * dx + dy * dy;
                var thresholdSq = mark.Kind == LevelEditorConstants.EventKindNote ? noteThresholdSq : bulletThresholdSq;
                if (d2 > thresholdSq)
                {
                    continue;
                }

                if (string.Equals(mark.Kind, preferredKind, StringComparison.OrdinalIgnoreCase))
                {
                    if (d2 < bestPreferredDistSq)
                    {
                        bestPreferredDistSq = d2;
                        bestPreferred = mark;
                    }
                }
                else if (d2 < bestOtherDistSq)
                {
                    bestOtherDistSq = d2;
                    bestOther = mark;
                }
            }

            var best = bestPreferred ?? bestOther;
            if (best is not null)
            {
                _commands.Enqueue(EditorCommand.DeleteByEventId(best.Kind, (int)best.EventId));
                _showPendingBulletPreview = false;
                _previewBullets.Clear();
                return;
            }
        }

        if (_colorWheelPopupOpen)
        {
            if (_editingHexColor)
            {
                HandleHexColorTyping(input);
            }

            return;
        }

        var consumedWheel = false;

        if (!_patternPanelCollapsed &&
            _movingPatternDropdownOpen &&
            input.MouseWheelDelta != 0 &&
            mouseVirtual.X >= movementListRect.Left &&
            mouseVirtual.X <= movementListRect.Right &&
            mouseVirtual.Y >= movementListRect.Top &&
            mouseVirtual.Y <= movementListRect.Bottom)
        {
            var wheelSteps = input.MouseWheelDelta / 120;
            _movingPatternScroll -= wheelSteps;
            _movingPatternScroll = Math.Clamp(_movingPatternScroll, 0, Math.Max(0, MovementOptions.Length - PatternVisibleCount));
            consumedWheel = true;
        }

        if (!_patternPanelCollapsed &&
            _staticPatternDropdownOpen &&
            input.MouseWheelDelta != 0 &&
            mouseVirtual.X >= staticListRect.Left &&
            mouseVirtual.X <= staticListRect.Right &&
            mouseVirtual.Y >= staticListRect.Top &&
            mouseVirtual.Y <= staticListRect.Bottom)
        {
            var wheelSteps = input.MouseWheelDelta / 120;
            _staticPatternScroll -= wheelSteps;
            _staticPatternScroll = Math.Clamp(_staticPatternScroll, 0, Math.Max(0, StaticPatterns.Length - PatternVisibleCount));
            consumedWheel = true;
        }

        if (!_patternPanelCollapsed &&
            _shapeDropdownOpen &&
            input.MouseWheelDelta != 0 &&
            mouseVirtual.X >= shapeListRect.Left &&
            mouseVirtual.X <= shapeListRect.Right &&
            mouseVirtual.Y >= shapeListRect.Top &&
            mouseVirtual.Y <= shapeListRect.Bottom)
        {
            var wheelSteps = input.MouseWheelDelta / 120;
            _shapeScroll -= wheelSteps;
            _shapeScroll = Math.Clamp(_shapeScroll, 0, Math.Max(0, BulletShapes.Length - PatternVisibleCount));
            consumedWheel = true;
        }

        if (!_songPanelCollapsed &&
            _songDropdownOpen &&
            input.MouseWheelDelta != 0 &&
            mouseVirtual.X >= songListRect.Left &&
            mouseVirtual.X <= songListRect.Right &&
            mouseVirtual.Y >= songListRect.Top &&
            mouseVirtual.Y <= songListRect.Bottom)
        {
            var wheelSteps = input.MouseWheelDelta / 120;
            _songScroll -= wheelSteps;
            _songScroll = Math.Clamp(_songScroll, 0, Math.Max(0, _model.SongOptions.Count - SongVisibleCount));
            consumedWheel = true;
        }

        var overTimelineForWheel =
            timelineRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) ||
            timelineNavTrackRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) ||
            timelineNavThumbRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) ||
            timelineZoomTrackRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y) ||
            timelineZoomThumbRect.Contains((int)mouseVirtual.X, (int)mouseVirtual.Y);
        if (!consumedWheel && input.MouseWheelDelta != 0 && overTimelineForWheel)
        {
            var wheelSteps = input.MouseWheelDelta / 120;
            var totalMs = GetTimelineTotalMs();
            var maxStartForWheel = Math.Max(0, totalMs - _timelineWindowMs);
            var nudgeMs = Math.Max(20, _timelineWindowMs / 20);
            _timelineViewStartMs = Math.Clamp(_timelineViewStartMs - wheelSteps * nudgeMs, 0, maxStartForWheel);
        }

        if (_editingTimelineWindowMs)
        {
            HandleTimelineWindowTyping(input);
            return;
        }

        if (_editingBulletSpeed || _editingGlowIntensity || _editingBulletSize || _editingMovementIntensity || _editingHexColor)
        {
            if (_editingHexColor)
            {
                HandleHexColorTyping(input);
            }
            else
            {
                HandleNumericFieldTyping(input);
            }
            return;
        }

        _pauseToggleBlockSec = MathF.Max(0f, _pauseToggleBlockSec - dt);
        if (_pauseToggleBlockSec <= 0f &&
            !input.LeftDown &&
            (input.IsKeyPressed(Keys.P) && !(input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl))))
        {
            _commands.Enqueue(EditorCommand.TogglePlayPause());
        }

        if (input.IsKeyPressed(Keys.K))
        {
            _commands.Enqueue(EditorCommand.Stop());
        }

        if (input.IsKeyPressed(Keys.Left))
        {
            _commands.Enqueue(EditorCommand.SeekDelta(ScaledSeekDelta(-500)));
        }

        if (input.IsKeyPressed(Keys.Right))
        {
            _commands.Enqueue(EditorCommand.SeekDelta(ScaledSeekDelta(500)));
            if (_model.CurrentTimeMs > _timelineViewStartMs + _timelineWindowMs - 2000)
            {
                _timelineViewStartMs = _timelineViewStartMs + _timelineWindowMs / 2;
            }
        }

        if (input.IsKeyPressed(Keys.OemOpenBrackets))
        {
            if (_editingTrajectoryDeg)
            {
                NudgeTrajectory(-5f);
            }
            else
            {
                _commands.Enqueue(EditorCommand.SeekDelta(ScaledSeekDelta(-50)));
            }
        }

        if (input.IsKeyPressed(Keys.OemCloseBrackets))
        {
            if (_editingTrajectoryDeg)
            {
                NudgeTrajectory(5f);
            }
            else
            {
                _commands.Enqueue(EditorCommand.SeekDelta(ScaledSeekDelta(50)));
            }
        }

        if (input.IsKeyPressed(Keys.OemSemicolon))
        {
            if (_editingTrajectoryDeg)
            {
                NudgeTrajectory(-1f);
            }
            else
            {
                _commands.Enqueue(EditorCommand.SeekDelta(ScaledSeekDelta(-1)));
            }
        }

        if (input.IsKeyPressed(Keys.OemQuotes))
        {
            if (_editingTrajectoryDeg)
            {
                NudgeTrajectory(1f);
            }
            else
            {
                _commands.Enqueue(EditorCommand.SeekDelta(ScaledSeekDelta(1)));
            }
        }

        if (input.IsKeyPressed(Keys.O))
        {
            _slowMoIndex = (_slowMoIndex + 1) % SlowMoScales.Length;
            _commands.Enqueue(EditorCommand.SetPlaybackRate(SlowMoScales[_slowMoIndex]));
            _messages.Enqueue($"Slow Edit: x{SlowMoScales[_slowMoIndex]:0.##}");
        }

        if (input.IsKeyPressed(Keys.Up))
        {
            _commands.Enqueue(EditorCommand.SelectPrevious());
        }

        if (input.IsKeyPressed(Keys.Down))
        {
            _commands.Enqueue(EditorCommand.SelectNext());
        }

        if (input.IsKeyPressed(Keys.Delete))
        {
            _commands.Enqueue(EditorCommand.DeleteSelected());
        }

        if (input.IsKeyPressed(Keys.Q))
        {
            var delta = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift) ? -50 : -10;
            _commands.Enqueue(EditorCommand.NudgeSelected(delta));
        }

        if (input.IsKeyPressed(Keys.E))
        {
            var delta = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift) ? 50 : 10;
            _commands.Enqueue(EditorCommand.NudgeSelected(delta));
        }

        if (input.IsKeyPressed(Keys.G))
        {
            _commands.Enqueue(EditorCommand.ToggleSnap());
        }

        if (input.IsKeyPressed(Keys.D1)) _commands.Enqueue(EditorCommand.SetLane(1));
        if (input.IsKeyPressed(Keys.D2)) _commands.Enqueue(EditorCommand.SetLane(2));
        if (input.IsKeyPressed(Keys.D3)) _commands.Enqueue(EditorCommand.SetLane(3));
        if (input.IsKeyPressed(Keys.D4)) _commands.Enqueue(EditorCommand.SetLane(4));
        if (input.IsKeyPressed(Keys.D5)) _commands.Enqueue(EditorCommand.SetLane(5));
        if (input.IsKeyPressed(Keys.D6)) _commands.Enqueue(EditorCommand.SetLane(6));
        if (input.IsKeyPressed(Keys.D7)) _commands.Enqueue(EditorCommand.SetLane(7));
        if (input.IsKeyPressed(Keys.D8)) _commands.Enqueue(EditorCommand.SetLane(8));
        if (input.IsKeyPressed(Keys.D9)) _commands.Enqueue(EditorCommand.SetLane(9));

        if (input.IsKeyPressed(Keys.N))
        {
            _placementTool = PlacementTool.NoteTap;
            _commands.Enqueue(EditorCommand.AddTap(mouseVirtualNormalized.X, mouseVirtualNormalized.Y));
            if (_model.IsPlaying) _commands.Enqueue(EditorCommand.TogglePlayPause());
        }

        if (input.IsKeyPressed(Keys.B))
        {
            _placementTool = PlacementTool.Bullet;
            _selectedPattern = string.IsNullOrWhiteSpace(_selectedStaticPattern) ? "static_single" : _selectedStaticPattern;
            _selectedMovingPattern = MapMovementOptionToPattern(_selectedMovementOption);
            _commands.Enqueue(EditorCommand.SetPattern(_selectedPattern));
            _commands.Enqueue(EditorCommand.AddBullet(mouseVirtualNormalized.X, mouseVirtualNormalized.Y, BuildBulletParameterPayload()));
            if (_model.IsPlaying) _commands.Enqueue(EditorCommand.TogglePlayPause());
        }

        if ((input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl)) && input.IsKeyPressed(Keys.S))
        {
            _commands.Enqueue(EditorCommand.Save());
        }

        if ((input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl)) && input.IsKeyPressed(Keys.L))
        {
            _commands.Enqueue(EditorCommand.Load());
        }

        if ((input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl)) && input.IsKeyPressed(Keys.P))
        {
            _commands.Enqueue(EditorCommand.PublishJson());
        }
    }

    public void GetTimelineViewRange(out int startMs, out int endMs)
    {
        startMs = Math.Max(0, _timelineViewStartMs);
        endMs = Math.Max(startMs, _timelineViewStartMs + _timelineWindowMs);
    }

    public void DrawOverlay(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, int virtualWidth, int virtualHeight)
    {
        if (_model is null)
        {
            return;
        }

        DrawTimelineBar(spriteBatch, render, text);
        DrawPreviewMarks(spriteBatch, render, text, virtualWidth, virtualHeight);
        DrawToolPanel(spriteBatch, render, text);
        DrawHoveredPatternPreview(spriteBatch, render, text);
        DrawColorWheelPopup(spriteBatch, render, text);
    }

    private void DrawPendingBulletPreview(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, int virtualWidth, int virtualHeight)
    {
        if (!_showPendingBulletPreview || _placementTool != PlacementTool.Bullet)
        {
            return;
        }

        var origin = new Vector2(_pendingBulletOriginNorm.X * virtualWidth, _pendingBulletOriginNorm.Y * virtualHeight);
        var color = new Color(255, 170, 110, 170);
        render.DrawCircleOutline(spriteBatch, origin, 10f, 2f, color);
        render.DrawCircleFilled(spriteBatch, origin, 3.5f, Color.White * 0.9f);

        for (var i = 0; i < _previewBullets.Count; i++)
        {
            var p = _previewBullets[i];
            var alpha = Math.Clamp(1f - (p.Age / p.Life), 0f, 1f);
            render.DrawCircleFilled(spriteBatch, p.Position, 3.6f, new Color(255, 170, 120) * alpha);
            render.DrawCircleOutline(spriteBatch, p.Position, 3.6f, 1f, new Color(255, 255, 255, 220) * alpha);
        }

        DrawMenuText(spriteBatch, text, $"Preview: {_selectedPattern}", origin + new Vector2(12f, -14f), new Color(255, 220, 185), 1.2f);
    }

    private void UpdatePendingPatternPreview(float dt)
    {
        if (!_showPendingBulletPreview || _placementTool != PlacementTool.Bullet)
        {
            _previewBullets.Clear();
            _previewSpawnTimer = 0f;
            return;
        }

        var origin = new Vector2(_pendingBulletOriginNorm.X * 1280f, _pendingBulletOriginNorm.Y * 720f);
        _previewSpawnTimer -= dt;
        while (_previewSpawnTimer <= 0f)
        {
            SpawnPreviewBurst(origin);
            _previewSpawnTimer += 0.18f;
        }

        for (var i = _previewBullets.Count - 1; i >= 0; i--)
        {
            var b = _previewBullets[i];
            b.Age += dt;
            b.Position += b.Velocity * dt;
            if (b.Age >= b.Life)
            {
                _previewBullets.RemoveAt(i);
            }
            else
            {
                _previewBullets[i] = b;
            }
        }
    }

    private void SpawnPreviewBurst(Vector2 origin)
    {
        var pattern = _selectedPattern.ToLowerInvariant();
        var center = new Vector2(640f, 360f);
        var toCenter = center - origin;
        var baseAngle = toCenter.LengthSquared() < 0.001f ? -MathHelper.PiOver2 : MathF.Atan2(toCenter.Y, toCenter.X);

        if (pattern.Contains("spiral"))
        {
            const int count = 6;
            for (var i = 0; i < count; i++)
            {
                var angle = _previewSpiralAngle + (MathHelper.TwoPi * i / count);
                AddPreviewBullet(origin, angle, 240f, 1.2f);
            }

            _previewSpiralAngle += 0.28f;
            return;
        }

        if (pattern.Contains("fan") || pattern == "arc_left" || pattern == "arc_right" || pattern == "shotgun")
        {
            const int rays = 7;
            var spread = pattern.Contains("wide") ? MathHelper.ToRadians(120f) : MathHelper.ToRadians(70f);
            for (var i = 0; i < rays; i++)
            {
                var t = i / (float)(rays - 1);
                var angle = baseAngle - spread * 0.5f + spread * t;
                AddPreviewBullet(origin, angle, 260f, 1.0f);
            }

            return;
        }

        if (pattern.Contains("aimed"))
        {
            AddPreviewBullet(origin, baseAngle, 280f, 1.0f);
            AddPreviewBullet(origin, baseAngle + 0.12f, 240f, 0.95f);
            AddPreviewBullet(origin, baseAngle - 0.12f, 240f, 0.95f);
            return;
        }

        const int radial = 10;
        for (var i = 0; i < radial; i++)
        {
            var angle = MathHelper.TwoPi * (i / (float)radial);
            AddPreviewBullet(origin, angle, 220f, 1.05f);
        }
    }

    private void AddPreviewBullet(Vector2 origin, float angle, float speed, float life)
    {
        _previewBullets.Add(new PreviewBullet
        {
            Position = origin,
            Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
            Age = 0f,
            Life = life
        });
    }

    private void DrawTimelineBar(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        if (_model is null)
        {
            return;
        }

        var timelineY = GetTimelineY();
        var swapTimelineRect = new Rectangle((int)(TimelineX + TimelineW + 28), (int)(timelineY - 2), 26, 18);
        var minusWindowRect = new Rectangle((int)(TimelineX + TimelineW + 60), (int)(timelineY - 2), 16, 18);
        var plusWindowRect = new Rectangle((int)(TimelineX + TimelineW + 78), (int)(timelineY - 2), 16, 18);
        var windowInputRect = new Rectangle((int)(TimelineX + TimelineW + 96), (int)(timelineY - 2), 72, 18);
        var windowSetRect = new Rectangle((int)(TimelineX + TimelineW + 170), (int)(timelineY - 2), 30, 18);
        var timelineTotalMs = GetTimelineTotalMs();
        var timelineNavTrackRect = GetTimelineNavTrackRect(timelineY);
        var timelineNavThumbRect = GetTimelineNavThumbRect(timelineNavTrackRect, timelineTotalMs);
        var timelineZoomTrackRect = GetTimelineZoomTrackRect(timelineY);
        var timelineZoomThumbRect = GetTimelineZoomThumbRect(timelineZoomTrackRect, timelineTotalMs);
        var zoomOutRect = new Rectangle(timelineZoomTrackRect.X - 18, timelineZoomTrackRect.Y - 2, 16, 12);
        var zoomInRect = new Rectangle(timelineZoomTrackRect.Right + 2, timelineZoomTrackRect.Y - 2, 16, 12);
        render.DrawRect(spriteBatch, swapTimelineRect, new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, minusWindowRect, new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, plusWindowRect, new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, windowInputRect, _editingTimelineWindowMs ? new Color(55, 88, 130, 235) : new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, windowSetRect, new Color(30, 36, 50, 235));
        DrawMenuText(spriteBatch, text, _timelineAtBottom ? "TOP" : "BOT", new Vector2(swapTimelineRect.X + 3, swapTimelineRect.Y + 4), Color.White, 1.05f);
        DrawPlusMinusGlyph(spriteBatch, render, minusWindowRect, plus: false);
        DrawPlusMinusGlyph(spriteBatch, render, plusWindowRect, plus: true);
        var inputText = _editingTimelineWindowMs ? _timelineWindowInput : _timelineWindowMs.ToString();
        DrawMenuText(spriteBatch, text, $"{inputText}ms", new Vector2(windowInputRect.X + 4, windowInputRect.Y + 4), Color.White, 1.05f);
        DrawMenuText(spriteBatch, text, "SET", new Vector2(windowSetRect.X + 4, windowSetRect.Y + 4), Color.White, 1.0f);

        render.DrawRect(spriteBatch, new Rectangle((int)TimelineX, (int)timelineY, (int)TimelineW, (int)TimelineH), new Color(35, 40, 52, 220));
        render.DrawRect(spriteBatch, new Rectangle((int)TimelineX, (int)timelineY, (int)TimelineW, 1), new Color(180, 190, 215, 240));
        render.DrawRect(spriteBatch, new Rectangle((int)TimelineX, (int)(timelineY + TimelineH - 1), (int)TimelineW, 1), new Color(180, 190, 215, 240));

        var startMs = _timelineViewStartMs;
        var endMs = startMs + _timelineWindowMs;
        DrawPerfectHitIndicators(spriteBatch, render, text, startMs, endMs, timelineY);
        var posT = Math.Clamp((_model.CurrentTimeMs - startMs) / (float)_timelineWindowMs, 0f, 1f);
        var markerX = (int)MathF.Round(TimelineX + posT * TimelineW);
        render.DrawRect(spriteBatch, new Rectangle(markerX - 1, (int)timelineY - 2, 3, (int)TimelineH + 4), new Color(120, 245, 255));

        var lastEntityT = Math.Clamp((_model.LastEntityEndMs - startMs) / (float)_timelineWindowMs, 0f, 1f);
        var lastEntityX = (int)MathF.Round(TimelineX + lastEntityT * TimelineW);
        render.DrawRect(spriteBatch, new Rectangle(lastEntityX - 1, (int)timelineY - 6, 2, (int)TimelineH + 10), new Color(255, 190, 120, 180));

        DrawMenuText(spriteBatch, text, "Click timeline", new Vector2(TimelineX - 130f, timelineY + 1f), new Color(205, 215, 230), 1.3f);
        DrawMenuText(spriteBatch, text, $"{_model.CurrentTimeMs}ms", new Vector2(markerX - 20f, timelineY + 17f), new Color(130, 240, 255), 1.2f);
        DrawMenuText(spriteBatch, text, $"SlowEdit x{SlowMoScales[_slowMoIndex]:0.##}", new Vector2(TimelineX + TimelineW + 172f, timelineY + 2f), new Color(210, 220, 235), 1.1f);

        render.DrawRect(spriteBatch, timelineNavTrackRect, new Color(26, 30, 42, 210));
        render.DrawRect(spriteBatch, new Rectangle(timelineNavTrackRect.X, timelineNavTrackRect.Y, timelineNavTrackRect.Width, 1), new Color(165, 176, 205, 190));
        render.DrawRect(spriteBatch, new Rectangle(timelineNavTrackRect.X, timelineNavTrackRect.Bottom - 1, timelineNavTrackRect.Width, 1), new Color(165, 176, 205, 190));
        render.DrawRect(spriteBatch, timelineNavThumbRect, new Color(95, 150, 210, 235));
        render.DrawRect(spriteBatch, new Rectangle(timelineNavThumbRect.X, timelineNavThumbRect.Y, timelineNavThumbRect.Width, 1), new Color(230, 240, 255, 220));
        DrawMenuText(spriteBatch, text, "NAV", new Vector2(timelineNavTrackRect.X - 28f, timelineNavTrackRect.Y - 1f), new Color(205, 215, 230), 1.2f);
        DrawMenuText(spriteBatch, text, $"{timelineTotalMs}ms", new Vector2(timelineNavTrackRect.Right + 6f, timelineNavTrackRect.Y - 1f), new Color(185, 195, 220), 1.1f);

        render.DrawRect(spriteBatch, timelineZoomTrackRect, new Color(26, 30, 42, 210));
        render.DrawRect(spriteBatch, new Rectangle(timelineZoomTrackRect.X, timelineZoomTrackRect.Y, timelineZoomTrackRect.Width, 1), new Color(165, 176, 205, 190));
        render.DrawRect(spriteBatch, new Rectangle(timelineZoomTrackRect.X, timelineZoomTrackRect.Bottom - 1, timelineZoomTrackRect.Width, 1), new Color(165, 176, 205, 190));
        render.DrawRect(spriteBatch, timelineZoomThumbRect, new Color(120, 205, 150, 235));
        render.DrawRect(spriteBatch, new Rectangle(timelineZoomThumbRect.X, timelineZoomThumbRect.Y, timelineZoomThumbRect.Width, 1), new Color(230, 250, 238, 220));
        render.DrawRect(spriteBatch, zoomOutRect, new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, zoomInRect, new Color(30, 36, 50, 235));
        DrawPlusMinusGlyph(spriteBatch, render, zoomOutRect, plus: false);
        DrawPlusMinusGlyph(spriteBatch, render, zoomInRect, plus: true);
        DrawMenuText(spriteBatch, text, "ZOOM IN", new Vector2(timelineZoomTrackRect.X - 46f, timelineZoomTrackRect.Y - 1f), new Color(205, 215, 230), 1.2f);
        DrawMenuText(spriteBatch, text, $"{_timelineWindowMs}ms", new Vector2(timelineZoomTrackRect.Right + 6f, timelineZoomTrackRect.Y - 1f), new Color(185, 195, 220), 1.1f);

        DrawTimingAnalysis(spriteBatch, render, text, startMs, endMs, timelineY);
    }

    private void DrawPerfectHitIndicators(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, int startMs, int endMs, float timelineY)
    {
        if (_model is null)
        {
            return;
        }

        var nextNoteX = -1f;
        var nextNoteTime = int.MaxValue;
        for (var i = 0; i < _model.TimelineRows.Count; i++)
        {
            var row = _model.TimelineRows[i];
            if (!string.Equals(row.Kind, LevelEditorConstants.EventKindNote, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tMs = row.TimeMs;
            if (tMs < startMs || tMs > endMs)
            {
                continue;
            }

            var x = TimelineX + ((tMs - startMs) / (float)Math.Max(1, _timelineWindowMs)) * TimelineW;
            render.DrawRect(spriteBatch, new Rectangle((int)x - 1, (int)timelineY - 7, 2, (int)TimelineH + 14), new Color(145, 255, 165, 190));
            render.DrawRect(spriteBatch, new Rectangle((int)x - 2, (int)timelineY - 8, 4, 2), new Color(210, 255, 220, 220));

            if (tMs >= _model.CurrentTimeMs && tMs < nextNoteTime)
            {
                nextNoteTime = tMs;
                nextNoteX = x;
            }
        }

        if (nextNoteX >= 0f)
        {
            DrawMenuText(spriteBatch, text, "Perfect", new Vector2(nextNoteX - 18f, timelineY - 19f), new Color(210, 255, 220), 1.0f);
        }
    }

    private void DrawTimingAnalysis(
        SpriteBatch spriteBatch,
        RenderHelpers render,
        BitmapTextRenderer text,
        int startMs,
        int endMs,
        float timelineY)
    {
        if (_model is null || _model.TimingAnalysis is null)
        {
            return;
        }

        var analysis = _model.TimingAnalysis;
        if (analysis.SectionDrift.Count == 0 && analysis.HumanizationHeatmap.Count == 0)
        {
            return;
        }

        var driftY = timelineY + 60f;
        var heatY = driftY + 10f;
        var labelColor = new Color(192, 205, 225);

        render.DrawRect(spriteBatch, new Rectangle((int)TimelineX, (int)driftY, (int)TimelineW, 6), new Color(18, 22, 30, 215));
        DrawMenuText(spriteBatch, text, "DRIFT", new Vector2(TimelineX - 42f, driftY - 2f), labelColor, 1.0f);
        foreach (var section in analysis.SectionDrift)
        {
            if (section.EndMs <= startMs || section.StartMs >= endMs)
            {
                continue;
            }

            var sx = (int)MathF.Round(TimelineX + ((section.StartMs - startMs) / (float)Math.Max(1, _timelineWindowMs)) * TimelineW);
            var ex = (int)MathF.Round(TimelineX + ((section.EndMs - startMs) / (float)Math.Max(1, _timelineWindowMs)) * TimelineW);
            var width = Math.Max(1, ex - sx);
            var driftMag = Math.Clamp(Math.Abs((float)section.MeanOffsetMs) / 6f, 0f, 1f);
            var color = section.MeanOffsetMs >= 0d
                ? Color.Lerp(new Color(50, 60, 80), new Color(255, 120, 90), driftMag)
                : Color.Lerp(new Color(50, 60, 80), new Color(90, 170, 255), driftMag);
            render.DrawRect(spriteBatch, new Rectangle(sx, (int)driftY, width, 6), color);
        }

        foreach (var redline in analysis.RedlineSuggestions)
        {
            if (redline.TimeMs < startMs || redline.TimeMs > endMs)
            {
                continue;
            }

            var x = (int)MathF.Round(TimelineX + ((redline.TimeMs - startMs) / (float)Math.Max(1, _timelineWindowMs)) * TimelineW);
            render.DrawRect(spriteBatch, new Rectangle(x, (int)driftY - 3, 1, 14), new Color(255, 70, 70, 220));
        }

        render.DrawRect(spriteBatch, new Rectangle((int)TimelineX, (int)heatY, (int)TimelineW, 6), new Color(18, 22, 30, 215));
        DrawMenuText(spriteBatch, text, "HUMAN", new Vector2(TimelineX - 42f, heatY - 2f), labelColor, 1.0f);
        foreach (var bin in analysis.HumanizationHeatmap)
        {
            if (bin.EndMs <= startMs || bin.StartMs >= endMs)
            {
                continue;
            }

            var sx = (int)MathF.Round(TimelineX + ((bin.StartMs - startMs) / (float)Math.Max(1, _timelineWindowMs)) * TimelineW);
            var ex = (int)MathF.Round(TimelineX + ((bin.EndMs - startMs) / (float)Math.Max(1, _timelineWindowMs)) * TimelineW);
            var width = Math.Max(1, ex - sx);
            var total = Math.Max(1, bin.EarlyCount + bin.LateCount);
            var bias = (bin.LateCount - bin.EarlyCount) / (float)total;
            var strength = Math.Clamp(Math.Abs(bias), 0f, 1f);
            var color = bias >= 0f
                ? Color.Lerp(new Color(55, 65, 85), new Color(250, 154, 94), strength)
                : Color.Lerp(new Color(55, 65, 85), new Color(110, 186, 255), strength);
            render.DrawRect(spriteBatch, new Rectangle(sx, (int)heatY, width, 6), color);
        }

        var instabilityText = $"Timing {analysis.Source} | Instability {analysis.TempoInstabilityPercent:0.00}% | BPM {analysis.EstimatedBpm:0.##}";
        DrawMenuText(spriteBatch, text, instabilityText, new Vector2(TimelineX, heatY + 9f), new Color(196, 208, 226), 1.0f);
    }

    private void DrawToolPanel(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        if (_model is null)
        {
            return;
        }

        var x = _uiPanelPos.X;
        var y = _uiPanelPos.Y;
        var panelHeaderRect = new Rectangle((int)x - 6, (int)y - 24, 420, 20);
        var panelToggleRect = new Rectangle(panelHeaderRect.Right - 18, panelHeaderRect.Y + 2, 14, 14);
        var patternHeight = _patternPanelCollapsed ? 0 :
            72 +
            (_movingPatternDropdownOpen ? PatternVisibleCount * 12 + 4 : 0) +
            (_staticPatternDropdownOpen ? PatternVisibleCount * 12 + 4 : 0) +
            (_shapeDropdownOpen ? PatternVisibleCount * 12 + 4 : 0);
        var songHeight = _songPanelCollapsed ? 0 : (_songDropdownOpen ? SongVisibleCount * 12 + 24 : 24);
        var colorHeight = 270;
        var panelHeight = _toolPanelCollapsed ? 0 : (96 + patternHeight + colorHeight + songHeight);

        render.DrawRect(spriteBatch, panelHeaderRect, new Color(10, 12, 18, 190));
        render.DrawRect(spriteBatch, panelToggleRect, new Color(32, 38, 50, 235));
        DrawMenuText(spriteBatch, text, "Placement Tools", new Vector2(x, y - 20f), new Color(220, 230, 245), 1.5f);
        DrawMenuText(spriteBatch, text, _toolPanelCollapsed ? "+" : "-", new Vector2(panelToggleRect.X + 4f, panelToggleRect.Y + 2f), Color.White, 1.2f);

        if (_toolPanelCollapsed)
        {
            return;
        }

        render.DrawRect(spriteBatch, new Rectangle((int)x - 6, (int)y - 6, 500, panelHeight), new Color(10, 12, 18, 170));

        DrawButton(spriteBatch, render, text, new Rectangle((int)x, (int)y, 120, 20), "Note Tap", _placementTool == PlacementTool.NoteTap);
        DrawButton(spriteBatch, render, text, new Rectangle((int)x, (int)(y + 24f), 120, 20), "Bullet", _placementTool == PlacementTool.Bullet);
        DrawButton(spriteBatch, render, text, new Rectangle((int)(x + 128f), (int)(y + 24f), 120, 20), "Publish JSON", false);

        var movementHeaderY = y + 48f;
        var staticHeaderY = movementHeaderY + 24f + (_movingPatternDropdownOpen ? PatternVisibleCount * 12 + 4 : 0f);
        var staticHeader = new Rectangle((int)x, (int)staticHeaderY, 360, 20);
        var movementHeader = new Rectangle((int)x, (int)movementHeaderY, 360, 20);
        var shapeHeaderY = staticHeaderY + 24f +
            (_staticPatternDropdownOpen ? PatternVisibleCount * 12 + 4 : 0f);
        var shapeHeader = new Rectangle((int)x, (int)shapeHeaderY, 360, 20);
        DrawListHeader(spriteBatch, render, text, staticHeader, $"Static: {_selectedStaticPattern}", _staticPatternDropdownOpen);
        DrawListHeader(spriteBatch, render, text, movementHeader, $"Movement: {_selectedMovementOption}", _movingPatternDropdownOpen);
        DrawListHeader(spriteBatch, render, text, shapeHeader, $"Shape: {_selectedBulletShape}", _shapeDropdownOpen);
        DrawSimpleList(
            spriteBatch,
            render,
            text,
            movementHeader,
            _movingPatternDropdownOpen,
            ref _movingPatternScroll,
            MovementOptions,
            _selectedMovementOption,
            trackPatternHover: true,
            isMovingPatternList: true);
        DrawSimpleList(
            spriteBatch,
            render,
            text,
            staticHeader,
            _staticPatternDropdownOpen,
            ref _staticPatternScroll,
            StaticPatterns,
            _selectedStaticPattern,
            trackPatternHover: true,
            isMovingPatternList: false);
        DrawSimpleList(spriteBatch, render, text, shapeHeader, _shapeDropdownOpen, ref _shapeScroll, BulletShapes, _selectedBulletShape);

        var songHeaderY = shapeHeaderY + 24f + (_shapeDropdownOpen ? PatternVisibleCount * 12 + 4 : 0f);
        var songHeader = new Rectangle((int)x, (int)songHeaderY, 360, 20);
        var songToggleRect = new Rectangle(songHeader.Right - 18, songHeader.Y + 2, 14, 14);
        render.DrawRect(spriteBatch, songHeader, new Color(32, 38, 50, 235));
        render.DrawRect(spriteBatch, new Rectangle(songHeader.X, songHeader.Y, songHeader.Width, 1), new Color(170, 185, 215, 220));
        render.DrawRect(spriteBatch, new Rectangle(songHeader.X, songHeader.Bottom - 1, songHeader.Width, 1), new Color(170, 185, 215, 220));
        render.DrawRect(spriteBatch, songToggleRect, new Color(28, 34, 46, 235));
        var songDisplay = string.IsNullOrWhiteSpace(_selectedSong) ? "(none)" : Path.GetFileName(_selectedSong);
        DrawMenuText(spriteBatch, text, $"Song: {songDisplay}", new Vector2(songHeader.X + 4, songHeader.Y + 4), Color.White, 1.2f);
        DrawMenuText(spriteBatch, text, _songPanelCollapsed ? "+" : "-", new Vector2(songToggleRect.X + 4f, songToggleRect.Y + 2f), Color.White, 1.2f);
        if (!_songPanelCollapsed)
        {
            DrawMenuText(spriteBatch, text, _songDropdownOpen ? "v" : ">", new Vector2(songHeader.Right - 30, songHeader.Y + 4), Color.White, 1.2f);
        }

        if (!_songPanelCollapsed && _songDropdownOpen)
        {
            var listRect = new Rectangle((int)x, (int)(songHeaderY + 22f), 360, SongVisibleCount * 12 + 2);
            render.DrawRect(spriteBatch, listRect, new Color(20, 24, 34, 235));
            var maxScroll = Math.Max(0, _model.SongOptions.Count - SongVisibleCount);
            _songScroll = Math.Clamp(_songScroll, 0, maxScroll);
            var start = _songScroll;
            var visible = Math.Min(SongVisibleCount, _model.SongOptions.Count - start);
            for (var i = 0; i < visible; i++)
            {
                var iy = songHeaderY + 23f + i * 12f;
                var option = _model.SongOptions[start + i];
                var isCurrent = string.Equals(option, _selectedSong, StringComparison.OrdinalIgnoreCase);
                if (isCurrent)
                {
                    render.DrawRect(spriteBatch, new Rectangle((int)x + 1, (int)iy - 1, 358, 12), new Color(55, 88, 130, 210));
                }

                DrawMenuText(spriteBatch, text, Path.GetFileName(option), new Vector2(x + 4f, iy), Color.White, 1.1f);
            }

            if (maxScroll > 0)
            {
                var trackX = (int)x + 362;
                var trackY = (int)(songHeaderY + 22f);
                var trackH = SongVisibleCount * 12 + 2;
                render.DrawRect(spriteBatch, new Rectangle(trackX, trackY, 6, trackH), new Color(36, 44, 58, 220));

                var thumbH = Math.Max(20, (int)MathF.Round(trackH * (SongVisibleCount / (float)_model.SongOptions.Count)));
                var thumbRange = Math.Max(1, trackH - thumbH);
                var thumbY = trackY + (int)MathF.Round((_songScroll / (float)maxScroll) * thumbRange);
                render.DrawRect(spriteBatch, new Rectangle(trackX + 1, thumbY, 4, thumbH), new Color(140, 180, 225, 230));
            }
        }

        var colorY = songHeaderY + 24f + (_songDropdownOpen ? SongVisibleCount * 12 + 10 : 10f);
        var colorButtonRect = new Rectangle((int)(x + 18f), (int)colorY + 24, 130, 22);
        DrawButton(spriteBatch, render, text, colorButtonRect, "Color Wheel", _colorWheelPopupOpen);
        DrawMenuText(spriteBatch, text, $"Color #{_primaryR:X2}{_primaryG:X2}{_primaryB:X2}", new Vector2(x + 18f, colorY), Color.White, 1.1f);
        render.DrawRect(spriteBatch, new Rectangle((int)(x + 150f), (int)colorY + 2, 26, 12), new Color(_primaryR, _primaryG, _primaryB));

        var speedInputRect = new Rectangle((int)(x + 190f), (int)colorY + 32, 160, 22);
        var glowInputRect = new Rectangle((int)(x + 190f), (int)colorY + 72, 160, 22);
        var sizeSliderRect = new Rectangle((int)(x + 190f), (int)colorY + 120, 120, 10);
        var sizeInputRect = new Rectangle(sizeSliderRect.Right + 8, (int)colorY + 114, 42, 22);
        var movementSliderRect = new Rectangle((int)(x + 190f), (int)colorY + 162, 120, 10);
        var movementInputRect = new Rectangle(movementSliderRect.Right + 8, (int)colorY + 156, 42, 22);
        var trajectoryInputRect = new Rectangle((int)(x + 190f), (int)colorY + 196, 86, 22);
        var trajectoryMinusRect = new Rectangle(trajectoryInputRect.Right + 6, trajectoryInputRect.Y, 22, 22);
        var trajectoryPlusRect = new Rectangle(trajectoryMinusRect.Right + 4, trajectoryInputRect.Y, 22, 22);
        var trajectorySnapRect = new Rectangle(trajectoryPlusRect.Right + 6, trajectoryInputRect.Y, 68, 22);
        DrawMenuText(spriteBatch, text, "Bullet Speed", new Vector2(x + 150f, colorY + 18f), new Color(210, 220, 235), 1.05f);
        DrawMenuText(spriteBatch, text, "Glow Intensity", new Vector2(x + 150f, colorY + 58f), new Color(210, 220, 235), 1.05f);
        DrawMenuText(spriteBatch, text, "Bullet Size", new Vector2(x + 150f, colorY + 98f), new Color(210, 220, 235), 1.05f);
        DrawMenuText(spriteBatch, text, "Move Intensity", new Vector2(x + 150f, colorY + 150f), new Color(210, 220, 235), 1.05f);
        DrawMenuText(spriteBatch, text, "Trajectory (deg)", new Vector2(x + 150f, colorY + 190f), new Color(210, 220, 235), 1.05f);
        render.DrawRect(spriteBatch, speedInputRect, _editingBulletSpeed ? new Color(55, 88, 130, 235) : new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, glowInputRect, _editingGlowIntensity ? new Color(55, 88, 130, 235) : new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, sizeInputRect, _editingBulletSize ? new Color(55, 88, 130, 235) : new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, trajectoryInputRect, _editingTrajectoryDeg ? new Color(55, 88, 130, 235) : new Color(30, 36, 50, 235));
        var speedText = _editingBulletSpeed ? _bulletSpeedInput : _bulletSpeed.ToString("0.##", CultureInfo.InvariantCulture);
        var glowText = _editingGlowIntensity ? _glowIntensityInput : _glowIntensity.ToString("0.##");
        var sizeText = _editingBulletSize ? _bulletSizeInput : ((int)MathF.Round(_bulletSize)).ToString();
        var trajectoryText = _editingTrajectoryDeg ? _trajectoryInput : _trajectoryDeg.ToString("0.##", CultureInfo.InvariantCulture);
        DrawMenuText(spriteBatch, text, speedText, new Vector2(speedInputRect.X + 4, speedInputRect.Y + 4), Color.White, 1.1f);
        DrawMenuText(spriteBatch, text, glowText, new Vector2(glowInputRect.X + 4, glowInputRect.Y + 4), Color.White, 1.1f);
        DrawMenuText(spriteBatch, text, trajectoryText, new Vector2(trajectoryInputRect.X + 4, trajectoryInputRect.Y + 4), Color.White, 1.1f);
        render.DrawRect(spriteBatch, sizeSliderRect, new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, new Rectangle(sizeSliderRect.X, sizeSliderRect.Y, sizeSliderRect.Width, 1), new Color(140, 155, 185, 210));
        var sizeT = Math.Clamp((_bulletSize - 2f) / 62f, 0f, 1f);
        var sizeX = sizeSliderRect.X + (int)MathF.Round(sizeT * sizeSliderRect.Width);
        render.DrawRect(spriteBatch, new Rectangle(sizeX - 2, sizeSliderRect.Y - 4, 4, sizeSliderRect.Height + 8), new Color(255, 215, 120, 235));
        DrawMenuText(spriteBatch, text, sizeText, new Vector2(sizeInputRect.X + 4, sizeInputRect.Y + 4), Color.White, 1.05f);
        render.DrawRect(spriteBatch, movementSliderRect, new Color(30, 36, 50, 235));
        render.DrawRect(spriteBatch, new Rectangle(movementSliderRect.X, movementSliderRect.Y, movementSliderRect.Width, 1), new Color(140, 155, 185, 210));
        var moveT = Math.Clamp(_movementIntensity / 2f, 0f, 1f);
        var moveX = movementSliderRect.X + (int)MathF.Round(moveT * movementSliderRect.Width);
        render.DrawRect(spriteBatch, new Rectangle(moveX - 2, movementSliderRect.Y - 4, 4, movementSliderRect.Height + 8), new Color(120, 240, 255, 235));
        render.DrawRect(spriteBatch, movementInputRect, _editingMovementIntensity ? new Color(55, 88, 130, 235) : new Color(30, 36, 50, 235));
        var moveText = _editingMovementIntensity ? _movementIntensityInput : _movementIntensity.ToString("0.00");
        DrawMenuText(spriteBatch, text, moveText, new Vector2(movementInputRect.X + 4f, movementInputRect.Y + 4f), Color.White, 1.05f);
        DrawButton(spriteBatch, render, text, trajectoryMinusRect, "-", false);
        DrawButton(spriteBatch, render, text, trajectoryPlusRect, "+", false);
        DrawButton(spriteBatch, render, text, trajectorySnapRect, _trajectorySnapEnabled ? "Snap ON" : "Snap OFF", _trajectorySnapEnabled);
    }

    private static void DrawListHeader(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, Rectangle rect, string label, bool open)
    {
        render.DrawRect(spriteBatch, rect, new Color(32, 38, 50, 235));
        render.DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(170, 185, 215, 220));
        render.DrawRect(spriteBatch, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(170, 185, 215, 220));
        DrawMenuText(spriteBatch, text, label, new Vector2(rect.X + 4, rect.Y + 4), Color.White, 1.1f);
        DrawMenuText(spriteBatch, text, open ? "v" : ">", new Vector2(rect.Right - 14, rect.Y + 4), Color.White, 1.2f);
    }

    private void DrawSimpleList(
        SpriteBatch spriteBatch,
        RenderHelpers render,
        BitmapTextRenderer text,
        Rectangle header,
        bool open,
        ref int scroll,
        IReadOnlyList<string> source,
        string selected,
        bool trackPatternHover = false,
        bool isMovingPatternList = false)
    {
        if (!open) return;
        var listRect = new Rectangle(header.X, header.Bottom + 2, 360, PatternVisibleCount * 12 + 2);
        render.DrawRect(spriteBatch, listRect, new Color(20, 24, 34, 235));
        var maxScroll = Math.Max(0, source.Count - PatternVisibleCount);
        scroll = Math.Clamp(scroll, 0, maxScroll);
        var start = scroll;
        var visible = Math.Min(PatternVisibleCount, source.Count - start);
        for (var i = 0; i < visible; i++)
        {
            var iy = header.Bottom + 3 + i * 12;
            var value = source[start + i];
            var isCurrent = string.Equals(value, selected, StringComparison.OrdinalIgnoreCase);
            if (isCurrent) render.DrawRect(spriteBatch, new Rectangle(header.X + 1, iy - 1, 358, 12), new Color(55, 88, 130, 210));
            DrawMenuText(spriteBatch, text, $"{start + i + 1:00}. {value}", new Vector2(header.X + 4f, iy), Color.White, 1.05f);
            if (trackPatternHover)
            {
                var rowRect = new Rectangle(header.X + 1, iy - 1, 358, 12);
                if (rowRect.Contains((int)_lastMouseVirtual.X, (int)_lastMouseVirtual.Y))
                {
                    _hoverPatternName = value;
                    _hoverPatternIsMoving = isMovingPatternList;
                    _hoverPatternActive = true;
                }
            }
        }
    }

    private void DrawHoveredPatternPreview(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        if (!_hoverPatternActive || string.IsNullOrWhiteSpace(_hoverPatternName))
        {
            _hoverPreviewSystem.ClearRuntime();
            _hoverPreviewDrawData.Clear();
            _hoverPreviewSignature = string.Empty;
            _hoverPreviewSongMs = 0;
            _hoverPreviewHadAny = false;
            return;
        }

        var panelW = 210;
        var panelH = 154;
        var px = (int)MathF.Round(Math.Clamp(_uiPanelPos.X + 386f, 0f, VirtualWidth - panelW - 4f));
        var py = (int)MathF.Round(Math.Clamp(_uiPanelPos.Y + 40f, 0f, VirtualHeight - panelH - 4f));
        var panelRect = new Rectangle(px, py, panelW, panelH);
        var previewRect = new Rectangle(panelRect.X + 10, panelRect.Y + 34, panelRect.Width - 20, panelRect.Height - 44);

        render.DrawRect(spriteBatch, panelRect, new Color(12, 16, 24, 236));
        render.DrawRect(spriteBatch, new Rectangle(panelRect.X, panelRect.Y, panelRect.Width, 1), new Color(180, 195, 225, 230));
        render.DrawRect(spriteBatch, new Rectangle(panelRect.X, panelRect.Bottom - 1, panelRect.Width, 1), new Color(180, 195, 225, 230));
        render.DrawRect(spriteBatch, previewRect, new Color(24, 30, 44, 236));
        render.DrawRect(spriteBatch, new Rectangle(previewRect.X, previewRect.Y, previewRect.Width, 1), new Color(100, 120, 155, 210));
        render.DrawRect(spriteBatch, new Rectangle(previewRect.X, previewRect.Bottom - 1, previewRect.Width, 1), new Color(100, 120, 155, 210));

        var listType = string.IsNullOrWhiteSpace(_hoverShapeOverride) ? (_hoverPatternIsMoving ? "Moving" : "Static") : "Shape";
        var name = string.IsNullOrWhiteSpace(_hoverShapeOverride) ? _hoverPatternName : _hoverShapeOverride;
        DrawMenuText(spriteBatch, text, $"{listType} Preview", new Vector2(panelRect.X + 8f, panelRect.Y + 8f), new Color(210, 220, 238), 1.1f);
        DrawMenuText(spriteBatch, text, name, new Vector2(panelRect.X + 8f, panelRect.Y + 20f), Color.White, 1.0f);

        UpdateHoverPatternPreviewSimulation(previewRect, _hoverPatternName);
        DrawHoverPreviewBullets(spriteBatch, render);
    }

    private void UpdateHoverPatternPreviewSimulation(Rectangle bounds, string patternName)
    {
        var nowTick = Environment.TickCount64;
        var dt = _hoverPreviewLastTickMs == 0 ? (1f / 60f) : Math.Clamp((nowTick - _hoverPreviewLastTickMs) / 1000f, 0f, 0.04f);
        _hoverPreviewLastTickMs = nowTick;

        var signature = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{patternName}|{_hoverPatternIsMoving}|{_hoverShapeOverride}|{_selectedStaticPattern}|{_selectedMovingPattern}|{_selectedBulletShape}|{_bulletSpeed:0.##}|{_bulletSize:0.##}|{_movementIntensity:0.##}");
        if (!string.Equals(_hoverPreviewSignature, signature, StringComparison.Ordinal))
        {
            _hoverPreviewSignature = signature;
            _hoverPreviewSystem.ClearRuntime();
            _hoverPreviewDrawData.Clear();
            _hoverPreviewSongMs = 0;
            _hoverPreviewHadAny = false;
            SpawnHoverPreviewEvent(patternName);
        }

        _hoverPreviewSongMs += (int)MathF.Round(dt * 1000f);
        _hoverPreviewSystem.Update(dt, _hoverPreviewSongMs, new Vector2(640f, 360f));
        if (_hoverPreviewSystem.ActiveBulletCount > 0)
        {
            _hoverPreviewHadAny = true;
        }

        if (_hoverPreviewHadAny && _hoverPreviewSystem.ActiveBulletCount == 0)
        {
            _hoverPreviewSystem.ClearRuntime();
            _hoverPreviewSongMs = 0;
            _hoverPreviewHadAny = false;
            SpawnHoverPreviewEvent(patternName);
        }

        _hoverPreviewSystem.FillPreviewDrawData(_hoverPreviewDrawData);

        var clipped = 0;
        for (var i = 0; i < _hoverPreviewDrawData.Count; i++)
        {
            var b = _hoverPreviewDrawData[i];
            if (b.Position.X < 0f || b.Position.X > 1280f || b.Position.Y < 0f || b.Position.Y > 720f)
            {
                continue;
            }

            _hoverPreviewDrawData[clipped++] = b;
        }

        if (clipped < _hoverPreviewDrawData.Count)
        {
            _hoverPreviewDrawData.RemoveRange(clipped, _hoverPreviewDrawData.Count - clipped);
        }
    }

    private void SpawnHoverPreviewEvent(string patternName)
    {
        var evt = BuildHoverPreviewBulletEvent(patternName, _hoverPatternIsMoving);
        _hoverPreviewSystem.SpawnImmediate(evt, 0, new Vector2(640f, 360f));
    }

    private BulletEvent BuildHoverPreviewBulletEvent(string patternName, bool hoveringMovingPattern)
    {
        var hovered = string.IsNullOrWhiteSpace(patternName) ? "static_single" : patternName.Trim().ToLowerInvariant();
        var staticBase = string.IsNullOrWhiteSpace(_selectedStaticPattern) ? "static_single" : _selectedStaticPattern.Trim().ToLowerInvariant();
        var p = hoveringMovingPattern ? staticBase : hovered;
        var motionPattern = hoveringMovingPattern ? hovered : _selectedMovingPattern;
        var previewShape = string.IsNullOrWhiteSpace(_hoverShapeOverride) ? _selectedBulletShape : _hoverShapeOverride;
        var count = p switch
        {
            "static_5" => 5,
            "static_10" => 10,
            "static_scatter_5" => 5,
            "static_scatter_10" => 10,
            "static_scatter_20" => 20,
            "static_15" => 15,
            "static_20" => 20,
            "static_25" => 25,
            "static_50" => 50,
            "ring_8" => 8,
            "ring_12" => 12,
            "ring_16" => 16,
            "ring_32" => 32,
            _ when p.Contains("wall") || p.Contains("grid") || p.Contains("lattice") => 20,
            _ when p.Contains("fan") || p.Contains("arc") => 9,
            _ when p.Contains("spiral") || p.Contains("helix") || p.Contains("vortex") || p.Contains("pinwheel") => 18,
            _ when p.Contains("ring") || p.Contains("radial") || p.Contains("flower") || p.Contains("star") => 16,
            _ => 12
        };
        var speed = Math.Clamp(_bulletSpeed, 0.1f, 520f);

        return new BulletEvent
        {
            TimeMs = 0,
            Pattern = p,
            X = 0.5f,
            Y = 0.28f,
            Count = count,
            Speed = speed,
            DirectionDeg = 90f,
            BulletType = previewShape,
            BulletSize = SnapBulletSize(_bulletSize),
            OutlineThickness = 2f,
            SpreadDeg = p.Contains("fan") || p.Contains("arc") ? 70f : (float?)null,
            IntervalMs = p.Contains("spiral") || p.Contains("helix") || p.Contains("vortex") ? 55 : 80,
            AngleStepDeg = p.Contains("spiral") || p.Contains("helix") || p.Contains("vortex") ? 12f : (float?)null,
            MotionPattern = string.IsNullOrWhiteSpace(motionPattern) ? null : motionPattern,
            MovementIntensity = _movementIntensity,
            Color = $"#{_primaryR:X2}{_primaryG:X2}{_primaryB:X2}",
            OutlineColor = $"#{_outlineR:X2}{_outlineG:X2}{_outlineB:X2}",
            GlowColor = $"#{_glowR:X2}{_glowG:X2}{_glowB:X2}",
            GlowIntensity = _glowIntensity
        };
    }

    private void DrawHoverPreviewBullets(SpriteBatch spriteBatch, RenderHelpers render)
    {
        var panelW = 210;
        var panelH = 154;
        var px = (int)MathF.Round(Math.Clamp(_uiPanelPos.X + 386f, 0f, VirtualWidth - panelW - 4f));
        var py = (int)MathF.Round(Math.Clamp(_uiPanelPos.Y + 40f, 0f, VirtualHeight - panelH - 4f));
        var previewRect = new Rectangle(px + 10, py + 34, panelW - 20, panelH - 44);
        var sx = previewRect.Width / 1280f;
        var sy = previewRect.Height / 720f;
        var s = MathF.Min(sx, sy);

        for (var i = 0; i < _hoverPreviewDrawData.Count; i++)
        {
            var b = _hoverPreviewDrawData[i];
            var x = previewRect.X + b.Position.X * sx;
            var y = previewRect.Y + b.Position.Y * sy;
            if (x < previewRect.Left + 1 || x > previewRect.Right - 1 || y < previewRect.Top + 1 || y > previewRect.Bottom - 1)
            {
                continue;
            }

            var rr = Math.Clamp(b.Radius * b.Scale * s, 1.4f, 5.2f);
            render.DrawCircleFilled(spriteBatch, new Vector2(x, y), rr, b.Fill);
            render.DrawCircleOutline(spriteBatch, new Vector2(x, y), rr, Math.Max(1f, b.OutlineThickness * 0.5f), b.Outline);
        }
    }

    private void DrawPreviewMarks(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, int virtualWidth, int virtualHeight)
    {
        if (_model is null)
        {
            return;
        }

        var now = _model.CurrentTimeMs;
        var startMs = _timelineViewStartMs;
        EnsurePreviewMarksWindowCache(startMs);
        var timelineY = GetTimelineY();
        var visibleMarks = _cachedVisiblePreviewMarks;
        var heavyMode = _cachedPreviewHeavyMode;
        if (heavyMode)
        {
            DrawPreviewMarksBatchedTimeline(spriteBatch, render, timelineY);
        }
        else
        {
            foreach (var mark in visibleMarks)
            {
                var startX = TimelineX + ((mark.StartMs - startMs) / (float)_timelineWindowMs) * TimelineW;
                var endX = TimelineX + ((mark.EndMs - startMs) / (float)_timelineWindowMs) * TimelineW;

                var segmentColor = mark.Kind == LevelEditorConstants.EventKindBullet
                    ? new Color(255, 130, 130, 150)
                    : new Color(130, 210, 255, 150);
                var sx = (int)Math.Clamp(startX, TimelineX, TimelineX + TimelineW);
                var ex = (int)Math.Clamp(endX, TimelineX, TimelineX + TimelineW);
                render.DrawRect(spriteBatch, new Rectangle(sx, (int)timelineY - 5, Math.Max(2, ex - sx), 3), segmentColor);
                var startMarkerX = (int)Math.Clamp(startX, TimelineX, TimelineX + TimelineW);
                render.DrawRect(spriteBatch, new Rectangle(startMarkerX - 1, (int)timelineY - 8, 2, 7), new Color(255, 255, 255, 170));
            }
        }

        var activeWorldDrawn = 0;
        var activeLabelDrawn = 0;
        for (var i = 0; i < visibleMarks.Count; i++)
        {
            var mark = visibleMarks[i];
            if (now < mark.StartMs || now > mark.EndMs)
            {
                continue;
            }

            // Bullet placement world markers are short-lived to reduce editor clutter.
            if (mark.Kind == LevelEditorConstants.EventKindBullet &&
                now > mark.StartMs + BulletPlacementMarkerLifetimeMs)
            {
                continue;
            }

            if (activeWorldDrawn >= MaxActiveWorldMarkersDrawn)
            {
                break;
            }

            var lifeT = (now - mark.StartMs) / (float)Math.Max(1, mark.EndMs - mark.StartMs);
            var alpha = 1f - MathF.Min(1f, lifeT * 0.85f);
            var px = mark.X * virtualWidth;
            var py = mark.Y * virtualHeight;
            var c = mark.Kind == LevelEditorConstants.EventKindBullet
                ? new Color(255, 100, 100) * alpha
                : new Color(100, 220, 255) * alpha;

            render.DrawCircleOutline(spriteBatch, new Vector2(px, py), 12f, 2f, c);
            render.DrawCircleFilled(spriteBatch, new Vector2(px, py), 3.8f, Color.White * alpha);
            activeWorldDrawn++;

            if (!heavyMode && activeLabelDrawn < MaxActiveLabelDrawn)
            {
                DrawMenuText(spriteBatch, text, mark.Label, new Vector2(px + 10f, py - 8f), Color.White * alpha, 1.2f);
                activeLabelDrawn++;
            }
        }
    }

    private void EnsurePreviewMarksWindowCache(int startMs)
    {
        if (_model is null)
        {
            return;
        }

        var source = _model.PreviewMarks;
        if (ReferenceEquals(source, _cachedPreviewMarksSource) &&
            _cachedPreviewStartMs == startMs &&
            _cachedPreviewWindowMs == _timelineWindowMs)
        {
            return;
        }

        _cachedPreviewMarksSource = source;
        _cachedPreviewStartMs = startMs;
        _cachedPreviewWindowMs = _timelineWindowMs;
        _cachedVisiblePreviewMarks.Clear();
        Array.Clear(_cachedPreviewBulletBins, 0, _cachedPreviewBulletBins.Length);
        Array.Clear(_cachedPreviewNoteBins, 0, _cachedPreviewNoteBins.Length);

        var endMs = startMs + _timelineWindowMs;
        for (var i = 0; i < source.Count; i++)
        {
            var mark = source[i];
            if (mark.EndMs < startMs || mark.StartMs > endMs)
            {
                continue;
            }

            _cachedVisiblePreviewMarks.Add(mark);
        }

        _cachedPreviewHeavyMode = _cachedVisiblePreviewMarks.Count >= HeavyPreviewMarkThreshold;
        if (!_cachedPreviewHeavyMode)
        {
            return;
        }

        for (var i = 0; i < _cachedVisiblePreviewMarks.Count; i++)
        {
            var mark = _cachedVisiblePreviewMarks[i];
            var clampedStart = Math.Clamp(mark.StartMs, startMs, endMs);
            var clampedEnd = Math.Clamp(mark.EndMs, startMs, endMs);
            if (clampedEnd < clampedStart)
            {
                continue;
            }

            var b0 = (int)(((long)(clampedStart - startMs) * PreviewTimelineBins) / Math.Max(1, _timelineWindowMs));
            var b1 = (int)(((long)(clampedEnd - startMs) * PreviewTimelineBins) / Math.Max(1, _timelineWindowMs));
            b0 = Math.Clamp(b0, 0, PreviewTimelineBins - 1);
            b1 = Math.Clamp(b1, 0, PreviewTimelineBins - 1);

            for (var b = b0; b <= b1; b++)
            {
                if (mark.Kind == LevelEditorConstants.EventKindBullet)
                {
                    if (_cachedPreviewBulletBins[b] < byte.MaxValue) _cachedPreviewBulletBins[b]++;
                }
                else
                {
                    if (_cachedPreviewNoteBins[b] < byte.MaxValue) _cachedPreviewNoteBins[b]++;
                }
            }
        }
    }

    private void DrawPreviewMarksBatchedTimeline(SpriteBatch spriteBatch, RenderHelpers render, float timelineY)
    {
        var binW = TimelineW / PreviewTimelineBins;
        for (var i = 0; i < PreviewTimelineBins; i++)
        {
            var x = (int)MathF.Round(TimelineX + i * binW);
            var w = Math.Max(1, (int)MathF.Ceiling(binW));
            if (_cachedPreviewBulletBins[i] > 0)
            {
                var a = Math.Clamp(80 + _cachedPreviewBulletBins[i] * 2, 80, 190);
                render.DrawRect(spriteBatch, new Rectangle(x, (int)timelineY - 5, w, 3), new Color(255, 130, 130, a));
            }
            if (_cachedPreviewNoteBins[i] > 0)
            {
                var a = Math.Clamp(80 + _cachedPreviewNoteBins[i] * 2, 80, 190);
                render.DrawRect(spriteBatch, new Rectangle(x, (int)timelineY - 10, w, 3), new Color(130, 210, 255, a));
            }
        }
    }

    private static void DrawButton(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text, Rectangle rect, string label, bool selected)
    {
        var fill = selected ? new Color(60, 105, 150, 235) : new Color(32, 38, 50, 235);
        render.DrawRect(spriteBatch, rect, fill);
        render.DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(170, 185, 215, 220));
        render.DrawRect(spriteBatch, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(170, 185, 215, 220));
        DrawMenuText(spriteBatch, text, label, new Vector2(rect.X + 6, rect.Y + 4), Color.White, 1.2f);
    }

    private static void DrawMenuText(SpriteBatch spriteBatch, BitmapTextRenderer text, string value, Vector2 position, Color color, float scale = 1f)
    {
        var o = MathF.Max(1f, MathF.Round(scale * 0.9f));
        var outline = Color.Black * Math.Clamp(color.A / 255f, 0.55f, 1f);
        text.DrawString(spriteBatch, value, position + new Vector2(-o, 0f), outline, scale);
        text.DrawString(spriteBatch, value, position + new Vector2(o, 0f), outline, scale);
        text.DrawString(spriteBatch, value, position + new Vector2(0f, -o), outline, scale);
        text.DrawString(spriteBatch, value, position + new Vector2(0f, o), outline, scale);
        text.DrawString(spriteBatch, value, position + new Vector2(-o, -o), outline * 0.85f, scale);
        text.DrawString(spriteBatch, value, position + new Vector2(o, -o), outline * 0.85f, scale);
        text.DrawString(spriteBatch, value, position + new Vector2(-o, o), outline * 0.85f, scale);
        text.DrawString(spriteBatch, value, position + new Vector2(o, o), outline * 0.85f, scale);
        text.DrawString(spriteBatch, value, position, color, scale);
    }

    private static void DrawPlusMinusGlyph(SpriteBatch spriteBatch, RenderHelpers render, Rectangle rect, bool plus)
    {
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        render.DrawRect(spriteBatch, new Rectangle(cx - 4, cy - 1, 8, 2), Color.White);
        if (plus)
        {
            render.DrawRect(spriteBatch, new Rectangle(cx - 1, cy - 4, 2, 8), Color.White);
        }
    }

    private void DrawColorWheelPopup(SpriteBatch spriteBatch, RenderHelpers render, BitmapTextRenderer text)
    {
        if (!_colorWheelPopupOpen)
        {
            return;
        }

        var popupRect = GetColorPopupRect();
        var wheelCenter = new Vector2(popupRect.X + 120f, popupRect.Y + 118f);
        const float wheelOuterR = 88f;
        const float wheelInnerR = 40f;
        var hexRect = new Rectangle(popupRect.X + 236, popupRect.Y + 82, 132, 24);
        var applyRect = new Rectangle(popupRect.X + 236, popupRect.Y + 112, 64, 22);
        var closeRect = new Rectangle(popupRect.X + 306, popupRect.Y + 112, 62, 22);

        render.DrawRect(spriteBatch, new Rectangle(0, 0, (int)VirtualWidth, (int)VirtualHeight), new Color(0, 0, 0, 120));
        render.DrawRect(spriteBatch, popupRect, new Color(14, 18, 26, 245));
        render.DrawRect(spriteBatch, new Rectangle(popupRect.X, popupRect.Y, popupRect.Width, 1), new Color(180, 195, 225, 230));
        render.DrawRect(spriteBatch, new Rectangle(popupRect.X, popupRect.Bottom - 1, popupRect.Width, 1), new Color(180, 195, 225, 230));
        DrawMenuText(spriteBatch, text, "Color Wheel", new Vector2(popupRect.X + 14f, popupRect.Y + 10f), Color.White, 1.6f);
        DrawColorWheel(spriteBatch, render, wheelCenter, wheelInnerR, wheelOuterR);
        var markerA = _wheelHue * MathHelper.TwoPi;
        var markerR = wheelInnerR + _wheelSat * (wheelOuterR - wheelInnerR);
        var marker = wheelCenter + new Vector2(MathF.Cos(markerA), MathF.Sin(markerA)) * markerR;
        render.DrawCircleOutline(spriteBatch, marker, 6f, 2f, Color.White);

        DrawMenuText(spriteBatch, text, "Hex", new Vector2(hexRect.X, hexRect.Y - 16f), new Color(210, 220, 235), 1.1f);
        DrawMenuText(spriteBatch, text, "Snap: 10deg hue", new Vector2(hexRect.X, hexRect.Y - 30f), new Color(175, 188, 212), 1.0f);
        render.DrawRect(spriteBatch, hexRect, _editingHexColor ? new Color(55, 88, 130, 235) : new Color(30, 36, 50, 235));
        var hexText = _editingHexColor ? _hexColorInput : $"{_primaryR:X2}{_primaryG:X2}{_primaryB:X2}";
        DrawMenuText(spriteBatch, text, $"#{hexText}", new Vector2(hexRect.X + 6f, hexRect.Y + 4f), Color.White, 1.2f);
        DrawButton(spriteBatch, render, text, applyRect, "Apply", false);
        DrawButton(spriteBatch, render, text, closeRect, "Close", false);
        render.DrawRect(spriteBatch, new Rectangle(popupRect.X + 236, popupRect.Y + 146, 132, 22), new Color(_primaryR, _primaryG, _primaryB));
    }

    private IReadOnlyDictionary<string, double> BuildBulletParameterPayload()
    {
        CommitNumericFieldEdits();
        var shapeIndex = Array.IndexOf(BulletShapes, _selectedBulletShape);
        if (shapeIndex < 0) shapeIndex = 0;
        _selectedMovingPattern = MapMovementOptionToPattern(_selectedMovementOption);
        var motionIndex = string.IsNullOrWhiteSpace(_selectedMovingPattern) ? -1 : Array.IndexOf(MovingPatterns, _selectedMovingPattern);
        if (motionIndex < 0 && !string.IsNullOrWhiteSpace(_selectedMovingPattern)) motionIndex = 0;
        var payload = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["speed"] = _bulletSpeed,
            ["count"] = 12,
            ["directionDeg"] = _trajectoryDeg,
            ["trajectoryDeg"] = _trajectoryDeg,
            ["bulletSize"] = SnapBulletSize(_bulletSize),
            ["shapeId"] = shapeIndex,
            ["primaryR"] = _primaryR,
            ["primaryG"] = _primaryG,
            ["primaryB"] = _primaryB,
            ["outlineR"] = _outlineR,
            ["outlineG"] = _outlineG,
            ["outlineB"] = _outlineB,
            ["glowR"] = _glowR,
            ["glowG"] = _glowG,
            ["glowB"] = _glowB,
            ["glowIntensity"] = _glowIntensity,
            ["movementIntensity"] = _movementIntensity,
            ["noMotion"] = string.IsNullOrWhiteSpace(_selectedMovingPattern) ? 1d : 0d
        };
        if (motionIndex >= 0)
        {
            payload["motionPatternId"] = motionIndex;
        }
        return payload;
    }

    private static Rectangle GetColorPopupRect()
    {
        return new Rectangle(320, 170, 390, 260);
    }

    private static string MapMovementOptionToPattern(string option)
    {
        return option switch
        {
            "none" => string.Empty,
            "fountain" => "fountain_arc",
            "fountain_bounce" => "fountain_bounce",
            "left_drift" => "left_drift",
            "right_drift" => "right_drift",
            "left_to_right" => "sinusoidal_path_deviation",
            "track_mouse" => "mouse_track",
            "shoot_at_mouse" => "shoot_at_mouse",
            _ => "uniform_outward_drift"
        };
    }

    private void HandleHexColorTyping(InputState input)
    {
        AppendHexIfPressed(input, Keys.D0, '0'); AppendHexIfPressed(input, Keys.NumPad0, '0');
        AppendHexIfPressed(input, Keys.D1, '1'); AppendHexIfPressed(input, Keys.NumPad1, '1');
        AppendHexIfPressed(input, Keys.D2, '2'); AppendHexIfPressed(input, Keys.NumPad2, '2');
        AppendHexIfPressed(input, Keys.D3, '3'); AppendHexIfPressed(input, Keys.NumPad3, '3');
        AppendHexIfPressed(input, Keys.D4, '4'); AppendHexIfPressed(input, Keys.NumPad4, '4');
        AppendHexIfPressed(input, Keys.D5, '5'); AppendHexIfPressed(input, Keys.NumPad5, '5');
        AppendHexIfPressed(input, Keys.D6, '6'); AppendHexIfPressed(input, Keys.NumPad6, '6');
        AppendHexIfPressed(input, Keys.D7, '7'); AppendHexIfPressed(input, Keys.NumPad7, '7');
        AppendHexIfPressed(input, Keys.D8, '8'); AppendHexIfPressed(input, Keys.NumPad8, '8');
        AppendHexIfPressed(input, Keys.D9, '9'); AppendHexIfPressed(input, Keys.NumPad9, '9');
        AppendHexIfPressed(input, Keys.A, 'A');
        AppendHexIfPressed(input, Keys.B, 'B');
        AppendHexIfPressed(input, Keys.C, 'C');
        AppendHexIfPressed(input, Keys.D, 'D');
        AppendHexIfPressed(input, Keys.E, 'E');
        AppendHexIfPressed(input, Keys.F, 'F');

        if (input.IsKeyPressed(Keys.Back) && _hexColorInput.Length > 0)
        {
            _hexColorInput = _hexColorInput[..^1];
        }

        if (input.IsKeyPressed(Keys.Enter))
        {
            ApplyHexColorInput();
            _editingHexColor = false;
        }

        if (input.IsKeyPressed(Keys.Escape))
        {
            _editingHexColor = false;
            _hexColorInput = $"{_primaryR:X2}{_primaryG:X2}{_primaryB:X2}";
        }
    }

    private static void AppendHexIfPressed(InputState input, Keys key, char c, ref string value)
    {
        if (input.IsKeyPressed(key) && value.Length < 6)
        {
            value += c;
        }
    }

    private void AppendHexIfPressed(InputState input, Keys key, char c)
    {
        AppendHexIfPressed(input, key, c, ref _hexColorInput);
    }

    private void ApplyHexColorInput()
    {
        if (!TryParseHexColor(_hexColorInput, out var color))
        {
            return;
        }

        _primaryR = color.R;
        _primaryG = color.G;
        _primaryB = color.B;
        ColorToHsv(color, out _wheelHue, out _wheelSat, out _);
        SnapWheelSelection(ref _wheelHue, ref _wheelSat);
        var snapped = HsvToColor(_wheelHue, _wheelSat, 1f);
        _primaryR = snapped.R;
        _primaryG = snapped.G;
        _primaryB = snapped.B;
        var c = new Color(_primaryR, _primaryG, _primaryB);
        _glowR = (byte)Math.Clamp((int)MathF.Round(Color.Lerp(c, Color.White, 0.35f).R), 0, 255);
        _glowG = (byte)Math.Clamp((int)MathF.Round(Color.Lerp(c, Color.White, 0.35f).G), 0, 255);
        _glowB = (byte)Math.Clamp((int)MathF.Round(Color.Lerp(c, Color.White, 0.35f).B), 0, 255);
        _hexColorInput = $"{_primaryR:X2}{_primaryG:X2}{_primaryB:X2}";
    }

    private static bool TryParseHexColor(string raw, out Color color)
    {
        color = Color.White;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var hex = raw.Trim().TrimStart('#');
        if (hex.Length != 6)
        {
            return false;
        }

        try
        {
            var r = Convert.ToByte(hex[0..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            color = new Color(r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SnapWheelSelection(ref float hue, ref float sat)
    {
        var hSteps = Math.Max(1, ColorWheelHueSnapSteps);
        var sSteps = Math.Max(1, ColorWheelSatSnapSteps);
        hue = MathF.Round(hue * hSteps) / hSteps;
        sat = MathF.Round(sat * sSteps) / sSteps;
        hue = (hue % 1f + 1f) % 1f;
        sat = Math.Clamp(sat, 0f, 1f);
    }

    private static void ColorToHsv(Color color, out float h, out float s, out float v)
    {
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;

        h = 0f;
        if (delta > 0.0001f)
        {
            if (MathF.Abs(max - r) < 0.0001f)
            {
                h = ((g - b) / delta) % 6f;
            }
            else if (MathF.Abs(max - g) < 0.0001f)
            {
                h = ((b - r) / delta) + 2f;
            }
            else
            {
                h = ((r - g) / delta) + 4f;
            }

            h /= 6f;
            if (h < 0f) h += 1f;
        }

        s = max <= 0f ? 0f : delta / max;
        v = max;
    }

    private void HandleNumericFieldTyping(InputState input)
    {
        if (_editingBulletSpeed)
        {
            HandleNumericInputString(input, ref _bulletSpeedInput, allowDecimal: true);
        }
        else if (_editingGlowIntensity)
        {
            HandleNumericInputString(input, ref _glowIntensityInput, allowDecimal: true);
        }
        else if (_editingBulletSize)
        {
            HandleNumericInputString(input, ref _bulletSizeInput, allowDecimal: false);
        }
        else if (_editingMovementIntensity)
        {
            HandleNumericInputString(input, ref _movementIntensityInput, allowDecimal: true);
        }
        else if (_editingTrajectoryDeg)
        {
            HandleNumericInputString(input, ref _trajectoryInput, allowDecimal: true);
        }

        if (input.IsKeyPressed(Keys.Enter))
        {
            CommitNumericFieldEdits();
            _editingBulletSpeed = false;
            _editingGlowIntensity = false;
            _editingBulletSize = false;
            _editingMovementIntensity = false;
            _editingTrajectoryDeg = false;
            return;
        }

        if (input.IsKeyPressed(Keys.Escape))
        {
            _bulletSpeedInput = _bulletSpeed.ToString("0.##", CultureInfo.InvariantCulture);
            _glowIntensityInput = _glowIntensity.ToString("0.##");
            _bulletSizeInput = ((int)MathF.Round(_bulletSize)).ToString();
            _movementIntensityInput = _movementIntensity.ToString("0.00");
            _trajectoryInput = _trajectoryDeg.ToString("0.##", CultureInfo.InvariantCulture);
            _editingBulletSpeed = false;
            _editingGlowIntensity = false;
            _editingBulletSize = false;
            _editingMovementIntensity = false;
            _editingTrajectoryDeg = false;
        }
    }

    private void CommitNumericFieldEdits()
    {
        if (TryParseFloatInvariant(_bulletSpeedInput, out var speedParsed))
        {
            _bulletSpeed = Math.Clamp(speedParsed, 0.1f, 2000f);
        }

        if (float.TryParse(_glowIntensityInput, out var glowParsed))
        {
            _glowIntensity = Math.Clamp(glowParsed, 0f, 2f);
        }

        if (int.TryParse(_bulletSizeInput, out var sizeParsed))
        {
            _bulletSize = SnapBulletSize(Math.Clamp(sizeParsed, 2f, 64f));
        }

        if (float.TryParse(_movementIntensityInput, out var movementParsed))
        {
            _movementIntensity = Math.Clamp(movementParsed, 0f, 2f);
        }

        if (TryParseFloatInvariant(_trajectoryInput, out var trajectoryParsed))
        {
            _trajectoryDeg = NormalizeDegrees(trajectoryParsed);
            if (_trajectorySnapEnabled)
            {
                _trajectoryDeg = SnapDegrees(_trajectoryDeg, TrajectorySnapStepDeg);
            }
        }

        _bulletSpeedInput = _bulletSpeed.ToString("0.##", CultureInfo.InvariantCulture);
        _glowIntensityInput = _glowIntensity.ToString("0.##");
        _bulletSizeInput = ((int)MathF.Round(_bulletSize)).ToString();
        _movementIntensityInput = _movementIntensity.ToString("0.00");
        _trajectoryInput = _trajectoryDeg.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static float SnapBulletSize(float raw)
    {
        var clamped = Math.Clamp(raw, 2f, 64f);
        return MathF.Round(clamped * 0.5f) * 2f;
    }

    private void HandleNumericInputString(InputState input, ref string value, bool allowDecimal)
    {
        AppendIfPressed(input, Keys.D0, ref value, '0');
        AppendIfPressed(input, Keys.D1, ref value, '1');
        AppendIfPressed(input, Keys.D2, ref value, '2');
        AppendIfPressed(input, Keys.D3, ref value, '3');
        AppendIfPressed(input, Keys.D4, ref value, '4');
        AppendIfPressed(input, Keys.D5, ref value, '5');
        AppendIfPressed(input, Keys.D6, ref value, '6');
        AppendIfPressed(input, Keys.D7, ref value, '7');
        AppendIfPressed(input, Keys.D8, ref value, '8');
        AppendIfPressed(input, Keys.D9, ref value, '9');
        AppendIfPressed(input, Keys.NumPad0, ref value, '0');
        AppendIfPressed(input, Keys.NumPad1, ref value, '1');
        AppendIfPressed(input, Keys.NumPad2, ref value, '2');
        AppendIfPressed(input, Keys.NumPad3, ref value, '3');
        AppendIfPressed(input, Keys.NumPad4, ref value, '4');
        AppendIfPressed(input, Keys.NumPad5, ref value, '5');
        AppendIfPressed(input, Keys.NumPad6, ref value, '6');
        AppendIfPressed(input, Keys.NumPad7, ref value, '7');
        AppendIfPressed(input, Keys.NumPad8, ref value, '8');
        AppendIfPressed(input, Keys.NumPad9, ref value, '9');

        if (allowDecimal && (input.IsKeyPressed(Keys.Decimal) || input.IsKeyPressed(Keys.OemPeriod)) && !value.Contains('.'))
        {
            value = value.Length == 0 ? "0." : value + ".";
        }

        if (input.IsKeyPressed(Keys.Back) && value.Length > 0)
        {
            value = value[..^1];
        }
    }

    private static void AppendIfPressed(InputState input, Keys key, ref string value, char c)
    {
        if (!input.IsKeyPressed(key))
        {
            return;
        }

        if (value.Length >= 8)
        {
            return;
        }

        if (value == "0")
        {
            value = c.ToString();
        }
        else
        {
            value += c;
        }
    }

    private void NudgeTrajectory(float deltaDeg)
    {
        CommitNumericFieldEdits();
        _trajectoryDeg = NormalizeDegrees(_trajectoryDeg + deltaDeg);
        if (_trajectorySnapEnabled)
        {
            _trajectoryDeg = SnapDegrees(_trajectoryDeg, TrajectorySnapStepDeg);
        }
        _trajectoryInput = _trajectoryDeg.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static float NormalizeDegrees(float deg)
    {
        var d = deg % 360f;
        if (d < 0f)
        {
            d += 360f;
        }
        return d;
    }

    private static float SnapDegrees(float deg, float stepDeg)
    {
        var step = Math.Max(0.001f, stepDeg);
        return NormalizeDegrees(MathF.Round(deg / step) * step);
    }

    private static bool TryParseFloatInvariant(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
               float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static Color HsvToColor(float h, float s, float v)
    {
        h = (h % 1f + 1f) % 1f;
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        var i = (int)MathF.Floor(h * 6f);
        var f = h * 6f - i;
        var p = v * (1f - s);
        var q = v * (1f - f * s);
        var t = v * (1f - (1f - f) * s);
        (float r, float g, float b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q)
        };
        return new Color((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f));
    }

    private void DrawColorWheel(SpriteBatch spriteBatch, RenderHelpers render, Vector2 center, float innerRadius, float outerRadius)
    {
        const int segments = 72;
        for (var i = 0; i < segments; i++)
        {
            var a0 = i / (float)segments * MathHelper.TwoPi;
            var a1 = (i + 1) / (float)segments * MathHelper.TwoPi;
            var hue = i / (float)segments;
            var color = HsvToColor(hue, 1f, 1f);
            var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * ((innerRadius + outerRadius) * 0.5f);
            var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * ((innerRadius + outerRadius) * 0.5f);
            render.DrawLine(spriteBatch, p0, p1, outerRadius - innerRadius, color);
        }

        render.DrawCircleOutline(spriteBatch, center, outerRadius, 1f, Color.Black * 0.7f);
        render.DrawCircleOutline(spriteBatch, center, innerRadius, 1f, Color.Black * 0.7f);
    }

    private void HandleTimelineWindowTyping(InputState input)
    {
        AppendIfPressed(input, Keys.D0, '0');
        AppendIfPressed(input, Keys.D1, '1');
        AppendIfPressed(input, Keys.D2, '2');
        AppendIfPressed(input, Keys.D3, '3');
        AppendIfPressed(input, Keys.D4, '4');
        AppendIfPressed(input, Keys.D5, '5');
        AppendIfPressed(input, Keys.D6, '6');
        AppendIfPressed(input, Keys.D7, '7');
        AppendIfPressed(input, Keys.D8, '8');
        AppendIfPressed(input, Keys.D9, '9');
        AppendIfPressed(input, Keys.NumPad0, '0');
        AppendIfPressed(input, Keys.NumPad1, '1');
        AppendIfPressed(input, Keys.NumPad2, '2');
        AppendIfPressed(input, Keys.NumPad3, '3');
        AppendIfPressed(input, Keys.NumPad4, '4');
        AppendIfPressed(input, Keys.NumPad5, '5');
        AppendIfPressed(input, Keys.NumPad6, '6');
        AppendIfPressed(input, Keys.NumPad7, '7');
        AppendIfPressed(input, Keys.NumPad8, '8');
        AppendIfPressed(input, Keys.NumPad9, '9');

        if (input.IsKeyPressed(Keys.Back) && _timelineWindowInput.Length > 0)
        {
            _timelineWindowInput = _timelineWindowInput[..^1];
        }

        if (input.IsKeyPressed(Keys.Enter))
        {
            if (int.TryParse(_timelineWindowInput, out var parsed))
            {
                var totalMs = GetTimelineTotalMs();
                var newWindow = Math.Clamp(parsed, MinTimelineWindowMs, Math.Min(MaxTimelineWindowMs, totalMs));
                ApplyTimelineWindowAroundFocus(newWindow, totalMs);
            }

            _timelineWindowInput = _timelineWindowMs.ToString();
            _editingTimelineWindowMs = false;
            _timelineZoomTouched = true;
        }

        if (input.IsKeyPressed(Keys.Escape))
        {
            _timelineWindowInput = _timelineWindowMs.ToString();
            _editingTimelineWindowMs = false;
        }
    }

    private void AppendIfPressed(InputState input, Keys key, char c)
    {
        if (input.IsKeyPressed(key))
        {
            if (_timelineWindowInput.Length >= 7)
            {
                return;
            }

            if (_timelineWindowInput == "0")
            {
                _timelineWindowInput = c.ToString();
            }
            else
            {
                _timelineWindowInput += c;
            }
        }
    }

    private int ScaledSeekDelta(int baseDelta)
    {
        var scale = SlowMoScales[Math.Clamp(_slowMoIndex, 0, SlowMoScales.Length - 1)];
        var scaled = (int)MathF.Round(baseDelta * scale);
        if (scaled == 0)
        {
            return Math.Sign(baseDelta);
        }

        return scaled;
    }

    private float GetTimelineY()
    {
        if (_dragTimeline)
        {
            return Math.Clamp(_timelineDragY, TimelineTopY, TimelineBottomY);
        }

        return _timelineAtBottom ? TimelineBottomY : TimelineTopY;
    }

    private static Vector2 ClampPanelToViewport(Vector2 pos, float panelHeight)
    {
        var clampedX = Math.Clamp(pos.X, 0f, VirtualWidth - 500f);
        var clampedY = Math.Clamp(pos.Y, 24f, VirtualHeight - panelHeight);
        return new Vector2(clampedX, clampedY);
    }

    private int GetTimelineTotalMs()
    {
        if (_model is null)
        {
            return _timelineWindowMs;
        }

        var songDuration = Math.Max(0, _model.SongDurationMs);
        var noSongFallbackMs = songDuration > 0 ? 0 : MinTimelineTotalMsNoSong;
        var maxEntity = Math.Max(_model.LastEntityEndMs, _model.CurrentTimeMs);
        var visibleEnd = _timelineViewStartMs + _timelineWindowMs;
        var timelineEnd = Math.Max(_model.TimelineEndMs, Math.Max(noSongFallbackMs, Math.Max(songDuration, Math.Max(maxEntity, visibleEnd))));
        return Math.Max(MinTimelineWindowMs, timelineEnd);
    }

    private static Rectangle GetTimelineNavTrackRect(float timelineY)
    {
        return new Rectangle((int)TimelineX, (int)(timelineY + 34f), (int)TimelineW, 8);
    }

    private static Rectangle GetTimelineZoomTrackRect(float timelineY)
    {
        return new Rectangle((int)(TimelineX + TimelineW - TimelineZoomTrackW), (int)(timelineY + 46f), (int)TimelineZoomTrackW, 8);
    }

    private Rectangle GetTimelineNavThumbRect(Rectangle trackRect, int totalMs)
    {
        var maxStart = Math.Max(0, totalMs - _timelineWindowMs);
        if (maxStart <= 0)
        {
            return new Rectangle(trackRect.X, trackRect.Y, trackRect.Width, trackRect.Height);
        }

        var visibleFraction = Math.Clamp(_timelineWindowMs / (float)Math.Max(1, totalMs), 0.01f, 1f);
        var thumbWidth = Math.Clamp((int)MathF.Round(trackRect.Width * visibleFraction), 30, trackRect.Width);
        var travel = Math.Max(1, trackRect.Width - thumbWidth);
        var startT = Math.Clamp(_timelineViewStartMs / (float)maxStart, 0f, 1f);
        var thumbX = trackRect.X + (int)MathF.Round(startT * travel);
        return new Rectangle(thumbX, trackRect.Y, thumbWidth, trackRect.Height);
    }

    private Rectangle GetTimelineZoomThumbRect(Rectangle trackRect, int totalMs)
    {
        var maxWindow = Math.Max(MinTimelineWindowMs, Math.Min(MaxTimelineWindowMs, totalMs));
        var minWindow = Math.Min(MinTimelineWindowMs, maxWindow);
        var span = Math.Max(1, maxWindow - minWindow);
        var t = Math.Clamp((_timelineWindowMs - minWindow) / (float)span, 0f, 1f);
        var thumbWidth = 12;
        var travel = Math.Max(1, trackRect.Width - thumbWidth);
        var thumbX = trackRect.X + (int)MathF.Round(t * travel);
        return new Rectangle(thumbX, trackRect.Y, thumbWidth, trackRect.Height);
    }

    private void SetTimelineStartFromSliderPosition(float thumbLeftX, Rectangle trackRect, int thumbWidth, int totalMs)
    {
        var maxStart = Math.Max(0, totalMs - _timelineWindowMs);
        if (maxStart <= 0)
        {
            _timelineViewStartMs = 0;
            return;
        }

        var travel = Math.Max(1f, trackRect.Width - thumbWidth);
        var t = Math.Clamp((thumbLeftX - trackRect.X) / travel, 0f, 1f);
        _timelineViewStartMs = (int)MathF.Round(t * maxStart);
    }

    private void SetTimelineWindowFromZoomSliderPosition(float thumbLeftX, Rectangle trackRect, int thumbWidth, int totalMs)
    {
        var maxWindow = Math.Max(MinTimelineWindowMs, Math.Min(MaxTimelineWindowMs, totalMs));
        var minWindow = Math.Min(MinTimelineWindowMs, maxWindow);
        var span = Math.Max(1, maxWindow - minWindow);
        var travel = Math.Max(1f, trackRect.Width - thumbWidth);
        var t = Math.Clamp((thumbLeftX - trackRect.X) / travel, 0f, 1f);
        var newWindow = minWindow + (int)MathF.Round(t * span);
        ApplyTimelineWindowAroundFocus(newWindow, totalMs);
    }

    private void AdjustTimelineZoom(bool zoomIn, int totalMs)
    {
        var maxWindow = Math.Max(MinTimelineWindowMs, Math.Min(MaxTimelineWindowMs, totalMs));
        var minWindow = Math.Min(MinTimelineWindowMs, maxWindow);
        var factor = zoomIn ? 0.8f : 1.25f;
        var newWindow = (int)MathF.Round(_timelineWindowMs * factor);
        newWindow = Math.Clamp(newWindow, minWindow, maxWindow);
        ApplyTimelineWindowAroundFocus(newWindow, totalMs);
        _timelineZoomTouched = true;
    }

    private void ApplyTimelineWindowAroundFocus(int newWindowMs, int totalMs)
    {
        var clampedWindow = Math.Clamp(newWindowMs, MinTimelineWindowMs, Math.Max(MinTimelineWindowMs, Math.Min(MaxTimelineWindowMs, totalMs)));
        var oldWindow = Math.Max(1, _timelineWindowMs);
        var focusMs = GetTimelineZoomFocusMs();
        var focusT = Math.Clamp((focusMs - _timelineViewStartMs) / (float)oldWindow, 0f, 1f);

        _timelineWindowMs = clampedWindow;
        _timelineWindowInput = _timelineWindowMs.ToString();

        var maxTimelineStart = Math.Max(0, totalMs - _timelineWindowMs);
        var desiredStart = (int)MathF.Round(focusMs - focusT * _timelineWindowMs);
        _timelineViewStartMs = Math.Clamp(desiredStart, 0, maxTimelineStart);
    }

    private int GetTimelineZoomFocusMs()
    {
        if (_model is null)
        {
            return _timelineViewStartMs + (_timelineWindowMs / 2);
        }

        var current = Math.Max(0, _model.CurrentTimeMs);
        return current;
    }

    private struct PreviewBullet
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
    }

    public bool TryGetPendingBulletPreview(out PendingBulletPreview preview)
    {
        preview = default;
        return false;
    }

    public void Render(EditorViewModel model)
    {
        _model = model;
    }

    public bool TryDequeueCommand(out EditorCommand command)
    {
        if (_commands.Count == 0)
        {
            command = null!;
            return false;
        }

        command = _commands.Dequeue();
        return true;
    }

    public void PushMessage(string message)
    {
        _messages.Enqueue(message);
    }
}

public readonly record struct PendingBulletPreview(string Pattern, string Shape, float X, float Y);


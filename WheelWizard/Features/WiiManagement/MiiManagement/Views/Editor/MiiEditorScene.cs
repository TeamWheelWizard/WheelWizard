using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Rig;
using Testably.Abstractions.RandomSystem;
using WheelWizard.MiiAnimations.Editor;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiImages.Views;
using WheelWizard.MiiRendering.Realtime;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared.Calendar;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Editor;

/// <summary>How the editor camera frames the Mii.</summary>
public enum MiiEditorFraming
{
    /// <summary>Two small Miis side by side (picking a new Mii).</summary>
    Picker,

    /// <summary>The whole Mii.</summary>
    Body,

    /// <summary>The whole Mii, moved aside for the info card.</summary>
    Info,

    /// <summary>Close-up of the head.</summary>
    Head,
}

/// <summary>
/// The Mii editor's 3D stage: the realtime Mii (or a rendered image without OpenGL), its camera, and everything you
/// do with the mouse on it. In the picker two Miis come in and you pick one; in the editor you click the head or
/// body to open their menus, and in the head close-up you hover, click and drag the parts of the face.
/// The page owns the Mii and the menus; this only shows the Mii and reports what was clicked or dragged.
/// </summary>
public sealed class MiiEditorScene : Grid
{
    private static readonly TimeSpan CameraTransition = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan PickTransition = TimeSpan.FromMilliseconds(700);

    private const double DragThreshold = 4;
    private const float DragDegreesPerPixel = 0.6f;
    private const float YawReturnSpeed = 5f;

    /// <summary>How much of the width the Mii moves aside for the info card.</summary>
    private const double InfoShift = 0.2;

    // Cursor tracking: the Mii looks at a point this far in front of its head, under the cursor (render units).
    private const float LookDepth = 140f;
    private const float MaxLookYaw = 45f;
    private const float MaxLookUp = 25f;
    private const float MaxLookDown = 20f;

    /// <summary>Face parts get a bit of extra room around them for the mouse.</summary>
    private const float MaskPartPadding = 1.3f;

    private static readonly MiiImageSpecifications BodyShot = MiiImageVariants.MiiEditorPreviewCarousel.Clone();

    /// <summary>
    /// Picker Miis are the editor's whole-Mii shot drawn smaller (see <see cref="MiiRealtimeView.ScreenScale"/>), so
    /// picking one is a smooth zoom to full size. (Zooming the camera out instead would pass its far plane.)
    /// </summary>
    private static readonly MiiImageSpecifications PickerShot = BodyShot;

    private const double PickerScale = 0.9;

    /// <summary>How far each picker Mii sits from the middle: a share of the stage's height, at most of its width.</summary>
    private const double PickerShiftPerHeight = 0.19;

    private const double PickerMaxShift = 0.23;

    private static readonly MiiImageSpecifications HeadShot = new()
    {
        Name = "MiiEditorHeadCloseUp",
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.medium,
        Expression = MiiImageSpecifications.FaceExpression.normal,
        CameraZoom = 0.9f,
        CameraVerticalOffset = 0f,
        InstanceCount = 1,
    };

    private readonly IMiiNativeRenderer _renderer;
    private readonly IMiiAnimationLibrary _library;
    private readonly ISeasonalCalendar _calendar;
    private readonly IRandom _random;
    private readonly List<Actor> _actors = [];
    private readonly Dictionary<string, IReadOnlyDictionary<MiiMaskLayers, Vector2[][]>> _maskQuads = new();
    private Mii3DRender? _fallback;
    private Grid? _fallbackPicker;

    private MiiEditorFraming _framing = MiiEditorFraming.Body;
    private MiiImageSpecifications _shot = BodyShot;
    private float _yaw;
    private float _targetYaw;
    private Mii? _shown;
    private string? _shownStudio;
    private (Mii Mii, string Cue)? _pending;

    private Point? _pointer;
    private Point? _pressedAt;
    private bool _dragging;
    private Press? _press;
    private int _hoveredActor = -1;

    /// <summary>One Mii on the stage. The picker has two; once one is picked it's the only one left.</summary>
    private sealed record Actor(MiiRealtimeView View, MiiEditorDirector Director, bool IsGirl)
    {
        public Mii? Mii { get; set; }
        public MiiAnimation? Entrance { get; set; }
    }

    /// <summary>What a press in the head close-up landed on, to tell a click from a drag of that part.</summary>
    private sealed record Press(MiiEditPart Part, Point At, Vector2? VerticalStep, Vector2? HorizontalStep, int Side)
    {
        public (int Vertical, int Horizontal) Steps { get; set; }
    }

    public MiiEditorScene(
        IMiiNativeRenderer renderer,
        IMiiAnimationLibrary library,
        ISeasonalCalendar calendar,
        IRandom random,
        bool animate
    )
    {
        _renderer = renderer;
        _library = library;
        _calendar = calendar;
        _random = random;
        Animate = animate;
        // Transparent so the whole stage takes pointer input; the GL views themselves don't.
        Background = Brushes.Transparent;
        ClipToBounds = false;

        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerExited += (_, _) =>
        {
            _pointer = null;
            ClearHover();
        };
        PointerCaptureLost += (_, _) =>
        {
            // Lost mid-drag (e.g. the window lost focus): the drag still ends.
            if (_dragging && _press is { } press)
                PartDragEnded?.Invoke(press.Part);
            _pressedAt = null;
            _dragging = false;
            _press = null;
        };
    }

    /// <summary>False for "reduce animations": no coming in, camera cuts instead of moves, parts swap instantly.</summary>
    public bool Animate { get; }

    /// <summary>False when OpenGL isn't available and the Mii is a rendered image (no clicking parts, no dragging).</summary>
    public bool IsRealtime => _fallback is null && _fallbackPicker is null;

    public MiiEditorFraming Framing => _framing;

    private long _cameraSettlesAt;

    /// <summary>Whether the camera is still moving to a new framing (so things on screen are still moving too).</summary>
    public bool IsCameraMoving => Environment.TickCount64 < _cameraSettlesAt;

    /// <summary>The animation director of the Mii being edited (after picking).</summary>
    public MiiEditorDirector? Director => _actors.Count == 1 ? _actors[0].Director : null;

    private MiiEditPart? _hoveredPart;

    /// <summary>A picker Mii was clicked: false for the boy (left), true for the girl (right).</summary>
    public event Action<bool>? Picked;

    /// <summary>The head or body was clicked in the whole-Mii view.</summary>
    public event Action<MiiBodyPart>? BodyClicked;

    /// <summary>A part was clicked in the head close-up.</summary>
    public event Action<MiiEditPart>? PartClicked;

    /// <summary>A part drag started (the page remembers the values it started from).</summary>
    public event Action<MiiEditPart>? PartDragStarted;

    /// <summary>A part is being dragged: steps (down, outwards/right) from where the drag started.</summary>
    public event Action<MiiEditPart, int, int>? PartDragged;

    public event Action<MiiEditPart>? PartDragEnded;

    /// <summary>Fired every drawn frame, so overlays can follow the Mii.</summary>
    public event Action? FrameDrawn;

    /// <summary>OpenGL turned out to be unavailable; the stage now shows rendered images.</summary>
    public event Action? FellBackToImages;

    #region Picker

    /// <summary>Shows a boy on the left and a girl on the right coming in (falling from the sky, mostly).</summary>
    public void ShowPicker(Mii boy, Mii girl)
    {
        _framing = MiiEditorFraming.Picker;
        _shot = PickerShot;
        AddActor(boy, isGirl: false);
        AddActor(girl, isGirl: true);
        LayoutPicker();
        SizeChanged += (_, _) => LayoutPicker();

        for (var i = 0; i < _actors.Count; i++)
        {
            var actor = _actors[i];
            var delay = TimeSpan.FromMilliseconds(i * 260);
            // Wait on the first frame (for a fall: in the sky, above the picture) until the Mii is ready to come in.
            var waiting = Animate ? HoldEntrance(actor) : false;
            actor.View.MiiShown += Started;
            void Started(string _)
            {
                actor.View.MiiShown -= Started;
                if (!waiting)
                {
                    actor.Director.Idle();
                    return;
                }

                Avalonia.Threading.DispatcherTimer.RunOnce(() => actor.View.IsPlaying = true, delay);
            }
        }
    }

    /// <summary>Puts a random entrance on the Mii, paused on its first frame. False when there is none to play.</summary>
    private bool HoldEntrance(Actor actor)
    {
        // Falling from the sky or appearing; any clip dropped in these folders joins in.
        string[] entrances = [.. _library.List("editor/picker"), .. _library.List("shared/appear")];
        if (entrances.Length == 0 || _library.Get(entrances[_random.Next(entrances.Length)]) is not { } clip)
            return false;
        actor.Entrance = clip;
        actor.View.Preload(clip);
        actor.View.Player.Play(clip, loop: false, fadeSeconds: 0);
        actor.View.IsPlaying = false;
        return true;
    }

    /// <summary>Whether a picker Mii is still coming in.</summary>
    private bool IsArriving => _actors.Any(a => a.Entrance is not null);

    private void LayoutPicker()
    {
        if (_framing != MiiEditorFraming.Picker || _actors.Count != 2)
            return;
        // Both views cover the whole stage; each Mii is drawn a bit smaller, moved to its side of the middle. Their
        // size follows the stage's height, so on a wide stage they stay close instead of drifting to the edges.
        var shift = Math.Min(Bounds.Width * PickerMaxShift, Bounds.Height * PickerShiftPerHeight);
        _actors[0].View.ScreenShiftX = -shift;
        _actors[1].View.ScreenShiftX = shift;
        foreach (var actor in _actors)
            actor.View.ScreenScale = PickerScale;
    }

    /// <summary>Both picker Miis turn into random ones (with their shuffle animation). Returns when they're shown.</summary>
    public async Task ShuffleAsync(Mii boy, Mii girl)
    {
        if (_fallbackPicker is not null)
        {
            ShowFallbackPicker(boy, girl);
            return;
        }

        // Let them arrive first; shuffling mid-air would yank them to the ground.
        for (var i = 0; i < 80 && IsArriving; i++)
            await Task.Delay(50);

        var waits = new List<Task>();
        foreach (var (actor, mii) in _actors.Zip([boy, girl]))
        {
            actor.Mii = mii;
            if (Animate && actor.Director.React(MiiEditorReaction.Randomize))
                waits.Add(ShowAtCueAsync(actor, mii));
            else
                ShowOn(actor, mii);
        }

        await Task.WhenAny(Task.WhenAll(waits), Task.Delay(2500));

        async Task ShowAtCueAsync(Actor actor, Mii mii)
        {
            actor.View.Prewarm(mii);
            await actor.Director.WhenReached(MiiEditorCues.Randomize);
            ShowOn(actor, mii);
        }
    }

    /// <summary>Keeps the picked Mii and zooms in on it; the other one fades away.</summary>
    public async Task ChooseAsync(bool girl)
    {
        // Without OpenGL the page shows the picked Mii with ShowEditorImage.
        if (_fallbackPicker is not null)
            return;

        var keep = _actors.First(a => a.IsGirl == girl);
        var drop = _actors.First(a => a.IsGirl != girl);
        _hoveredActor = -1;
        foreach (var actor in _actors)
            Unhighlight(actor.View);

        _framing = MiiEditorFraming.Body;
        _shot = BodyShot;
        // The other one fades away while the picked one grows to full size and glides to the middle.
        var shift = keep.View.ScreenShiftX;
        await Task.WhenAll(
            Tween(value => drop.View.Alpha = (float)value, 1, 0, TimeSpan.FromMilliseconds(300)),
            Tween(
                t =>
                {
                    keep.View.ScreenShiftX = shift * (1 - t);
                    keep.View.ScreenScale = PickerScale + (1 - PickerScale) * t;
                },
                0,
                1,
                PickTransition
            )
        );
        Children.Remove(drop.View);
        _actors.Remove(drop);
        _shown = keep.Mii;
        _shownStudio = keep.Mii is { } shown ? Studio(shown) : null;
    }

    #endregion

    #region Editor

    /// <summary>Shows the Mii to edit right away (editing an existing Mii: no picker).</summary>
    public void ShowEditor(Mii mii)
    {
        _framing = MiiEditorFraming.Body;
        _shot = BodyShot;
        var actor = AddActor(mii, mii.IsGirl);
        actor.View.MiiShown += Started;
        void Started(string _)
        {
            actor.View.MiiShown -= Started;
            actor.Director.Start();
        }
    }

    /// <summary>
    /// Shows the Mii as it is now (it's copied, so later edits don't show until the next call). With
    /// <paramref name="change"/> only that part swaps, with a slide. With <paramref name="reaction"/> the Mii reacts,
    /// and with <paramref name="showAtCue"/> the new look only shows when the reaction reaches that marker.
    /// </summary>
    public void Present(Mii mii, HeadPartChange? change = null, MiiEditorReaction? reaction = null, string? showAtCue = null)
    {
        if (MiiEditorSession.Copy(mii) is not { } copy)
            return;
        var director = Director;
        if (reaction is { } r)
            director?.React(r);

        showAtCue ??= _pending?.Cue;
        if (showAtCue is not null && IsRealtime && director?.WhenReached(showAtCue) is { IsCompleted: false } reached)
        {
            _pending = (copy, showAtCue);
            _actors[0].View.Prewarm(copy);
            _ = ShowPendingAsync(copy, reached);
            return;
        }

        _pending = null;
        Show(copy, Animate ? change : null);
    }

    /// <summary>Shows <paramref name="mii"/> once <paramref name="reached"/> completes, unless a newer look replaced it.</summary>
    private async Task ShowPendingAsync(Mii mii, Task reached)
    {
        await reached;
        if (_actors.Count != 1 || _pending is not { } pending || !ReferenceEquals(pending.Mii, mii))
            return;
        _pending = null;
        Show(mii, null);
    }

    public void SetFraming(MiiEditorFraming framing)
    {
        if (_framing == framing || _actors.Count != 1 && _fallback is null)
            return;
        var previous = _framing;
        _framing = framing;
        _shot = framing == MiiEditorFraming.Head ? HeadShot : BodyShot;
        _targetYaw = 0;
        ClearHover();

        if (Director is { } director)
            director.HoldStill = framing == MiiEditorFraming.Head;

        if (_fallback is { } fallback)
        {
            fallback.ImageVariant = framing == MiiEditorFraming.Head ? HeadShot : MiiImageVariants.MiiEditorPreviewCarousel;
            fallback.RefreshCurrentMii();
            return;
        }

        var view = _actors[0].View;
        _cameraSettlesAt = Environment.TickCount64 + (long)(Animate ? CameraTransition.TotalMilliseconds : 0) + 150;
        view.TransitionTo(WithYaw(_shot, _yaw), Animate ? CameraTransition : TimeSpan.Zero);
        var shiftFrom = view.ScreenShiftX;
        var shiftTo = framing == MiiEditorFraming.Info ? -Bounds.Width * InfoShift : 0;
        if (Math.Abs(shiftFrom - shiftTo) > 0.5 || previous == MiiEditorFraming.Info)
            _ = Tween(value => view.ScreenShiftX = value, shiftFrom, shiftTo, CameraTransition);
    }

    private void Show(Mii mii, HeadPartChange? change)
    {
        _shown = mii;
        if (_fallback is { } fallback)
        {
            fallback.Mii = mii;
            fallback.RefreshCurrentMii();
            return;
        }

        if (_actors.Count == 0)
            return;
        ShowOn(_actors[0], mii, change);
    }

    private void ShowOn(Actor actor, Mii mii, HeadPartChange? change = null)
    {
        actor.Mii = mii;
        var studio = Studio(mii);
        if (actor == _actors[0])
            _shownStudio = studio;
        actor.View.SetMii(mii, studio, change);
    }

    private string? Studio(Mii mii) =>
        MiiStudioDataSerializer.Serialize(mii, _calendar.IsAprilFirst) is { IsSuccess: true } studio ? studio.Value : null;

    #endregion

    #region Actors and the CPU fallback

    private Actor AddActor(Mii mii, bool isGirl)
    {
        var view = new MiiRealtimeView(_renderer) { IsHitTestVisible = false, Specifications = _shot };
        var director = new MiiEditorDirector(view.Player, _library, _random);
        var actor = new Actor(view, director, isGirl) { Mii = mii };
        view.FrameUpdating += delta => OnFrame(actor, delta);
        view.RealtimeUnavailable += _ => SwitchToImages();
        view.Player.Finished += clip =>
        {
            if (ReferenceEquals(clip, actor.Entrance))
            {
                actor.Entrance = null;
                director.Idle();
            }
        };
        _actors.Add(actor);
        Children.Add(view);
        ShowOn(actor, mii);
        return actor;
    }

    private void SwitchToImages()
    {
        if (!IsRealtime)
            return;
        var miis = _actors.Select(a => a.Mii).ToList();
        foreach (var actor in _actors)
            Children.Remove(actor.View);
        var picking = _framing == MiiEditorFraming.Picker && miis.Count == 2;
        _actors.Clear();

        if (picking)
        {
            ShowFallbackPicker(miis[0]!, miis[1]!);
        }
        else
        {
            _fallback = new Mii3DRender
            {
                ReloadMethod = BaseMiiImage.ReloadMethodType.KeepInstanceUntilNew,
                ImageVariant = _framing == MiiEditorFraming.Head ? HeadShot : MiiImageVariants.MiiEditorPreviewCarousel,
                IsHitTestVisible = false,
            };
            Children.Add(_fallback);
            _pending = null;
            if ((_shown ?? miis.FirstOrDefault()) is { } mii)
                Show(mii, null);
        }

        FellBackToImages?.Invoke();
    }

    private void ShowFallbackPicker(Mii boy, Mii girl)
    {
        if (_fallbackPicker is { } old)
            Children.Remove(old);
        _fallbackPicker = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), IsHitTestVisible = false };
        foreach (var (mii, column) in new[] { (boy, 0), (girl, 1) })
        {
            var image = new Mii3DRender
            {
                ReloadMethod = BaseMiiImage.ReloadMethodType.KeepInstanceUntilNew,
                ImageVariant = MiiImageVariants.MiiEditorPreviewCarousel,
                Mii = mii,
            };
            SetColumn(image, column);
            _fallbackPicker.Children.Add(image);
        }

        Children.Add(_fallbackPicker);
    }

    /// <summary>After picking without OpenGL: the picked Mii as a rendered image.</summary>
    public void ShowEditorImage(Mii mii)
    {
        if (_fallbackPicker is { } picker)
        {
            Children.Remove(picker);
            _fallbackPicker = null;
        }

        _framing = MiiEditorFraming.Body;
        _fallback ??= new Mii3DRender
        {
            ReloadMethod = BaseMiiImage.ReloadMethodType.KeepInstanceUntilNew,
            ImageVariant = MiiImageVariants.MiiEditorPreviewCarousel,
            IsHitTestVisible = false,
        };
        if (!Children.Contains(_fallback))
            Children.Add(_fallback);
        Show(mii, null);
    }

    #endregion

    #region Per frame: turning back and looking at the cursor

    private void OnFrame(Actor actor, double deltaSeconds)
    {
        var editing = _actors.Count == 1;
        if (editing && !_dragging && MathF.Abs(_targetYaw - _yaw) > 0.01f)
        {
            _yaw += (_targetYaw - _yaw) * (1f - MathF.Exp(-YawReturnSpeed * (float)deltaSeconds));
            actor.View.Specifications = WithYaw(_shot, _yaw);
        }

        var look = actor.View.Player.Look;
        // Still in the close-up, so parts stay put under the mouse.
        look.TargetWeight = _framing == MiiEditorFraming.Head ? 0f : actor.Director.LookWeight;
        if (
            _pointer is { } pointer
            && !_dragging
            && LookAngles(actor.View, this.TranslatePoint(pointer, actor.View) ?? pointer) is { } angles
        )
            (look.TargetYaw, look.TargetPitch) = angles;
        else
            (look.TargetYaw, look.TargetPitch) = (0f, 0f);

        if (_actors.Count > 0 && ReferenceEquals(actor, _actors[^1]))
            Avalonia.Threading.Dispatcher.UIThread.Post(() => FrameDrawn?.Invoke());
    }

    /// <summary>Head turn (degrees, relative to where the body faces) to look at what's under the cursor.</summary>
    private static (float Yaw, float Pitch)? LookAngles(MiiRealtimeView view, Point pointer)
    {
        if (view.HeadCenterOnStage() is not { } head || view.ScreenRay(pointer) is not { } ray || view.LastFrame is not { } frame)
            return null;

        var planePoint = head - ray.Direction * LookDepth;
        var target = ray.Origin + ray.Direction * Vector3.Dot(planePoint - ray.Origin, ray.Direction);
        var to = target - head;

        var facing = Vector3.Transform(Vector3.UnitZ, frame.Pose.WorldRotation[(int)MiiBone.Root]);
        var bodyYaw = MathF.Atan2(facing.X, facing.Z);
        var yaw = WrapRadians(MathF.Atan2(to.X, to.Z) - bodyYaw) * 180f / MathF.PI;
        var pitch = MathF.Atan2(to.Y, MathF.Sqrt(to.X * to.X + to.Z * to.Z)) * 180f / MathF.PI;
        if (MathF.Abs(yaw) > 100f)
            return (0f, 0f);
        return (Math.Clamp(yaw, -MaxLookYaw, MaxLookYaw), Math.Clamp(pitch, -MaxLookDown, MaxLookUp));
    }

    private static float WrapRadians(float radians)
    {
        while (radians > MathF.PI)
            radians -= MathF.Tau;
        while (radians < -MathF.PI)
            radians += MathF.Tau;
        return radians;
    }

    private static MiiImageSpecifications WithYaw(MiiImageSpecifications shot, float yaw)
    {
        var specifications = shot.Clone();
        specifications.CharacterRotate = new(shot.CharacterRotate.X, shot.CharacterRotate.Y + yaw, shot.CharacterRotate.Z);
        return specifications;
    }

    #endregion

    #region Pointer

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        _pointer = point;

        if (_pressedAt is { } pressedAt)
        {
            if (!_dragging && Distance(point, pressedAt) > DragThreshold)
            {
                _dragging = true;
                if (_press is { } press)
                    PartDragStarted?.Invoke(press.Part);
            }

            if (_dragging)
            {
                if (_press is { } press)
                    DragPart(press, point);
                else if (_framing is MiiEditorFraming.Body or MiiEditorFraming.Info && _actors.Count == 1)
                    RotateBy(point, pressedAt);
            }
        }

        if (!_dragging)
            UpdateHover(point);
        else
            Cursor = _press switch
            {
                null => new Cursor(StandardCursorType.SizeWestEast),
                { VerticalStep: null, HorizontalStep: null } => Cursor.Default,
                _ => new Cursor(StandardCursorType.SizeAll),
            };
        foreach (var actor in _actors)
            actor.View.Invalidate();
    }

    private void RotateBy(Point point, Point pressedAt)
    {
        _yaw += (float)(point.X - pressedAt.X) * DragDegreesPerPixel;
        _targetYaw = _yaw;
        _pressedAt = point;
        _actors[0].View.Specifications = WithYaw(_shot, _yaw);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
            return;
        var point = e.GetPosition(this);
        _pressedAt = point;
        _dragging = false;
        _press = _framing == MiiEditorFraming.Head && IsRealtime ? PressOnPart(point) : null;
        e.Pointer.Capture(this);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedAt is null)
            return;
        var wasDragging = _dragging;
        var press = _press;
        _pressedAt = null;
        _dragging = false;
        _press = null;
        e.Pointer.Capture(null);
        var point = e.GetPosition(this);

        if (wasDragging)
        {
            if (press is not null)
                PartDragEnded?.Invoke(press.Part);
            UpdateHover(point);
            return;
        }

        switch (_framing)
        {
            case MiiEditorFraming.Picker:
                if (ActorAt(point) is { } picked)
                    Picked?.Invoke(_actors[picked].IsGirl);
                else if (_fallbackPicker is not null)
                    Picked?.Invoke(point.X > Bounds.Width / 2);
                break;
            case MiiEditorFraming.Body
            or MiiEditorFraming.Info:
                if (IsRealtime && _actors.Count == 1 && HitTestBody(_actors[0].View, ToView(_actors[0].View, point)) is { } part)
                    BodyClicked?.Invoke(part);
                break;
            case MiiEditorFraming.Head:
                if (PartAt(point) is { } clicked)
                    PartClicked?.Invoke(clicked);
                break;
        }
    }

    private void UpdateHover(Point point)
    {
        switch (_framing)
        {
            case MiiEditorFraming.Picker:
            {
                var hovered = ActorAt(point) ?? -1;
                if (_fallbackPicker is not null)
                {
                    Cursor = new Cursor(StandardCursorType.Hand);
                    return;
                }

                if (hovered != _hoveredActor)
                {
                    _hoveredActor = hovered;
                    for (var i = 0; i < _actors.Count; i++)
                    {
                        if (i == hovered)
                        {
                            _actors[i].View.HighlightedPart = HeadPart.Head;
                            _actors[i].View.BodyHoverMask = AllBodyBones;
                        }
                        else
                        {
                            Unhighlight(_actors[i].View);
                        }
                    }
                }

                Cursor = hovered >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
                break;
            }
            case MiiEditorFraming.Body
            or MiiEditorFraming.Info:
            {
                if (!IsRealtime || _actors.Count != 1)
                {
                    Cursor = Cursor.Default;
                    return;
                }

                var view = _actors[0].View;
                var part = HitTestBody(view, ToView(view, point));
                view.HighlightedPart = part == MiiBodyPart.Head ? HeadPart.Head : null;
                view.BodyHoverMask = part is { } p && p != MiiBodyPart.Head ? AllBodyBones : 0;
                Cursor = part is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
                break;
            }
            case MiiEditorFraming.Head:
            {
                var part = IsRealtime ? PartAt(point) : null;
                _hoveredPart = part;
                UpdateHighlight();
                Cursor = part is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
                break;
            }
        }
    }

    private void ClearHover()
    {
        _hoveredPart = null;
        _hoveredActor = -1;
        foreach (var actor in _actors)
            Unhighlight(actor.View);
        UpdateHighlight();
        Cursor = Cursor.Default;
    }

    private void UpdateHighlight()
    {
        if (_actors.Count != 1 || _framing != MiiEditorFraming.Head)
            return;
        // Only what's under the mouse lights up; the floating arrows already show what's selected.
        var part = _hoveredPart;
        _actors[0].View.HighlightedPart = part is { } p ? MiiEditorParts.Get(p).HighlightPart : null;
    }

    private static void Unhighlight(MiiRealtimeView view)
    {
        view.HighlightedPart = null;
        view.BodyHoverMask = 0;
    }

    /// <summary>Every body bone but the head and root (they light up together as "the body").</summary>
    private static readonly int AllBodyBones = Enum.GetValues<MiiBone>()
        .Where(b => b is not (MiiBone.Head or MiiBone.Root))
        .Aggregate(0, (mask, bone) => mask | 1 << (int)bone);

    private Point ToView(Visual view, Point point) => this.TranslatePoint(point, view) ?? point;

    private int? ActorAt(Point point)
    {
        if (_framing != MiiEditorFraming.Picker || _fallbackPicker is not null)
            return null;
        for (var i = 0; i < _actors.Count; i++)
        {
            var view = _actors[i].View;
            if (HitTestBody(view, ToView(view, point)) is not null)
                return i;
        }

        return null;
    }

    #endregion

    #region Hit testing

    /// <summary>Which part of the Mii is under a point of the view (from the last drawn pose).</summary>
    private static MiiBodyPart? HitTestBody(MiiRealtimeView view, Point point)
    {
        if (view.LastFrame is null)
            return null;

        Point? Screen(MiiBone bone) => view.BoneOnStage(bone) is { } stage ? view.ProjectToScreen(stage) : null;

        if (view.HeadCenterOnStage() is { } headCenter && view.ProjectToScreen(headCenter) is { } head)
        {
            var top = view.ProjectToScreen(headCenter + new Vector3(0, 30f, 0));
            var radius = top is { } t ? Distance(head, t) : 0;
            if (Distance(point, head) <= radius)
                return MiiBodyPart.Head;
        }

        var scale =
            view.ProjectToScreen(Vector3.Zero) is { } a && view.ProjectToScreen(new Vector3(0, 10f, 0)) is { } b ? Distance(a, b) / 10 : 1;
        double Segment(MiiBone from, MiiBone to) =>
            Screen(from) is { } p && Screen(to) is { } q ? DistanceToSegment(point, p, q) : double.MaxValue;

        var body = new[]
        {
            Segment(MiiBone.Hip, MiiBone.Chest),
            Segment(MiiBone.ArmL1, MiiBone.ArmL2),
            Segment(MiiBone.ArmL2, MiiBone.HandL),
            Segment(MiiBone.ArmR1, MiiBone.ArmR2),
            Segment(MiiBone.ArmR2, MiiBone.HandR),
            Segment(MiiBone.LegL1, MiiBone.LegL2),
            Segment(MiiBone.LegL2, MiiBone.FootL),
            Segment(MiiBone.LegR1, MiiBone.LegR2),
            Segment(MiiBone.LegR2, MiiBone.FootR),
        }.Min();
        return body <= 20 * scale ? MiiBodyPart.Body : null;
    }

    /// <summary>The part of the face under a point of the stage, among the parts of the open head group.</summary>
    private MiiEditPart? PartAt(Point point) => PartAt(point, out _);

    private MiiEditPart? PartAt(Point point, out HeadHit? hit)
    {
        hit = null;
        if (_actors.Count != 1 || _framing != MiiEditorFraming.Head)
            return null;
        var view = _actors[0].View;
        var hits = view.PickHead(ToView(view, point));

        // Brows and eyes win over the glasses and hair in front of them, which would otherwise hide them.
        if (hits.Count > 0 && hits[0].Shape is HeadShape.Glass or HeadShape.Hair or HeadShape.Cap)
        {
            foreach (var candidate in hits)
            {
                if (candidate.Shape != HeadShape.Mask)
                    continue;
                if (
                    MaskPartAt(candidate.Uv, [(MiiMaskLayers.Eyebrows, MiiEditPart.Eyebrows), (MiiMaskLayers.Eyes, MiiEditPart.Eyes)]) is
                    { } behind
                )
                {
                    hit = candidate;
                    return behind;
                }
            }
        }

        foreach (var candidate in hits)
        {
            hit = candidate;
            switch (candidate.Shape)
            {
                case HeadShape.Hair
                or HeadShape.Cap:
                    return MiiEditPart.Hair;
                case HeadShape.Nose
                or HeadShape.NoseLine:
                    return MiiEditPart.Nose;
                case HeadShape.Beard:
                    return MiiEditPart.Beard;
                case HeadShape.Glass:
                    return MiiEditPart.Glasses;
                case HeadShape.Mask:
                    // The mask is see-through away from its parts: look at what's behind it.
                    if (MaskPartAt(candidate.Uv) is { } maskPart)
                        return maskPart;
                    continue;
                default:
                    return MiiEditPart.FaceShape;
            }
        }

        hit = null;
        return null;
    }

    /// <summary>Small parts first, so the mole wins over the cheek and the mustache over the mouth.</summary>
    private static readonly (MiiMaskLayers Layer, MiiEditPart Part)[] MaskPartOrder =
    [
        (MiiMaskLayers.Mole, MiiEditPart.Mole),
        (MiiMaskLayers.Eyes, MiiEditPart.Eyes),
        (MiiMaskLayers.Eyebrows, MiiEditPart.Eyebrows),
        (MiiMaskLayers.Mustache, MiiEditPart.Mustache),
        (MiiMaskLayers.Mouth, MiiEditPart.Mouth),
    ];

    private MiiEditPart? MaskPartAt(Vector2 uv, (MiiMaskLayers Layer, MiiEditPart Part)[]? order = null)
    {
        if (MaskQuads() is not { } quads)
            return null;
        foreach (var (layer, part) in order ?? MaskPartOrder)
        {
            if (!quads.TryGetValue(layer, out var partQuads))
                continue;
            var padding = layer == MiiMaskLayers.Mole ? 2.2f : MaskPartPadding;
            if (partQuads.Any(quad => InsideQuad(uv, quad, padding)))
                return part;
        }

        return null;
    }

    private IReadOnlyDictionary<MiiMaskLayers, Vector2[][]>? MaskQuads()
    {
        if (_shownStudio is not { } studio)
            return null;
        if (_maskQuads.TryGetValue(studio, out var quads))
            return quads;
        if (_renderer.GetMaskPartQuads(studio) is not { IsSuccess: true } result)
            return null;
        if (_maskQuads.Count > 8)
            _maskQuads.Clear();
        return _maskQuads[studio] = result.Value;
    }

    private static bool InsideQuad(Vector2 point, Vector2[] quad, float padding)
    {
        var center = (quad[0] + quad[1] + quad[2] + quad[3]) / 4f;
        var p = center + (point - center) / padding;
        var sign = 0f;
        for (var i = 0; i < 4; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % 4];
            var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (MathF.Abs(cross) < 1e-9f)
                continue;
            if (sign == 0f)
                sign = MathF.Sign(cross);
            else if (MathF.Sign(cross) != sign)
                return false;
        }

        return true;
    }

    #endregion

    #region Dragging parts

    private Press? PressOnPart(Point point)
    {
        if (PartAt(point, out var hit) is not { } part || hit is null)
            return null;
        var definition = MiiEditorParts.Get(part);
        if (!definition.IsDraggable || _shown is null || !definition.IsPresent(_shown))
            return new Press(part, point, null, null, 1);

        var vertical = definition.DragVertical is { } v ? StepOnScreen(v, hit) : null;
        var horizontal = definition.DragHorizontal is { } h ? StepOnScreen(h, hit) : null;
        // Mirrored spacing: on the left eye, moving outwards is moving left.
        var side = hit.Uv.X < 0.5f ? -1 : 1;
        return new Press(part, point, vertical, horizontal, side);
    }

    /// <summary>How far (and which way) one step of a value moves the part on screen, measured where it was grabbed.</summary>
    private Vector2? StepOnScreen(MiiDragAxis axis, HeadHit hit)
    {
        var view = _actors[0].View;
        Point? from,
            to;
        if (axis.MaskStep != Vector2.Zero)
        {
            from = view.MaskPointToHead(hit.Uv) is { } a ? view.ProjectHeadPoint(a) : null;
            to = view.MaskPointToHead(hit.Uv + axis.MaskStep) is { } b ? view.ProjectHeadPoint(b) : null;
            if (
                to is null
                && view.MaskPointToHead(hit.Uv - axis.MaskStep) is { } c
                && view.ProjectHeadPoint(c) is { } back
                && from is { } f
            )
                to = new Point(2 * f.X - back.X, 2 * f.Y - back.Y);
        }
        else
        {
            from = view.ProjectHeadPoint(hit.Position);
            to = view.ProjectHeadPoint(hit.Position + axis.HeadStep);
        }

        if (from is not { } start || to is not { } end)
            return null;
        var step = new Vector2((float)(end.X - start.X), (float)(end.Y - start.Y));
        return step.LengthSquared() < 0.01f ? null : step;
    }

    /// <summary>
    /// Moves a dragged part on screen right away, by the steps its values moved since the drag started (the new head
    /// is only built when the drag ends; see <see cref="MiiRealtimeView.BeginNudge"/>).
    /// </summary>
    public void NudgePart(MiiEditPart part, int down, int across)
    {
        if (!IsRealtime || _actors.Count != 1)
            return;
        var view = _actors[0].View;
        var definition = MiiEditorParts.Get(part);
        var vertical = definition.DragVertical;
        var horizontal = definition.DragHorizontal;
        var maskShift = (vertical?.MaskStep ?? Vector2.Zero) * down;
        var meshShift = (vertical?.HeadStep ?? Vector3.Zero) * down + (horizontal?.HeadStep ?? Vector3.Zero) * across;
        var spread = 0f;
        if (horizontal is { Mirrored: true })
            spread = horizontal.MaskStep.X * across;
        else if (horizontal is not null)
            maskShift += horizontal.MaskStep * across;
        view.NudgePart(maskShift, spread, meshShift);
    }

    /// <summary>A part drag starts: the head on screen stays, and only the part moves until <see cref="EndNudge"/>.</summary>
    public void BeginNudge(MiiEditPart part)
    {
        if (IsRealtime && _actors.Count == 1)
            _actors[0].View.BeginNudge(MiiEditorParts.Get(part).RenderPart);
    }

    /// <summary>The drag is over: the dragged part's new head is built and replaces the moved one.</summary>
    public void EndNudge()
    {
        if (IsRealtime && _actors.Count == 1)
            _actors[0].View.EndNudge();
    }

    private void DragPart(Press press, Point point)
    {
        var delta = new Vector2((float)(point.X - press.At.X), (float)(point.Y - press.At.Y));
        static int Steps(Vector2 delta, Vector2? step) => step is { } s ? (int)MathF.Round(Vector2.Dot(delta, s) / s.LengthSquared()) : 0;

        var steps = (
            Steps(delta, press.VerticalStep),
            Steps(delta, press.HorizontalStep) * (MiiEditorParts.Get(press.Part).DragHorizontal?.Mirrored == true ? press.Side : 1)
        );
        if (steps == press.Steps)
            return;
        press.Steps = steps;
        PartDragged?.Invoke(press.Part, steps.Item1, steps.Item2);
    }

    #endregion

    #region Where parts are on screen (for the floating buttons)

    /// <summary>The screen area of a part in stage coordinates, or null when it isn't on screen.</summary>
    public Rect? PartRect(MiiEditPart part)
    {
        if (!IsRealtime || _actors.Count != 1 || _shown is null)
            return null;
        var view = _actors[0].View;
        var definition = MiiEditorParts.Get(part);
        Rect? rect;
        if (definition.RenderPart.IsMaskPart())
        {
            if (
                MaskQuads() is not { } quads
                || !quads.TryGetValue(definition.RenderPart.MaskLayer(), out var partQuads)
                || partQuads.Length == 0
            )
                return null;
            rect = BoundsOf(partQuads.SelectMany(q => q).Select(uv => view.MaskPointToHead(uv) is { } p ? view.ProjectHeadPoint(p) : null));
        }
        else
        {
            var renderPart = definition.RenderPart is HeadPart.Head ? HeadPart.Faceline : definition.RenderPart;
            rect = view.PartBounds(renderPart);
        }

        return rect is { } r ? FromView(view, r) : null;
    }

    /// <summary>A point on the face (mask texture coordinates) in stage coordinates.</summary>
    public Point? FacePoint(Vector2 uv)
    {
        if (!IsRealtime || _actors.Count != 1)
            return null;
        var view = _actors[0].View;
        return view.MaskPointToHead(uv) is { } p && view.ProjectHeadPoint(p) is { } screen ? view.TranslatePoint(screen, this) : null;
    }

    /// <summary>The screen area of the face skin, in stage coordinates.</summary>
    public Rect? FaceRect() => PartRect(MiiEditPart.FaceShape);

    private Rect FromView(Visual view, Rect rect)
    {
        var topLeft = view.TranslatePoint(rect.TopLeft, this) ?? rect.TopLeft;
        return new Rect(topLeft, rect.Size);
    }

    private static Rect? BoundsOf(IEnumerable<Point?> points)
    {
        double left = double.MaxValue,
            top = double.MaxValue,
            right = double.MinValue,
            bottom = double.MinValue;
        foreach (var point in points)
        {
            if (point is not { } p)
                continue;
            left = Math.Min(left, p.X);
            right = Math.Max(right, p.X);
            top = Math.Min(top, p.Y);
            bottom = Math.Max(bottom, p.Y);
        }

        return left <= right ? new Rect(left, top, right - left, bottom - top) : null;
    }

    #endregion

    /// <summary>
    /// Eases a value from <paramref name="from"/> to <paramref name="to"/> on a UI timer. Used for things drawn by the
    /// GL views (shifting and fading a Mii), which Avalonia animations can't reach.
    /// </summary>
    private Task Tween(Action<double> apply, double from, double to, TimeSpan duration)
    {
        if (!Animate || duration <= TimeSpan.Zero)
        {
            apply(to);
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var easing = new CubicEaseInOut();
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed / duration);
            apply(from + (to - from) * easing.Ease(t));
            if (t < 1)
                return;
            timer.Stop();
            done.TrySetResult();
        };
        apply(from);
        timer.Start();
        return done.Task;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var abX = b.X - a.X;
        var abY = b.Y - a.Y;
        var lengthSquared = abX * abX + abY * abY;
        var t = lengthSquared <= 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * abX + (p.Y - a.Y) * abY) / lengthSquared, 0, 1);
        return Distance(p, new Point(a.X + abX * t, a.Y + abY * t));
    }
}

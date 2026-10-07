using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MiiAnim.Core.Rig;
using Testably.Abstractions.RandomSystem;
using WheelWizard.MiiAnimations.Editor;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiImages.Views;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared.Calendar;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Dialogs.MiiEditor;

/// <summary>
/// The Mii editor's live 3D Mii: it idles, reacts to edits (see <see cref="MiiEditorDirector"/>), looks at the cursor,
/// flinches when clicked, turns when dragged and zooms in on the face for the face menus.
/// Falls back to the interactive CPU-rendered Mii when OpenGL isn't available.
/// </summary>
public sealed class MiiEditorStage : Grid
{
    private static readonly TimeSpan CameraTransition = TimeSpan.FromMilliseconds(650);

    private const double DragThreshold = 4;
    private const float DragDegreesPerPixel = 0.6f;
    private const float YawReturnSpeed = 5f;

    // Cursor tracking: the Mii looks at a point this far in front of its head, under the cursor (render units).
    private const float LookDepth = 140f;
    private const float MaxLookYaw = 45f;
    private const float MaxLookUp = 25f;
    private const float MaxLookDown = 20f;

    private static readonly MiiImageSpecifications BodyShot = MiiImageVariants.MiiEditorPreviewCarousel.Clone();
    private static readonly MiiImageSpecifications FaceShot = new()
    {
        Name = "MiiEditorFaceCloseUp",
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.medium,
        Expression = MiiImageSpecifications.FaceExpression.normal,
        // Head and shoulders, with room for the hair.
        CameraZoom = 1.4f,
        CameraVerticalOffset = -6f,
        InstanceCount = 1,
    };

    private readonly ISeasonalCalendar _calendar;
    private readonly MiiRealtimeView _view;
    private Mii3DRender? _fallback;

    private Mii? _shown;
    private (Mii Mii, string Cue)? _pending;
    private MiiImageSpecifications _shot = BodyShot;
    private float _yaw;
    private float _targetYaw;

    private Point? _pointer;
    private Point? _pressedAt;
    private bool _dragging;

    public MiiEditorStage(IMiiNativeRenderer renderer, IMiiAnimationLibrary library, ISeasonalCalendar calendar, IRandom random)
    {
        _calendar = calendar;
        // Transparent so the whole area takes pointer input; the GL view itself doesn't.
        Background = Brushes.Transparent;
        ClipToBounds = false;

        _view = new MiiRealtimeView(renderer) { IsHitTestVisible = false, Specifications = BodyShot };
        _view.FrameUpdating += OnFrame;
        _view.RealtimeUnavailable += _ => SwitchToCpu();
        Children.Add(_view);

        Director = new MiiEditorDirector(_view.Player, library, random);
        Director.Cue += (_, cue) =>
        {
            if (_pending is { } pending && pending.Cue == cue)
                ShowPending();
        };
        Director.ReactionEnded += _ => ShowPending();

        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerExited += (_, _) =>
        {
            _pointer = null;
            Cursor = Cursor.Default;
        };
        PointerCaptureLost += (_, _) =>
        {
            _pressedAt = null;
            _dragging = false;
        };
    }

    public MiiEditorDirector Director { get; }

    /// <summary>False when OpenGL isn't available and the Mii is shown without animation.</summary>
    public bool IsAnimated => _fallback is null;

    /// <summary>Plays the entrance as soon as the Mii is on screen.</summary>
    public void Start()
    {
        // Wait until the Mii is actually on screen (its head is built), so the entrance isn't half over by then.
        _view.MiiShown += StartWhenShown;
    }

    private void StartWhenShown(string _)
    {
        _view.MiiShown -= StartWhenShown;
        Director.Start();
    }

    /// <summary>
    /// Shows the Mii as it is now (it's copied, so later edits don't show until the next call), optionally playing a
    /// reaction. With <paramref name="showAtCue"/> the Mii only changes when the reaction reaches that event marker
    /// (e.g. mid-twirl for a gender swap); it changes right away when the reaction can't play.
    /// </summary>
    public void Present(Mii mii, MiiEditorReaction? reaction = null, string? showAtCue = null)
    {
        if (reaction is { } r)
            Director.React(r);
        if (mii.Clone() is not { IsSuccess: true } copy)
            return;

        // Also hold back edits made while an earlier change still waits for its cue, or they'd give it away early.
        showAtCue ??= _pending?.Cue;
        if (showAtCue is not null && _fallback is null && Director.WillCue(showAtCue))
        {
            _pending = (copy.Value, showAtCue);
            _view.Prewarm(copy.Value);
            return;
        }

        _pending = null;
        Show(copy.Value);
    }

    public void SetFocus(MiiEditorFocus focus)
    {
        Director.SetFocus(focus);
        var shot = focus == MiiEditorFocus.Face ? FaceShot : BodyShot;
        if (ReferenceEquals(shot, _shot))
            return;
        _shot = shot;
        _targetYaw = 0;
        _view.TransitionTo(WithYaw(_shot, _yaw), CameraTransition);
    }

    private void ShowPending()
    {
        if (_pending is not { } pending)
            return;
        _pending = null;
        Show(pending.Mii);
    }

    private void Show(Mii mii)
    {
        _shown = mii;
        if (_fallback is { } fallback)
        {
            fallback.Mii = mii;
            fallback.RefreshCurrentMii();
            return;
        }

        var studio = MiiStudioDataSerializer.Serialize(mii, _calendar.IsAprilFirst);
        _view.SetMii(mii, studio.IsSuccess ? studio.Value : null);
    }

    #region Per frame: turning back and looking at the cursor

    private void OnFrame(double deltaSeconds)
    {
        if (!_dragging && MathF.Abs(_targetYaw - _yaw) > 0.01f)
        {
            _yaw += (_targetYaw - _yaw) * (1f - MathF.Exp(-YawReturnSpeed * (float)deltaSeconds));
            _view.Specifications = WithYaw(_shot, _yaw);
        }

        var look = _view.Player.Look;
        look.TargetWeight = Director.LookWeight;
        if (_pointer is { } pointer && !_dragging && LookAngles(pointer) is { } angles)
            (look.TargetYaw, look.TargetPitch) = angles;
        else
            (look.TargetYaw, look.TargetPitch) = (0f, 0f);
    }

    /// <summary>Head turn (degrees, relative to where the body faces) to look at what's under the cursor.</summary>
    private (float Yaw, float Pitch)? LookAngles(Point pointer)
    {
        if (_view.HeadCenterOnStage() is not { } head || _view.ScreenRay(pointer) is not { } ray || _view.LastFrame is not { } frame)
            return null;

        // The point under the cursor on a plane a little in front of the head (towards the camera).
        var planePoint = head - ray.Direction * LookDepth;
        var target = ray.Origin + ray.Direction * Vector3.Dot(planePoint - ray.Origin, ray.Direction);
        var to = target - head;

        var facing = Vector3.Transform(Vector3.UnitZ, frame.Pose.WorldRotation[(int)MiiBone.Root]);
        var bodyYaw = MathF.Atan2(facing.X, facing.Z);
        var yaw = WrapRadians(MathF.Atan2(to.X, to.Z) - bodyYaw) * 180f / MathF.PI;
        var pitch = MathF.Atan2(to.Y, MathF.Sqrt(to.X * to.X + to.Z * to.Z)) * 180f / MathF.PI;
        // Behind the Mii (turned around by dragging): just look ahead.
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

    #region Pointer: hover, click and drag

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        _pointer = point;
        if (_pressedAt is { } pressedAt)
        {
            if (!_dragging && Distance(point, pressedAt) > DragThreshold)
                _dragging = true;
            if (_dragging)
            {
                _yaw += (float)(point.X - pressedAt.X) * DragDegreesPerPixel;
                _targetYaw = _yaw;
                _pressedAt = point;
                _view.Specifications = WithYaw(_shot, _yaw);
            }
        }

        Cursor =
            _dragging ? new Cursor(StandardCursorType.SizeWestEast)
            : HitTestMii(point) is not null ? new Cursor(StandardCursorType.Hand)
            : Cursor.Default;
        _view.Invalidate();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _pressedAt = e.GetPosition(this);
        _dragging = false;
        e.Pointer.Capture(this);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedAt is null)
            return;
        var wasDragging = _dragging;
        _pressedAt = null;
        _dragging = false;
        e.Pointer.Capture(null);
        if (!wasDragging && HitTestMii(e.GetPosition(this)) is { } part)
            Director.Poke(part);
    }

    /// <summary>Which part of the Mii is under a point of this control (from the last drawn pose).</summary>
    private MiiBodyPart? HitTestMii(Point point)
    {
        if (_fallback is not null || _view.LastFrame is null)
            return null;

        Point? Screen(MiiBone bone) => _view.BoneOnStage(bone) is { } stage ? _view.ProjectToScreen(stage) : null;

        // Head: a circle around its middle, as big as the head looks on screen.
        if (_view.HeadCenterOnStage() is { } headCenter && _view.ProjectToScreen(headCenter) is { } head)
        {
            var top = _view.ProjectToScreen(headCenter + new Vector3(0, 30f, 0));
            var radius = top is { } t ? Distance(head, t) : 0;
            if (Distance(point, head) <= radius)
                return MiiBodyPart.Head;
        }

        // Limbs and body as thick lines between joints. Thickness scales with the zoom (head size above).
        var scale =
            _view.ProjectToScreen(Vector3.Zero) is { } a && _view.ProjectToScreen(new Vector3(0, 10f, 0)) is { } b
                ? Distance(a, b) / 10
                : 1;
        double Segment(MiiBone from, MiiBone to) =>
            Screen(from) is { } p && Screen(to) is { } q ? DistanceToSegment(point, p, q) : double.MaxValue;

        var leftLeg = Math.Min(Segment(MiiBone.LegL1, MiiBone.LegL2), Segment(MiiBone.LegL2, MiiBone.FootL));
        var rightLeg = Math.Min(Segment(MiiBone.LegR1, MiiBone.LegR2), Segment(MiiBone.LegR2, MiiBone.FootR));
        var body = new[]
        {
            Segment(MiiBone.Hip, MiiBone.Chest),
            Segment(MiiBone.ArmL1, MiiBone.ArmL2),
            Segment(MiiBone.ArmL2, MiiBone.HandL),
            Segment(MiiBone.ArmR1, MiiBone.ArmR2),
            Segment(MiiBone.ArmR2, MiiBone.HandR),
        }.Min();

        var legReach = 9 * scale;
        var bodyReach = 20 * scale;
        if (Math.Min(leftLeg, rightLeg) <= legReach && Math.Min(leftLeg, rightLeg) < body)
            return leftLeg <= rightLeg ? MiiBodyPart.LeftLeg : MiiBodyPart.RightLeg;
        return body <= bodyReach ? MiiBodyPart.Body : null;
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

    #endregion

    private void SwitchToCpu()
    {
        if (_fallback is not null)
            return;
        Children.Remove(_view);
        _fallback = new Mii3DRender
        {
            ReloadMethod = BaseMiiImage.ReloadMethodType.KeepInstanceUntilNew,
            ImageVariant = MiiImageVariants.MiiEditorPreviewCarousel,
        };
        Children.Add(_fallback);
        // Without the animation there are no cues to wait for.
        ShowPending();
        if (_shown is { } mii)
            Show(mii);
    }
}

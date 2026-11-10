using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Rig;
using Testably.Abstractions.RandomSystem;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiImages.Views;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared.Calendar;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WheelWizardData.Views;

/// <summary>
/// The leaderboard's top three on a podium. Their Miis stand on the steps in realtime 3D: when the leaderboard loads
/// the steps appear, the Miis drop onto them one by one (third, second, then the winner, who cheers in a burst of
/// confetti while the others clap), and then they play little scenes together, like high fives and copycat dances.
/// <para>
/// The steps are this panel's children (<see cref="LeaderboardPodiumStep"/>s), each placed under its Mii by
/// <see cref="PlaceProperty"/> (1, 2 or 3). Without OpenGL or with animations turned off the Miis are still pictures.
/// Call <see cref="Initialize"/> once with the services it needs.
/// </para>
/// </summary>
public sealed class LeaderboardPodiumStage : Panel
{
    public static readonly AttachedProperty<int> PlaceProperty = AvaloniaProperty.RegisterAttached<LeaderboardPodiumStage, Control, int>(
        "Place"
    );

    public static int GetPlace(Control control) => control.GetValue(PlaceProperty);

    public static void SetPlace(Control control, int place) => control.SetValue(PlaceProperty, place);

    public static readonly StyledProperty<Mii?> FirstProperty = AvaloniaProperty.Register<LeaderboardPodiumStage, Mii?>(nameof(First));
    public static readonly StyledProperty<Mii?> SecondProperty = AvaloniaProperty.Register<LeaderboardPodiumStage, Mii?>(nameof(Second));
    public static readonly StyledProperty<Mii?> ThirdProperty = AvaloniaProperty.Register<LeaderboardPodiumStage, Mii?>(nameof(Third));

    public Mii? First
    {
        get => GetValue(FirstProperty);
        set => SetValue(FirstProperty, value);
    }

    public Mii? Second
    {
        get => GetValue(SecondProperty);
        set => SetValue(SecondProperty, value);
    }

    public Mii? Third
    {
        get => GetValue(ThirdProperty);
        set => SetValue(ThirdProperty, value);
    }

    private static readonly MiiImageSpecifications Shot = MiiImageVariants.PodiumStage;

    /// <summary>
    /// The podium the leaderboard scenes were made for (canonical units): where each place stands and the height of its
    /// step. Each step is <see cref="StepWidth"/> wide. Index 0 is first place.
    /// </summary>
    private static readonly Vector3[] Platforms = [new(0, 70, 0), new(-132, 46, 0), new(132, 28, 0)];

    private const float StepWidth = 132f;
    private const double StepGap = 3;
    private static readonly string[] Roles = ["first", "second", "third"];
    private const string Scenes = "leaderboard/scenes";

    /// <summary>The steps fade in one after another, lowest first.</summary>
    private static readonly TimeSpan StepFade = TimeSpan.FromMilliseconds(260);

    private static readonly TimeSpan StepStagger = TimeSpan.FromMilliseconds(110);

    /// <summary>The light behind a Mii comes on this long after it landed on its step.</summary>
    private static readonly TimeSpan GlowDelay = TimeSpan.FromSeconds(1);

    /// <summary>The entrance event marking the moment a Mii lands on its step.</summary>
    private const string LandedEvent = "landed";

    /// <summary>Don't keep everyone waiting on a Mii whose head takes long to build.</summary>
    private static readonly TimeSpan ShowTimeout = TimeSpan.FromSeconds(1.5);

    /// <summary>Played scenes stay in step with the winner's: further apart than this (frames) and they're pulled back.</summary>
    private const double MaxDrift = 1.5;

    /// <summary>The camera doesn't depend on the Mii for whole-body shots; any Mii will do to ask the renderer for it.</summary>
    private static readonly Lazy<string?> CameraStudio = new(
        () => MiiStudioDataSerializer.Serialize(MiiFactory.CreateDefaultMale()) is { IsSuccess: true } studio ? studio.Value : null
    );

    private IMiiNativeRenderer? _renderer;
    private ISeasonalCalendar? _calendar;
    private MiiClipPicker? _picker;
    private bool _animate;
    private bool _realtimeUnavailable;

    private readonly Actor?[] _actors = new Actor?[3];
    private Matrix4x4? _camera;
    private Size _cameraSize;
    private MiiAnimation? _scene;
    private bool _showQueued;
    private int _show;
    private DispatcherTimer? _showTimeout;

    /// <summary>One place on the podium: its Mii in 3D, or as a picture without animations.</summary>
    private sealed class Actor(int place, Mii mii)
    {
        public int Place { get; } = place;
        public Mii Mii { get; } = mii;
        public MiiRealtimeView? View { get; set; }
        public MiiImageLoader? Still { get; set; }
        public MiiAnimation? Entrance { get; set; }
        public bool Shown { get; set; }
    }

    static LeaderboardPodiumStage()
    {
        FirstProperty.Changed.AddClassHandler<LeaderboardPodiumStage>((stage, _) => stage.QueueShow());
        SecondProperty.Changed.AddClassHandler<LeaderboardPodiumStage>((stage, _) => stage.QueueShow());
        ThirdProperty.Changed.AddClassHandler<LeaderboardPodiumStage>((stage, _) => stage.QueueShow());
        PlaceProperty.Changed.AddClassHandler<Control>((control, _) => (control.Parent as LeaderboardPodiumStage)?.InvalidateArrange());
    }

    public LeaderboardPodiumStage()
    {
        ClipToBounds = false;
        Background = Brushes.Transparent;
    }

    public void Initialize(
        IMiiNativeRenderer renderer,
        IMiiAnimationLibrary library,
        IRandom random,
        ISeasonalCalendar calendar,
        bool animate
    )
    {
        _renderer = renderer;
        _calendar = calendar;
        _picker = new MiiClipPicker(library, random);
        _animate = animate;
        InvalidateArrange();
        QueueShow();
    }

    #region Layout

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 440 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 380 : availableSize.Height;
        var size = new Size(width, height);
        foreach (var child in Children)
        {
            // Square image templates must be measured at their podium size, not the whole stage.
            var actor = _actors.FirstOrDefault(a => a?.Still == child);
            child.Measure(actor is null ? availableSize : StillBounds(actor.Place, size).Size);
        }
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var place = GetPlace(child);
            if (place is >= 1 and <= 3)
                child.Arrange(StepBounds(place - 1, finalSize));
            else if (child is MiiImageLoader still && _actors.FirstOrDefault(a => a?.Still == still) is { } actor)
                child.Arrange(StillBounds(actor.Place, finalSize));
            else
                child.Arrange(new Rect(finalSize));
        }
        return finalSize;
    }

    /// <summary>A step goes from its top (where its Mii's feet are) down to the bottom of the stage.</summary>
    private Rect StepBounds(int place, Size size)
    {
        var platform = Platforms[place];
        var left = Project(Canonical(platform.X - StepWidth / 2, platform.Y), size);
        var right = Project(Canonical(platform.X + StepWidth / 2, platform.Y), size);
        if (left is not { } l || right is not { } r)
            return default;
        return new Rect(l.X + StepGap / 2, l.Y, Math.Max(0, r.X - l.X - StepGap), Math.Max(0, size.Height - l.Y));
    }

    /// <summary>
    /// Where a whole-body picture of a place's Mii goes: same scale as the stage, feet on the step. (The picture is
    /// the zoom 1 whole-body shot: 200 render units tall, with the floor 2.5% above its bottom edge.)
    /// </summary>
    private Rect StillBounds(int place, Size size)
    {
        var platform = Platforms[place];
        if (
            Project(Canonical(platform.X, platform.Y), size) is not { } feet
            || Project(Canonical(platform.X, platform.Y) + new Vector3(0, 100, 0), size) is not { } above
        )
            return default;
        var picture = (feet.Y - above.Y) * 2;
        return new Rect(feet.X - picture / 2, feet.Y - picture * 0.975, picture, picture);
    }

    /// <summary>A podium point (canonical) where it's drawn: the steps are sized for an average Mii (see <see cref="MiiStage"/>).</summary>
    private static Vector3 Canonical(float x, float y) =>
        new Vector3(x, y, 0) * MiiBodyModel.CanonicalToRenderUnits * MiiStage.DefaultBodyScale;

    /// <summary>Where a point of the scene (render units) shows up in the stage, with the same camera as the Miis.</summary>
    private Point? Project(Vector3 point, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0 || _renderer is null || CameraStudio.Value is not { } studio)
            return null;
        if (_camera is null || _cameraSize != size)
        {
            var setup = _renderer.GetRealtimeFrameSetup(studio, Shot, (float)(size.Width / size.Height));
            if (setup.IsFailure)
                return null;
            _camera = setup.Value.View * setup.Value.Projection;
            _cameraSize = size;
        }

        var clip = Vector4.Transform(new Vector4(point, 1), _camera.Value);
        if (clip.W <= 1e-4f)
            return null;
        return new Point((clip.X / clip.W * 0.5 + 0.5) * size.Width, (0.5 - clip.Y / clip.W * 0.5) * size.Height);
    }

    #endregion

    #region The show

    private void QueueShow()
    {
        if (_showQueued)
            return;
        _showQueued = true;
        // The three places change one after another; start once they're all in (and laid out).
        Dispatcher.UIThread.Post(StartShow, DispatcherPriority.Background);
    }

    private void StartShow()
    {
        _showQueued = false;
        if (_renderer is null)
            return;

        ClearActors();
        _show++;
        foreach (var step in Children.Where(c => GetPlace(c) is >= 1 and <= 3))
            step.Classes.Set("Lit", false);

        Mii?[] miis = [First, Second, Third];
        var live = _animate && !_realtimeUnavailable;
        for (var place = 0; place < 3; place++)
        {
            if (miis[place] is { } mii)
                _actors[place] = live ? CreateLive(place, mii) : CreateStill(place, mii);
            // Still pictures need no entrance; empty steps stay unlit.
            if (!live && _actors[place] is not null)
                Light(place, _animate ? GlowDelay : TimeSpan.Zero);
        }

        // Draw the winner last, so its confetti falls in front of the others.
        foreach (var place in new[] { 2, 1, 0 })
            if (_actors[place] is { } actor)
                Children.Add((Control?)actor.View ?? actor.Still!);

        ShowSteps();
        if (live && _actors.Any(a => a is not null))
        {
            _showTimeout?.Stop();
            _showTimeout = new DispatcherTimer { Interval = ShowTimeout };
            _showTimeout.Tick += (_, _) => DropIn();
            _showTimeout.Start();
        }
    }

    private Actor CreateLive(int place, Mii mii)
    {
        var actor = new Actor(place, mii);
        var studio = MiiStudioDataSerializer.Serialize(mii, _calendar?.IsAprilFirst ?? false) is { IsSuccess: true } serialized
            ? serialized.Value
            : null;
        var view = new MiiRealtimeView(_renderer!)
        {
            IsHitTestVisible = false,
            Specifications = Shot,
            Placement = HeightCorrection(place, mii),
            // Heads are small on the stage; the motion (drops, confetti) is quick, so no frame cap.
            Detail = MiiHeadDetail.Small,
        };
        view.SetMii(mii, studio);
        view.MiiShown += _ =>
        {
            actor.Shown = true;
            if (_actors.All(a => a is null || a.Shown))
                DropIn();
        };
        view.RealtimeUnavailable += _ => SwitchToStills();
        view.ClipFinished += clip => OnClipFinished(actor, clip);
        view.ClipEvent += (clip, animEvent) =>
        {
            if (ReferenceEquals(clip, actor.Entrance) && animEvent.Name == LandedEvent)
                Light(place, GlowDelay);
        };

        // Wait out of sight on the entrance's first frame until everyone's head is built, then drop in together.
        if (_picker!.Pick($"leaderboard/enter/{Roles[place]}") is { } entrance)
        {
            actor.Entrance = entrance;
            view.Preload(entrance);
            view.Player.Play(entrance, loop: false, fadeSeconds: 0);
        }
        view.IsPlaying = false;
        actor.View = view;
        return actor;
    }

    private Actor CreateStill(int place, Mii mii) =>
        new(place, mii)
        {
            Still = new MiiImageLoader
            {
                Mii = mii,
                IsHitTestVisible = false,
                LoadingColor = Brushes.Transparent,
                ImageVariant = MiiImageVariants.FullBodyCarousel,
            },
        };

    /// <summary>
    /// The scenes stand an average Mii's feet on its step; a shorter or taller Mii's feet end up lower or higher
    /// (root height scales with the body), so move it back onto its step.
    /// </summary>
    private static Vector3 HeightCorrection(int place, Mii mii)
    {
        var scale = MiiBodyModel.CalculateBodyScale(mii.Weight.Value, mii.Height.Value);
        return new Vector3(0, MiiBodyModel.CanonicalToRenderUnits * Platforms[place].Y * (MiiStage.DefaultBodyScale.Y - scale.Y), 0);
    }

    /// <summary>Everyone's ready (or we stopped waiting): start the entrances together.</summary>
    private void DropIn()
    {
        if (_showTimeout is null)
            return;
        _showTimeout.Stop();
        _showTimeout = null;
        foreach (var actor in _actors)
        {
            if (actor?.View is not { } view)
                continue;
            view.IsPlaying = true;
            if (actor.Entrance is null)
                Light(actor.Place, GlowDelay);
        }

        if (_actors.FirstOrDefault(a => a?.View is not null) is { Entrance: null })
            StartScene();
    }

    private void OnClipFinished(Actor actor, MiiAnimation clip)
    {
        // An entrance without a landing marker: it's landed once it's over.
        if (ReferenceEquals(clip, actor.Entrance) && clip.Events.All(e => e.Name != LandedEvent))
            Light(actor.Place, GlowDelay);

        // The first Mii still standing directs: when its entrance or scene ends, everyone starts the next scene.
        if (!ReferenceEquals(actor, _actors.FirstOrDefault(a => a?.View is not null)))
            return;
        if (ReferenceEquals(clip, actor.Entrance) || _scene is not null && ReferenceEquals(clip, _scene.ForActor(actor.Place)))
            StartScene();
    }

    private void StartScene()
    {
        if (_picker!.Pick(Scenes) is not { } scene || scene.ActorCount < 3)
            return;
        _scene = scene;
        var lead = _actors.First(a => a?.View is not null)!;
        lead.View!.FrameUpdating -= KeepInStep;
        lead.View.FrameUpdating += KeepInStep;
        foreach (var actor in _actors)
            actor?.View?.Player.Play(scene.ForActor(actor.Place), loop: false, fadeSeconds: 0.3);
    }

    /// <summary>
    /// Each Mii has its own view and clock. Keep their scene playheads together (a view that skipped frames would
    /// otherwise miss its high five).
    /// </summary>
    private void KeepInStep(double _)
    {
        if (_scene is null || _actors.First(a => a?.View is not null) is not { View: { } lead } leader)
            return;
        if (!ReferenceEquals(lead.Player.Current, _scene.ForActor(leader.Place)))
            return;
        foreach (var actor in _actors)
        {
            if (
                actor?.View is not { } view
                || ReferenceEquals(actor, leader)
                || !ReferenceEquals(view.Player.Current, _scene.ForActor(actor.Place))
            )
                continue;
            if (Math.Abs(view.PlayheadFrames - lead.PlayheadFrames) > MaxDrift)
                view.Seek(lead.PlayheadFrames);
        }
    }

    /// <summary>Turns on the light behind a place's step after <paramref name="delay"/> (unless the show started over by then).</summary>
    private void Light(int place, TimeSpan delay)
    {
        var show = _show;
        Dispatcher.UIThread.Post(() =>
        {
            if (delay > TimeSpan.Zero)
                DispatcherTimer.RunOnce(TurnOn, delay);
            else
                TurnOn();
        });

        void TurnOn()
        {
            if (show == _show && Children.FirstOrDefault(c => GetPlace(c) == place + 1) is { } step)
                step.Classes.Set("Lit", true);
        }
    }

    /// <summary>The steps appear in place, lowest first.</summary>
    private void ShowSteps()
    {
        var steps = Children.Where(c => GetPlace(c) is >= 1 and <= 3).OrderByDescending(GetPlace).ToList();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!_animate)
            {
                step.Opacity = 1;
                step.RenderTransform = null;
                continue;
            }

            step.Transitions = null;
            step.Opacity = 0;
            step.RenderTransform = null;
            var delay = StepStagger * i;
            DispatcherTimer.RunOnce(
                () =>
                {
                    step.Transitions =
                    [
                        new DoubleTransition
                        {
                            Property = OpacityProperty,
                            Duration = StepFade,
                            Easing = new SineEaseInOut(),
                        },
                    ];
                    step.Opacity = 1;
                },
                delay + TimeSpan.FromMilliseconds(16)
            );
        }
    }

    private void SwitchToStills()
    {
        _realtimeUnavailable = true;
        QueueShow();
    }

    private void ClearActors()
    {
        _showTimeout?.Stop();
        _showTimeout = null;
        _scene = null;
        for (var place = 0; place < 3; place++)
        {
            if (_actors[place] is not { } actor)
                continue;
            if (actor.View is { } view)
            {
                view.FrameUpdating -= KeepInStep;
                Children.Remove(view);
            }
            if (actor.Still is { } still)
                Children.Remove(still);
            _actors[place] = null;
        }
    }

    #endregion
}

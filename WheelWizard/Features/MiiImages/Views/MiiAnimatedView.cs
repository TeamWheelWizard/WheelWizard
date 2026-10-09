using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MiiAnim.Core.Animation;
using Testably.Abstractions.RandomSystem;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiAnimations.Playback;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Settings;
using WheelWizard.Shared.Calendar;
using WheelWizard.Views.Shell;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// The view behind <see cref="MiiAnimatedImage"/>: a <see cref="MiiRealtimeView"/> run by a <see cref="MiiPerformer"/>,
/// or a still <see cref="MiiImageView"/> when there's nothing to perform, animations are off or OpenGL isn't there.
/// </summary>
public sealed class MiiAnimatedView : BaseMiiImage
{
    /// <summary>A performance change counts as the same Mii changing state (waking up, say) once it showed this long.</summary>
    private static readonly TimeSpan SettledAfter = TimeSpan.FromSeconds(1.5);

    private const double ExitFadeSeconds = 0.08;

    /// <summary>Once OpenGL failed for one view it will for all of them; don't make every Mii try again.</summary>
    private static bool _realtimeUnavailable;

    private readonly IMiiNativeRenderer _renderer;
    private readonly IMiiAnimationLibrary _library;
    private readonly IRandom _random;
    private readonly ISettingsManager _settings;
    private readonly ISeasonalCalendar _calendar;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Grid _host = new();
    private readonly MiiImageView _still;

    private MiiRealtimeView? _live;
    private MiiPerformer? _performer;
    private Mii? _mii;
    private string? _studio;
    private string? _liveStudio;
    private string? _shownStudio;
    private TimeSpan _settledAt;
    private (IReadOnlyList<string> Entrance, IReadOnlyList<string>? Exit)? _arrival;
    private bool _leaving;
    private bool _waitingToShow;

    /// <summary>
    /// Whether it has been on screen. Starting OpenGL for a Mii is the expensive part, so in a long list that only
    /// happens once a card is scrolled to (and then it's kept).
    /// </summary>
    private bool _seen;

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty =
        MiiAnimatedImage.ImageVariantProperty.AddOwner<MiiAnimatedView>();

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications?> StillVariantProperty =
        MiiAnimatedImage.StillVariantProperty.AddOwner<MiiAnimatedView>();

    public MiiImageSpecifications? StillVariant
    {
        get => GetValue(StillVariantProperty);
        set => SetValue(StillVariantProperty, value);
    }

    public static readonly StyledProperty<MiiPerformance?> PerformanceProperty =
        MiiAnimatedImage.PerformanceProperty.AddOwner<MiiAnimatedView>();

    public MiiPerformance? Performance
    {
        get => GetValue(PerformanceProperty);
        set => SetValue(PerformanceProperty, value);
    }

    public static readonly StyledProperty<double> MaxFramesPerSecondProperty =
        MiiAnimatedImage.MaxFramesPerSecondProperty.AddOwner<MiiAnimatedView>();

    public double MaxFramesPerSecond
    {
        get => GetValue(MaxFramesPerSecondProperty);
        set => SetValue(MaxFramesPerSecondProperty, value);
    }

    public static readonly StyledProperty<IBrush> LoadingColorProperty = MiiAnimatedImage.LoadingColorProperty.AddOwner<MiiAnimatedView>();

    public IBrush LoadingColor
    {
        get => GetValue(LoadingColorProperty);
        set => SetValue(LoadingColorProperty, value);
    }

    public static readonly StyledProperty<IBrush> FallBackColorProperty =
        MiiAnimatedImage.FallBackColorProperty.AddOwner<MiiAnimatedView>();

    public IBrush FallBackColor
    {
        get => GetValue(FallBackColorProperty);
        set => SetValue(FallBackColorProperty, value);
    }

    public MiiAnimatedView(
        IMiiImagesSingletonService images,
        ISeasonalCalendar calendar,
        IMiiNativeRenderer renderer,
        IMiiAnimationLibrary library,
        IRandom random,
        ISettingsManager settings
    )
        : base(images)
    {
        _calendar = calendar;
        _renderer = renderer;
        _library = library;
        _random = random;
        _settings = settings;
        _still = new MiiImageView(images, calendar) { IsVisible = false };
        _still.Bind(MiiImageView.LoadingColorProperty, this.GetObservable(LoadingColorProperty));
        _still.Bind(MiiImageView.FallBackColorProperty, this.GetObservable(FallBackColorProperty));
        _still.MiiImageLoaded += (_, _) => MiiLoaded = true;
        _host.Children.Add(_still);
        Content = _host;
        EffectiveViewportChanged += (_, e) =>
        {
            _viewport = e.EffectiveViewport;
            CheckSeen();
        };
        SizeChanged += (_, _) => CheckSeen();
    }

    private Rect? _viewport;

    /// <summary>Starts the live Mii the first time any of it is on screen.</summary>
    private void CheckSeen()
    {
        if (_seen || _viewport is not { } viewport || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        if (!new Rect(Bounds.Size).Intersects(viewport))
            return;
        _seen = true;
        if (IsImageAttached)
            RefreshCurrentMii();
    }

    /// <summary>See <see cref="MiiAnimatedImage.Play"/>.</summary>
    public void Play(IReadOnlyList<string> folders)
    {
        if (_performer is { } performer && !_leaving && !_waitingToShow)
            performer.Play(folders);
    }

    /// <summary>See <see cref="MiiAnimatedImage.ArriveWith"/>.</summary>
    public void ArriveWith(IReadOnlyList<string> entrance, IReadOnlyList<string>? exit) => _arrival = (entrance, exit);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImageVariantProperty && _live is { } live)
        {
            live.Specifications = ImageVariant;
            live.Invalidate();
        }
        else if (change.Property == MaxFramesPerSecondProperty && _live is { } capped)
            capped.MaxFramesPerSecond = MaxFramesPerSecond;
        else if (change.Property == StillVariantProperty || change.Property == ImageVariantProperty)
        {
            if (_still.IsVisible)
                _still.ImageVariant = StillVariant ?? ImageVariant;
        }
        else if (change.Property == PerformanceProperty && IsImageAttached)
            OnPerformanceChanged();
    }

    private void OnPerformanceChanged()
    {
        var wasLive = _live is not null;
        UpdateMode();
        if (_live is null)
            return;
        if (!wasLive)
        {
            OnMiiChanged(_mii);
            return;
        }

        // A Mii that's been showing for a bit changes state (e.g. a friend comes online); a fresh one just starts.
        // Mid-entrance the entrance carries on and hands over to the new performance itself.
        if (_leaving || _waitingToShow || Performance is not { } performance)
            return;
        var settled = _clock.Elapsed >= _settledAt;
        _performer!.Perform(performance, arrive: settled, desync: !settled);
    }

    public override void RefreshCurrentMii()
    {
        UpdateMode();
        OnMiiChanged(Mii);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // A list may hand this card a different Mii when it comes back; start that one fresh.
        RemoveLive();
    }

    protected override void OnMiiChanged(Mii? newMii)
    {
        _mii = newMii;
        _studio =
            newMii is not null && MiiStudioDataSerializer.Serialize(newMii, _calendar.IsAprilFirst) is { IsSuccess: true } serialized
                ? serialized.Value
                : null;
        if (!IsImageAttached)
            return;
        if (_live is null)
        {
            // Not started yet because it hasn't been on screen: nothing to show (and no still picture to render).
            if (!WantsLive)
                ShowStill();
            return;
        }

        if (_leaving)
            return;
        if (_arrival is { } arrival)
        {
            // Only a Mii that's actually on screen leaves first.
            var exit = arrival.Exit is { } folders && _liveStudio is not null && !_waitingToShow ? _performer!.Pick(folders) : null;
            if (exit is null)
                Arrive(arrival.Entrance);
            else
                Leave(exit, arrival.Entrance);
            return;
        }

        if (_studio == _liveStudio)
        {
            _live.SetMii(_mii, _studio);
            return;
        }

        ShowLive();
        if (Performance is { } performance)
            _performer!.Perform(performance, desync: true);
    }

    /// <summary>Realtime when there's something to perform, OpenGL works and animations are on; otherwise a still image.</summary>
    private bool WantsLive => Performance is not null && !_realtimeUnavailable && _settings.ENABLE_ANIMATIONS.Get();

    private void UpdateMode()
    {
        var live = WantsLive;
        if (live && _live is null && _seen)
            QueueStart();
        else if (!live && _live is not null)
        {
            RemoveLive();
            ShowStill();
        }
    }

    /// <summary>Views waiting to start their live Mii, see <see cref="QueueStart"/>.</summary>
    private static readonly Queue<MiiAnimatedView> Starting = new();

    private static bool _starting;
    private bool _queued;

    /// <summary>
    /// A live Mii starts OpenGL on its first frame, which takes a moment on the UI thread. When many show up at once
    /// (opening the friends page) they start one per frame, so the page opens right away and they appear one by one.
    /// </summary>
    private void QueueStart()
    {
        if (_queued)
            return;
        _queued = true;
        Starting.Enqueue(this);
        if (!_starting)
        {
            _starting = true;
            StartNext();
        }
    }

    private static void StartNext()
    {
        TopLevel? topLevel = null;
        while (Starting.TryDequeue(out var view))
        {
            view._queued = false;
            if (!view.IsImageAttached || !view._seen || !view.WantsLive || view._live is not null)
                continue;
            view.CreateLive();
            view.OnMiiChanged(view._mii);
            topLevel = TopLevel.GetTopLevel(view);
            break;
        }

        if (Starting.Count == 0)
            _starting = false;
        else if (topLevel is not null)
            topLevel.RequestAnimationFrame(_ => StartNext());
        else
            Dispatcher.UIThread.Post(StartNext, DispatcherPriority.Background);
    }

    private void CreateLive()
    {
        // These Miis are small and often many at once (lists): small face textures, a capped frame rate, and a
        // fade in of the right Mii rather than showing the previous one while it loads.
        _live = new MiiRealtimeView(_renderer)
        {
            IsHitTestVisible = false,
            Specifications = ImageVariant,
            Detail = MiiHeadDetail.Small,
            ShowsPreviousMii = false,
            MaxFramesPerSecond = MaxFramesPerSecond,
        };
        _live.MiiShown += OnLiveMiiShown;
        _live.RealtimeUnavailable += _ =>
        {
            _realtimeUnavailable = true;
            UpdateMode();
        };
        _performer = new MiiPerformer(_live.Player, _library, _random);
        _liveStudio = _shownStudio = null;
        _still.IsVisible = false;
        _still.Mii = null;
        _host.Children.Add(_live);
    }

    private void RemoveLive()
    {
        if (_live is not { } live)
            return;
        _performer?.Detach();
        _performer = null;
        live.MiiShown -= OnLiveMiiShown;
        _host.Children.Remove(live);
        _live = null;
        _liveStudio = _shownStudio = null;
        _leaving = _waitingToShow = false;
    }

    private void ShowStill()
    {
        _still.ImageVariant = StillVariant ?? ImageVariant;
        _still.Mii = _mii;
        _still.IsVisible = true;
    }

    private void ShowLive()
    {
        _live!.SetMii(_mii, _studio);
        _liveStudio = _studio;
        _settledAt = _clock.Elapsed + SettledAfter;
        MiiLoaded = _studio is null || _shownStudio == _studio;
    }

    /// <summary>The Mii on screen leaves with <paramref name="exit"/>; the new one arrives once it's gone.</summary>
    private void Leave(MiiAnimation exit, IReadOnlyList<string> entrance)
    {
        _leaving = true;
        _live!.IsPlaying = true;
        if (_mii is { } next)
            _live!.Prewarm(next);
        _performer!.Play(
            exit,
            ExitFadeSeconds,
            then: () =>
            {
                _leaving = false;
                Arrive(entrance);
            }
        );
    }

    /// <summary>
    /// Shows the current Mii with an entrance. The entrance starts out of sight and waits on its first frame until
    /// the Mii's head is built, so it never starts with the previous Mii or an empty view.
    /// </summary>
    private void Arrive(IReadOnlyList<string> entrance)
    {
        _arrival = null;
        ShowLive();
        var clip = _performer!.Pick(entrance);
        if (Performance is { } performance)
        {
            if (clip is null)
                _performer.Perform(performance);
            else
                _performer.Perform(performance, clip);
        }
        else if (clip is not null)
            _performer.Play(clip, fadeSeconds: 0);

        _waitingToShow = clip is not null && _studio is not null && _shownStudio != _studio;
        _live!.IsPlaying = !_waitingToShow;
    }

    private void OnLiveMiiShown(string studio)
    {
        _shownStudio = studio;
        if (studio != _studio)
            return;
        MiiLoaded = true;
        if (_waitingToShow)
        {
            _waitingToShow = false;
            _live!.IsPlaying = true;
        }
    }
}

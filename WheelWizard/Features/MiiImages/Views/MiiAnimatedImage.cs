using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using WheelWizard.MiiAnimations.Playback;
using WheelWizard.MiiImages.Domain;
using WheelWizard.Views.Shell;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// A Mii that lives: it plays its <see cref="Performance"/> (idles, extras) in realtime 3D, framed by
/// <see cref="ImageVariant"/>. Without a performance, with animations turned off or without OpenGL it's a still image
/// of <see cref="StillVariant"/> (or <see cref="ImageVariant"/>) instead.
/// </summary>
public sealed class MiiAnimatedImage : MiiImageControl
{
    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        MiiAnimatedImage,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.FriendsSideProfileLive);

    /// <summary>Framing of the animated Mii (the clips move the Mii, so this usually has no character rotation).</summary>
    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications?> StillVariantProperty = AvaloniaProperty.Register<
        MiiAnimatedImage,
        MiiImageSpecifications?
    >(nameof(StillVariant));

    /// <summary>The still image shown instead of the animated Mii; <see cref="ImageVariant"/> when not set.</summary>
    public MiiImageSpecifications? StillVariant
    {
        get => GetValue(StillVariantProperty);
        set => SetValue(StillVariantProperty, value);
    }

    public static readonly StyledProperty<MiiPerformance?> PerformanceProperty = AvaloniaProperty.Register<
        MiiAnimatedImage,
        MiiPerformance?
    >(nameof(Performance));

    /// <summary>What the Mii does (see <see cref="MiiAnimations.MiiPerformances"/>); null shows the still image.</summary>
    public MiiPerformance? Performance
    {
        get => GetValue(PerformanceProperty);
        set => SetValue(PerformanceProperty, value);
    }

    public static readonly StyledProperty<IBrush> LoadingColorProperty = AvaloniaProperty.Register<MiiAnimatedImage, IBrush>(
        nameof(LoadingColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral900)
    );

    public IBrush LoadingColor
    {
        get => GetValue(LoadingColorProperty);
        set => SetValue(LoadingColorProperty, value);
    }

    public static readonly StyledProperty<IBrush> FallBackColorProperty = AvaloniaProperty.Register<MiiAnimatedImage, IBrush>(
        nameof(FallBackColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral700)
    );

    public IBrush FallBackColor
    {
        get => GetValue(FallBackColorProperty);
        set => SetValue(FallBackColorProperty, value);
    }

    private MiiAnimatedView? _view;
    private (IReadOnlyList<string> Entrance, IReadOnlyList<string>? Exit)? _arrival;

    /// <summary>Plays a random clip from <paramref name="folders"/> once (a reaction), then the performance carries on.</summary>
    public void Play(params string[] folders) => _view?.Play(folders);

    /// <summary>
    /// The next Mii that's set arrives with a clip from <paramref name="entrance"/> (which starts with the Mii out of
    /// sight). When a Mii is showing, it first leaves with a clip from <paramref name="exit"/>, if given.
    /// </summary>
    public void ArriveWith(IReadOnlyList<string> entrance, IReadOnlyList<string>? exit = null)
    {
        if (_view is { } view)
            view.ArriveWith(entrance, exit);
        else
            _arrival = (entrance, exit);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _view = e.NameScope.Find<MiiAnimatedView>("PART_Renderer");
        if (_view is not null && _arrival is { } arrival)
        {
            _arrival = null;
            _view.ArriveWith(arrival.Entrance, arrival.Exit);
        }
    }
}

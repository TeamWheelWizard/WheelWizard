using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using WheelWizard.MiiAnimations;
using WheelWizard.MiiAnimations.Playback;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// A Mii peeking over the edge of whatever sits right below it (a card, a panel): its lower body hides behind that
/// edge, which is this control's bottom edge. It dozes on the edge and wakes up while hovered or keyboard-focused.
/// <para>
/// The <see cref="ContentControl.Content"/> is a nametag that rides on the Mii's head (it follows the head as it
/// moves), drawn upright above the head and so partly above this control: leave some room there. It's a button, so
/// Click and Command pick it; <see cref="IsActive"/> marks the picked one. Make it about 100 px tall and at least as
/// wide (the clips are framed for that), with the edge directly under it.
/// </para>
/// </summary>
public class MiiPeeker : Button
{
    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<MiiPeeker, Mii?>(nameof(Mii));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<MiiPeeker, bool>(nameof(IsActive));

    /// <summary>The picked one of a group (shows as <c>:active</c>, e.g. a highlighted nametag).</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly StyledProperty<MiiPerformance> AsleepPerformanceProperty = AvaloniaProperty.Register<MiiPeeker, MiiPerformance>(
        nameof(AsleepPerformance),
        MiiPerformances.PeekAsleep
    );

    /// <summary>What the Mii does while nobody pays attention to it.</summary>
    public MiiPerformance AsleepPerformance
    {
        get => GetValue(AsleepPerformanceProperty);
        set => SetValue(AsleepPerformanceProperty, value);
    }

    public static readonly StyledProperty<MiiPerformance> AwakePerformanceProperty = AvaloniaProperty.Register<MiiPeeker, MiiPerformance>(
        nameof(AwakePerformance),
        MiiPerformances.PeekAwake
    );

    /// <summary>What the Mii does while hovered or focused.</summary>
    public MiiPerformance AwakePerformance
    {
        get => GetValue(AwakePerformanceProperty);
        set => SetValue(AwakePerformanceProperty, value);
    }

    /// <summary>Gap between the top of the head and the nametag.</summary>
    private const double TagGap = 2;

    /// <summary>Where the top of the head is without a live Mii (a still picture), as a part of the height.</summary>
    private const double StillCrown = 0.3;

    private MiiAnimatedImage? _mii;
    private Control? _tag;
    private MiiHeadOnScreen? _head;

    static MiiPeeker()
    {
        IsActiveProperty.Changed.AddClassHandler<MiiPeeker>((peeker, _) => peeker.PseudoClasses.Set(":active", peeker.IsActive));
        AsleepPerformanceProperty.Changed.AddClassHandler<MiiPeeker>((peeker, _) => peeker.UpdatePerformance());
        AwakePerformanceProperty.Changed.AddClassHandler<MiiPeeker>((peeker, _) => peeker.UpdatePerformance());
    }

    protected override Type StyleKeyOverride => typeof(MiiPeeker);

    /// <summary>Hovered, or focused with the keyboard (clicking focuses it too, which shouldn't keep it awake).</summary>
    public bool IsAwake => IsPointerOver || IsFocused && PseudoClasses.Contains(":focus-visible");

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_mii is not null)
            _mii.HeadMoved -= OnHeadMoved;
        if (_tag is not null)
            _tag.SizeChanged -= OnTagSizeChanged;

        _mii = e.NameScope.Find<MiiAnimatedImage>("PART_Mii");
        _tag = e.NameScope.Find<Control>("PART_Tag");
        if (_mii is not null)
            _mii.HeadMoved += OnHeadMoved;
        if (_tag is not null)
            _tag.SizeChanged += OnTagSizeChanged;
        UpdatePerformance();
        PlaceTag();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        UpdatePerformance();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        UpdatePerformance();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        UpdatePerformance();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        UpdatePerformance();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PlaceTag();
    }

    private void UpdatePerformance()
    {
        if (_mii is null)
            return;
        var performance = IsAwake ? AwakePerformance : AsleepPerformance;
        if (!ReferenceEquals(_mii.Performance, performance))
            _mii.Performance = performance;
    }

    private void OnHeadMoved(MiiHeadOnScreen? head)
    {
        _head = head;
        PlaceTag();
    }

    private void OnTagSizeChanged(object? sender, SizeChangedEventArgs e) => PlaceTag();

    /// <summary>Puts the tag's bottom middle just above the top of the head. The tag follows the head but stays upright.</summary>
    private void PlaceTag()
    {
        if (_tag is null || _mii is null)
            return;

        Point crown;
        if (_head is { } head)
        {
            // From the middle of the head up through its top: the way the head points on screen.
            var up = head.Crown - head.Middle;
            var length = Math.Sqrt(up.X * up.X + up.Y * up.Y);
            var direction = length > 0.001 ? new Point(up.X / length, up.Y / length) : new Point(0, -1);
            crown = _mii.TranslatePoint(head.Crown, this) ?? head.Crown;
            crown += direction * TagGap;
        }
        else
            crown = new Point(Bounds.Width / 2, Bounds.Height * StillCrown - TagGap);

        var size = _tag.Bounds.Size;
        _tag.RenderTransform = new TranslateTransform(crown.X - size.Width / 2, crown.Y - size.Height);
    }
}

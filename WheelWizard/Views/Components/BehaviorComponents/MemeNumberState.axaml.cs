using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace WheelWizard.Views.Components.BehaviorComponents;

public class MemeNumberState : TemplatedControl
{
    private StateBox? _stateBox;
    private FormFieldLabel? _niceLabel;

    public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<MemeNumberState, string>(nameof(Value), "0");

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly StyledProperty<Geometry> IconDataProperty = AvaloniaProperty.Register<MemeNumberState, Geometry>(
        nameof(IconData)
    );

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<MemeNumberState, double>(
        nameof(IconSize),
        20
    );

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly StyledProperty<string> TipTextProperty = AvaloniaProperty.Register<MemeNumberState, string>(nameof(TipText));

    public string TipText
    {
        get => GetValue(TipTextProperty);
        set => SetValue(TipTextProperty, value);
    }

    public static readonly StyledProperty<StateBox.StateBoxVariantType> VariantProperty = AvaloniaProperty.Register<
        MemeNumberState,
        StateBox.StateBoxVariantType
    >(nameof(Variant), StateBox.StateBoxVariantType.Default);

    public StateBox.StateBoxVariantType Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _stateBox = e.NameScope.Find<StateBox>("PART_StateBox");
        _niceLabel = e.NameScope.Find<FormFieldLabel>("PART_NiceLabel");

        UpdateState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            UpdateState();
        }
    }

    private void UpdateState()
    {
        if (_stateBox == null || _niceLabel == null)
            return;

        var val = Value;

        // Reset defaults
        _niceLabel.IsVisible = false;
        _stateBox.Content = null;
        _stateBox.Text = val; // Always ensure text is set for normal cases

        if (val == "69")
        {
            _niceLabel.IsVisible = true;
        }
        else if (val == "67")
        {
            _stateBox.Content = CreateBobbingBadge();
        }
    }

    private Control CreateBobbingBadge()
    {
        // 6 7 bobbing animation
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, Auto") };

        var t6 = new TextBlock
        {
            Text = "6",
            Foreground = Brushes.LightGoldenrodYellow,
            FontWeight = FontWeight.Bold,
            FontSize = 14, // Match StateBox default or bind it
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var t7 = new TextBlock
        {
            Text = "7",
            Foreground = Brushes.LightGoldenrodYellow,
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        // Add to grid
        Grid.SetColumn(t6, 0);
        Grid.SetColumn(t7, 1);
        grid.Children.Add(t6);
        grid.Children.Add(t7);

        // Animations
        var anim6 = CreateBobAnimation(0);
        var anim7 = CreateBobAnimation(0.5); // Phase shift

        anim6.RunAsync(t6);
        anim7.RunAsync(t7);

        return grid;
    }

    private Animation CreateBobAnimation(double delayRatio)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Delay = TimeSpan.FromSeconds(delayRatio), // Just initial offset, might not be perfect out of phase
        };

        // Keyframes: 0% -> 0, 100% -> -5 (up)
        // Since it's Alternate, it will go 0 -> -5 -> 0 -> -5...

        var kf1 = new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.YProperty, 0d) } };

        var kf2 = new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.YProperty, -4d) } };

        animation.Children.Add(kf1);
        animation.Children.Add(kf2);

        return animation;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public class MemeNumberState : TemplatedControl
{
    private StateBox? _stateBox;
    private StateBox? _specialBadge;
    private FormFieldLabel? _niceLabel;

    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<MemeNumberState, string>(nameof(Text), "0");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
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

    public static readonly StyledProperty<bool> Enable67Property = AvaloniaProperty.Register<MemeNumberState, bool>(nameof(Enable67), true);

    public bool Enable67
    {
        get => GetValue(Enable67Property);
        set => SetValue(Enable67Property, value);
    }

    public static readonly StyledProperty<bool> Enable69Property = AvaloniaProperty.Register<MemeNumberState, bool>(nameof(Enable69), true);

    public bool Enable69
    {
        get => GetValue(Enable69Property);
        set => SetValue(Enable69Property, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _stateBox = e.NameScope.Find<StateBox>("PART_StateBox");
        _specialBadge = e.NameScope.Find<StateBox>("PART_SpecialBadge");
        _niceLabel = e.NameScope.Find<FormFieldLabel>("PART_NiceLabel");

        UpdateState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            UpdateState();
        }
    }

    private void UpdateState()
    {
        if (_stateBox == null || _niceLabel == null || _specialBadge == null)
            return;

        var val = Text;

        // Reset defaults
        _niceLabel.IsVisible = false;
        _stateBox.IsVisible = true;
        _specialBadge.IsVisible = false;

        _stateBox.Text = val; // Always ensure text is set for normal cases

        if (val == "69" && Enable69)
        {
            _niceLabel.IsVisible = true;
        }
        else if (val == "67" && Enable67)
        {
            _stateBox.IsVisible = false;
            _specialBadge.IsVisible = true;
        }
    }
}

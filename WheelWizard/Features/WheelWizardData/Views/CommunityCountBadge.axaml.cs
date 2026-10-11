using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using WheelWizard.Views.Components;

namespace WheelWizard.WheelWizardData.Views;

public class CommunityCountBadge : TemplatedControl
{
    private StatusBadge? _stateBox;
    private StatusBadge? _specialBadge;
    private FormFieldLabel? _niceLabel;

    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CommunityCountBadge, string>(nameof(Text), "0");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<Geometry> IconDataProperty = AvaloniaProperty.Register<CommunityCountBadge, Geometry>(
        nameof(IconData)
    );

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<string> TipTextProperty = AvaloniaProperty.Register<CommunityCountBadge, string>(nameof(TipText));

    public string TipText
    {
        get => GetValue(TipTextProperty);
        set => SetValue(TipTextProperty, value);
    }

    public static readonly StyledProperty<StatusVariant> VariantProperty = AvaloniaProperty.Register<CommunityCountBadge, StatusVariant>(
        nameof(Variant),
        StatusVariant.Gray
    );

    public StatusVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly StyledProperty<bool> Enable67Property = AvaloniaProperty.Register<CommunityCountBadge, bool>(
        nameof(Enable67),
        true
    );

    public bool Enable67
    {
        get => GetValue(Enable67Property);
        set => SetValue(Enable67Property, value);
    }

    public static readonly StyledProperty<bool> Enable69Property = AvaloniaProperty.Register<CommunityCountBadge, bool>(
        nameof(Enable69),
        true
    );

    public bool Enable69
    {
        get => GetValue(Enable69Property);
        set => SetValue(Enable69Property, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _stateBox = e.NameScope.Find<StatusBadge>("PART_StateBox");
        _specialBadge = e.NameScope.Find<StatusBadge>("PART_SpecialBadge");
        _niceLabel = e.NameScope.Find<FormFieldLabel>("PART_NiceLabel");

        UpdateState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == Enable67Property || change.Property == Enable69Property)
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

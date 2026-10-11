using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public enum StatusVariant
{
    Gray,
    Dark,
    Brand,
    Success,
    Warning,
    Error,
    Info,
    Important,
}

public class StatusBadge : ContentControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<StatusBadge, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<StatusBadge, Geometry?>(nameof(IconData));

    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<string?> TipTextProperty = AvaloniaProperty.Register<StatusBadge, string?>(nameof(TipText));

    public string? TipText
    {
        get => GetValue(TipTextProperty);
        set => SetValue(TipTextProperty, value);
    }

    public static readonly StyledProperty<StatusVariant> VariantProperty = AvaloniaProperty.Register<StatusBadge, StatusVariant>(
        nameof(Variant),
        StatusVariant.Gray
    );

    public StatusVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly StyledProperty<PlacementMode> TipPlacementProperty = AvaloniaProperty.Register<StatusBadge, PlacementMode>(
        nameof(TipPlacement),
        PlacementMode.Top
    );

    public PlacementMode TipPlacement
    {
        get => GetValue(TipPlacementProperty);
        set => SetValue(TipPlacementProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == ContentProperty || change.Property == IconDataProperty)
            PseudoClasses.Set(":icon-only", string.IsNullOrEmpty(Text) && Content is null);
        if (change.Property == HeightProperty)
            SetValue(FontSizeProperty, double.IsFinite(Height) ? Math.Clamp(Height / 2, 10, 18) : 12);
    }
}

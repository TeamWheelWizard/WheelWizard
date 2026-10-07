using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public enum StatusVariant
{
    Default,
    Dark,
    Success,
    Warning,
    Danger,
}

public class StatusBadge : ContentControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<StatusBadge, string?>(nameof(Text), "0");

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

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<StatusBadge, double>(nameof(IconSize), 20);

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly StyledProperty<string?> TipTextProperty = AvaloniaProperty.Register<StatusBadge, string?>(nameof(TipText));

    public string? TipText
    {
        get => GetValue(TipTextProperty);
        set => SetValue(TipTextProperty, value);
    }

    public static readonly StyledProperty<StatusVariant> VariantProperty = AvaloniaProperty.Register<StatusBadge, StatusVariant>(
        nameof(Variant),
        StatusVariant.Default
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
}

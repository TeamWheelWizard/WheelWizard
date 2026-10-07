using Avalonia;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public enum ButtonVariant
{
    Default,
    Primary,
    Warning,
    Danger,
    Light,
}

public enum ButtonSize
{
    Regular,
    Compact,
}

public class Button : Avalonia.Controls.Button
{
    public static readonly StyledProperty<ButtonVariant> VariantProperty = AvaloniaProperty.Register<Button, ButtonVariant>(
        nameof(Variant),
        ButtonVariant.Default
    );

    public ButtonVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly StyledProperty<ButtonSize> SizeProperty = AvaloniaProperty.Register<Button, ButtonSize>(
        nameof(Size),
        ButtonSize.Regular
    );

    public ButtonSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<Button, Geometry?>(nameof(IconData));

    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<Button, double>(nameof(IconSize), 20);

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<Button, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<bool> IsIconLeftProperty = AvaloniaProperty.Register<Button, bool>(nameof(IsIconLeft), true);

    public bool IsIconLeft
    {
        get => GetValue(IsIconLeftProperty);
        set => SetValue(IsIconLeftProperty, value);
    }
}

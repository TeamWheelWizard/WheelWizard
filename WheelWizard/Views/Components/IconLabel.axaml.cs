using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public class IconLabel : TemplatedControl
{
    public static readonly StyledProperty<Geometry> IconDataProperty = AvaloniaProperty.Register<IconLabel, Geometry>(nameof(IconData));

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<IconLabel, double>(nameof(IconSize), 20.0); // Add a default value here

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly StyledProperty<object> TextProperty = AvaloniaProperty.Register<IconLabel, object>(nameof(Text));

    public object Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<bool> IsIconLeftProperty = AvaloniaProperty.Register<IconLabel, bool>(nameof(IsIconLeft), true);

    public bool IsIconLeft
    {
        get => GetValue(IsIconLeftProperty);
        set => SetValue(IsIconLeftProperty, value);
    }

    public static readonly StyledProperty<bool> IsUnderlinedProperty = AvaloniaProperty.Register<IconLabel, bool>(
        nameof(IsUnderlined),
        false
    );

    public bool IsUnderlined
    {
        get => GetValue(IsUnderlinedProperty);
        set => SetValue(IsUnderlinedProperty, value);
    }
}

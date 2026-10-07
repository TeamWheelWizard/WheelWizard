using Avalonia;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public class LinkButton : Button
{
    public static readonly StyledProperty<IBrush?> HoverForegroundProperty = AvaloniaProperty.Register<LinkButton, IBrush?>(
        nameof(HoverForeground)
    );

    public IBrush? HoverForeground
    {
        get => GetValue(HoverForegroundProperty);
        set => SetValue(HoverForegroundProperty, value);
    }

    public static readonly StyledProperty<bool> IsUnderlinedProperty = AvaloniaProperty.Register<LinkButton, bool>(
        nameof(IsUnderlined),
        false
    );

    public bool IsUnderlined
    {
        get => GetValue(IsUnderlinedProperty);
        set => SetValue(IsUnderlinedProperty, value);
    }
}

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace WheelWizard.Views.Components.StandardLibrary;

public class IconLabelButton : IconLabel
{
    public static readonly StyledProperty<IBrush> HoverForegroundProperty =
        AvaloniaProperty.Register<IconLabelButton, IBrush>(nameof(HoverForeground));

    public IBrush HoverForeground
    {
        get => GetValue(HoverForegroundProperty);
        set => SetValue(HoverForegroundProperty, value);
    }
    
    public event EventHandler? Click;
    
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Click?.Invoke(this, EventArgs.Empty);
    }
}


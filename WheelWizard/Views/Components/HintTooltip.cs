using Avalonia;
using Avalonia.Controls;

namespace WheelWizard.Views.Components;

/// <summary>Attach to any control through ToolTip.Tip.</summary>
public class HintTooltip : ToolTip
{
    protected override Type StyleKeyOverride => typeof(ToolTip);
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<HintTooltip, string?>(nameof(Text));
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public HintTooltip()
    {
        MaxWidth = 280;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            SetCurrentValue(ContentProperty, Text);
    }
}

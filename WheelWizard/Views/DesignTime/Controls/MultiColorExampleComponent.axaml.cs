using Avalonia;
using WheelWizard.Views.Components;

namespace WheelWizard.Views.DesignTime.Controls;

public class MultiColorExampleComponent : MultiColoredIcon
{
    public static readonly StyledProperty<string> IconNameProperty = AvaloniaProperty.Register<IconExampleComponent, string>(
        nameof(IconName)
    );

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }
}

using Avalonia;
using Avalonia.Controls.Primitives;

namespace WheelWizard.Views.Components.StandardLibrary;

public class LoadingIcon : TemplatedControl
{
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<LoadingIcon, double>(nameof(IconSize), 20.0);

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }
    
    public static readonly StyledProperty<string> AdditionalTextProperty =
        AvaloniaProperty.Register<LoadingIcon, string>(nameof(AdditionalText), "");
    
    public string AdditionalText
    {
        get => GetValue(AdditionalTextProperty);
        set => SetValue(AdditionalTextProperty, value);
    }
}


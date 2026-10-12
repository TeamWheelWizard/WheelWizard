using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WheelWizard.Views.DesignTime.Controls;

public partial class IconExampleComponent : UserControl
{
    public IconExampleComponent()
    {
        InitializeComponent();
        DataContext = this;
    }

    public static readonly StyledProperty<string> IconNameProperty = AvaloniaProperty.Register<IconExampleComponent, string>(
        nameof(IconName)
    );

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public static readonly StyledProperty<Geometry> IconDataProperty = AvaloniaProperty.Register<IconExampleComponent, Geometry>(
        nameof(IconData)
    );

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
}

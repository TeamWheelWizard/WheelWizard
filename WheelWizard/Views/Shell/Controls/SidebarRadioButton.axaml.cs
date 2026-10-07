using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WheelWizard.Views.Shell.Views;

namespace WheelWizard.Views.Shell.Controls;

public class SidebarRadioButton : RadioButton
{
    public static readonly StyledProperty<Geometry> IconDataProperty = AvaloniaProperty.Register<SidebarRadioButton, Geometry>(
        nameof(IconData)
    );

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<SidebarRadioButton, string>(nameof(Text));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<Type?> PageTypeProperty = AvaloniaProperty.Register<SidebarRadioButton, Type?>(nameof(PageType));

    public Type? PageType
    {
        get => GetValue(PageTypeProperty);
        set => SetValue(PageTypeProperty, value);
    }

    public static readonly StyledProperty<string> BoxTextProperty = AvaloniaProperty.Register<SidebarRadioButton, string>(nameof(BoxText));

    public string BoxText
    {
        get => GetValue(BoxTextProperty);
        set => SetValue(BoxTextProperty, value);
    }

    public static readonly StyledProperty<string> BoxTipProperty = AvaloniaProperty.Register<SidebarRadioButton, string>(nameof(BoxTip));

    public string BoxTip
    {
        get => GetValue(BoxTipProperty);
        set => SetValue(BoxTipProperty, value);
    }

    public static readonly StyledProperty<Geometry> BoxIconDataProperty = AvaloniaProperty.Register<SidebarRadioButton, Geometry>(
        nameof(BoxIconData)
    );

    public Geometry BoxIconData
    {
        get => GetValue(BoxIconDataProperty);
        set => SetValue(BoxIconDataProperty, value);
    }

    public static readonly StyledProperty<double> BoxIconSizeProperty = AvaloniaProperty.Register<SidebarRadioButton, double>(
        nameof(BoxIconSize)
    );

    public double BoxIconSize
    {
        get => GetValue(BoxIconSizeProperty);
        set => SetValue(BoxIconSizeProperty, value);
    }

    public event EventHandler<Type>? NavigationRequested;

    protected override void OnClick()
    {
        base.OnClick();
        NavigationRequested?.Invoke(this, PageType ?? typeof(NotFoundPage));
    }
}

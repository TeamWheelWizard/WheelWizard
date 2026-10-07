using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WheelWizard.Views.Pages;

namespace WheelWizard.Views.Patterns;

public partial class SidebarRadioButton : RadioButton
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

    //todo: after patches is more stable, uncomment this

    // public static readonly StyledProperty<bool> WarningVisibleProperty = AvaloniaProperty.Register<SidebarRadioButton, bool>(
    //     nameof(WarningVisible)
    // );
    //
    // public bool WarningVisible
    // {
    //     get => GetValue(WarningVisibleProperty);
    //     set => SetValue(WarningVisibleProperty, value);
    // }
    //
    // public static readonly StyledProperty<string> WarningTipProperty = AvaloniaProperty.Register<SidebarRadioButton, string>(
    //     nameof(WarningTip)
    // );
    //
    // public string WarningTip
    // {
    //     get => GetValue(WarningTipProperty);
    //     set => SetValue(WarningTipProperty, value);
    // }

    public event EventHandler<Type>? NavigationRequested;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        PageType ??= typeof(NotFoundPage);

        NavigationRequested?.Invoke(this, PageType);
    }
}

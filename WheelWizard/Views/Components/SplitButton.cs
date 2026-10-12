using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace WheelWizard.Views.Components;

public class SplitButton : Avalonia.Controls.SplitButton
{
    public static readonly StyledProperty<string?> TextProperty = ActionButton.TextProperty.AddOwner<SplitButton>();
    public static readonly StyledProperty<Geometry?> IconDataProperty = ActionButton.IconDataProperty.AddOwner<SplitButton>();
    public static readonly StyledProperty<ActionButtonTone> ToneProperty = ActionButton.ToneProperty.AddOwner<SplitButton>();
    public static readonly StyledProperty<ButtonSize> SizeProperty = ActionButton.SizeProperty.AddOwner<SplitButton>();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ConfigureMenu();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FlyoutProperty)
            ConfigureMenu();
    }

    private void ConfigureMenu()
    {
        if (Flyout is MenuFlyout menu)
        {
            // The theme becomes available after the component joins the styled tree.
            if (this.FindResource("SplitButtonMenu") is ControlTheme theme)
                menu.FlyoutPresenterTheme = theme;
            menu.Placement = PlacementMode.BottomEdgeAlignedRight;
        }
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
    public ActionButtonTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
    public ButtonSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }
}

public class SplitButtonItem : MenuItem
{
    public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<SplitButtonItem, Geometry?>(
        nameof(IconData)
    );
    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
}

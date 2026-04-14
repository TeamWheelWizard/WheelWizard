using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace WheelWizard.Views.Components;

public class FormFieldLabel : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<FormFieldLabel, string>(nameof(Text));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<string> TipTextProperty = AvaloniaProperty.Register<FormFieldLabel, string>(nameof(TipText));

    public string TipText
    {
        get => GetValue(TipTextProperty);
        set => SetValue(TipTextProperty, value);
    }

    public static readonly StyledProperty<PlacementMode> TipPlacementProperty = AvaloniaProperty.Register<FormFieldLabel, PlacementMode>(
        nameof(TipPlacement),
        PlacementMode.TopEdgeAlignedLeft
    );

    public PlacementMode TipPlacement
    {
        get => GetValue(TipPlacementProperty);
        set => SetValue(TipPlacementProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != TipPlacementProperty)
            return;

        ToolTip.SetPlacement(this, TipPlacement);
    }
}

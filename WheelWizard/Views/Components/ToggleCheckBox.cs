using Avalonia;

namespace WheelWizard.Views.Components;

public enum ToggleVariant
{
    Checkbox,
    Switch,
    Radio,
}

public class ToggleCheckBox : Avalonia.Controls.CheckBox
{
    public static readonly StyledProperty<bool> SelectionPreviewEnabledProperty = AvaloniaProperty.Register<ToggleCheckBox, bool>(
        nameof(SelectionPreviewEnabled),
        true
    );

    public bool SelectionPreviewEnabled
    {
        get => GetValue(SelectionPreviewEnabledProperty);
        set => SetValue(SelectionPreviewEnabledProperty, value);
    }

    public static readonly StyledProperty<ToggleVariant> VariantProperty = AvaloniaProperty.Register<ToggleCheckBox, ToggleVariant>(
        nameof(Variant),
        ToggleVariant.Checkbox
    );

    public ToggleVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
}

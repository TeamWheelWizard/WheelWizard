using Avalonia;

namespace WheelWizard.Views.Components;

public class ToggleRadioButton : Avalonia.Controls.RadioButton
{
    public static readonly StyledProperty<bool> SelectionPreviewEnabledProperty =
        ToggleCheckBox.SelectionPreviewEnabledProperty.AddOwner<ToggleRadioButton>();

    public bool SelectionPreviewEnabled
    {
        get => GetValue(SelectionPreviewEnabledProperty);
        set => SetValue(SelectionPreviewEnabledProperty, value);
    }

    public static readonly StyledProperty<ToggleVariant> VariantProperty = ToggleCheckBox.VariantProperty.AddOwner<ToggleRadioButton>(
        new StyledPropertyMetadata<ToggleVariant>(ToggleVariant.Radio)
    );

    public ToggleVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
}

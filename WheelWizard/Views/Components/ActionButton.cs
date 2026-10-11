using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public enum ButtonSize
{
    Regular,
    Compact,
}

public enum ActionButtonVariant
{
    Button,
    Ghost,
    Label,
    Link,
}

public enum ActionButtonTone
{
    Primary,
    Brand,
    Secondary,
    Danger,
}

public class ActionButton : Avalonia.Controls.Button
{
    public static readonly StyledProperty<ActionButtonVariant> VariantProperty = AvaloniaProperty.Register<
        ActionButton,
        ActionButtonVariant
    >(nameof(Variant));
    public static readonly StyledProperty<ActionButtonTone> ToneProperty = AvaloniaProperty.Register<ActionButton, ActionButtonTone>(
        nameof(Tone)
    );
    public ActionButtonTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public static readonly StyledProperty<bool> IsLoadingProperty = AvaloniaProperty.Register<ActionButton, bool>(nameof(IsLoading));
    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public static readonly StyledProperty<bool> IsCircularProperty = AvaloniaProperty.Register<ActionButton, bool>(nameof(IsCircular));
    public bool IsCircular
    {
        get => GetValue(IsCircularProperty);
        set => SetValue(IsCircularProperty, value);
    }

    public static readonly StyledProperty<ButtonSize> SizeProperty = AvaloniaProperty.Register<ActionButton, ButtonSize>(nameof(Size));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ActionButton, string?>(nameof(Text));
    public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<ActionButton, Geometry?>(
        nameof(IconData)
    );
    public static readonly StyledProperty<bool> IsIconLeftProperty = AvaloniaProperty.Register<ActionButton, bool>(
        nameof(IsIconLeft),
        true
    );
    public ActionButtonVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
    public ButtonSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
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
    public bool IsIconLeft
    {
        get => GetValue(IsIconLeftProperty);
        set => SetValue(IsIconLeftProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Keep the label in native Content for button automation and command behavior.
        if (change.Property == TextProperty)
            SetCurrentValue(ContentProperty, Text);
        if (
            change.Property == ContentProperty
            || change.Property == IconDataProperty
            || change.Property == IsCircularProperty
            || change.Property == IsLoadingProperty
        )
        {
            var hasLabel = Content is string text ? !string.IsNullOrEmpty(text) : Content is not null;
            PseudoClasses.Set(":has-label", hasLabel && !IsCircular);
            PseudoClasses.Set(":icon-only", IsCircular || !hasLabel && (IconData is not null || IsLoading));
        }
    }
}

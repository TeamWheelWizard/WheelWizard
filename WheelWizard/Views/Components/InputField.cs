using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WheelWizard.Views.Components;

public class InputField : TextBox
{
    private TopLevel? _owner;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RemoveOutsideHandler();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnOutsidePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source != this && !source.GetVisualAncestors().Contains(this))
            _owner?.FocusManager?.Focus(null);
    }

    private void RemoveOutsideHandler()
    {
        _owner?.RemoveHandler(PointerPressedEvent, OnOutsidePressed);
        _owner = null;
    }

    public static readonly StyledProperty<InputFieldVariant> VariantProperty = AvaloniaProperty.Register<InputField, InputFieldVariant>(
        nameof(Variant)
    );
    public static readonly StyledProperty<string?> NoteProperty = AvaloniaProperty.Register<InputField, string?>(nameof(Note));
    public InputFieldVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<InputField, Geometry?>(nameof(IconData));
    public static readonly StyledProperty<string?> PlaceholderProperty = AvaloniaProperty.Register<InputField, string?>(
        nameof(Placeholder)
    );
    public static readonly StyledProperty<string?> ErrorTextProperty = AvaloniaProperty.Register<InputField, string?>(nameof(ErrorText));
    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
    public string? Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }
    public string? ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsFocusedProperty)
        {
            RemoveOutsideHandler();
            if (IsFocused)
            {
                _owner = TopLevel.GetTopLevel(this);
                _owner?.AddHandler(PointerPressedEvent, OnOutsidePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            }
        }
        if (change.Property == PlaceholderProperty)
            SetCurrentValue(PlaceholderTextProperty, Placeholder);
        if (change.Property == ErrorTextProperty || change.Property == NoteProperty)
        {
            PseudoClasses.Set(":has-error", HasError);
            PseudoClasses.Set(":has-note", !HasError && !string.IsNullOrWhiteSpace(Note));
        }
    }
}

public enum InputFieldVariant
{
    Bordered,
    Borderless,
}

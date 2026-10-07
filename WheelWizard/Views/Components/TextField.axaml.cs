using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace WheelWizard.Views.Components;

public enum TextFieldVariant
{
    Default,
    Dark,
}

public class TextField : TemplatedControl
{
    private TextBox? _input;
    private bool _hasError;

    public static readonly StyledProperty<TextFieldVariant> VariantProperty = AvaloniaProperty.Register<TextField, TextFieldVariant>(
        nameof(Variant),
        TextFieldVariant.Default
    );

    public TextFieldVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly StyledProperty<string?> ErrorTextProperty = AvaloniaProperty.Register<TextField, string?>(nameof(ErrorText));

    public string? ErrorText
    {
        get => GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<TextField, string?>(
        nameof(Text),
        defaultBindingMode: BindingMode.TwoWay
    );

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<string?> PlaceholderTextProperty = AvaloniaProperty.Register<TextField, string?>(
        nameof(PlaceholderText)
    );

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Label));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly StyledProperty<string?> TipTextProperty = AvaloniaProperty.Register<TextField, string?>(nameof(TipText));

    public string? TipText
    {
        get => GetValue(TipTextProperty);
        set => SetValue(TipTextProperty, value);
    }

    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<TextField, bool>(nameof(IsReadOnly), false);

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly DirectProperty<TextField, bool> HasErrorProperty = AvaloniaProperty.RegisterDirect<TextField, bool>(
        nameof(HasError),
        field => field.HasError
    );
    public bool HasError => _hasError;

    public static readonly RoutedEvent<TextChangedEventArgs> TextChangedEvent = RoutedEvent.Register<TextField, TextChangedEventArgs>(
        nameof(TextChanged),
        RoutingStrategies.Bubble
    );

    public event EventHandler<TextChangedEventArgs>? TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_input is not null)
            _input.TextChanged -= InputTextChanged;
        base.OnApplyTemplate(e);
        _input = e.NameScope.Find<TextBox>("PART_Input");
        if (_input is not null)
            _input.TextChanged += InputTextChanged;
        UpdateAppearance();
    }

    private void InputTextChanged(object? sender, TextChangedEventArgs e) => RaiseEvent(new TextChangedEventArgs(TextChangedEvent, this));

    private void UpdateAppearance()
    {
        SetAndRaise(HasErrorProperty, ref _hasError, !string.IsNullOrWhiteSpace(ErrorText));
        PseudoClasses.Set(":error", HasError);
        _input?.Classes.Set("error", HasError);
        _input?.Classes.Set("dark", Variant == TextFieldVariant.Dark);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ErrorTextProperty || change.Property == VariantProperty)
            UpdateAppearance();
    }
}

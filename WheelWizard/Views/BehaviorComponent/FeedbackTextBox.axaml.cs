using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WheelWizard.Views.BehaviorComponent;

public partial class FeedbackTextBox : UserControl
{
    #region Properties

    public static readonly StyledProperty<TextBoxVariantType> VariantProperty = AvaloniaProperty.Register<
        FeedbackTextBox,
        TextBoxVariantType
    >(nameof(Variant), TextBoxVariantType.Default);

    public static readonly StyledProperty<string> ErrorMessageProperty = AvaloniaProperty.Register<FeedbackTextBox, string>(
        nameof(ErrorMessage)
    );

    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<FeedbackTextBox, string>(nameof(Text));

    public static readonly StyledProperty<string> WatermarkProperty = AvaloniaProperty.Register<FeedbackTextBox, string>(
        nameof(Watermark),
        "Enter text here..."
    );

    public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<FeedbackTextBox, string>(nameof(Label));

    public static readonly StyledProperty<string> TipTextProperty = AvaloniaProperty.Register<FeedbackTextBox, string>(nameof(TipText));

    public static readonly RoutedEvent<TextChangedEventArgs> TextChangedEvent = RoutedEvent.Register<TextBox, TextChangedEventArgs>(
        nameof(TextChanged),
        RoutingStrategies.Bubble
    );

    public enum TextBoxVariantType
    {
        Default,
        Dark,
    }

    public TextBoxVariantType Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public string ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string TipText
    {
        get => GetValue(TipTextProperty);
        set => SetValue(TipTextProperty, value);
    }

    public event EventHandler<TextChangedEventArgs>? TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    #endregion

    public FeedbackTextBox()
    {
        InitializeComponent();
        DataContext = this;

        InputField.TextChanged += (_, _) => RaiseEvent(new TextChangedEventArgs(TextChangedEvent, this));
        // If there is uses for more other events, then we can always add them
    }

    private void UpdateStyleClasses(TextBoxVariantType variant)
    {
        if (variant == TextBoxVariantType.Dark)
            InputField.Classes.Add("dark");
        else
            InputField.Classes.Remove("dark");
    }

    private void UpdateErrorState(bool hasError)
    {
        if (!hasError)
        {
            InputField.Classes.Remove("error");
            return;
        }

        if (!InputField.Classes.Contains("error"))
            InputField.Classes.Add("error");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VariantProperty)
            UpdateStyleClasses(change.GetNewValue<TextBoxVariantType>());

        if (change.Property == ErrorMessageProperty)
            UpdateErrorState(hasError: !string.IsNullOrWhiteSpace(change.GetNewValue<string?>()));
    }
}

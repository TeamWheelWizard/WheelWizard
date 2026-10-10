using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs.Base;
using Button = WheelWizard.Views.Components.ActionButton;

namespace WheelWizard.Views.Dialogs;

public partial class TextInputWindow : PopupContent
{
    private string? _result;
    private TaskCompletionSource<string?>? _tcs;
    private string? _initialText;
    private Func<string?, string, OperationResult>? inputValidationFunc; // (oldText?, newText) => OperationResult

    // Constructor with dynamic label parameter
    public TextInputWindow()
        : base(true, false, true, "Wheel Wizard")
    {
        InitializeComponent();
        InputField.TextChanged += InputField_TextChanged;
        UpdateSubmitButtonState();
    }

    public TextInputWindow SetMainText(string mainText)
    {
        MainTextBlock.Text = mainText;
        return this;
    }

    public TextInputWindow SetPlaceholderText(string placeholder)
    {
        InputField.Placeholder = placeholder;
        return this;
    }

    public TextInputWindow SetExtraText(string extraText)
    {
        ExtraTextBlock.Text = extraText;
        return this;
    }

    public TextInputWindow SetCustomCharacters(IEnumerable<char> characters, bool initiallyOpen = false)
    {
        SetupCustomChars(characters);
        CustomChars.IsVisible = CustomChars.Children.Count > 0 && initiallyOpen;
        CustomCharsButton.IsVisible = CustomChars.Children.Count > 0 && !initiallyOpen;
        return this;
    }

    public TextInputWindow SetButtonText(string cancelText, string submitText)
    {
        CancelButton.Text = cancelText;
        SubmitButton.Text = submitText;

        // It really depends on the text length what looks best
        ButtonContainer.HorizontalAlignment =
            (submitText.Length + cancelText.Length) > 12 ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        return this;
    }

    public TextInputWindow SetInitialText(string text)
    {
        _initialText = text;
        InputField.Text = text;
        UpdateSubmitButtonState();
        return this;
    }

    public TextInputWindow SetValidation(Func<string?, string, OperationResult> validationFunction)
    {
        inputValidationFunc = validationFunction;
        UpdateSubmitButtonState();
        return this;
    }

    public new async Task<string?> ShowDialog()
    {
        _tcs = new();
        Show(); // or ShowDialog(parentWindow);
        return await _tcs.Task;
    }

    private void SetupCustomChars(IEnumerable<char> characters)
    {
        CustomChars.Children.Clear();

        foreach (var c in characters)
        {
            var button = new ActionButton()
            {
                Tone = ActionButtonTone.Secondary,
                Text = c.ToString(),
                FontSize = 24,
                Padding = new(0),
                Margin = new(1),
            };
            button.Click += (_, _) => InputField.Text += c;
            CustomChars.Children.Add(button);
        }
    }

    // Handle text changes to enable/disable Submit button
    private void InputField_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateSubmitButtonState();
    }

    // Update the Submit button's IsEnabled property based on input
    private void UpdateSubmitButtonState()
    {
        var inputText = GetInputText() ?? string.Empty;
        var validationError = inputValidationFunc?.Invoke(_initialText, inputText).Error?.Message;

        var hasError = !string.IsNullOrWhiteSpace(validationError);

        SubmitButton.IsEnabled = !hasError;
        InputField.ErrorText = hasError ? validationError! : null;
    }

    private void CustomCharsButton_Click(object sender, RoutedEventArgs e)
    {
        CustomChars.IsVisible = true;
        CustomCharsButton.IsVisible = false;
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateSubmitButtonState();
        if (!SubmitButton.IsEnabled)
            return;
        _result = GetInputText();
        _tcs?.TrySetResult(_result); // Set the result of the task
        Close();
    }

    private string? GetInputText() => InputField.Text;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void BeforeClose()
    {
        // If you want to return something different, then to the TrySetResult before you close it
        _tcs?.TrySetResult(null);
    }
}

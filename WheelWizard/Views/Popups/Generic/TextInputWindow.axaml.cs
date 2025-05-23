using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using WheelWizard.Views.Popups.Base;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Popups.Generic;

public partial class TextInputWindow : PopupContent
{
    private string? _result;
    private TaskCompletionSource<string?>? _tcs;
    private string? _initialText;
    private Func<string?, string, OperationResult>? inputValidationFunc; // (oldText?, newText) => OperationResult

    // Constructor with dynamic label parameter
    public TextInputWindow()
        : base(true, false, true, "Text Field")
    {
        InitializeComponent();
        InputField.TextChanged += InputField_TextChanged;
        UpdateSubmitButtonState();
        SetupCustomChars();
    }

    public TextInputWindow SetMainText(string mainText)
    {
        MainTextBlock.Text = mainText;
        return this;
    }

    public TextInputWindow SetPlaceholderText(string placeholder)
    {
        InputField.Watermark = placeholder;
        return this;
    }

    public TextInputWindow SetExtraText(string extraText)
    {
        ExtraTextBlock.Text = extraText;
        return this;
    }

    public TextInputWindow SetAllowCustomChars(bool allow)
    {
        CustomCharsButton.IsVisible = allow;
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
        InputField.Text = text;
        _initialText = text;
        return this;
    }

    public TextInputWindow SetValidation(Func<string?, string, OperationResult> validationFunction)
    {
        inputValidationFunc = validationFunction;
        return this;
    }

    public new async Task<string?> ShowDialog()
    {
        _tcs = new();
        Show(); // or ShowDialog(parentWindow);
        return await _tcs.Task;
    }

    private void SetupCustomChars()
    {
        CustomChars.Children.Clear();
        // All the custom chars that are grouped together
        var charRanges = new List<(char, char)>
        {
            ((char)0x2460, (char)0x246e),
            ((char)0xe000, (char)0xe01c),
            ((char)0xf061, (char)0xf06d),
            ((char)0xf074, (char)0xf07c),
            ((char)0xf107, (char)0xf12f),
        };

        var chars = new List<char>();
        foreach (var (start, end) in charRanges)
        {
            for (var i = start; i <= end; i++)
            {
                chars.Add(i);
            }
        }

        // All the left-over chars that we cant make easy groups out of
        chars.AddRange(
            [
                (char)0xe028,
                (char)0xe068,
                (char)0xe067,
                (char)0xe06a,
                (char)0xe06b,
                (char)0xf030,
                (char)0xf031,
                (char)0xf034,
                (char)0xf035,
                (char)0xf038,
                (char)0xf039,
                (char)0xf03c,
                (char)0xf03d,
                (char)0xf041,
                (char)0xf043,
                (char)0xf044,
                (char)0xf047,
                (char)0xf050,
                (char)0xf058,
                (char)0xf05e,
                (char)0xf05f,
                (char)0xf103,
            ]
        );

        foreach (var c in chars)
        {
            var button = new Button()
            {
                Text = c.ToString(),
                IconSize = 0,
                FontSize = 24,
                Padding = new(0),
                Margin = new(1),
            };
            button.Click += (_, _) => InputField.Text += c;
            CustomChars.Children.Add(button);
        }
    }

    // Handle text changes to enable/disable Submit button
    private void InputField_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSubmitButtonState();
    }

    // Update the Submit button's IsEnabled property based on input
    private void UpdateSubmitButtonState()
    {
        var inputText = GetTrimmedTextInput();
        var validationResultError = inputValidationFunc?.Invoke(_initialText, inputText!).Error?.Message;

        SubmitButton.IsEnabled = validationResultError == null;
        InputField.ErrorMessage = validationResultError ?? "";
    }

    private void CustomCharsButton_Click(object sender, EventArgs e)
    {
        CustomChars.IsVisible = true;
        CustomCharsButton.IsVisible = false;
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        _result = GetTrimmedTextInput();
        _tcs?.TrySetResult(_result); // Set the result of the task
        Close();
    }

    private string? GetTrimmedTextInput() => InputField.Text?.Trim();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void BeforeClose()
    {
        // If you want to return something different, then to the TrySetResult before you close it
        _tcs?.TrySetResult(null);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WheelWizard.Views.Popups.Generic;

public partial class TextInputWindow : PopupContent
{
    private string? _result;
    private TaskCompletionSource<string?>? _tcs;

    // Constructor with dynamic label parameter
    public TextInputWindow() : base(true, false, true, "Text Field")
    {
        InitializeComponent();
   
        InputField.TextChanged += InputField_TextChanged;
        UpdateSubmitButtonState();
    }

    public TextInputWindow setLabelText(string labelText)
    {
        DynamicLabel.Text = labelText;
        return this;
    }
    
    public new async Task<string?> ShowDialog()
    {
        _tcs = new TaskCompletionSource<string?>();
        Show(); // or ShowDialog(parentWindow);
        return await _tcs.Task;
    }

    // Handle text changes to enable/disable Submit button
    private void InputField_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSubmitButtonState();
    }

    // Update the Submit button's IsEnabled property based on input
    private void UpdateSubmitButtonState()
    {
        SubmitButton.IsEnabled = !string.IsNullOrWhiteSpace(InputField.Text);
    }

    // Handle Submit button click
    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        _result = InputField.Text.Trim();
        _tcs?.TrySetResult(_result); // Set the result of the task
        Close();
    }

    // Handle Cancel button click
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _result = null;
        _tcs?.TrySetResult(null); // Set the result of the task to null
        Close();
    }
    
    public void PopulateText(string text)
    {
        InputField.Text = text;
    }
}


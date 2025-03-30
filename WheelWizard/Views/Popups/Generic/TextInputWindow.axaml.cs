using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Popups.Generic;

public partial class TextInputWindow : PopupContent
{
    private string? _result;
    private string? initialText;
    private bool allowCustomChars;
    private TaskCompletionSource<string?>? _tcs;

    // Constructor with dynamic label parameter
    public TextInputWindow() : base(true, false, true, "Text Field")
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
        allowCustomChars = allow;
        CustomCharsButton.IsVisible = allow;
        // This is not really reversible, but that is also not really a problem, since when do we even want to sue that
        if (allow)
        {
            var newSize = new Vector(Window.InternalSize.X, Window.InternalSize.Y + 40);
            Window.SetWindowSize(newSize);
        }
        return this;
    }
    
    public TextInputWindow SetButtonText(string cancelText, string submitText)
    {
        CancelButton.Text = cancelText;
        SubmitButton.Text = submitText;
        
        // It really depends on the text length what looks best
        ButtonContainer.HorizontalAlignment = (submitText.Length + cancelText.Length) > 12
            ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        return this;
    }
    
    public TextInputWindow SetInitialText(string text)
    {
        InputField.Text = text;
        initialText = text;
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
        // All the up to's are inclusive
        // 2460 up to 246e
        // e000 up to e01c
        // f061 up to f06d
        // f074 up to f07c
        // f107 up to f12f
        // leftovers: e028, e068, e067, e06a, e06b, f030, f031, f034, f035, f038, f039, f03c, f03d, f041, f043, f044, f047, f050, f058, f05e, f05f, f102, f103, 
        
        var chars = new List<char>();
        for (var i = 0x2460; i <= 0x246e; i++)
        {
            chars.Add((char)i);
        }
        for (var i = 0xe000; i <= 0xe01c; i++)
        {
            chars.Add((char)i);
        }
        for (var i = 0xf061; i <= 0xf06d; i++)
        {
            chars.Add((char)i);
        }
        for (var i = 0xf074; i <= 0xf07c; i++)
        {
            chars.Add((char)i);
        }
        for (var i = 0xf107; i <= 0xf12f; i++)
        {
            chars.Add((char)i);
        }
        chars.AddRange(new[]
        {
            (char)0xe028, (char)0xe068, (char)0xe067, (char)0xe06a, (char)0xe06b,
            (char)0xf030, (char)0xf031, (char)0xf034, (char)0xf035, (char)0xf038,
            (char)0xf039, (char)0xf03c, (char)0xf03d, (char)0xf041, (char)0xf043,
            (char)0xf044, (char)0xf047, (char)0xf050, (char)0xf058, (char)0xf05e, 
            (char)0xf05f, (char)0xf103,
        });
        
        foreach (var c in chars)
        {
            var button = new Button
            {
                Text = c.ToString(),
                IconSize = 0,
                FontSize = 24,
                Padding = new(0),
                Margin = new(1),
            };
            button.Click += (_,_) => InputField.Text += c;
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
        var sameAsInitial = InputField.Text == initialText;
        var empty = string.IsNullOrWhiteSpace(InputField.Text);
        SubmitButton.IsEnabled = !sameAsInitial && !empty;
    }
    
    private void CustomCharsButton_Click(object sender, EventArgs e)
    {
        CustomChars.IsVisible = true;
        CustomCharsButton.IsVisible = false;
        var newSize = new Vector(Window.InternalSize.X, Window.InternalSize.Y + 370);
        Window.SetWindowSize(newSize);
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
}


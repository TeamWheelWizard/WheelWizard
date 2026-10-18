using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WheelWizard.Views.Shell.Couch;

/// <summary>Noninteractive Kenney controller prompts shared by the shell and text keyboard.</summary>
public sealed class CouchInputHints : WrapPanel
{
    private readonly bool _keyboard;
    private readonly IBrush _labelBrush = new SolidColorBrush((Color)Application.Current!.FindResource("Neutral300")!);

    public CouchInputHints()
        : this(false) { }

    public CouchInputHints(bool keyboard)
    {
        _keyboard = keyboard;
        IsHitTestVisible = false;
        RefreshLabels();
    }

    public void RefreshLabels()
    {
        Children.Clear();
        Add("couch.input_move", _keyboard ? "couch.hint_choose_key" : "couch.hint_move", CouchPrompt.DPad, CouchPrompt.LeftStick);
        Add("couch.input_confirm", _keyboard ? "couch.hint_type" : "couch.hint_select", CouchPrompt.Confirm);
        Add("couch.input_back", _keyboard ? "action.cancel" : "action.back", CouchPrompt.Back);
        if (_keyboard)
            return;
        Add("couch.input_sections", "couch.hint_sections", CouchPrompt.PreviousSection, CouchPrompt.NextSection);
        Add("couch.input_menu", "couch.hint_options", CouchPrompt.Menu);
        Add("couch.input_text", "couch.text_input", CouchPrompt.TextInput);
    }

    private void Add(string inputKey, string actionKey, CouchPrompt first, CouchPrompt? second = null)
    {
        var label = t(actionKey);
        var hint = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 24, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(hint, $"{t(inputKey)}: {label}");
        var icons = new StackPanel { Orientation = Orientation.Horizontal };
        icons.Children.Add(CouchInputGlyphs.Create(first));
        if (second is { } next)
            icons.Children.Add(CouchInputGlyphs.Create(next));
        var text = new TextBlock
        {
            Text = label,
            FontSize = 14,
            Foreground = _labelBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        hint.Children.Add(icons);
        hint.Children.Add(text);
        Children.Add(hint);
    }
}

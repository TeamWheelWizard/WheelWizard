using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Shell.Couch.Input;

internal sealed class CouchMenu : ModalContent
{
    public CouchMenu(string status, bool canNavigate, bool canEditText, Action settings, Action fullscreen, Action quit, Action textInput)
        : base(t("couch.menu"))
    {
        IsDismissible = true;
        var body = new StackPanel { Spacing = 12, Width = 600 };
        body.Children.Add(new TextBlock { Text = t("couch.menu"), FontSize = 28 });
        body.Children.Add(new TextBlock { Text = status, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Add(t("couch.resume"), () => { });
        Add(t("couch.text_input_shortcut"), textInput, canEditText);
        Add(t("couch.toggle_fullscreen"), fullscreen, canNavigate);
        Add(t("page_title.settings"), settings, canNavigate);
        Add(t("couch.quit"), quit);
        if (!canNavigate)
            body.Children.Add(new TextBlock { Text = t("couch.navigation_blocked"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Content = body;

        void Add(string label, Action action, bool enabled = true)
        {
            var button = new ActionButton
            {
                Text = label,
                MinHeight = 48,
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = enabled,
            };
            button.Click += (_, _) =>
            {
                // Remove this modal synchronously before mode switching or text focus.
                CloseImmediately();
                action();
            };
            body.Children.Add(button);
        }
    }
}

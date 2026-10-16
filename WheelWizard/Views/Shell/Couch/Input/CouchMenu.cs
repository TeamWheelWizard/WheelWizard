using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Shell.Couch.Input;

internal sealed class CouchMenu : ModalContent
{
    private readonly ActionButton _resume;

    public CouchMenu(string? error, bool canNavigate, bool canEditText, Action settings, Action fullscreen, Action textInput)
        : base(t("couch.menu"))
    {
        IsDismissible = true;
        var body = new StackPanel { Spacing = 16, Width = 480 };
        body.Children.Add(
            new ModalHeading
            {
                Content = new TextBlock { Text = t("couch.menu"), Classes = { "TitleText" } },
            }
        );
        var actions = new StackPanel { Spacing = 8 };
        _resume = Add(t("couch.resume"), () => { }, ActionButtonTone.Brand, icon: "Play");
        if (canEditText)
            Add(t("couch.text_input_shortcut"), textInput);
        Add(t("page_title.settings"), settings, enabled: canNavigate, icon: "Gear");
        Add(t("couch.toggle_fullscreen"), fullscreen, enabled: canNavigate);
        body.Children.Add(actions);
        if (!canNavigate)
            body.Children.Add(new TextBlock { Text = t("couch.navigation_blocked"), Classes = { "BodyText" } });
        if (!string.IsNullOrWhiteSpace(error))
            body.Children.Add(new TextBlock { Text = error, Classes = { "BodyText" } });
        Content = body;

        ActionButton Add(
            string label,
            Action action,
            ActionButtonTone tone = ActionButtonTone.Secondary,
            bool enabled = true,
            string? icon = null
        )
        {
            var button = new ActionButton
            {
                Text = label,
                Tone = tone,
                IconData = icon is null ? null : Application.Current!.FindResource(icon) as Geometry,
                MinHeight = 52,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = enabled,
            };
            button.Click += (_, _) =>
            {
                // Remove this modal synchronously before navigation or text focus.
                CloseImmediately();
                action();
            };
            actions.Children.Add(button);
            return button;
        }
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();
        Dispatcher.UIThread.Post(() => _resume.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }
}

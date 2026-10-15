using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Shell.Couch.Input;

/// <summary>A transaction over a native TextBox: Cancel never changes the source, including passwords.</summary>
internal sealed class CouchKeyboard : ModalContent
{
    private readonly TextBox _target;
    private readonly TextBox _editor;
    private readonly Action _submit;
    private readonly List<(ActionButton Button, char Character)> _letters = [];
    private readonly ActionButton _firstKey;
    private bool _upper;

    public CouchKeyboard(TextBox target, Action submit)
        : base(t("couch.text_input"))
    {
        _target = target;
        _submit = submit;
        IsDismissible = true;
        DismissAction = Cancel;
        _editor = new TextBox
        {
            Text = target.Text,
            PasswordChar = target.PasswordChar,
            MaxLength = target.MaxLength,
            AcceptsReturn = target.AcceptsReturn,
            TextWrapping = target.TextWrapping,
            MinHeight = 48,
            FontSize = 22,
            ClearSelectionOnLostFocus = false,
            IsInactiveSelectionHighlightEnabled = true,
        };
        _editor.CaretIndex = target.CaretIndex;
        _editor.SelectionStart = target.SelectionStart;
        _editor.SelectionEnd = target.SelectionEnd;
        AddHandler(InputElement.TextInputEvent, OnPhysicalTextInput, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyDownEvent, OnPhysicalEditingKey, RoutingStrategies.Tunnel);
        var body = new StackPanel
        {
            Spacing = 10,
            Width = 860,
            MaxWidth = 860,
        };
        body.Children.Add(new TextBlock { Text = t("couch.text_input"), FontSize = 26 });
        body.Children.Add(_editor);
        var characters = new UniformGrid { Columns = 10, Rows = 5 };
        const string keys = "1234567890qwertyuiopasdfghjkl;zxcvbnm,./@_-+=!?():";
        ActionButton? first = null;
        foreach (var character in keys)
        {
            var key = MakeButton(character.ToString(), () => Insert((_upper ? char.ToUpperInvariant(character) : character).ToString()));
            first ??= key;
            characters.Children.Add(key);
            _letters.Add((key, character));
        }
        _firstKey = first!;
        body.Children.Add(characters);
        var editing = new UniformGrid { Columns = 6 };
        editing.Children.Add(MakeButton(t("couch.case"), ToggleCase));
        editing.Children.Add(MakeButton(t("couch.space"), () => Insert(" ")));
        editing.Children.Add(MakeButton(t("couch.backspace"), () => CouchNativeInput.SendKey(_editor, Key.Back)));
        editing.Children.Add(MakeButton(t("couch.cursor_left"), () => CouchNativeInput.SendKey(_editor, Key.Left)));
        editing.Children.Add(MakeButton(t("couch.cursor_right"), () => CouchNativeInput.SendKey(_editor, Key.Right)));
        editing.Children.Add(
            MakeButton(
                t(target.AcceptsReturn ? "couch.newline" : "couch.clear"),
                () =>
                {
                    if (target.AcceptsReturn)
                        Insert("\n");
                    else
                    {
                        _editor.SelectAll();
                        CouchNativeInput.SendKey(_editor, Key.Back);
                    }
                }
            )
        );
        body.Children.Add(editing);
        var actions = new UniformGrid { Columns = 2 };
        actions.Children.Add(MakeButton(t("action.cancel"), Cancel));
        actions.Children.Add(MakeButton(t("couch.submit"), Submit));
        body.Children.Add(actions);
        body.Children.Add(new TextBlock { Text = t("couch.keyboard_hints"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static ActionButton MakeButton(string text, Action action)
    {
        var button = new ActionButton
        {
            Text = text,
            MinHeight = 48,
            Margin = new Thickness(3),
            FontSize = 20,
        };
        button.Click += (_, _) => action();
        return button;
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();
        Dispatcher.UIThread.Post(() => _firstKey.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }

    private void Insert(string text)
    {
        // The TextBox performs selection replacement, max-length enforcement and undo.
        // No clipboard is used, and password contents are never echoed into labels/logs.
        _editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
    }

    private void OnPhysicalTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source == _editor)
            return;
        Insert(e.Text ?? "");
        e.Handled = true;
    }

    private void OnPhysicalEditingKey(object? sender, KeyEventArgs e)
    {
        if (e.Source == _editor || e.KeyDeviceType == KeyDeviceType.Gamepad)
            return;
        if (e.Key is Key.Back or Key.Delete || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            _editor.RaiseEvent(
                new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = e.Key,
                    KeyModifiers = e.KeyModifiers,
                    KeyDeviceType = e.KeyDeviceType,
                }
            );
            e.Handled = true;
        }
    }

    private void ToggleCase()
    {
        _upper = !_upper;
        foreach (var (button, character) in _letters)
            button.Text = (_upper ? char.ToUpperInvariant(character) : character).ToString();
    }

    private void Cancel()
    {
        CloseImmediately();
        _target.Focus(NavigationMethod.Directional);
    }

    private void Submit()
    {
        // The modal intentionally disables its source layer while editing.
        if (!_target.IsReadOnly && _target.IsEnabled && TopLevel.GetTopLevel(_target) != null)
        {
            _target.SetCurrentValue(TextBox.TextProperty, _editor.Text);
            _target.CaretIndex = _editor.CaretIndex;
            _target.SelectionStart = _editor.SelectionStart;
            _target.SelectionEnd = _editor.SelectionEnd;
            CloseImmediately();
            _target.Focus(NavigationMethod.Directional);
            if (!_target.AcceptsReturn)
                _submit();
        }
        else
            CloseImmediately();
    }
}

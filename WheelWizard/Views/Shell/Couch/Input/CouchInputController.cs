using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Shell.Couch.Input;

/// <summary>Window-local semantic gamepad input. Never activates a window or synthesizes pointer input.</summary>
public sealed class CouchInputController : IDisposable
{
    private const CouchButtons Directions = CouchButtons.Up | CouchButtons.Down | CouchButtons.Left | CouchButtons.Right;
    private readonly Layout _window;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<Key> _pressedKeys = [];
    private SdlGamepadBackend? _backend;
    private bool _backendAttempted;
    private string? _backendError;
    private bool _backendErrorShown;
    private bool _accepting;
    private bool _armed;
    private long _neutralSince;
    private CouchButtons _previous;
    private CouchButtons _repeatDirection;
    private long _nextRepeat;
    private CouchButtons _lastControllerButtons;
    private long _lastControllerTime;
    private Control? _lastFocus;
    private Control? _lastScope;
    private ComboBox? _openCombo;
    private bool _dispatching;
    private bool _disposed;
    private int _deviceGeneration;

    public CouchInputController(Layout window)
    {
        _window = window;
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        _window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private bool CanAccept =>
        _window.IsCouchMode
        && _window.IsActive
        && _window.IsVisible
        && _window.Modals.IsEnabled
        && (_window.CompleteContentEnabled || _window.Modals.HasModals);
    private Control Scope => _window.Modals.TopModal ?? (Control)_window;
    private Control? Focused => _window.FocusManager?.GetFocusedElement() as Control;

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_window.IsCouchMode)
        {
            _backend?.Dispose();
            _backend = null;
            _backendAttempted = false;
            _backendError = null;
            _backendErrorShown = false;
            _accepting = false;
            _pressedKeys.Clear();
            Rearm();
            return;
        }
        if (!_backendAttempted)
        {
            _backendAttempted = true;
            try
            {
                _backend = new SdlGamepadBackend();
            }
            catch (Exception exception)
                when (exception
                        is DllNotFoundException
                            or EntryPointNotFoundException
                            or BadImageFormatException
                            or InvalidOperationException
                )
            {
                _backendError = exception.Message;
                Serilog.Log.Error(exception, "Couch controller input could not initialize SDL3");
            }
        }

        var state = _backend?.Read() ?? CouchButtons.None;
        if (_backend != null && _backend.Generation != _deviceGeneration)
        {
            _deviceGeneration = _backend.Generation;
            Rearm();
        }
        if (!CanAccept)
        {
            if (_accepting)
            {
                _pressedKeys.Clear();
                Rearm();
            }
            _accepting = false;
            return;
        }
        if (!_accepting)
        {
            _accepting = true;
            Rearm();
            EnsureFocus();
        }
        if (_backendError != null && !_backendErrorShown)
        {
            _backendErrorShown = true;
            ShowMenu();
        }
        if (_lastScope != Scope)
        {
            _lastScope = Scope;
            Rearm();
        }
        EnsureFocus();
        var now = Environment.TickCount64;
        if (!_armed)
        {
            if (state != CouchButtons.None)
                _neutralSince = 0;
            else if (_neutralSince == 0)
                _neutralSince = now;
            else if (now - _neutralSince >= 120)
                _armed = true;
            _previous = state;
            return;
        }

        var pressed = state & ~_previous;
        _previous = state;
        if (state != CouchButtons.None)
        {
            _lastControllerButtons = state;
            _lastControllerTime = now;
        }
        // Action priority avoids activating an action and changing page in the same frame.
        if ((pressed & CouchButtons.Menu) != 0)
            ShowMenu();
        else if ((pressed & CouchButtons.Back) != 0)
            GoBack();
        else if ((pressed & CouchButtons.TextInput) != 0)
            ShowTextInput();
        else if ((pressed & CouchButtons.PreviousSection) != 0)
            ChangeSection(-1);
        else if ((pressed & CouchButtons.NextSection) != 0)
            ChangeSection(1);
        else if ((pressed & CouchButtons.Confirm) != 0)
            Confirm();
        else
        {
            var direction = FirstDirection(state & Directions);
            if (direction == CouchButtons.None)
                _repeatDirection = CouchButtons.None;
            else if (direction != _repeatDirection)
            {
                _repeatDirection = direction;
                _nextRepeat = now + 380;
                Move(DirectionKey(direction), controller: true);
            }
            else if (now >= _nextRepeat)
            {
                _nextRepeat = now + 105;
                Move(DirectionKey(direction), controller: true);
            }
        }
    }

    private void Rearm()
    {
        _armed = false;
        _neutralSince = 0;
        _previous = CouchButtons.None;
        _repeatDirection = CouchButtons.None;
        _lastControllerButtons = CouchButtons.None;
    }

    private static CouchButtons FirstDirection(CouchButtons buttons)
    {
        if ((buttons & CouchButtons.Up) != 0)
            return CouchButtons.Up;
        if ((buttons & CouchButtons.Down) != 0)
            return CouchButtons.Down;
        if ((buttons & CouchButtons.Left) != 0)
            return CouchButtons.Left;
        if ((buttons & CouchButtons.Right) != 0)
            return CouchButtons.Right;
        return CouchButtons.None;
    }

    private static Key DirectionKey(CouchButtons direction) =>
        direction switch
        {
            CouchButtons.Up => Key.Up,
            CouchButtons.Down => Key.Down,
            CouchButtons.Left => Key.Left,
            _ => Key.Right,
        };

    private static CouchButtons KeyboardAction(Key key) =>
        key switch
        {
            Key.Up => CouchButtons.Up,
            Key.Down => CouchButtons.Down,
            Key.Left => CouchButtons.Left,
            Key.Right => CouchButtons.Right,
            Key.Enter or Key.Space => CouchButtons.Confirm,
            Key.Escape => CouchButtons.Back,
            Key.PageUp => CouchButtons.PreviousSection,
            Key.PageDown => CouchButtons.NextSection,
            Key.F10 => CouchButtons.Menu,
            Key.F2 => CouchButtons.TextInput,
            _ => CouchButtons.None,
        };

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (_dispatching || !CanAccept)
            return;
        var action = KeyboardAction(e.Key);
        // Steam Input can deliver both an SDL button and a mapped keyboard key. Sample
        // before routing even when the keyboard event arrived before our timer tick.
        var state = _backend?.Read(consumePresses: false) ?? CouchButtons.None;
        if (
            action != CouchButtons.None
            && ((state & action) != 0 || Environment.TickCount64 - _lastControllerTime < 180 && (_lastControllerButtons & action) != 0)
        )
        {
            e.Handled = true;
            return;
        }
        var first = _pressedKeys.Add(e.Key);
        if (!first && (e.Key is Key.Enter or Key.Space) && Focused is not TextBox)
            e.Handled = true;
        if (e.Key == Key.F10 && first)
        {
            ShowMenu();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && first)
        {
            ShowTextInput();
            e.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_dispatching)
            return;
        _pressedKeys.Remove(e.Key);
        if (
            CanAccept
            && KeyboardAction(e.Key) is var action
            && action != CouchButtons.None
            && Environment.TickCount64 - _lastControllerTime < 180
            && (_lastControllerButtons & action) != 0
        )
            e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dispatching || !CanAccept || e.Handled || e.KeyModifiers != KeyModifiers.None)
            return;
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            Move(e.Key, controller: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            GoBack();
            e.Handled = true;
        }
        else if ((e.Key is Key.PageUp or Key.PageDown) && Focused is not TextBox)
        {
            ChangeSection(e.Key == Key.PageUp ? -1 : 1);
            e.Handled = true;
        }
    }

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!CanAccept || e.Source is not Control target)
            return;
        if (!IsWithin(target, Scope) && _openCombo?.IsDropDownOpen != true)
        {
            Dispatcher.UIThread.Post(EnsureFocus, DispatcherPriority.Input);
            return;
        }
        _lastFocus = target;
        if (target is ComboBox combo)
            _openCombo = combo;
        target.BringIntoView();
    }

    private static bool IsWithin(Visual target, Visual root)
    {
        for (Visual? current = target; current != null; current = current.GetVisualParent())
            if (current == root)
                return true;
        return false;
    }

    private static bool IsCandidate(Control target) =>
        target.Focusable
        && target.IsTabStop
        && target.IsEffectivelyVisible
        && target.IsEffectivelyEnabled
        && target.Bounds.Width > 0
        && target.Bounds.Height > 0
        && target is not TextBlock;

    private void EnsureFocus()
    {
        if (!CanAccept)
            return;
        var scope = Scope;
        if (_openCombo?.IsDropDownOpen == true)
            return;
        var focused = Focused;
        if (focused != null && IsCandidate(focused) && IsWithin(focused, scope))
            return;
        if (_lastFocus != null && IsCandidate(_lastFocus) && IsWithin(_lastFocus, scope))
        {
            _lastFocus.Focus(NavigationMethod.Directional);
            return;
        }
        foreach (var visual in scope.GetVisualDescendants())
        {
            if (visual is not Control candidate || !IsCandidate(candidate))
                continue;
            candidate.Focus(NavigationMethod.Directional);
            candidate.BringIntoView();
            return;
        }
    }

    private void Move(Key key, bool controller)
    {
        EnsureFocus();
        var current = Focused;
        if (current == null)
            return;
        // A controller enters text with Confirm/Y; arrows leave ordinary text fields.
        // Native editor arrows remain native on the OSK and on a physical keyboard.
        if (controller && (current is not TextBox || Scope is CouchKeyboard))
        {
            var before = Focused;
            if (SendKey(current, key) || Focused != before)
            {
                Focused?.BringIntoView();
                if (controller && Focused is TextBox focusedText && Scope is not CouchKeyboard)
                    ShowTextInput(focusedText);
                return;
            }
        }
        if (current.TranslatePoint(default, _window) is not { } origin)
            return;
        var center = origin + new Vector(current.Bounds.Width / 2, current.Bounds.Height / 2);
        Control? best = null;
        var bestScore = double.PositiveInfinity;
        foreach (var visual in Scope.GetVisualDescendants())
        {
            if (
                visual is not Control candidate
                || candidate == current
                || !IsCandidate(candidate)
                || IsWithin(candidate, current)
                || candidate.TranslatePoint(default, _window) is not { } point
            )
                continue;
            var dx = point.X + candidate.Bounds.Width / 2 - center.X;
            var dy = point.Y + candidate.Bounds.Height / 2 - center.Y;
            var forward = key switch
            {
                Key.Left => -dx,
                Key.Right => dx,
                Key.Up => -dy,
                _ => dy,
            };
            if (forward <= 1)
                continue;
            var sideways = key is Key.Left or Key.Right ? Math.Abs(dy) : Math.Abs(dx);
            var score = forward + sideways * 3 + sideways * sideways / forward;
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        if (best == null)
            return;
        _dispatching = controller;
        try
        {
            best.Focus(NavigationMethod.Directional);
        }
        finally
        {
            _dispatching = false;
        }
        best.BringIntoView();
        if (controller && best is TextBox nextText && Scope is not CouchKeyboard)
            ShowTextInput(nextText);
    }

    private void Confirm()
    {
        EnsureFocus();
        if (Focused is TextBox text && Scope is not CouchKeyboard)
        {
            ShowTextInput(text);
            return;
        }
        if (Focused is ComboBox combo)
            _openCombo = combo;
        if (Focused is { } target)
            SendKey(target, target is Button ? Key.Space : Key.Enter);
    }

    private void GoBack()
    {
        if (_openCombo?.IsDropDownOpen == true)
        {
            _openCombo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            _openCombo.Focus(NavigationMethod.Directional);
            return;
        }
        if (_window.Modals.TopModal is { } modal)
            modal.Dismiss();
        else
            _window.GoCouchBack();
    }

    private void ChangeSection(int offset)
    {
        if (!_window.Modals.HasModals)
        {
            _window.NavigateCouchSection(offset);
            Dispatcher.UIThread.Post(EnsureFocus, DispatcherPriority.Loaded);
        }
    }

    private void ShowTextInput(TextBox? target = null)
    {
        if (!CanAccept || Scope is CouchKeyboard)
            return;
        target ??= Focused as TextBox;
        if (target is not { IsReadOnly: false, IsEffectivelyEnabled: true } || !IsWithin(target, Scope))
            return;
        var keyboard = new CouchKeyboard(target, () => SendKey(target, Key.Enter));
        keyboard.SetModalOwner(target).Show();
    }

    private void ShowMenu()
    {
        if (!CanAccept)
            return;
        if (_window.Modals.TopModal is CouchMenu existing)
        {
            existing.Close();
            return;
        }
        var textTarget = Focused as TextBox;
        var canNavigate = !_window.Modals.HasModals;
        var status =
            _backendError == null
                ? t("couch.controller_status", new { devices = _backend?.DeviceCount ?? 0 })
                : t("couch.controller_unavailable", new { error = _backendError });
        var menu = new CouchMenu(
            status,
            canNavigate,
            textTarget is { IsReadOnly: false },
            _window.ShowSettings,
            _window.ToggleCouchFullscreen,
            _window.Close,
            () => ShowTextInput(textTarget)
        );
        menu.SetModalOwner(_window.Modals.TopModal ?? (Control)_window).Show();
    }

    private bool SendKey(Control target, Key key)
    {
        _dispatching = true;
        try
        {
            return CouchNativeInput.SendKey(target, key);
        }
        finally
        {
            _dispatching = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDownTunnel);
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        _window.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        _backend?.Dispose();
        _backend = null;
    }
}

internal static class CouchNativeInput
{
    public static bool SendKey(Control target, Key key)
    {
        var down = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyDeviceType = KeyDeviceType.Gamepad,
        };
        target.RaiseEvent(down);
        target.RaiseEvent(
            new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = key,
                KeyDeviceType = KeyDeviceType.Gamepad,
            }
        );
        return down.Handled;
    }
}

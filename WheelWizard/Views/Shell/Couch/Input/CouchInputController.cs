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
    private readonly CouchButtonTracker _buttons = new();
    private CouchButtons _repeatDirection;
    private long _nextRepeat;
    private CouchButtons _lastControllerButtons;
    private long _lastControllerTime;
    private Control? _lastFocus;
    private Control? _lastScope;
    private Avalonia.Controls.Primitives.Popup? OpenPopup =>
        PopupNavigation.GetTopPopup(_window, _window.Modals.TopModal ?? (Control)_window);
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
    private Control Scope => OpenPopup?.Child ?? _window.Modals.TopModal ?? (Control)_window;
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
            ResetButtons(default);
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

        var state = _backend?.Read() ?? default;
        var devicesChanged = _backend != null && _backend.Generation != _deviceGeneration;
        if (_backend != null)
            _deviceGeneration = _backend.Generation;
        if (!CanAccept)
        {
            _pressedKeys.Clear();
            _accepting = false;
            ResetButtons(state.Held);
            return;
        }
        if (!_accepting || devicesChanged)
        {
            _accepting = true;
            ResetButtons(state.Held);
            _lastScope = Scope;
            EnsureFocus();
            return;
        }
        if (_backendError != null && !_backendErrorShown)
        {
            _backendErrorShown = true;
            ShowMenu();
        }
        var scopeChanged = _lastScope != Scope;
        _lastScope = Scope;
        if (scopeChanged)
            _repeatDirection = CouchButtons.None;
        EnsureFocus();
        var now = Environment.TickCount64;
        var input = _buttons.Update(state, scopeChanged);
        var pressed = input.Pressed;
        if (state.Buttons != CouchButtons.None)
        {
            _lastControllerButtons = state.Buttons;
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
            var direction = FirstDirection((input.Held | pressed) & Directions);
            if (direction == CouchButtons.None)
                _repeatDirection = CouchButtons.None;
            else if (direction != _repeatDirection || (pressed & direction) != 0)
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

    private void ResetButtons(CouchButtons held)
    {
        _buttons.Reset(held);
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
        var state = _backend?.Read(consumePresses: false).Buttons ?? CouchButtons.None;
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
        if (!IsWithin(target, Scope))
        {
            Dispatcher.UIThread.Post(EnsureFocus, DispatcherPriority.Input);
            return;
        }
        _lastFocus = target;
        target.BringIntoView();
    }

    private static bool IsWithin(Visual target, Visual root)
    {
        for (Visual? current = target; current != null; current = current.GetVisualParent())
            if (current == root)
                return true;
        return false;
    }

    internal static bool IsCandidate(Control target)
    {
        if (
            !target.Focusable
            || !target.IsTabStop
            || !target.IsEffectivelyVisible
            || !target.IsEffectivelyEnabled
            || !target.IsHitTestVisible
            || target.Bounds.Width <= 0
            || target.Bounds.Height <= 0
            || target is TextBlock or TopLevel or Panel or ICouchNavigationPage
        )
            return false;
        for (Visual? current = target; current != null; current = current.GetVisualParent())
            if (current.Opacity <= 0)
                return false;
        return true;
    }

    private void EnsureFocus()
    {
        if (!CanAccept)
            return;
        var scope = Scope;
        var focused = Focused;
        if (focused != null && IsCandidate(focused) && IsWithin(focused, scope))
            return;
        if (_lastFocus != null && IsCandidate(_lastFocus) && IsWithin(_lastFocus, scope))
        {
            _lastFocus.Focus(NavigationMethod.Directional);
            return;
        }
        if (scope == _window && _window.CouchPageFocusTarget is { } entry && IsCandidate(entry))
        {
            entry.Focus(NavigationMethod.Directional);
            entry.BringIntoView();
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
        // Only controls with an adjustment/selection model own directional input.
        // Buttons use an explicitly scoped search, not a synthetic global arrow event.
        var sliderAdjusts =
            current is Slider slider
            && (slider.Orientation == Avalonia.Layout.Orientation.Horizontal ? key is Key.Left or Key.Right : key is Key.Up or Key.Down);
        if (
            controller
            && (OpenPopup != null || sliderAdjusts || (current is TextBox && Scope is CouchKeyboard && key is Key.Left or Key.Right))
        )
        {
            var before = Focused;
            if (SendKey(current, key) || Focused != before)
            {
                Focused?.BringIntoView();
                return;
            }
        }
        var direction = key switch
        {
            Key.Left => NavigationDirection.Left,
            Key.Right => NavigationDirection.Right,
            Key.Up => NavigationDirection.Up,
            _ => NavigationDirection.Down,
        };
        MoveFocus(Scope, current, direction);
    }

    internal static bool MoveFocus(Control scope, Control current, NavigationDirection direction)
    {
        var next =
            TopLevel
                .GetTopLevel(scope)
                ?.FocusManager?.FindNextElement(
                    direction,
                    new FindNextElementOptions
                    {
                        FocusedElement = current,
                        SearchRoot = scope,
                        // A clipped scroll child is still reachable; revealing it is part of focus.
                        // Modal and popup boundaries are enforced by SearchRoot, not occlusion.
                        IgnoreOcclusivity = true,
                    }
                ) as Control;
        if (next == null || !IsCandidate(next) || !next.Focus(NavigationMethod.Directional))
            return false;
        next.BringIntoView();
        return true;
    }

    private void Confirm()
    {
        EnsureFocus();
        if (Focused is TextBox text && Scope is not CouchKeyboard)
        {
            ShowTextInput(text);
            return;
        }
        if (Focused is { } target)
            SendKey(target, target is Button ? Key.Space : Key.Enter);
    }

    private void GoBack() => _window.GoCouchBack();

    private void ChangeSection(int offset)
    {
        if (OpenPopup != null)
            return;
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
        PopupNavigation.TryCloseTopPopup(_window, _window.Modals.TopModal ?? (Control)_window);
        var textTarget = Focused as TextBox;
        var canNavigate = !_window.Modals.HasModals;
        var error = _backendError == null ? null : t("couch.controller_unavailable", new { error = _backendError });
        var menu = new CouchMenu(
            error,
            canNavigate,
            textTarget is { IsReadOnly: false },
            _window.ShowSettings,
            _window.ToggleCouchFullscreen,
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
        // The router owns slider-axis navigation. Use Slider's native editing path,
        // not its separate gamepad focus-engagement mode (which consumes Confirm/Back).
        var device = target is Slider ? KeyDeviceType.Keyboard : KeyDeviceType.Gamepad;
        var down = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyDeviceType = device,
        };
        target.RaiseEvent(down);
        target.RaiseEvent(
            new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = key,
                KeyDeviceType = device,
            }
        );
        return down.Handled;
    }
}

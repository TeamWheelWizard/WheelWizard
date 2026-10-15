using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WheelWizard.Views.Shell;

namespace WheelWizard.Views.Dialogs.Base;

/// <summary>A window-local modal stack. It never creates an OS window.</summary>
public sealed class ModalHost : Grid
{
    private readonly BaseWindow _owner;
    private readonly List<Entry> _entries = [];
    public ModalContent? TopModal => _entries.LastOrDefault()?.Content;
    public bool HasModals => _entries.Count != 0;
    internal double BackdropOpacity => 1 - _entries.Aggregate(1d, (remaining, entry) => remaining * (1 - 120d / 255 * entry.Layer.Opacity));

    public ModalHost(BaseWindow owner)
    {
        _owner = owner;
        IsVisible = false;
        ClipToBounds = true;
        ZIndex = 1000;
        Margin = new Thickness(0, Math.Max(0, _owner.ExtendClientAreaTitleBarHeightHint), 0, 0);
        _owner.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public void Show(ModalContent content)
    {
        var previousFocus = _owner.FocusManager?.GetFocusedElement();
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) };
        backdrop.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(backdrop).Properties.IsLeftButtonPressed && TopModal == content)
            {
                content.Dismiss();
                e.Handled = true;
            }
        };
        var body = new Border { Padding = (Thickness)_owner.FindResource("EdgeGap")!, Child = content };
        var panel = new Border
        {
            Name = "ModalPanel",
            Child = body,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = _owner.FindResource("BackgroundColor") as IBrush,
            BorderBrush = _owner.FindResource("BackgroundLineColor") as IBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Focusable = true,
        };
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        var layer = new Grid();
        layer.Children.Add(backdrop);
        layer.Children.Add(panel);
        var entry = new Entry(content, layer, panel, previousFocus);
        var duration = GetAnimationDuration();
        if (duration > TimeSpan.Zero)
        {
            layer.Opacity = 0;
            panel.RenderTransform = new TranslateTransform(0, 6);
        }
        _entries.Add(entry);
        layer.PropertyChanged += OnLayerOpacityChanged;
        Children.Add(layer);
        UpdateStack();
        FocusTopModal();
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!entry.IsClosing && _entries.Contains(entry))
                {
                    ConfigureTransitions(entry, duration);
                    layer.Opacity = 1;
                    if (panel.RenderTransform is TranslateTransform offset)
                        offset.Y = 0;
                    if (TopModal == content)
                        FocusTopModal();
                }
            },
            DispatcherPriority.Loaded
        );
    }

    private TimeSpan GetAnimationDuration() =>
        _owner.FindResource(WindowAppearance.ControlAnimationDurationResourceKey) is TimeSpan duration
            ? duration
            : TimeSpan.FromMilliseconds(120);

    private static void ConfigureTransitions(Entry entry, TimeSpan duration)
    {
        entry.Layer.Transitions ??= new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = duration,
                Easing = new CubicEaseOut(),
            },
        };
        if (entry.Panel.RenderTransform is not TranslateTransform offset)
            entry.Panel.RenderTransform = offset = new TranslateTransform();
        offset.Transitions ??= new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = duration,
                Easing = new CubicEaseOut(),
            },
        };
    }

    public void Close(ModalContent content, Action closed, bool animate = true)
    {
        var entry = _entries.FirstOrDefault(entry => entry.Content == content);
        if (entry == null)
            return;
        if (!entry.IsClosing)
        {
            entry.IsClosing = true;
            entry.OnClosed = closed;
        }
        else if (animate)
            return;
        var duration = animate ? GetAnimationDuration() : TimeSpan.Zero;
        if (duration <= TimeSpan.Zero)
        {
            RemoveEntry(entry);
            return;
        }
        // Keep the backdrop and interaction gate until the exit has finished.
        entry.Layer.IsEnabled = false;
        ConfigureTransitions(entry, duration);
        entry.Layer.Opacity = 0;
        ((TranslateTransform)entry.Panel.RenderTransform!).Y = 4;
        _ = FinishCloseAsync(entry, duration);
    }

    private async Task FinishCloseAsync(Entry entry, TimeSpan duration)
    {
        await Task.Delay(duration);
        await Dispatcher.UIThread.InvokeAsync(() => RemoveEntry(entry));
    }

    private void RemoveEntry(Entry entry)
    {
        var index = _entries.IndexOf(entry);
        if (index < 0)
            return;
        var wasTop = index == _entries.Count - 1;
        _entries.RemoveAt(index);
        entry.Layer.PropertyChanged -= OnLayerOpacityChanged;
        foreach (var remaining in _entries)
        {
            if (remaining.PreviousFocus is Visual focused && focused.GetVisualAncestors().Contains(entry.Panel))
                remaining.PreviousFocus = entry.PreviousFocus;
        }
        Children.Remove(entry.Layer);
        // Detach the content so a modal can be shown again without a logical parent conflict.
        ((Border)entry.Panel.Child!).Child = null;
        UpdateStack();
        if (wasTop)
        {
            if (entry.PreviousFocus is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous)
                previous.Focus();
            else if (HasModals)
                FocusTopModal();
        }
        entry.OnClosed?.Invoke();
    }

    public void CloseAll()
    {
        foreach (var entry in _entries.ToArray().Reverse())
            entry.Content.CloseImmediately();
    }

    public void FocusTopModal()
    {
        var entry = _entries.LastOrDefault();
        var panel = entry?.Panel;
        if (panel == null || entry!.IsClosing || !IsEffectivelyEnabled)
            return;
        // Prefer inputs/actions over selectable labels; fall back to the panel itself.
        var candidates = entry!
            .Content.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control is not TextBlock)
            .ToList();
        var target = candidates.OfType<TextBox>().FirstOrDefault() ?? candidates.FirstOrDefault();
        (target ?? panel).Focus();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Overlay content must not change the size of its application window.
        var size = new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : _owner.ClientSize.Width,
            double.IsFinite(availableSize.Height) ? availableSize.Height : _owner.ClientSize.Height
        );
        foreach (var entry in _entries)
        {
            entry.Panel.MaxWidth = Math.Max(0, size.Width - 48);
            entry.Panel.MaxHeight = Math.Max(0, size.Height - 48);
        }
        base.MeasureOverride(size);
        return default;
    }

    private void UpdateStack()
    {
        for (var i = 0; i < _entries.Count; i++)
            _entries[i].Layer.IsEnabled = i == _entries.Count - 1 && !_entries[i].IsClosing;
        IsVisible = HasModals;
        _owner.UpdateModalInteraction();
    }

    private void OnLayerOpacityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == OpacityProperty)
            _owner.UpdateModalChromeDimming();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (TopModal == null || _entries[^1].IsClosing || !IsEffectivelyEnabled)
            return;
        if (e.Key == Key.Escape)
        {
            TopModal.Dismiss();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && _owner.FocusManager?.GetFocusedElement() == null)
        {
            FocusTopModal();
            e.Handled = true;
        }
    }

    private sealed record Entry(ModalContent Content, Grid Layer, Border Panel, IInputElement? InitialFocus)
    {
        public IInputElement? PreviousFocus { get; set; } = InitialFocus;
        public bool IsClosing { get; set; }
        public Action? OnClosed { get; set; }
    }
}

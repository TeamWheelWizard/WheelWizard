using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WheelWizard.Views.Behaviors;

public sealed class HorizontalPanBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty = AvaloniaProperty.RegisterAttached<
        HorizontalPanBehavior,
        ScrollViewer,
        bool
    >("IsEnabled");
    private static readonly ConditionalWeakTable<ScrollViewer, PanState> States = new();

    static HorizontalPanBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(
            (viewer, args) =>
            {
                if (args.GetNewValue<bool>())
                    States.GetValue(viewer, view => new PanState(view));
                else if (States.TryGetValue(viewer, out var state))
                {
                    state.Detach();
                    States.Remove(viewer);
                }
            }
        );
    }

    public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(IsEnabledProperty, value);

    private sealed class PanState
    {
        private readonly ScrollViewer _viewer;
        private IPointer? _pointer;
        private Point _start;
        private Vector _offset;
        private bool _dragging;

        public PanState(ScrollViewer viewer)
        {
            _viewer = viewer;
            viewer.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
            viewer.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
            viewer.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
            viewer.AddHandler(InputElement.PointerWheelChangedEvent, WheelChanged, RoutingStrategies.Tunnel);
            viewer.PointerCaptureLost += CaptureLost;
            viewer.DetachedFromVisualTree += Detached;
        }

        private void Pressed(object? sender, PointerPressedEventArgs e)
        {
            Reset();
            if (
                _viewer.FindAncestorOfType<TextBox>() != null
                || e.Pointer.Type != PointerType.Mouse
                || !e.GetCurrentPoint(_viewer).Properties.IsLeftButtonPressed
                || _viewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
                || _viewer.Extent.Width <= _viewer.Viewport.Width
            )
                return;
            // Leave text selection, sliders, scrollbars, and nested scrollers to their own controls.
            for (var visual = e.Source as Visual; visual != null; visual = visual.GetVisualParent())
            {
                if (visual is TextBox or SelectableTextBlock or RangeBase || visual is ScrollViewer && visual != _viewer)
                    return;
                if (visual == _viewer)
                    break;
            }
            _pointer = e.Pointer;
            _start = e.GetPosition(_viewer);
            _offset = _viewer.Offset;
        }

        private void WheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (
                e.Handled
                || e.Delta.X != 0
                || e.KeyModifiers != KeyModifiers.None
                || _viewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
                || _viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                || _viewer.Extent.Width <= _viewer.Viewport.Width
            )
                return;
            for (var visual = e.Source as Visual; visual != null && visual != _viewer; visual = visual.GetVisualParent())
                if (visual is ScrollViewer or TextBox or RangeBase)
                    return;

            _viewer.Offset = new Vector(
                Math.Clamp(_viewer.Offset.X - e.Delta.Y * 50, 0, _viewer.Extent.Width - _viewer.Viewport.Width),
                _viewer.Offset.Y
            );
            e.Handled = true;
        }

        private void Moved(object? sender, PointerEventArgs e)
        {
            if (_pointer != e.Pointer)
                return;
            if (!e.GetCurrentPoint(_viewer).Properties.IsLeftButtonPressed)
            {
                Reset();
                return;
            }
            var delta = e.GetPosition(_viewer) - _start;
            if (!_dragging)
            {
                if (Math.Abs(delta.X) < 5 || Math.Abs(delta.X) < Math.Abs(delta.Y))
                    return;
                _dragging = true;
                _pointer.Capture(_viewer);
            }
            _viewer.Offset = new Vector(
                Math.Clamp(_offset.X - delta.X, 0, Math.Max(0, _viewer.Extent.Width - _viewer.Viewport.Width)),
                _viewer.Offset.Y
            );
            e.Handled = true;
        }

        private void Released(object? sender, PointerReleasedEventArgs e)
        {
            if (_pointer != e.Pointer)
                return;
            e.Handled = _dragging;
            Reset();
        }

        private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_dragging && _pointer?.Captured != _viewer)
                Reset();
        }

        private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => Reset();

        private void Reset()
        {
            var pointer = _pointer;
            _pointer = null;
            _dragging = false;
            if (pointer?.Captured == _viewer)
                pointer.Capture(null);
        }

        public void Detach()
        {
            Reset();
            _viewer.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
            _viewer.RemoveHandler(InputElement.PointerMovedEvent, Moved);
            _viewer.RemoveHandler(InputElement.PointerReleasedEvent, Released);
            _viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, WheelChanged);
            _viewer.PointerCaptureLost -= CaptureLost;
            _viewer.DetachedFromVisualTree -= Detached;
        }
    }
}

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace WheelWizard.Views.Behaviors;

public static class ToolTipBubbleBehavior
{
    private const string BubblePointerLeftClass = "BubblePointerLeft";
    private const string BubblePointerMiddleClass = "BubblePointerMiddle";
    private const string BubblePointerRightClass = "BubblePointerRight";
    private const string BubbleAnimateInClass = "BubbleAnimateIn";
    private const string BubbleAnimateOutClass = "BubbleAnimateOut";
    private const double TailCenterOffsetFromSide = 22d;
    private const double TooltipVerticalOffset = -4d;
    private static readonly TimeSpan HoverOpenDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MinimumVisibleTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CloseAnimationDuration = TimeSpan.FromMilliseconds(40);
    private static readonly ConditionalWeakTable<Control, ToolTipState> ToolTipStates = new();
    private static bool _isInitialized;

    public static void Initialize()
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        ToolTip.TipProperty.Changed.AddClassHandler<Control>(OnTipChanged);
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>(OnToolTipOpening);
        ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>(OnIsOpenChanged);
        InputElement.IsPointerOverProperty.Changed.AddClassHandler<Control>(OnIsPointerOverChanged);
    }

    private static void OnTipChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        var newTip = args.GetNewValue<object?>();
        if (newTip == null || ReferenceEquals(newTip, AvaloniaProperty.UnsetValue))
        {
            var state = GetState(control);
            CancelPendingOpen(state);
            CancelPendingClose(state);
            ToolTip.SetIsOpen(control, false);
            ToolTip.SetServiceEnabled(control, true);
            return;
        }

        var normalizedPlacement = NormalizePlacement(ToolTip.GetPlacement(control));
        if (ToolTip.GetPlacement(control) != normalizedPlacement)
            ToolTip.SetPlacement(control, normalizedPlacement);

        ToolTip.SetServiceEnabled(control, false);

        if (newTip is ToolTip existingToolTip)
        {
            ApplyPointerClass(existingToolTip, normalizedPlacement);
            return;
        }

        var wrappedToolTip = new ToolTip { Content = newTip };
        ApplyPointerClass(wrappedToolTip, normalizedPlacement);
        ToolTip.SetTip(control, wrappedToolTip);
    }

    private static void OnToolTipOpening(Control control, CancelRoutedEventArgs _) => PrepareToolTip(control);

    private static void OnIsPointerOverChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            OnPointerEntered(control);
            return;
        }

        OnPointerExited(control);
    }

    private static void OnIsOpenChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        var wasOpen = args.GetOldValue<bool>();
        var isOpen = args.GetNewValue<bool>();
        if (wasOpen == isOpen)
            return;

        var state = GetState(control);

        if (isOpen)
        {
            state.OpenedAt = DateTimeOffset.UtcNow;
            CancelPendingOpen(state);
            CancelPendingClose(state);
            return;
        }

        CancelPendingOpen(state);
        CancelPendingClose(state);
        ClearBubbleAnimationClasses(control);
    }

    private static void OnPointerEntered(Control control)
    {
        if (!HasToolTip(control))
            return;

        var state = GetState(control);
        var hadPendingClose = state.PendingCloseCts != null;
        CancelPendingOpen(state);
        CancelPendingClose(state);

        if (ToolTip.GetIsOpen(control))
        {
            if (hadPendingClose && HasBubbleClass(control, BubbleAnimateOutClass))
                ApplyBubbleAnimationClass(control, animateIn: true);
            return;
        }

        var cts = new CancellationTokenSource();
        state.PendingOpenCts = cts;
        _ = DeferredOpenAsync(control, state, HoverOpenDelay, cts.Token);
    }

    private static void OnPointerExited(Control control)
    {
        if (!HasToolTip(control) && !ToolTip.GetIsOpen(control))
            return;

        var state = GetState(control);
        CancelPendingOpen(state);

        var elapsed = DateTimeOffset.UtcNow - state.OpenedAt;
        var remaining = MinimumVisibleTime - elapsed;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        CancelPendingClose(state);
        var cts = new CancellationTokenSource();
        state.PendingCloseCts = cts;
        _ = DeferredCloseAsync(control, state, remaining, cts.Token);
    }

    private static void ApplyPointerClass(ToolTip toolTip, PlacementMode placement)
    {
        toolTip.Classes.Remove(BubblePointerLeftClass);
        toolTip.Classes.Remove(BubblePointerMiddleClass);
        toolTip.Classes.Remove(BubblePointerRightClass);
        toolTip.Classes.Add(GetPointerClass(placement));
    }

    private static void ApplyPointerAnchorOffset(Control control, PlacementMode placement)
    {
        var controlCenterX = control.Bounds.Width / 2d;
        var horizontalOffset = placement switch
        {
            PlacementMode.TopEdgeAlignedLeft => controlCenterX - TailCenterOffsetFromSide,
            PlacementMode.TopEdgeAlignedRight => TailCenterOffsetFromSide - controlCenterX,
            _ => 0d,
        };

        ToolTip.SetHorizontalOffset(control, horizontalOffset);
        ToolTip.SetVerticalOffset(control, TooltipVerticalOffset);
    }

    private static ToolTipState GetState(Control control) => ToolTipStates.GetOrCreateValue(control);

    private static bool HasToolTip(Control control)
    {
        var tip = ToolTip.GetTip(control);
        return tip != null && !ReferenceEquals(tip, AvaloniaProperty.UnsetValue);
    }

    private static void PrepareToolTip(Control control)
    {
        var normalizedPlacement = NormalizePlacement(ToolTip.GetPlacement(control));
        if (ToolTip.GetPlacement(control) != normalizedPlacement)
            ToolTip.SetPlacement(control, normalizedPlacement);

        var toolTip = GetToolTipInstance(control);
        if (toolTip == null)
            return;

        ApplyPointerClass(toolTip, normalizedPlacement);
        ApplyPointerAnchorOffset(control, normalizedPlacement);
    }

    private static ToolTip? GetToolTipInstance(Control control) =>
        control.GetValue(ToolTipDiagnostics.ToolTipProperty) as ToolTip ?? ToolTip.GetTip(control) as ToolTip;

    private static bool HasBubbleClass(Control control, string className)
    {
        var toolTip = GetToolTipInstance(control);
        return toolTip != null && toolTip.Classes.Contains(className);
    }

    private static void ApplyBubbleAnimationClass(Control control, bool animateIn)
    {
        var toolTip = GetToolTipInstance(control);
        if (toolTip == null)
            return;

        toolTip.Classes.Remove(BubbleAnimateInClass);
        toolTip.Classes.Remove(BubbleAnimateOutClass);
        toolTip.Classes.Add(animateIn ? BubbleAnimateInClass : BubbleAnimateOutClass);
    }

    private static void ClearBubbleAnimationClasses(Control control)
    {
        var toolTip = GetToolTipInstance(control);
        if (toolTip == null)
            return;

        toolTip.Classes.Remove(BubbleAnimateInClass);
        toolTip.Classes.Remove(BubbleAnimateOutClass);
    }

    private static void CancelPendingClose(ToolTipState state)
    {
        if (state.PendingCloseCts == null)
            return;

        state.PendingCloseCts.Cancel();
        state.PendingCloseCts.Dispose();
        state.PendingCloseCts = null;
    }

    private static void CancelPendingOpen(ToolTipState state)
    {
        if (state.PendingOpenCts == null)
            return;

        state.PendingOpenCts.Cancel();
        state.PendingOpenCts.Dispose();
        state.PendingOpenCts = null;
    }

    private static async Task DeferredOpenAsync(Control control, ToolTipState state, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            state.PendingOpenCts?.Dispose();
            state.PendingOpenCts = null;

            if (!control.IsPointerOver || ToolTip.GetIsOpen(control) || !HasToolTip(control))
                return;

            PrepareToolTip(control);
            ApplyBubbleAnimationClass(control, animateIn: true);
            ToolTip.SetIsOpen(control, true);
        });
    }

    private static async Task DeferredCloseAsync(Control control, ToolTipState state, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        var shouldAnimateOut = false;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (control.IsPointerOver || !ToolTip.GetIsOpen(control))
            {
                state.PendingCloseCts?.Dispose();
                state.PendingCloseCts = null;
                return;
            }

            shouldAnimateOut = true;
            ApplyBubbleAnimationClass(control, animateIn: false);
        });

        if (!shouldAnimateOut)
            return;

        try
        {
            await Task.Delay(CloseAnimationDuration, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            state.PendingCloseCts?.Dispose();
            state.PendingCloseCts = null;
            if (control.IsPointerOver)
            {
                if (HasBubbleClass(control, BubbleAnimateOutClass))
                    ApplyBubbleAnimationClass(control, animateIn: true);
                return;
            }

            ToolTip.SetIsOpen(control, false);
        });
    }

    private static string GetPointerClass(PlacementMode placement) =>
        placement switch
        {
            PlacementMode.TopEdgeAlignedLeft => BubblePointerLeftClass,
            PlacementMode.TopEdgeAlignedRight => BubblePointerRightClass,
            _ => BubblePointerMiddleClass,
        };

    private static PlacementMode NormalizePlacement(PlacementMode placement) =>
        placement switch
        {
            PlacementMode.Left => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.LeftEdgeAlignedTop => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.LeftEdgeAlignedBottom => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.TopEdgeAlignedLeft => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.BottomEdgeAlignedLeft => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.Right => PlacementMode.TopEdgeAlignedRight,
            PlacementMode.RightEdgeAlignedTop => PlacementMode.TopEdgeAlignedRight,
            PlacementMode.RightEdgeAlignedBottom => PlacementMode.TopEdgeAlignedRight,
            PlacementMode.TopEdgeAlignedRight => PlacementMode.TopEdgeAlignedRight,
            PlacementMode.BottomEdgeAlignedRight => PlacementMode.TopEdgeAlignedRight,
            _ => PlacementMode.Top,
        };

    private sealed class ToolTipState
    {
        public DateTimeOffset OpenedAt { get; set; }
        public CancellationTokenSource? PendingOpenCts { get; set; }
        public CancellationTokenSource? PendingCloseCts { get; set; }
    }
}

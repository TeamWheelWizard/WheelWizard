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
    private const string BubbleSideLeftClass = "BubbleSideLeft";
    private const string BubbleSideRightClass = "BubbleSideRight";
    private const string BubbleAnimateInClass = "BubbleAnimateIn";
    private const string BubbleAnimateOutClass = "BubbleAnimateOut";
    private const double TailCenterOffsetFromSide = 22d;
    private const double TooltipVerticalOffset = -4d;
    private static readonly TimeSpan CloseAnimationDuration = TimeSpan.FromMilliseconds(40);
    private static readonly ConditionalWeakTable<Control, ToolTipState> ToolTipStates = new();
    private static readonly ConditionalWeakTable<Window, HashSet<Control>> OpenToolTips = new();
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
        WindowBase.IsActiveProperty.Changed.AddClassHandler<Window>(OnWindowActiveChanged);
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

        ToolTip.SetServiceEnabled(control, false);

        if (newTip is ToolTip existingToolTip)
        {
            ApplyPointerClass(existingToolTip, normalizedPlacement);
            return;
        }

        // Keep the original ToolTip.Tip binding intact for dynamic values (e.g. live player/room counts).
        // If we set ToolTip.Tip here, we overwrite bindings and the tooltip content gets stuck.
        var generatedToolTip = control.GetValue(ToolTipDiagnostics.ToolTipProperty) as ToolTip;
        if (generatedToolTip != null)
        {
            generatedToolTip.Content = newTip;
            ApplyPointerClass(generatedToolTip, normalizedPlacement);
        }
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

    private static void OnWindowActiveChanged(Window window, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>() || !OpenToolTips.TryGetValue(window, out var controls))
            return;

        // Native tooltip popups must disappear immediately when their owner loses focus.
        foreach (var control in controls.ToArray())
        {
            var state = GetState(control);
            CancelPendingOpen(state);
            CancelPendingClose(state);
            ToolTip.SetIsOpen(control, false);
        }
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
            state.OwnerWindow = TopLevel.GetTopLevel(control) as Window;
            if (state.OwnerWindow != null)
                OpenToolTips.GetOrCreateValue(state.OwnerWindow).Add(control);
            CancelPendingOpen(state);
            CancelPendingClose(state);
            return;
        }

        CancelPendingOpen(state);
        CancelPendingClose(state);
        if (state.OwnerWindow != null && OpenToolTips.TryGetValue(state.OwnerWindow, out var controls))
            controls.Remove(control);
        state.OwnerWindow = null;
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
        _ = OpenAsync(control, state, cts.Token);
    }

    private static void OnPointerExited(Control control)
    {
        if (!HasToolTip(control) && !ToolTip.GetIsOpen(control))
            return;

        var state = GetState(control);
        CancelPendingOpen(state);

        CancelPendingClose(state);
        var cts = new CancellationTokenSource();
        state.PendingCloseCts = cts;
        _ = CloseAsync(control, state, cts.Token);
    }

    private static void ApplyPointerClass(ToolTip toolTip, PlacementMode placement)
    {
        toolTip.Classes.Remove(BubblePointerLeftClass);
        toolTip.Classes.Remove(BubblePointerMiddleClass);
        toolTip.Classes.Remove(BubblePointerRightClass);
        toolTip.Classes.Remove(BubbleSideLeftClass);
        toolTip.Classes.Remove(BubbleSideRightClass);
        toolTip.Classes.Add(GetPointerClass(placement));
    }

    private static void ApplyPointerAnchorOffset(Control control, PlacementMode placement)
    {
        var controlCenterX = control.Bounds.Width / 2d;
        var horizontalOffset = placement switch
        {
            PlacementMode.TopEdgeAlignedLeft => controlCenterX - TailCenterOffsetFromSide,
            PlacementMode.TopEdgeAlignedRight => TailCenterOffsetFromSide - controlCenterX,
            PlacementMode.Left => -4d,
            PlacementMode.Right => 4d,
            _ => 0d,
        };

        ToolTip.SetHorizontalOffset(control, horizontalOffset);
        ToolTip.SetVerticalOffset(control, placement is PlacementMode.Left or PlacementMode.Right ? 0d : TooltipVerticalOffset);
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

    private static async Task OpenAsync(Control control, ToolTipState state, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            state.PendingOpenCts?.Dispose();
            state.PendingOpenCts = null;

            if (TopLevel.GetTopLevel(control) is Window { IsActive: false })
                return;

            if (!control.IsPointerOver || ToolTip.GetIsOpen(control) || !HasToolTip(control))
                return;

            PrepareToolTip(control);
            ToolTip.SetIsOpen(control, true);
            PrepareToolTipAfterOpen(control, cancellationToken);
            ApplyBubbleAnimationClass(control, animateIn: true);
        });
    }

    private static void PrepareToolTipAfterOpen(Control control, CancellationToken cancellationToken)
    {
        // Avalonia creates the generated ToolTip lazily on the first open, so the pre-open
        // PrepareToolTip pass can miss the instance that needs the pointer class.
        PrepareToolTip(control);

        Dispatcher.UIThread.Post(
            () =>
            {
                if (cancellationToken.IsCancellationRequested || !ToolTip.GetIsOpen(control))
                    return;

                PrepareToolTip(control);
            },
            DispatcherPriority.Loaded
        );
    }

    private static async Task CloseAsync(Control control, ToolTipState state, CancellationToken cancellationToken)
    {
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
            PlacementMode.Left => BubbleSideLeftClass,
            PlacementMode.Right => BubbleSideRightClass,
            _ => BubblePointerMiddleClass,
        };

    private static PlacementMode NormalizePlacement(PlacementMode placement) =>
        placement switch
        {
            PlacementMode.Left => PlacementMode.Left,
            PlacementMode.LeftEdgeAlignedTop => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.LeftEdgeAlignedBottom => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.TopEdgeAlignedLeft => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.BottomEdgeAlignedLeft => PlacementMode.TopEdgeAlignedLeft,
            PlacementMode.Right => PlacementMode.Right,
            PlacementMode.RightEdgeAlignedTop => PlacementMode.TopEdgeAlignedRight,
            PlacementMode.RightEdgeAlignedBottom => PlacementMode.TopEdgeAlignedRight,
            PlacementMode.TopEdgeAlignedRight => PlacementMode.TopEdgeAlignedRight,
            PlacementMode.BottomEdgeAlignedRight => PlacementMode.TopEdgeAlignedRight,
            _ => PlacementMode.Top,
        };

    private sealed class ToolTipState
    {
        public Window? OwnerWindow { get; set; }
        public CancellationTokenSource? PendingOpenCts { get; set; }
        public CancellationTokenSource? PendingCloseCts { get; set; }
    }
}

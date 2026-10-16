using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace WheelWizard.Views.Dialogs.Base;

internal static class PopupNavigation
{
    public static Popup? GetTopPopup(TopLevel owner, Control scope)
    {
        for (var i = owner.OpenedPopups.Count - 1; i >= 0; i--)
        {
            var popup = owner.OpenedPopups[i];
            if (!IsInteractive(popup) || (scope != owner && !IsOwnedBy(popup, scope)))
                continue;
            return GetDeepest(popup);
        }
        return null;
    }

    public static bool IsInsideOwnedPopup(TopLevel owner, Control focused, Control scope)
    {
        for (var i = owner.OpenedPopups.Count - 1; i >= 0; i--)
        {
            var popup = owner.OpenedPopups[i];
            if (IsInteractive(popup) && IsOwnedBy(popup, scope) && popup.IsInsidePopup(focused))
                return true;
        }
        return false;
    }

    public static bool TryCloseTopPopup(TopLevel owner, Control scope)
    {
        if (GetTopPopup(owner, scope) is not { } popup)
            return false;
        var target = popup.PlacementTarget;
        popup.SetCurrentValue(Popup.IsOpenProperty, false);
        for (var current = target; current != null; current = current.GetVisualParent() as Control)
            if (current.IsEffectivelyVisible && current.IsEffectivelyEnabled && current.Focus(NavigationMethod.Directional))
                break;
        return true;
    }

    private static bool IsInteractive(Popup popup) => popup.IsOpen && popup.Child is not null and not ToolTip;

    private static bool IsOwnedBy(Popup popup, Control scope) =>
        popup.PlacementTarget is { } target
        && (target == scope || target.GetVisualAncestors().Contains(scope) || target.GetSelfAndLogicalAncestors().Contains(scope));

    private static Popup GetDeepest(Popup popup)
    {
        for (var i = popup.OpenedPopups.Count - 1; i >= 0; i--)
            if (IsInteractive(popup.OpenedPopups[i]))
                return GetDeepest(popup.OpenedPopups[i]);
        return popup;
    }
}

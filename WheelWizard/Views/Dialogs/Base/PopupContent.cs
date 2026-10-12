using Avalonia;
using Avalonia.Controls;
using WheelWizard.Views.Shell;

namespace WheelWizard.Views.Dialogs.Base;

public abstract class PopupContent : UserControl
{
    protected PopupWindow Window { get; private set; }
    private double? _preferredContentWidth;

    protected PopupContent(bool allowClose, bool allowParentInteraction, bool isTopMost, string title = "")
    {
        Window = new(allowClose, allowParentInteraction, isTopMost, title)
        {
            PopupContent = { Content = this },
            BeforeClose = BeforeClose,
            BeforeOpen = BeforeOpen,
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_preferredContentWidth == null && Content is Control root && double.IsFinite(root.Width))
        {
            // A defined root width remains the popup's preferred size, but does not limit arrangement.
            _preferredContentWidth = root.Width;
            root.MinWidth = Math.Max(root.MinWidth, root.Width);
            root.ClearValue(WidthProperty);
        }

        var measureWidth = Math.Min(availableSize.Width, _preferredContentWidth ?? double.PositiveInfinity);
        return base.MeasureOverride(new Size(measureWidth, availableSize.Height));
    }

    protected virtual void BeforeClose() { } // Meant to be overwritten if needed

    protected virtual void BeforeOpen() { } // Meant to be overwritten if needed

    public void Show() => Window.Show();

    public Task ShowDialog() => Window.ShowDialog(ViewUtils.GetLayout());

    public Task<T> ShowDialog<T>() => Window.ShowDialog<T>(ViewUtils.GetLayout());

    public void Close() => Window.Close();

    public void Minimize() => Window.WindowState = WindowState.Minimized;

    public void Focus() => Window.Focus();

    /// <summary>
    /// Should be called Before opening the popup (internally).
    /// It can be used to disable wether or not a popup can be opened
    /// </summary>
    protected void DisableOpen(bool value) => Window.DisableOpen(value);
}

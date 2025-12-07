using Avalonia.Controls;

namespace WheelWizard.Views.Popups.Base;

public abstract class PopupContent : UserControlBase
{
    protected PopupWindow Window { get; private set; }

    protected PopupContent(bool allowClose, bool allowParentInteraction, bool isTopMost, string title = "")
    {
        Window = new(allowClose, allowParentInteraction, isTopMost, title)
        {
            PopupContent = { Content = this },
            BeforeClose = BeforeClose,
            BeforeOpen = BeforeOpen,
        };

        // If layout interaction is enabled, that means you can close the application
        // That means you can close the base application without closing these popups. And that is just annoying
        if (allowParentInteraction)
            ViewUtils.GetLayout().Closing += (_, _) => Close();
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

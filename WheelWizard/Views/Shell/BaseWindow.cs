using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WheelWizard.Views.Dialogs.Base;

namespace WheelWizard.Views.Shell;

public abstract class BaseWindow : Window
{
    private ModalHost? _modals;
    private Border? _modalChromeDimmer;
    private static readonly List<BaseWindow> OpenWindows = [];
    public ModalHost Modals => _modals ?? throw new InvalidOperationException("The window must be open before showing a modal.");

    internal static BaseWindow GetModalOwner()
    {
        var foreground = OpenWindows.LastOrDefault(window => window.IsActive && window.IsVisible && window.Modals.IsEnabled);
        if (foreground != null)
            return foreground;
        var current = OpenWindows.LastOrDefault(window => window.IsVisible && window.Modals.IsEnabled);
        if (current != null)
            return current;
        if (
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: BaseWindow main }
            && main.IsVisible
        )
            return main;
        throw new InvalidOperationException("Open an application window before showing an in-app modal.");
    }

    private void InstallModalHost()
    {
        if (_modals != null)
            return;
        var content = Content as Control ?? throw new InvalidOperationException("The window needs a control layout.");
        Content = null;
        var root = new Grid();
        root.Children.Add(content);
        _modalChromeDimmer = new Border
        {
            Name = "ModalChromeDimmer",
            Height = Math.Max(0, ExtendClientAreaTitleBarHeightHint),
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Black,
            IsHitTestVisible = false,
            IsVisible = false,
            ZIndex = 1000,
        };
        root.Children.Add(_modalChromeDimmer);
        _modals = new ModalHost(this);
        root.Children.Add(_modals);
        Content = root;
    }

    internal void UpdateModalInteraction()
    {
        InteractionOverlay.IsVisible = _disableCount != 0;
        InteractionContent.IsEnabled = _disableCount == 0;
        ModalInteractionContent.IsEnabled = _disableCount == 0 && !(_modals?.HasModals ?? false);
        if (_modals != null)
            _modals.IsEnabled = _disableCount == 0;
        UpdateModalChromeDimming();
    }

    internal void UpdateModalChromeDimming()
    {
        if (_modalChromeDimmer != null)
        {
            _modalChromeDimmer.IsVisible = _modals?.HasModals ?? false;
            // Use the backdrop's actual animated opacity, so chrome follows every fade frame.
            _modalChromeDimmer.Opacity = _modals?.BackdropOpacity ?? 0;
        }
    }

    private int _disableCount = 0;
    private readonly Action<Size, Size>? _setWaylandSizeConstraints;
    private bool _waylandSizeUpdateQueued;

    protected abstract Control InteractionOverlay { get; } // Just the visual part of the interaction
    protected abstract Control InteractionContent { get; } // The content that will be disabled when the overlay is shown

    protected virtual Control ModalInteractionContent => InteractionContent;

    private void InstallMacWindowMenu()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        // Avalonia owns the menu bar; AppKit owns the contents of the Window submenu.
        var menu = new NativeMenu();
        var profiles = new NativeMenu();
        void UpdateProfiles()
        {
            if (
                Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: Layout layout }
            )
                layout.PopulateMacProfiles(profiles);
            Avalonia.Threading.Dispatcher.UIThread.Post(AttachMacWindowMenu, Avalonia.Threading.DispatcherPriority.Background);
        }
        UpdateProfiles();
        profiles.NeedsUpdate += (_, _) => UpdateProfiles();
        menu.Add(new NativeMenuItem("Profiles") { Menu = profiles });
        menu.Add(new NativeMenuItem("Window") { Menu = new NativeMenu() });
        NativeMenu.SetMenu(this, menu);
        Avalonia.Threading.Dispatcher.UIThread.Post(AttachMacWindowMenu, Avalonia.Threading.DispatcherPriority.Background);
    }

    protected BaseWindow()
    {
        var platform = PlatformImpl;
        if (platform?.GetType().FullName == "Avalonia.Wayland.WindowImpl")
        {
            // Avalonia 12 makes the platform interface internal; bind only the pinned
            // Wayland implementation's native size-hint method, without changing backends.
            _setWaylandSizeConstraints = platform.GetType().GetMethod("SetMinMaxSize")?.CreateDelegate<Action<Size, Size>>(platform);
        }
        Activated += (_, _) =>
        {
            if (OperatingSystem.IsMacOS())
                Avalonia.Threading.Dispatcher.UIThread.Post(AttachMacWindowMenu, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        if (CanResize || _setWaylandSizeConstraints == null || _waylandSizeUpdateQueued)
            return;

        // On Wayland, the compositor controls the visible window size. In particular,
        // Hyprland does not resize a floating window just because its buffer grows.
        // Publish the current size as native constraints for our fixed-size windows.
        // Do not set Window.Min/MaxWidth/Height: that would prevent SizeToContent from
        // measuring a dialog's next size when its content changes.
        // Wait for layout and decoration changes to settle, and coalesce acknowledgements.
        _waylandSizeUpdateQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _waylandSizeUpdateQueued = false;
                if (IsVisible && !CanResize && PlatformImpl != null)
                    _setWaylandSizeConstraints(ClientSize, ClientSize);
            },
            DispatcherPriority.Background
        );
    }

    private void AttachMacWindowMenu()
    {
        if (IsVisible && TryGetPlatformHandle()?.HandleDescriptor == "NSWindow")
            MacWindowMenu.Attach(TryGetPlatformHandle()!.Handle);
    }

    protected override void OnOpened(EventArgs e)
    {
        InstallModalHost();
        OpenWindows.Add(this);
        InstallMacWindowMenu();
        base.OnOpened(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Blocking application work still gates native close; in-app modals leave window controls usable.
        if (_disableCount != 0 && !e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
            e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _modals?.CloseAll();
        OpenWindows.Remove(this);
        base.OnClosed(e);
    }

    public void SetInteractable(bool value)
    {
        if (!value)
            _disableCount++;
        else if (_disableCount > 0)
            _disableCount--;

        UpdateModalInteraction();
    }
}

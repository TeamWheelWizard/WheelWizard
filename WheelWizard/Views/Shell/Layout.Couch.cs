using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using WheelWizard.RrRooms.Views;
using WheelWizard.Settings.Views;
using WheelWizard.Views.Shell.Couch;
using WheelWizard.Views.Shell.Couch.Input;
using WheelWizard.Views.Shell.Views;
using WheelWizard.WiiManagement.MiiManagement.Views;
using WheelWizard.WiiManagement.MiiManagement.Views.Editor;

namespace WheelWizard.Views.Shell;

public partial class Layout
{
    private CouchInputController? _couchInput;
    private bool _presentationReady;
    private bool _applyingPresentation;
    private Size _desktopSize;
    private PixelPoint _desktopPosition;
    private WindowState _desktopWindowState;
    private Size _couchSize = new(1280, 800);
    private IInputElement? _desktopFocus;
    private IInputElement? _couchFocus;

    public bool IsCouchMode => CouchMode.GetIsEnabled(this);

    private void InitializeCouchPresentation()
    {
        CouchPresentation.NavigationRequested += NavigateFromSidebar;
        CouchPresentation.FullscreenRequested += ToggleCouchFullscreen;
        CouchPresentation.BackRequested += GoCouchBack;
        CouchPresentation.ContentHost.SizeChanged += (_, _) => UpdateCurrentPageWidth();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _couchInput = new CouchInputController(this);
    }

    private void InitializePresentationMode()
    {
        if (_presentationReady)
            return;
        _presentationReady = true;
        RefreshCouchNavigation();
        var arguments =
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Args ?? Environment.GetCommandLineArgs();
        var requested = arguments.LastOrDefault(argument => argument is "--view=couch" or "--view=desktop");
        var couch = requested == "--view=couch" || (requested == null && SettingsService.COUCH_VIEW.Get());
        ApplyPresentation(couch);
        if (couch && arguments.Contains("--fullscreen", StringComparer.Ordinal))
            WindowState = WindowState.FullScreen;
    }

    public void TogglePresentation()
    {
        if (!_presentationReady || !PresentationRoot.IsEnabled || Modals.HasModals)
            return;
        var couch = !IsCouchMode;
        // Saving publishes synchronously; the signal applies the presentation only after a successful save.
        if (!SettingsService.Set(SettingsService.COUCH_VIEW, couch))
        {
            new Dialogs.MessageBoxWindow()
                .SetTitleText(t("couch.enabled"))
                .SetInfoText(SettingsService.COUCH_VIEW.SaveError?.Message ?? t("couch.save_failed"))
                .Show();
            return;
        }
        ApplyPresentation(couch);
    }

    private void ApplyPresentation(bool couch)
    {
        if (_applyingPresentation || couch == IsCouchMode)
            return;
        _applyingPresentation = true;
        try
        {
            SidebarInfoContextMenu.Close();
            var focused = FocusManager?.GetFocusedElement();
            if (couch)
            {
                _desktopFocus = focused;
                _desktopSize = new Size(Width, Height);
                _desktopPosition = Position;
                _desktopWindowState = WindowState;
            }
            else
            {
                _couchFocus = focused;
                if (WindowState == WindowState.Normal)
                    _couchSize = ClientSize;
            }

            // Detach only the host: retain the exact page instance, editor state, and running operations.
            var parent = (Panel)ContentArea.Parent!;
            parent.Children.Remove(ContentArea);
            ((Panel)Snackbar.Parent!).Children.Remove(Snackbar);
            Grid.SetColumn(Snackbar, couch ? 0 : 1);
            Grid.SetRow(Snackbar, couch ? 0 : 4);
            Grid.SetRowSpan(Snackbar, couch ? 1 : 2);
            Snackbar.Margin = couch ? new Thickness(30, 13, 30, 80) : new Thickness(30, 13, 30, 16);
            Snackbar.ZIndex = 10;
            (couch ? PresentationRoot : CompleteGrid).Children.Add(Snackbar);
            CouchMode.SetIsEnabled(this, couch);
            Classes.Set("couch", couch);
            CompleteGrid.IsVisible = !couch;
            CouchPresentation.IsVisible = couch;
            WindowState = WindowState.Normal;
            CanResize = couch;
            CanMaximize = couch;
            MinWidth = couch ? 960 : 0;
            MinHeight = couch ? 540 : 0;
            if (couch)
            {
                RefreshCouchNavigation();
                ContentArea.Margin = default;
                CouchPresentation.ContentHost.Children.Add(ContentArea);
                var screen = Screens.ScreenFromWindow(this);
                var available = screen?.WorkingArea.Size.ToSize(screen.Scaling) ?? new Size(1280, 800);
                Width = Math.Min(_couchSize.Width, available.Width);
                Height = Math.Min(_couchSize.Height, available.Height);
            }
            else
            {
                ContentArea.Margin = (Thickness)this.FindResource("EdgeGap")!;
                DesktopContentHost.Children.Add(ContentArea);
                OnSettingChanged(SettingsService.SAVED_WINDOW_SCALE);
                if (_desktopSize.Width > 0)
                {
                    Width = _desktopSize.Width;
                    Height = _desktopSize.Height;
                    Position = _desktopPosition;
                    WindowState = _desktopWindowState;
                }
            }
            AttachMacWindowMenu();
            UpdateCurrentPageWidth();
            if (Navigation.CurrentPage is { } page)
                CouchPresentation.SelectPage(page);
            Dispatcher.UIThread.Post(() => RestorePresentationFocus(couch), DispatcherPriority.Loaded);
        }
        finally
        {
            _applyingPresentation = false;
        }
    }

    private void RestorePresentationFocus(bool couch)
    {
        var previous = couch ? _couchFocus : _desktopFocus;
        if (
            previous is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } control
            && ReferenceEquals(TopLevel.GetTopLevel(control), this)
        )
            control.Focus(NavigationMethod.Directional);
        else if (couch)
            CouchPresentation.FocusNavigation();
        else
            SidePanelButtons.Children.OfType<Control>().FirstOrDefault(control => control.Focusable)?.Focus();
    }

    private void RefreshCouchNavigation()
    {
        CouchPresentation.SetRoutes(SidePanelButtons.Children.OfType<Controls.SidebarRadioButton>());
        CouchPresentation.RefreshLabels();
        if (Navigation.CurrentPage is { } page)
            CouchPresentation.SelectPage(page);
    }

    private void UpdateCurrentPageWidth()
    {
        if (ContentArea.Content is not SettingsPage settings)
            return;
        var width = IsCouchMode
            ? CouchPresentation.ContentHost.Bounds.Width
            : (CompleteGrid.Bounds.Width > 0 ? CompleteGrid.Bounds.Width : WindowWidth)
                - 64
                - ContentArea.Margin.Left
                - ContentArea.Margin.Right;
        settings.UpdateLayoutWidth(Math.Max(0, width));
    }

    public void NavigateCouchSection(int offset)
    {
        if (IsCouchMode && PresentationRoot.IsEnabled && !Modals.HasModals)
            CouchPresentation.NavigateSection(offset);
    }

    public void GoCouchBack()
    {
        if (!IsCouchMode)
            return;
        if (Modals.TopModal is { } modal)
        {
            modal.Dismiss();
            return;
        }
        if (!PresentationRoot.IsEnabled)
            return;
        switch (Navigation.CurrentPage)
        {
            case RoomDetailsPage:
                Navigation.NavigateTo<RoomsPage>();
                break;
            case MiiEditorPage:
                Navigation.NavigateTo<MiiListPage>();
                break;
            case HomePage:
                CouchPresentation.FocusNavigation();
                break;
            default:
                Navigation.NavigateTo<HomePage>();
                break;
        }
    }

    public void ToggleCouchFullscreen()
    {
        if (IsCouchMode && !Modals.HasModals)
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }
}

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WheelWizard.AutoUpdating;
using WheelWizard.Branding;
using WheelWizard.Localization;
using WheelWizard.MiiImages.Views;
using WheelWizard.Mods;
using WheelWizard.RrRooms;
using WheelWizard.RrRooms.Views;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Settings.Views;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Shared.Polling;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell.Controls;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Views;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement.Views;
using WheelWizard.WiiManagement.MiiManagement.Views.Editor;

namespace WheelWizard.Views.Shell;

public partial class Layout : BaseWindow, IPollingListener
{
    private INavigationService Navigation { get; }
    private readonly IAutoUpdaterSingletonService _autoUpdater;

    protected override Control InteractionOverlay => DisabledDarkenEffect;
    protected override Control InteractionContent => WindowFrame;

    public const double WindowHeight = 876;
    public const double WindowWidth = 656;
    private const int TesterClicksRequired = 10;

    // so this is not really "Secret" its just ment to hold out people who are not meant to be testers
    // if you came here to find it, it will be useless to you, you can not actually download or play
    // testing builds since they are behind authentication walls.
    // but have fun with the beta button :)
    private const string TesterSecretPhrase = "WhenSonicInRR?";
    private static readonly TimeSpan PageSwapDuration = TimeSpan.FromMilliseconds(250);
    private static readonly IPageTransition RoomsPageTransition = new CompositePageTransition
    {
        PageTransitions =
        [
            new PageSlide { Duration = PageSwapDuration, Orientation = PageSlide.SlideAxis.Horizontal },
            new CrossFade { Duration = PageSwapDuration, FillMode = FillMode.None },
        ],
    };

    private int _testerClickCount;
    private bool _testerPromptOpen;
    private IDisposable? _settingsSignalSubscription;
    private readonly Task _modsLoaded;
    private readonly TaskCompletionSource _loaded = new();

    private LiveRoomsService LiveRooms { get; }

    private LiveStatusService LiveStatus { get; }

    private IBrandingSingletonService BrandingService { get; }

    private IGameLicenseSingletonService GameLicenseService { get; }

    private ISettingsManager SettingsService { get; }

    private ISettingsSignalBus SettingsSignalBus { get; }

    private IModManager ModManagerService { get; }

    public Layout(
        INavigationService navigation,
        LiveRoomsService liveRooms,
        LiveStatusService liveStatus,
        IBrandingSingletonService brandingService,
        IGameLicenseSingletonService gameLicenseService,
        ISettingsManager settingsService,
        ISettingsSignalBus settingsSignalBus,
        IModManager modManagerService,
        IAutoUpdaterSingletonService autoUpdater
    )
    {
        Navigation = navigation;
        LiveRooms = liveRooms;
        LiveStatus = liveStatus;
        BrandingService = brandingService;
        GameLicenseService = gameLicenseService;
        SettingsService = settingsService;
        SettingsSignalBus = settingsSignalBus;
        ModManagerService = modManagerService;
        InitializeComponent();
        _autoUpdater = autoUpdater;
        _autoUpdater.UpdateAvailable += OnUpdateAvailable;
        UpdateVersionBadge();

        // Wayland does not expose the drawn caption buttons from our platform decoration template.
        HeaderWindowControls.IsVisible =
            OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        // Respects tiling window managers better if resizable.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            CanResize = true;

        Navigation.PageChanged += Navigation_OnPageChanged;
        foreach (var button in SidePanelButtons.Children.OfType<SidebarRadioButton>())
            button.NavigationRequested += (_, pageType) => NavigateFromSidebar(pageType);
        SidebarCurrentUserProfile.ProfileRequested += (_, _) => NavigateFromSidebar(typeof(UserProfilePage));
        UpdateSidebarProfile();

        ClampSavedWindowScaleToCurrentScreen();
        OnSettingChanged(SettingsService.SAVED_WINDOW_SCALE);
        _settingsSignalSubscription = SettingsSignalBus.Subscribe(OnSettingSignal);
        UpdateTestingButtonVisibility();

        UpdateMadeByText();
        LocalizationProvider.LanguageChanged += OnLanguageChanged;

        LiveStatus.Subscribe(this);
        LiveRooms.Subscribe(this);
        GameLicenseService.Subscribe(this);
        ModManagerService.PropertyChanged += ModManager_PropertyChanged;
        _modsLoaded = ReloadModsAndShowErrorsAsync();
        UpdateOtherSectionVisibility();
        if (SettingsService.SIDEBAR_COLLAPSED.Get())
            _ = SetSidebarCollapsedAsync(true, animate: false);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Title = BrandingService.Branding.DisplayName;
        VersionTagText.Text = $"v{BrandingService.Branding.Version}";
        UpdateModsButtonText();
        // UpdateModsActionIndicator();

        if (Navigation.CurrentPage is { } page)
            Navigation_OnPageChanged(this, page);
        else
            Navigation.NavigateTo<HomePage>();
        _loaded.TrySetResult();
    }

    public async Task WaitForInitialContentAsync(CancellationToken cancellationToken)
    {
        await _loaded.Task.WaitAsync(cancellationToken);
        LiveStatus.Start();
        LiveRooms.Start();
        var initialPageReady = Navigation.CurrentPage is HomePage home ? home.InitialContentReady : Task.CompletedTask;
        await Task.WhenAll(_modsLoaded, initialPageReady, LiveStatus.InitialUpdate, LiveRooms.InitialUpdate).WaitAsync(cancellationToken);
        UpdateLayout();
        await Task.WhenAll(
            this.GetVisualDescendants()
                .OfType<BaseMiiImage>()
                .Where(image => image.IsEffectivelyVisible)
                .Select(image => image.WaitUntilLoadedAsync(cancellationToken))
        );
        await Dispatcher.UIThread.InvokeAsync(UpdateLayout, DispatcherPriority.Background, cancellationToken);
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoUpdater.UpdateAvailable -= OnUpdateAvailable;
        Navigation.PageChanged -= Navigation_OnPageChanged;
        DetachLiveSubscriptions();
        _settingsSignalSubscription?.Dispose();
        _settingsSignalSubscription = null;
        LocalizationProvider.LanguageChanged -= OnLanguageChanged;
        ModManagerService.PropertyChanged -= ModManager_PropertyChanged;
        base.OnClosed(e);
    }

    private void OnUpdateAvailable(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateVersionBadge);

    private void UpdateVersionBadge()
    {
        var available = _autoUpdater.IsUpdateAvailable;
        VersionTagBorder.Classes.Set("UpdateAvailable", available);
        VersionTagBorder.IsHitTestVisible = available;
        VersionTagBorder.Focusable = available;
        VersionUpdateIcon.IsVisible = available;
    }

    private async void VersionTag_OnClick(object? sender, RoutedEventArgs e) => await _autoUpdater.ShowAvailableUpdateAsync();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateModsButtonText();
        UpdateMadeByText();
        UpdateLiveAlert();
    }

    private void UpdateMadeByText()
    {
        var completeString = t("text.made_by_string", new { firstAuthor = "Patchzy", secondAuthor = "WantToBeeMe" });
        MadeBy.Text = completeString;
    }

    private void ModManager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        //todo: after patches is more stable, uncomment this
        // if (e.PropertyName == nameof(ModManager.Mods))
        //     UpdateModsActionIndicator();
    }

    private async Task ReloadModsAndShowErrorsAsync()
    {
        var reloadResult = await ModManagerService.ReloadAsync();
        if (reloadResult.IsFailure)
            MessageTranslationHelper.ShowMessage(reloadResult.Error);
    }

    private void OnSettingSignal(SettingChangedSignal signal) => OnSettingChanged(signal.Setting);

    private void OnSettingChanged(Setting setting)
    {
        // Note that this method will also be called whenever the setting changes
        if (setting == SettingsService.WINDOW_SCALE || setting == SettingsService.SAVED_WINDOW_SCALE)
        {
            var scaleFactor = GetUsableWindowScale(SettingsService.WINDOW_SCALE.Value);
            CompleteGrid.Resources["SettingsRowGap"] = 2d / scaleFactor;
            Height = WindowHeight * scaleFactor;
            // Reserve native chrome inside the existing window height, outside content scaling.
            var contentHeight = WindowHeight - 30 / scaleFactor;
            Width = WindowWidth * scaleFactor;
            CompleteGrid.RenderTransform = new ScaleTransform(scaleFactor, scaleFactor);
            var marginXCorrection = ((scaleFactor * WindowWidth) - WindowWidth) / 2f;
            var marginYCorrection = ((scaleFactor * contentHeight) - contentHeight) / 2f;
            CompleteGrid.Margin = new(marginXCorrection, marginYCorrection);
            //ExtendClientAreaToDecorationsHint = scaleFactor <= 1.2f;
            return;
        }

        if (setting == SettingsService.TESTING_MODE_ENABLED)
            UpdateTestingButtonVisibility();
    }

    private void ClampSavedWindowScaleToCurrentScreen()
    {
        var savedScale = SettingsService.Get<double>(SettingsService.SAVED_WINDOW_SCALE);
        var usableScale = GetUsableWindowScale(savedScale);
        if (!savedScale.Equals(usableScale))
            SettingsService.Set(SettingsService.SAVED_WINDOW_SCALE, usableScale);
    }

    private double GetUsableWindowScale(double requestedScale) =>
        ViewUtils.GetUsableWindowScale(requestedScale, new Size(WindowWidth, WindowHeight), this);

    private void UpdateModsButtonText()
    {
        ModsButton.Text = t("page_title.patches");
    }

    //todo: after patches is more stable, uncomment this
    // private void UpdateModsActionIndicator()
    // {
    //     ModsButton.WarningVisible = ModManagerService.Mods.Any(mod => mod.HasIncompatibleFiles);
    //     ModsButton.WarningTip = "Some mods need to be converted to patches.";
    // }

    private void Navigation_OnPageChanged(object? sender, UserControl page) => NavigateToPage(page);

    /// <summary>
    /// The current page may ask before letting you go (see <see cref="INavigationGuard"/>); until it does, the
    /// sidebar keeps showing the page you're on.
    /// </summary>
    private void NavigateFromSidebar(Type pageType)
    {
        Navigation.NavigateTo(pageType);
        if (Navigation.CurrentPage is { } current && current.GetType() != pageType)
            UpdateSidebarSelection(current);
    }

    private bool _closeConfirmed;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && Navigation.CurrentPage is INavigationGuard { HasUnsavedWork: true } guard && InteractionContent.IsEnabled)
        {
            e.Cancel = true;
            base.OnClosing(e);
            if (!await guard.ConfirmLeaveAsync())
                return;
            _closeConfirmed = true;
            Close();
            return;
        }

        base.OnClosing(e);
    }

    private void HeaderMinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void HeaderCloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private static void WindowControl_PointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    public void NavigateToPage(UserControl page)
    {
        var oldPage = ContentArea.Content as Control;
        var isRoomsToDetails = oldPage is RoomsPage && page is RoomDetailsPage;
        var isDetailsToRooms = oldPage is RoomDetailsPage && page is RoomsPage;
        var isMiisToEditor = oldPage is MiiListPage && page is MiiEditorPage;
        var isEditorToMiis = oldPage is MiiEditorPage && page is MiiListPage;

        ContentArea.PageTransition = isRoomsToDetails || isDetailsToRooms || isMiisToEditor || isEditorToMiis ? RoomsPageTransition : null;
        ContentArea.IsTransitionReversed = isDetailsToRooms || isEditorToMiis;
        ContentArea.Content = page;
        UpdateSidebarSelection(page);
        _ = LockSidebarAsync(page is IFullWidthPage || page is SettingsPage);
    }

    private void UpdateSidebarSelection(UserControl page)
    {
        // Update the IsChecked state of the SidebarRadioButtons
        foreach (var child in SidePanelButtons.Children)
        {
            if (child is not SidebarRadioButton button)
                continue;

            var buttonPageType = button.PageType;
            button.IsChecked = buttonPageType == page.GetType();

            // TODO: make a better way to have these type of exceptions
            if (button.PageType == typeof(RoomsPage) && typeof(RoomDetailsPage) == page.GetType())
                button.IsChecked = true;
            if (button.PageType == typeof(MiiListPage) && page is MiiEditorPage)
                button.IsChecked = true;
        }
    }

    public void OnUpdate(ObservablePollingService sender)
    {
        switch (sender)
        {
            case LiveRoomsService liveRooms:
                UpdatePlayerAndRoomCount(liveRooms);
                break;
            case LiveStatusService liveAlerts:
                UpdateLiveAlert(liveAlerts);
                break;
        }
    }

    public void UpdateFriendCount()
    {
        var friends = GameLicenseService.ActiveCurrentFriends;
        var onlineCount = friends.Count(friend => friend.IsOnline);
        FriendsButton.BoxText = $"{onlineCount}/{friends.Count}";
        FriendsButton.BoxTip = onlineCount == 0 ? t("hover.friends_online.none") : t("hover.friends_online", count: onlineCount);
    }

    public void UpdateSidebarProfile()
    {
        GameLicenseService.RefreshOnlineStatus();
        GameLicenseService.LoadLicense();
        var user = GameLicenseService.ActiveUser;
        SidebarCurrentUserProfile.DisplayProfile(user.NameOfMii, user.FriendCode, user.Mii);
    }

    public void DetachLiveSubscriptions()
    {
        LiveRooms.Unsubscribe(this);
        LiveStatus.Unsubscribe(this);
        GameLicenseService.Unsubscribe(this);
    }

    public void UpdatePlayerAndRoomCount() => UpdatePlayerAndRoomCount(LiveRooms);

    public void UpdatePlayerAndRoomCount(LiveRoomsService sender)
    {
        var playerCount = sender.PlayerCount;
        RoomsButton.BoxText = playerCount.ToString();
        RoomsButton.BoxTip = playerCount == 0 ? t("hover.players_online.none") : t("hover.players_online", count: playerCount);
        UpdateFriendCount();
    }

    public void UpdateLiveAlert() => UpdateLiveAlert(LiveStatus);

    private void UpdateLiveAlert(LiveStatusService sender)
    {
        var hasVariant = sender.Status?.Variant != null && sender.Status.Variant != WhWzStatusVariant.None;
        var hasCustomIcon = !string.IsNullOrEmpty(sender.Status?.Icon);
        var visible = hasVariant || hasCustomIcon;

        LiveStatusBorder.IsVisible = visible;
        if (!visible)
            return;

        ToolTip.SetTip(LiveStatusBorder, sender.Status!.Message);
        LiveStatusBorder.Classes.Clear();
        LiveStatusBorder.Classes.Add("BottomSidebarIcon");

        // If custom icon is provided, use it instead of variant
        if (hasCustomIcon)
        {
            // Clear any variant-based classes
            LiveStatusBorder.Classes.Add("Custom");

            // Find the PathIcon in the LiveStatusBorder and update it dynamically
            if (LiveStatusBorder.Child is PathIcon pathIcon)
            {
                // Parse the SVG path data
                var geometry = Geometry.Parse(sender.Status.Icon!);
                pathIcon.Data = geometry;

                // Apply custom color if provided, otherwise use a default
                if (!string.IsNullOrEmpty(sender.Status.Color))
                {
                    pathIcon.Foreground = new SolidColorBrush(Color.Parse(sender.Status.Color));
                }
                else
                {
                    pathIcon.Foreground = new SolidColorBrush(Colors.White);
                }
            }
        }
        else
        {
            // Use variant-based styling
            LiveStatusBorder.Classes.Add(sender.Status.Variant.ToString()!);
        }
    }

    private void TopBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void TitleLabel_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;

        if (SettingsService.Get<bool>(SettingsService.TESTING_MODE_ENABLED))
            return;

        if (_testerPromptOpen)
            return;

        _testerClickCount++;
        if (_testerClickCount < TesterClicksRequired)
            return;

        _testerClickCount = 0;
        _testerPromptOpen = true;

        try
        {
            var result = await new TextInputWindow()
                .SetMainText("Welcome tester, write your secret phrase")
                .SetPlaceholderText("Secret phrase")
                .SetButtonText("Cancel", "Submit")
                .ShowDialog();

            if (string.IsNullOrWhiteSpace(result))
                return;

            if (result == TesterSecretPhrase)
            {
                SettingsService.Set(SettingsService.TESTING_MODE_ENABLED, true);
                ShowSnackbar("Testing mode enabled", ViewUtils.SnackbarType.Success);
            }
            else
            {
                ShowSnackbar("Incorrect secret phrase", ViewUtils.SnackbarType.Danger);
            }
        }
        finally
        {
            _testerPromptOpen = false;
        }
    }

    private void UpdateTestingButtonVisibility()
    {
        TestingButton.IsVisible = SettingsService.Get<bool>(SettingsService.TESTING_MODE_ENABLED);
        UpdateOtherSectionVisibility();
    }

    private void UpdateOtherSectionVisibility()
    {
        OtherSectionText.IsVisible = TestingButton.IsVisible;
    }

    public void HideDevelopmentFeatures()
    {
        UpdateOtherSectionVisibility();
        if (ContentArea.Content is SettingsPage settingsPage)
            settingsPage.HideDevelopmentFeatures();
    }

    private void SidebarInfoButton_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SidebarInfoContextMenu.Open();
        e.Handled = true;
    }

    private void SidebarSettingsButton_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Navigation.NavigateTo<SettingsPage>();
        e.Handled = true;
    }

    private void SidebarProfileBlock_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        SidebarProfileBlock.Background = GetResourceBrush("Neutral800");
        SidebarProfileBlock.BorderBrush = GetResourceBrush("Primary400");
        SidebarProfileHoverEffect.IsVisible = true;
    }

    private void SidebarProfileBlock_OnPointerExited(object? sender, PointerEventArgs e)
    {
        SidebarProfileBlock.Background = Brushes.Transparent;
        SidebarProfileBlock.BorderBrush = GetResourceBrush("Neutral600");
        SidebarProfileHoverEffect.IsVisible = false;
    }

    private void SidebarProfileBlock_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(sender as Control);
        SidebarProfileHoverEffect.Margin = new(
            position.X - (SidebarProfileHoverEffect.Width / 2),
            position.Y - (SidebarProfileHoverEffect.Height / 2),
            0,
            0
        );
    }

    private static IBrush GetResourceBrush(string resourceName) =>
        new SolidColorBrush((Color)Application.Current!.FindResource(resourceName)!);

    private void Discord_Click(object? sender, RoutedEventArgs e) => ViewUtils.OpenLink(BrandingService.Branding.DiscordUrl.ToString());

    private void Github_Click(object? sender, RoutedEventArgs e) => ViewUtils.OpenLink(BrandingService.Branding.RepositoryUrl.ToString());

    private void Support_Click(object? sender, RoutedEventArgs e) => ViewUtils.OpenLink(BrandingService.Branding.SupportUrl.ToString());

    private void SupportUs_OnClick(object? sender, RoutedEventArgs e) => ViewUtils.OpenLink(BrandingService.Branding.SupportUrl.ToString());

    public bool CompleteContentEnabled => InteractionContent.IsEnabled;

    public void ShowAppInfo() => Navigation.NavigateTo<SettingsPage>(typeof(AppInfo));

    public void ShowSettings() => Navigation.NavigateTo<SettingsPage>();

    public void OpenCommunityLink(string link) =>
        ViewUtils.OpenLink(
            link switch
            {
                "github" => BrandingService.Branding.RepositoryUrl.ToString(),
                "discord" => BrandingService.Branding.DiscordUrl.ToString(),
                "support" => BrandingService.Branding.SupportUrl.ToString(),
                _ => throw new ArgumentOutOfRangeException(nameof(link)),
            }
        );

    private void About_Click(object? sender, RoutedEventArgs e) => Navigation.NavigateTo<SettingsPage>(typeof(AppInfo));

    private void CloseSnackbar_OnClick(object? sender, RoutedEventArgs e)
    {
        Snackbar.Classes.Remove("show");
        Snackbar.IsVisible = false;
    }

    public void ShowSnackbar(string message, ViewUtils.SnackbarType type)
    {
        Snackbar.Classes.Clear();

        SnackbarText.Text = message;
        Snackbar.Classes.Add("show");
        Snackbar.Classes.Add(type.ToString().ToLower());
    }
}

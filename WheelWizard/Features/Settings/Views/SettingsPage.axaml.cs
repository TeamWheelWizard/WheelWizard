using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Settings;
using WheelWizard.Settings.Views;
using WheelWizard.Shared;
using WheelWizard.Views.Components;
using WheelWizard.Views.DesignTime;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell.Navigation;

namespace WheelWizard.Settings.Views;

public partial class SettingsPage : UserControl, ILockedSidebarPage
{
    // Reveal the navigation by clipping; keep the settings content stationary during the shell resize.
    public void UpdateLayoutWidth(double availableWidth) => SettingsRoot.Width = availableWidth;

    private IPopupFactory Popups { get; }

    private ISettingsManager SettingsService { get; }

    private ISettingsSignalBus SettingsSignalBus { get; }

    private IPageFactory Pages { get; }
    private IDisposable? _settingsSignalSubscription;
    private bool _navigationReady;

    public SettingsPage(
        IPopupFactory popups,
        ISettingsManager settingsService,
        ISettingsSignalBus settingsSignalBus,
        IPageFactory pages,
        Type? initialPage = null
    )
    {
        Popups = popups;
        SettingsService = settingsService;
        SettingsSignalBus = settingsSignalBus;
        Pages = pages;
        InitializeComponent();
        VersionInfoSidebar.Content = Pages.Create<SettingsVersionInfo>();
        UpdateTabVisibility();
        _settingsSignalSubscription = SettingsSignalBus.Subscribe(OnSettingChanged);

        DevButton.IsVisible = DevelopmentMode.IsEnabled;

        var initialSettingsPage = Pages.Create(initialPage ?? typeof(WhWzSettings));
        SettingsContent.Content = initialSettingsPage;
        SetCheckedSidebarButton(initialSettingsPage);
        _navigationReady = true;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _settingsSignalSubscription?.Dispose();
        _settingsSignalSubscription = null;
        base.OnUnloaded(e);
    }

    private void OnSettingChanged(SettingChangedSignal signal)
    {
        if (signal.Setting == SettingsService.ENABLE_RECOMP)
            UpdateTabVisibility();
    }

    /// <summary>
    /// In recomp mode the WiiCompiled tab is the video/data settings surface and Dolphin's Video tab
    /// is hidden: those settings only affect Dolphin and would silently do nothing for the recomp.
    /// </summary>
    private void UpdateTabVisibility()
    {
        var recompMode = SettingsService.IsRecompModeActive();
        RecompSettingsTab.IsVisible = recompMode;
        VideoSettingsTab.IsVisible = !recompMode;

        // Never leave the content on a tab that just disappeared.
        var hiddenSelected =
            (recompMode && SettingsContent.Content is VideoSettings) || (!recompMode && SettingsContent.Content is RecompSettings);
        if (!hiddenSelected)
            return;

        var fallback = Pages.Create<WhWzSettings>();
        SettingsContent.Content = fallback;
        SetCheckedSidebarButton(fallback);
    }

    private void SettingsNavigation_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_navigationReady || SettingsContent is null || SettingPages.SelectedItem is not SegmentOption radioButton)
            return;

        // Settings sub-pages stay in the nested Settings namespace.
        var settingsSubPagesNamespace = typeof(WhWzSettings).Namespace;
        var typeName = $"{settingsSubPagesNamespace}.{radioButton.Tag}";
        var type = Type.GetType(typeName);
        if (type == null || !typeof(UserControl).IsAssignableFrom(type) || SettingsContent.Content?.GetType() == type)
            return;

        SettingsContent.Content = Pages.Create(type);
    }

    private void SetCheckedSidebarButton(UserControl settingsPage)
    {
        foreach (var child in SettingPages.Items)
        {
            if (child is not SegmentOption radioButton)
                continue;

            if (radioButton.Tag?.ToString() == settingsPage.GetType().Name)
                SettingPages.SelectedItem = radioButton;
        }
    }

    private void DevButton_OnClick(object? sender, RoutedEventArgs e) => Popups.Create<DevToolWindow>().Show();

    public void HideDevelopmentFeatures()
    {
        DevButton.IsVisible = false;
        if (SettingsContent.Content is AppInfo)
            SettingsContent.Content = Pages.Create<AppInfo>();
    }
}

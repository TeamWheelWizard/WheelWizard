using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Pages.Settings;
using WheelWizard.Views.Popups;

namespace WheelWizard.Views.Pages;

public partial class SettingsPage : UserControlBase
{
    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    [Inject]
    private ISettingsSignalBus SettingsSignalBus { get; set; } = null!;

    private IDisposable? _settingsSignalSubscription;

    public SettingsPage()
        : this(new WhWzSettings()) { }

    public SettingsPage(UserControl initialSettingsPage)
    {
        InitializeComponent();
        UpdateTabVisibility();
        _settingsSignalSubscription = SettingsSignalBus.Subscribe(OnSettingChanged);

        DevButton.IsVisible = DevelopmentMode.IsEnabled;

        SettingsContent.Content = initialSettingsPage;
        SetCheckedTopBarButton(initialSettingsPage);
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

        var fallback = new WhWzSettings();
        SettingsContent.Content = fallback;
        SetCheckedTopBarButton(fallback);
    }

    private void TopBarRadio_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton)
            return;

        // Settings sub-pages stay in the nested Settings namespace.
        var settingsSubPagesNamespace = typeof(WhWzSettings).Namespace;
        var typeName = $"{settingsSubPagesNamespace}.{radioButton.Tag}";
        var type = Type.GetType(typeName);
        if (type == null || !typeof(UserControl).IsAssignableFrom(type))
            return;

        if (Activator.CreateInstance(type) is not UserControl instance)
            return;

        SettingsContent.Content = instance;
    }

    private void SetCheckedTopBarButton(UserControl settingsPage)
    {
        foreach (var child in SettingPages.Children)
        {
            if (child is not RadioButton radioButton)
                continue;

            radioButton.IsChecked = radioButton.Tag?.ToString() == settingsPage.GetType().Name;
        }
    }

    private void DevButton_OnClick(object? sender, RoutedEventArgs e) => new DevToolWindow().Show();

    public void HideDevelopmentFeatures()
    {
        DevButton.IsVisible = false;
        if (SettingsContent.Content is AppInfo)
            SettingsContent.Content = new AppInfo();
    }
}

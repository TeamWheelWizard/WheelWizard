using Avalonia.Interactivity;
using WheelWizard.CustomDistributions;
using WheelWizard.Recomp;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Pages.Settings;

public partial class OtherSettings : UserControlBase
{
    private readonly bool _settingsAreDisabled;

    [Inject]
    private ICustomDistributionSingletonService CustomDistributionSingletonService { get; set; } = null!;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    public OtherSettings()
    {
        InitializeComponent();
        _settingsAreDisabled = !SettingsService.DolphinPathsSetupCorrectly();
        DisabledWarningText.IsVisible = _settingsAreDisabled;

        // Recomp can be enabled with only a game image configured. Disable the Dolphin-only
        // controls individually so the recomp switch never becomes trapped behind Dolphin setup.
        LaunchRrOnStartup.IsEnabled = !_settingsAreDisabled;
        DolphinReinstallButton.IsEnabled = !_settingsAreDisabled;
        OpenSaveFolderButton.IsEnabled = !_settingsAreDisabled;
        if (!_settingsAreDisabled)
            LoadSettings();
        ForceLoadSettings();

        // Attach event handlers after loading settings to avoid unwanted triggers
        LaunchRrOnStartup.IsCheckedChanged += ClickLaunchRrOnStartup;
        EnableRecomp.IsCheckedChanged += ClickEnableRecomp;
    }

    private void LoadSettings()
    {
        // Only loads when the settings are not disabled (aka when the paths are set up correctly)
        LaunchRrOnStartup.IsChecked = SettingsService.Get<bool>(SettingsService.LAUNCH_RR_ON_STARTUP);
        OpenSaveFolderButton.IsEnabled = Directory.Exists(PathManager.SaveFolderPath);
    }

    private void ForceLoadSettings()
    {
        // Always loads

        // The recomp only runs where a setup backend exists for it, so elsewhere the whole section stays
        // hidden. The one exception is a Linux Flatpak: Linux is supported, the sandbox is what is not,
        // and a Steam Deck user looking for the option deserves to be told that rather than shown nothing.
        var recompSupported = RecompPlatform.IsSupported;
        var recompBlockedBySandbox = RecompPlatform.IsLinuxFlatpak;
        RecompSectionLabel.IsVisible = recompSupported || recompBlockedBySandbox;
        RecompBorder.IsVisible = recompSupported || recompBlockedBySandbox;
        EnableRecomp.IsEnabled = recompSupported;
        if (recompBlockedBySandbox)
            EnableRecompLabel.TipText = t("helper_text.enable_recomp_flatpak");
        if (recompSupported)
            EnableRecomp.IsChecked = SettingsService.Get<bool>(SettingsService.ENABLE_RECOMP);
    }

    private void ClickLaunchRrOnStartup(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.LAUNCH_RR_ON_STARTUP, LaunchRrOnStartup.IsChecked == true);
    }

    private void ClickEnableRecomp(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.ENABLE_RECOMP, EnableRecomp.IsChecked == true);
    }

    private async void Reinstall_RetroRewind(object sender, RoutedEventArgs e)
    {
        var progressWindow = new ProgressWindow();
        progressWindow.Show();
        await CustomDistributionSingletonService.RetroRewind.ReinstallAsync(progressWindow);
        progressWindow.Close();
    }

    private void OpenSaveFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        FilePickerHelper.OpenFolderInFileManager(PathManager.SaveFolderPath);
    }
}

using Avalonia.Interactivity;
using WheelWizard.CustomDistributions;
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
        RefreshRetroRewindVersion();

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

        // The recomp only ships for Windows, so on every other platform the whole section stays hidden.
        var recompSupported = OperatingSystem.IsWindows();
        RecompSectionLabel.IsVisible = recompSupported;
        RecompBorder.IsVisible = recompSupported;
        if (recompSupported)
            EnableRecomp.IsChecked = SettingsService.Get<bool>(SettingsService.ENABLE_RECOMP);
    }

    private void RefreshRetroRewindVersion()
    {
        var version = CustomDistributionSingletonService.RetroRewind.GetCurrentVersion()?.ToString() ?? t("state.unknown");
        RetroRewindVersionText.Text = t("helper_text.retro_rewind_version", version);
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
        RefreshRetroRewindVersion();
    }

    private void OpenSaveFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        FilePickerHelper.OpenFolderInFileManager(PathManager.SaveFolderPath);
    }
}

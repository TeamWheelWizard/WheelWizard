using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.CustomDistributions;
using WheelWizard.Recomp;
using WheelWizard.Settings;
using WheelWizard.Views.Distributions;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Storage;

namespace WheelWizard.Views.Pages.Settings;

public partial class OtherSettings : UserControl
{
    private readonly bool _settingsAreDisabled;

    private IFilePickerService FilePicker { get; }

    private ICustomDistributionPaths DistributionPaths { get; }

    private ICustomDistributionSingletonService CustomDistributionSingletonService { get; }

    private ISettingsManager SettingsService { get; }

    public OtherSettings(
        IFilePickerService filePicker,
        ICustomDistributionPaths distributionPaths,
        ICustomDistributionSingletonService customDistributionSingletonService,
        ISettingsManager settingsService
    )
    {
        FilePicker = filePicker;
        DistributionPaths = distributionPaths;
        CustomDistributionSingletonService = customDistributionSingletonService;
        SettingsService = settingsService;
        InitializeComponent();
        _settingsAreDisabled = !SettingsService.DolphinPathsSetupCorrectly();
        DisabledWarningText.IsVisible = _settingsAreDisabled;

        // Recomp can be enabled with only a game image configured. Disable the Dolphin-only
        // controls individually so the recomp switch never becomes trapped behind Dolphin setup.
        LaunchRrOnStartup.IsEnabled = !_settingsAreDisabled;
        DolphinReinstallButton.IsEnabled = !_settingsAreDisabled;
        OpenGameFolderButton.IsEnabled = !_settingsAreDisabled && Directory.Exists(DistributionPaths.RootFolderPath);
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
        OpenGameFolderButton.IsEnabled = Directory.Exists(DistributionPaths.RootFolderPath);
        OpenSaveFolderButton.IsEnabled = Directory.Exists(DistributionPaths.SaveFolderPath);
    }

    private void ForceLoadSettings()
    {
        // Always loads

        // The recomp only runs where a setup backend exists for it, so elsewhere the whole section stays hidden.
        var recompSupported = RecompPlatform.IsSupported;
        RecompSectionLabel.IsVisible = recompSupported;
        RecompBorder.IsVisible = recompSupported;
        if (recompSupported)
            EnableRecomp.IsChecked = SettingsService.Get<bool>(SettingsService.ENABLE_RECOMP);
    }

    private void RefreshRetroRewindVersion()
    {
        var version = CustomDistributionSingletonService.RetroRewind.GetCurrentVersion()?.ToString() ?? t("state.unknown");
        RetroRewindVersionText.Text = t("helper_text.installed_version", version);
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
        FilePicker.OpenFolderInFileManager(DistributionPaths.SaveFolderPath);
    }

    private void GameFileFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(DistributionPaths.RootFolderPath))
            return;

        FilePicker.OpenFolderInFileManager(DistributionPaths.RootFolderPath);
    }
}

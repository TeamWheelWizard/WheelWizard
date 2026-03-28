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
        _settingsAreDisabled = !SettingsService.PathsSetupCorrectly();
        DisabledWarningText.IsVisible = _settingsAreDisabled;

        DolphinBorder.IsEnabled = !_settingsAreDisabled;
        if (!_settingsAreDisabled)
            LoadSettings();
        ForceLoadSettings();

        // Attach event handlers after loading settings to avoid unwanted triggers
        DisableForce.IsCheckedChanged += ClickForceWiimote;
        LaunchWithDolphin.IsCheckedChanged += ClickLaunchWithDolphinWindow;
    }

    private void LoadSettings()
    {
        // Only loads when the settings are not disabled (aka when the paths are set up correctly)
        DisableForce.IsChecked = SettingsService.Get<bool>(SettingsService.FORCE_WIIMOTE);
        LaunchWithDolphin.IsChecked = SettingsService.Get<bool>(SettingsService.LAUNCH_WITH_DOLPHIN);
        OpenSaveFolderButton.IsEnabled = Directory.Exists(PathManager.SaveFolderPath);
    }

    private void ForceLoadSettings()
    {
        // Always loads
    }

    private void ClickForceWiimote(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.FORCE_WIIMOTE, DisableForce.IsChecked == true);
    }

    private void ClickLaunchWithDolphinWindow(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.LAUNCH_WITH_DOLPHIN, LaunchWithDolphin.IsChecked == true);
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

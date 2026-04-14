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
    private readonly AspectModeOption[] _aspectOptions =
    [
        new(RrAspectRatioMode.Widescreen16By9, "16:9", "Default mode. Uses regular widescreen."),
        new(RrAspectRatioMode.UltraWide21By9, "21:9", "Applies the 21:9 in-game memory patch and widescreen output."),
        new(RrAspectRatioMode.SuperUltraWide32By9, "32:9", "Applies the 32:9 in-game memory patch and widescreen output."),
        new(RrAspectRatioMode.OpenMatte16By10, "16:10 (4:3 Open Matte)", "Applies open matte patching and forces Wii 4:3."),
    ];

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
        RrAspectRatioDropdown.SelectionChanged += RrAspectRatioDropdown_OnSelectionChanged;
        LaunchRrOnStartup.IsCheckedChanged += ClickLaunchRrOnStartup;
    }

    private void LoadSettings()
    {
        // Only loads when the settings are not disabled (aka when the paths are set up correctly)
        DisableForce.IsChecked = SettingsService.Get<bool>(SettingsService.FORCE_WIIMOTE);
        LaunchWithDolphin.IsChecked = SettingsService.Get<bool>(SettingsService.LAUNCH_WITH_DOLPHIN);
        LaunchRrOnStartup.IsChecked = SettingsService.Get<bool>(SettingsService.LAUNCH_RR_ON_STARTUP);
        OpenSaveFolderButton.IsEnabled = Directory.Exists(PathManager.SaveFolderPath);
    }

    private void ForceLoadSettings()
    {
        RrAspectRatioDropdown.Items.Clear();
        foreach (var option in _aspectOptions)
        {
            RrAspectRatioDropdown.Items.Add(option.Label);
        }

        var currentMode = (RrAspectRatioMode)SettingsManager.RR_ASPECT_RATIO.Get();
        var selectedIndex = Array.FindIndex(_aspectOptions, option => option.Mode == currentMode);
        if (selectedIndex < 0)
            selectedIndex = 0;
        RrAspectRatioDropdown.SelectedIndex = selectedIndex;
        RrAspectRatioDescription.Text = _aspectOptions[selectedIndex].Description;
    }

    private void ClickForceWiimote(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.FORCE_WIIMOTE, DisableForce.IsChecked == true);
    }

    private void ClickLaunchWithDolphinWindow(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.LAUNCH_WITH_DOLPHIN, LaunchWithDolphin.IsChecked == true);
    }

    private void ClickLaunchRrOnStartup(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.LAUNCH_RR_ON_STARTUP, LaunchRrOnStartup.IsChecked == true);
    }

    private void RrAspectRatioDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedIndex = RrAspectRatioDropdown.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _aspectOptions.Length)
            return;

        var selectedOption = _aspectOptions[selectedIndex];
        SettingsManager.RR_ASPECT_RATIO.Set(selectedOption.Mode);
        RrAspectRatioDescription.Text = selectedOption.Description;
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

    private sealed record AspectModeOption(RrAspectRatioMode Mode, string Label, string Description);
}

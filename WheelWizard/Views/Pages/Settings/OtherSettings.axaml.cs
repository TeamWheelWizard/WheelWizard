using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.CustomDistributions;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Services.Installation;
using WheelWizard.Services.Settings;
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

    public OtherSettings()
    {
        InitializeComponent();
        _settingsAreDisabled = !SettingsHelper.PathsSetupCorrectly();
        DisabledWarningText.IsVisible = _settingsAreDisabled;

        DolphinBorder.IsEnabled = !_settingsAreDisabled;
        if (!_settingsAreDisabled)
            LoadSettings();
        ForceLoadSettings();

        // Attach event handlers after loading settings to avoid unwanted triggers
        DisableForce.IsCheckedChanged += ClickForceWiimote;
        LaunchWithDolphin.IsCheckedChanged += ClickLaunchWithDolphinWindow;
        RrAspectRatioDropdown.SelectionChanged += RrAspectRatioDropdown_OnSelectionChanged;
    }

    private void LoadSettings()
    {
        // Only loads when the settings are not disabled (aka when the paths are set up correctly)
        DisableForce.IsChecked = (bool)SettingsManager.FORCE_WIIMOTE.Get();
        LaunchWithDolphin.IsChecked = (bool)SettingsManager.LAUNCH_WITH_DOLPHIN.Get();
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
        SettingsManager.FORCE_WIIMOTE.Set(DisableForce.IsChecked == true);
    }

    private void ClickLaunchWithDolphinWindow(object? sender, RoutedEventArgs e)
    {
        SettingsManager.LAUNCH_WITH_DOLPHIN.Set(LaunchWithDolphin.IsChecked == true);
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

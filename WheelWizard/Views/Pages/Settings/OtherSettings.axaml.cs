using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Helpers;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Installation;
using WheelWizard.Services.Settings;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Pages.Settings;

public partial class OtherSettings : UserControl
{
    private readonly bool _settingsAreDisabled;

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
        InGameLanguageDropdown.SelectionChanged += RrLanguageDropdown_OnSelectionChanged;
        WhWzLanguageDropdown.SelectionChanged += WhWzLanguageDropdown_OnSelectionChanged;
    }

    private void LoadSettings()
    {
        DisableForce.IsChecked = (bool)SettingsManager.FORCE_WIIMOTE.Get();
        LaunchWithDolphin.IsChecked = (bool)SettingsManager.LAUNCH_WITH_DOLPHIN.Get();
    }

    private void ForceLoadSettings()
    {
        // -----------------
        // Retro Rewind Language Dropdown
        // -----------------
        InGameLanguageDropdown.Items.Clear(); // Clear existing items
        foreach (var lang in SettingValues.RrLanguages.Values)
        {
            InGameLanguageDropdown.Items.Add(lang());
        }

        var currentRrLanguage = (int)SettingsManager.RR_LANGUAGE.Get();
        var rrLanguageDisplayName = SettingValues.RrLanguages[currentRrLanguage];
        InGameLanguageDropdown.SelectedItem = rrLanguageDisplayName();

        // -----------------
        // Wheel Wizard Language Dropdown
        // -----------------
        WhWzLanguageDropdown.Items.Clear(); // Clear existing items
        foreach (var lang in SettingValues.WhWzLanguages.Values)
        {
            WhWzLanguageDropdown.Items.Add(lang());
        }

        var currentWhWzLanguage = (string)SettingsManager.WW_LANGUAGE.Get();
        var whWzLanguageDisplayName = SettingValues.WhWzLanguages[currentWhWzLanguage];
        WhWzLanguageDropdown.SelectedItem = whWzLanguageDisplayName();

        if (WheelWizard.Resources.Languages.Settings.CompletePercentage != "100")
        {
            TranslationsPercentageText.IsVisible = true;
            var percentage = WheelWizard.Resources.Languages.Settings.CompletePercentage;
            var phrase = Phrases.Text_WhWzTranslationPercentage;
            TranslationsPercentageText.Text = Humanizer.ReplaceDynamic(phrase, percentage);
        }
    }

    private void ClickForceWiimote(object? sender, RoutedEventArgs e)
    {
        SettingsManager.FORCE_WIIMOTE.Set(DisableForce.IsChecked == true);
    }

    private void ClickLaunchWithDolphinWindow(object? sender, RoutedEventArgs e)
    {
        SettingsManager.LAUNCH_WITH_DOLPHIN.Set(LaunchWithDolphin.IsChecked == true);
    }

    private void RrLanguageDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (InGameLanguageDropdown.SelectedItem == null) return;
        
        var selectedLanguage = InGameLanguageDropdown.SelectedItem.ToString();
        var key = SettingValues.RrLanguages.FirstOrDefault(x => x.Value() == selectedLanguage).Key;
        SettingsManager.RR_LANGUAGE.Set(key);
    }

    private async void WhWzLanguageDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (WhWzLanguageDropdown.SelectedItem == null) return;
        
        var selectedLanguage = WhWzLanguageDropdown.SelectedItem.ToString();
        var key = SettingValues.WhWzLanguages.FirstOrDefault(x => x.Value() == selectedLanguage).Key;

        var currentLanguage = (string)SettingsManager.WW_LANGUAGE.Get();
        if (key == null || key == currentLanguage)
            return;

        var yesNoWindow = await new YesNoWindow()
            .SetMainText("Do you want to apply the new language settings?")
            .SetExtraText("This will close the current window and open a new one with the new language settings.")
            .SetButtonText(Common.Action_Apply, Common.Action_Cancel).AwaitAnswer();
        
        if (!yesNoWindow)
        {
            var currentWhWzLanguage = (string)SettingsManager.WW_LANGUAGE.Get();
            var whWzLanguageDisplayName = SettingValues.WhWzLanguages[currentWhWzLanguage];
            WhWzLanguageDropdown.SelectedItem = whWzLanguageDisplayName;
            return; // We only want to change the setting if we really apply this change
        }

        SettingsManager.WW_LANGUAGE.Set(key);
        ViewUtils.RefreshWindow();
    }

    private async void Reinstall_RetroRewind(object sender, RoutedEventArgs e) => await RetroRewindInstaller.ReinstallRR();
}

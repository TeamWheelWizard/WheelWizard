using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;

namespace WheelWizard.Settings.Views;

public partial class AppearanceSettings : UserControl
{
    private ISettingsManager SettingsService { get; }

    private readonly IMainWindowService _mainWindow;
    private readonly ISettingsLocalizationService LocalizationService;
    private readonly bool _pageLoaded;
    private bool _editingScale;
    private bool _updatingLanguageDropdown;

    private sealed record LanguageDropdownItem(string Key, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public AppearanceSettings(
        ISettingsManager settingsService,
        ISettingsLocalizationService localizationService,
        IMainWindowService mainWindow
    )
    {
        SettingsService = settingsService;
        LocalizationService = localizationService;
        _mainWindow = mainWindow;
        InitializeComponent();
        EnableAnimations.IsChecked = SettingsService.ENABLE_ANIMATIONS.Get();
        LoadSettings();
        _pageLoaded = true;
        WhWzLanguageDropdown.SelectionChanged += WhWzLanguageDropdown_OnSelectionChanged;
    }

    private void EnableAnimations_OnClick(object sender, RoutedEventArgs e) =>
        SettingsEditing.Set(SettingsService, SettingsService.ENABLE_ANIMATIONS, EnableAnimations.IsChecked == true);

    private void LoadSettings()
    {
        // -----------------
        // Wheel Wizard Language Dropdown
        // -----------------
        RefreshLanguageDropdown();
        RefreshTranslationCredit();

        // -----------------
        // Window Scale settings
        // -----------------
        // IMPORTANT: Make sure that the number and percentage is always the last word in the string,
        // If you don't want this, you should change the code below that parses the string back to an actual value

        foreach (var scale in SettingValues.WindowScales)
        {
            WindowScaleDropdown.Items.Add(ScaleToString(scale));
        }

        var selectedItemText = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
        if (!WindowScaleDropdown.Items.Contains(selectedItemText))
            WindowScaleDropdown.Items.Add(selectedItemText);
        WindowScaleDropdown.SelectedItem = selectedItemText;
    }

    private void RefreshLanguageDropdown()
    {
        var currentWhWzLanguage = (string)SettingsService.WW_LANGUAGE.Get();
        _updatingLanguageDropdown = true;
        try
        {
            WhWzLanguageDropdown.Items.Clear();
            foreach (var (key, displayNameFactory) in SettingValues.WhWzLanguages)
            {
                WhWzLanguageDropdown.Items.Add(new LanguageDropdownItem(key, displayNameFactory()));
            }

            WhWzLanguageDropdown.SelectedItem = WhWzLanguageDropdown
                .Items.OfType<LanguageDropdownItem>()
                .FirstOrDefault(item => item.Key == currentWhWzLanguage);
        }
        finally
        {
            _updatingLanguageDropdown = false;
        }
    }

    private void RefreshTranslationCredit()
    {
        TranslationsPercentageText.Text = t("text.language_translated_by", new { translators = t("value.language.z_translators") });
        TranslationsPercentageText.IsVisible = t("value.language.z_translators") != "-";
    }

    private static string ScaleToString(double scale)
    {
        var percentageString = (int)Math.Round(scale * 100) + "%";
        if (SettingValues.WindowScales.Contains(scale))
            return percentageString;

        return t("state.custom") + ": " + percentageString;
    }

    private async void WindowScaleDropdown_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pageLoaded || _editingScale)
            return;

        _editingScale = true;
        var selectedScale = WindowScaleDropdown.SelectedItem?.ToString() ?? "1";
        var scale = double.Parse(selectedScale.Split(" ").Last().Replace("%", "")) / 100;
        scale = ViewUtils.GetUsableWindowScale(scale, new Avalonia.Size(Layout.WindowWidth, Layout.WindowHeight), ViewUtils.GetLayout());
        var selectedItemText = ScaleToString(scale);
        if (!WindowScaleDropdown.Items.Contains(selectedItemText))
            WindowScaleDropdown.Items.Add(selectedItemText);
        WindowScaleDropdown.SelectedItem = selectedItemText;

        if (!SettingsEditing.Set(SettingsService, SettingsService.WINDOW_SCALE, scale))
        {
            WindowScaleDropdown.SelectedItem = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
            _editingScale = false;
            return;
        }
        var seconds = 10;

        string ExtraScaleText() => t("question.apply_scale.extra", new { remainingTime = tTime(seconds) });

        var yesNoWindow = new YesNoWindow()
            .SetButtonText(t("action.apply"), t("action.revert"))
            .SetMainText(t("question.apply_scale.title"))
            .SetExtraText(ExtraScaleText());
        // we want to now set up a timer every second to update the text, and at the last second close the window
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        timer.Tick += (_, args) =>
        {
            seconds--;
            yesNoWindow.SetExtraText(ExtraScaleText());
            if (seconds != 0)
                return;
            yesNoWindow.Close();
            timer.Stop();
        };
        timer.Start();

        var yesNoAnswer = await yesNoWindow.AwaitAnswer();
        if (yesNoAnswer)
            SettingsEditing.Set(SettingsService, SettingsService.SAVED_WINDOW_SCALE, SettingsService.WINDOW_SCALE.Value);
        else
        {
            SettingsService.WINDOW_SCALE.Set(SettingsService.SAVED_WINDOW_SCALE.Get());
            WindowScaleDropdown.SelectedItem = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
        }

        _editingScale = false;
    }

    private async void WhWzLanguageDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingLanguageDropdown)
            return;

        if (WhWzLanguageDropdown.SelectedItem == null)
            return;

        if (WhWzLanguageDropdown.SelectedItem is not LanguageDropdownItem selectedLanguage)
            return;

        var currentLanguage = (string)SettingsService.WW_LANGUAGE.Get();
        if (selectedLanguage.Key == currentLanguage)
            return;

        var titleCurrent = t($"{currentLanguage}.question.apply_language_settings.title");
        var titleTarget = t($"{selectedLanguage.Key}.question.apply_language_settings.title");

        var extraCurrent = t($"{currentLanguage}.question.apply_language_settings.extra");
        var extraTarget = t($"{selectedLanguage.Key}.question.apply_language_settings.extra");

        // popup now shows its selection in both languages
        var yesNoWindow = await new YesNoWindow()
            .SetMainText($"{titleCurrent}\n\n{titleTarget}")
            .SetExtraText($"{extraCurrent}\n\n{extraTarget}")
            .SetButtonText(t("action.apply"), t("action.cancel"))
            .AwaitAnswer();

        if (!yesNoWindow)
        {
            var currentWhWzLanguage = (string)SettingsService.WW_LANGUAGE.Get();
            WhWzLanguageDropdown.SelectedItem = WhWzLanguageDropdown
                .Items.OfType<LanguageDropdownItem>()
                .FirstOrDefault(item => item.Key == currentWhWzLanguage);
            return; // We only want to change the setting if we really apply this change
        }

        if (SettingsEditing.Set(SettingsService, SettingsService.WW_LANGUAGE, selectedLanguage.Key))
        {
            LocalizationService.ApplyCurrentLanguage();
            RefreshLanguageDropdown();
            RefreshTranslationCredit();
            _mainWindow.Refresh();
        }
    }
}

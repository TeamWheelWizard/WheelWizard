using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Settings;
using WheelWizard.Settings.Domain;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;

namespace WheelWizard.Views.Pages.Settings;

public partial class VideoSettings : UserControlBase
{
    private readonly bool _settingsAreDisabled;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    public VideoSettings()
    {
        InitializeComponent();
        _settingsAreDisabled = !SettingsService.PathsSetupCorrectly();
        DisabledWarningText.IsVisible = _settingsAreDisabled;
        VideoBorder.IsEnabled = !_settingsAreDisabled;

        if (!_settingsAreDisabled)
            LoadSettings();
        ForceLoadSettings();

        // Attach event handlers after loading settings to avoid unwanted triggers
        foreach (RadioButton rb in ResolutionStackPanel.Children)
        {
            rb.Checked += UpdateResolution;
        }

        VSyncButton.IsCheckedChanged += VSync_OnClick;
        RecommendedButton.IsCheckedChanged += Recommended_OnClick;
        ShowFPSButton.IsCheckedChanged += ShowFPS_OnClick;
        RemoveBlurButton.IsCheckedChanged += RemoveBlur_OnClick;
        RendererDropdown.SelectionChanged += RendererDropdown_OnSelectionChanged;
    }

    private void LoadSettings()
    {
        // Load settings that are enabled for editing
        VSyncButton.IsChecked = SettingsService.Get<bool>(SettingsService.VSYNC);
        RecommendedButton.IsChecked = SettingsService.Get<bool>(SettingsService.RECOMMENDED_SETTINGS);
        ShowFPSButton.IsChecked = SettingsService.Get<bool>(SettingsService.SHOW_FPS);
        RemoveBlurButton.IsChecked = SettingsService.Get<bool>(SettingsService.REMOVE_BLUR);

        var finalResolution = SettingsService.Get<int>(SettingsService.INTERNAL_RESOLUTION);
        foreach (RadioButton radioButton in ResolutionStackPanel.Children)
        {
            radioButton.IsChecked = (radioButton.Tag.ToString() == finalResolution.ToString());
        }
    }

    private void ForceLoadSettings()
    {
        // Load settings that always display, regardless of editing being enabled
        foreach (var renderer in SettingValues.GFXRenderers.Keys)
        {
            RendererDropdown.Items.Add(renderer);
        }

        var currentRenderer = SettingsService.Get<string>(SettingsService.GFX_BACKEND);
        var renderDisplayName = SettingValues.GFXRenderers.FirstOrDefault(x => x.Value == currentRenderer).Key;
        if (renderDisplayName != null)
        {
            RendererDropdown.SelectedItem = renderDisplayName;
        }
    }

    private void UpdateResolution(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radioButton && radioButton.IsChecked == true)
        {
            SettingsService.Set(SettingsService.INTERNAL_RESOLUTION, int.Parse(radioButton.Tag.ToString()!));
        }
    }

    private void VSync_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.VSYNC, VSyncButton.IsChecked == true);
    }

    private void Recommended_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.RECOMMENDED_SETTINGS, RecommendedButton.IsChecked == true);
    }

    private void ShowFPS_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.SHOW_FPS, ShowFPSButton.IsChecked == true);
    }

    private void RemoveBlur_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.REMOVE_BLUR, RemoveBlurButton.IsChecked == true);
    }

    private void RendererDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedDisplayName = RendererDropdown.SelectedItem?.ToString();
        if (SettingValues.GFXRenderers.TryGetValue(selectedDisplayName, out var actualValue))
        {
            SettingsService.Set(SettingsService.GFX_BACKEND, actualValue);
        }
        else
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_UnkownRendererSelected, null, [selectedDisplayName]);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models.Settings;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Pages.Settings;

public partial class VideoSettings : UserControl
{
    private readonly bool _settingsAreDisabled;

    public VideoSettings()
    {
        InitializeComponent();
        _settingsAreDisabled = !SettingsHelper.PathsSetupCorrectly();
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
        VSyncButton.IsChecked = (bool)SettingsManager.VSYNC.Get();
        RecommendedButton.IsChecked = (bool)SettingsManager.RECOMMENDED_SETTINGS.Get();
        ShowFPSButton.IsChecked = (bool)SettingsManager.SHOW_FPS.Get();
        RemoveBlurButton.IsChecked = (bool)SettingsManager.REMOVE_BLUR.Get();

        var finalResolution = (int)SettingsManager.INTERNAL_RESOLUTION.Get();
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

        var currentRenderer = (string)SettingsManager.GFX_BACKEND.Get();
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
            SettingsManager.INTERNAL_RESOLUTION.Set(int.Parse(radioButton.Tag.ToString()!));
        }
    }

    private void VSync_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsManager.VSYNC.Set(VSyncButton.IsChecked == true);
    }

    private void Recommended_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsManager.RECOMMENDED_SETTINGS.Set(RecommendedButton.IsChecked == true);
    }

    private void ShowFPS_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsManager.SHOW_FPS.Set(ShowFPSButton.IsChecked == true);
    }

    private void RemoveBlur_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsManager.REMOVE_BLUR.Set(RemoveBlurButton.IsChecked == true);
    }

    private void RendererDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedDisplayName = RendererDropdown.SelectedItem?.ToString();
        if (SettingValues.GFXRenderers.TryGetValue(selectedDisplayName, out var actualValue))
        {
            SettingsManager.GFX_BACKEND.Set(actualValue);
        }
        else
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_UnkownRendererSelected, null, [selectedDisplayName]);
        }
    }
}

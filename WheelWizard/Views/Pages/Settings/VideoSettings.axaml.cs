using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;

namespace WheelWizard.Views.Pages.Settings;

public partial class VideoSettings : UserControlBase
{
    private static readonly string[] ResolutionOptions =
    [
        "1x (640x528)",
        "2x (1280x1056)",
        "3x (1920x1584)",
        "4x (2560x2112)",
        "5x (3200x2640)",
        "6x (3840x3168)",
        "7x (4480x3696)",
        "8x (5120x4224)",
    ];

    private readonly bool _settingsAreDisabled;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    public VideoSettings()
    {
        InitializeComponent();
        _settingsAreDisabled = !SettingsService.DolphinPathsSetupCorrectly();
        DisabledWarningText.IsVisible = _settingsAreDisabled;
        VideoBorder.IsEnabled = !_settingsAreDisabled;

        foreach (var resolution in ResolutionOptions)
            ResolutionDropdown.Items.Add(resolution);

        if (!_settingsAreDisabled)
            LoadSettings();
        ForceLoadSettings();

        // Attach event handlers after loading settings to avoid unwanted triggers
        ResolutionDropdown.SelectionChanged += ResolutionDropdown_OnSelectionChanged;
        VSyncButton.IsCheckedChanged += VSync_OnClick;
        RecommendedButton.IsCheckedChanged += Recommended_OnClick;
        ShowFPSButton.IsCheckedChanged += ShowFPS_OnClick;
        RendererDropdown.SelectionChanged += RendererDropdown_OnSelectionChanged;
        DisableForce.IsCheckedChanged += ClickForceWiimote;
        LaunchWithDolphin.IsCheckedChanged += ClickLaunchWithDolphinWindow;
    }

    private void ClickForceWiimote(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.FORCE_WIIMOTE, DisableForce.IsChecked == true);
    }

    private void ClickLaunchWithDolphinWindow(object? sender, RoutedEventArgs e)
    {
        SettingsService.Set(SettingsService.LAUNCH_WITH_DOLPHIN, LaunchWithDolphin.IsChecked == true);
    }

    private void LoadSettings()
    {
        // Load settings that are enabled for editing
        VSyncButton.IsChecked = SettingsService.Get<bool>(SettingsService.VSYNC);
        RecommendedButton.IsChecked = SettingsService.Get<bool>(SettingsService.RECOMMENDED_SETTINGS);
        ShowFPSButton.IsChecked = SettingsService.Get<bool>(SettingsService.SHOW_FPS);
        DisableForce.IsChecked = SettingsService.Get<bool>(SettingsService.FORCE_WIIMOTE);
        LaunchWithDolphin.IsChecked = SettingsService.Get<bool>(SettingsService.LAUNCH_WITH_DOLPHIN);

        var resolution = SettingsService.Get<int>(SettingsService.INTERNAL_RESOLUTION);
        ResolutionDropdown.SelectedIndex = resolution is >= 1 and <= 8 ? resolution - 1 : -1;
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

    private void ResolutionDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ResolutionDropdown.SelectedIndex >= 0)
            SettingsService.Set(SettingsService.INTERNAL_RESOLUTION, ResolutionDropdown.SelectedIndex + 1);
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

    private void RendererDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedDisplayName = RendererDropdown.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selectedDisplayName))
            return;

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

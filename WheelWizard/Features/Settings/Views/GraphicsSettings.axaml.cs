using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Settings;

namespace WheelWizard.Settings.Views;

public partial class GraphicsSettings : UserControl
{
    private ISettingsManager SettingsService { get; }

    public GraphicsSettings(ISettingsManager settingsService)
    {
        SettingsService = settingsService;
        InitializeComponent();
        EnableAnimations.IsChecked = SettingsService.ENABLE_ANIMATIONS.Get();
    }

    private void EnableAnimations_OnClick(object sender, RoutedEventArgs e) =>
        SettingsEditing.Set(SettingsService, SettingsService.ENABLE_ANIMATIONS, EnableAnimations.IsChecked == true);
}

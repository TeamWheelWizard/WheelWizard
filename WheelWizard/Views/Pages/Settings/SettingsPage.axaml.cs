using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Branding;
using WheelWizard.CustomDistributions;
using WheelWizard.Services.Installation;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Popups;

namespace WheelWizard.Views.Pages.Settings;

public partial class SettingsPage : UserControlBase
{
    public SettingsPage()
        : this(new WhWzSettings()) { }

    public SettingsPage(UserControl initialSettingsPage)
    {
        InitializeComponent();

#if DEBUG
        DevButton.IsVisible = true;
#endif

        SettingsContent.Content = initialSettingsPage;
    }

    private void TopBarRadio_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton)
            return;

        // As long as the Ks... files are next to this file, it works.
        var namespaceName = GetType().Namespace;
        var typeName = $"{namespaceName}.{radioButton.Tag}";
        var type = Type.GetType(typeName);
        if (type == null || !typeof(UserControl).IsAssignableFrom(type))
            return;

        if (Activator.CreateInstance(type) is not UserControl instance)
            return;

        SettingsContent.Content = instance;
    }

    private void DevButton_OnClick(object? sender, RoutedEventArgs e) => new DevToolWindow().Show();
}

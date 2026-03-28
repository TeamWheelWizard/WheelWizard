using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Views.Pages.Settings;
using WheelWizard.Views.Popups;

namespace WheelWizard.Views.Pages;

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

        // Settings sub-pages stay in the nested Settings namespace.
        var settingsSubPagesNamespace = typeof(WhWzSettings).Namespace;
        var typeName = $"{settingsSubPagesNamespace}.{radioButton.Tag}";
        var type = Type.GetType(typeName);
        if (type == null || !typeof(UserControl).IsAssignableFrom(type))
            return;

        if (Activator.CreateInstance(type) is not UserControl instance)
            return;

        SettingsContent.Content = instance;
    }

    private void DevButton_OnClick(object? sender, RoutedEventArgs e) => new DevToolWindow().Show();
}

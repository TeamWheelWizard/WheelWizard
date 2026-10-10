using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Views.Shell.Navigation;

namespace WheelWizard.Views.DesignTime;

public partial class KitchenSinkPage : UserControl, ILockedSidebarPage
{
    public KitchenSinkPage()
    {
        InitializeComponent();
    }

    private void BackgroundSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (ToggleCard is null)
            return;

        ToggleCard.Classes.Set("BlockBackground900", ((CheckBox)sender!).IsChecked == true);
    }
}

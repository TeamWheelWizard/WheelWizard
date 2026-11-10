using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using WheelWizard.Views.Shell;

namespace WheelWizard.Settings.Views;

public partial class AppInfo : UserControl
{
    public AppInfo() => InitializeComponent();

    private void OpenLick_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not TemplatedControl control)
            return;
        if (control.Tag == null)
            return;

        ViewUtils.OpenLink(control.Tag.ToString()!);
    }
}

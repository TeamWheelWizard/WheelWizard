using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WheelWizard.Views;

namespace WheelWizard.UI.Test;

public class MacApplicationMenuTests
{
    [AvaloniaFact]
    public void DeferredStartup_RegistersApplicationMenuBeforeVisualResourcesLoad()
    {
        var app = new App(() => throw new InvalidOperationException("Startup should not run during initialization."));
        app.Initialize();

        Assert.Equal("Wheel Wizard", app.Name);
        if (!OperatingSystem.IsMacOS())
            return;

        var menu = NativeMenu.GetMenu(app);
        Assert.NotNull(menu);
        Assert.Equal(
            new[] { "About Wheel Wizard…", "Settings…", "GitHub", "Discord", "Support Wheel Wizard" },
            menu.Items.OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).Select(item => item.Header)
        );
    }
}

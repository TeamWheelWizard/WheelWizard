using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(WheelWizard.UI.Test.TestAppBuilder))]

namespace WheelWizard.UI.Test;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Views.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

using Avalonia;

namespace WheelWizard;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        PrintStartUpMessage();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Views.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void PrintStartUpMessage()
    {
        var modeCheck = "release";
        var osCheck = "unknown";

#if DEBUG
        modeCheck = "debug";
#endif

#if WINDOWS
        osCheck = "windows";
#elif LINUX
        osCheck = "linux";
#elif MACOS
        osCheck = "macos";
#endif

        Console.WriteLine($"Application start [mode: {modeCheck}, os: {osCheck}]");
    }
}

using Avalonia;
using WheelWizard.Services.Installation.AutoUpdater;
using WheelWizard.Services.Settings;
using WheelWizard.Services.UrlProtocol;

namespace WheelWizard;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        PrintStartUpMessage();
        Setup();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Views.App>()
                     .UsePlatformDetect()
                     .WithInterFont()
                     .LogToTrace();

    private static void Setup()
    {
        SettingsManager.Instance.LoadSettings();
        UrlProtocolManager.SetWhWzSchemeAsync();
        AutoUpdater.CheckForUpdatesAsync();
    }


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

using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Logging;
using Microsoft.Extensions.Logging;
using WheelWizard.Helpers;
using WheelWizard.Services.Settings;
using WheelWizard.Services.UrlProtocol;
using WheelWizard.Shared.Services;
using WheelWizard.Views;

namespace WheelWizard;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Setup();

        // Start the application
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    // ReSharper disable once MemberCanBePrivate.Global
    public static AppBuilder BuildAvaloniaApp()
        => ConfigureAvaloniaApp(AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
        );

    private static AppBuilder ConfigureAvaloniaApp(AppBuilder builder)
    {
        var services = new ServiceCollection();
        services.AddWheelWizardServices();

        var serviceProvider = services.BuildServiceProvider();

        // Write startup message
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        LogPlatformInformation(logger);

        // Override the default TraceLogSink with our AvaloniaLoggerAdapter
        Logger.Sink = serviceProvider.GetRequiredService<AvaloniaLoggerAdapter>();

        // First, set up the application instance
        builder.AfterSetup(appBuilder =>
        {
            if (appBuilder.Instance is not App app)
                throw new InvalidOperationException("The application instance is not of type App.");

            // Set the service provider in the application instance
            app.SetServiceProvider(serviceProvider);
        });

        return builder;
    }

    private static void SetupWorkingDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && EnvHelper.IsFlatpakSandboxed())
        {
            // In this case, we would not want executable directory-relative paths, since this is in `/app/bin`.
            // We are going to use the home directory instead (this should be the original working directory anyway).
            Environment.CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else
        {
            // Resolve all relative paths based on the WheelWizard executable's directory by default
            var executableDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            Environment.CurrentDirectory = executableDirectory;
        }

        // Enable overriding this base/working directory through the `WW_BASEDIR` environment variable
        // (this can be relative to the default WheelWizard working directory as well).
        // This override also influences the `portable-ww.txt` portability check.
        var whWzBaseDir = Environment.GetEnvironmentVariable("WW_BASEDIR") ?? string.Empty;
        try
        {
            var whWzBaseDirAbsolute = Path.GetFullPath(whWzBaseDir);
            Environment.CurrentDirectory = whWzBaseDirAbsolute;
        }
        catch
        {
            // Keep the default base/working directory
        }
    }

    private static void Setup()
    {
        SetupWorkingDirectory();
        SettingsManager.Instance.LoadSettings();
        UrlProtocolManager.SetWhWzScheme();
    }

    private static void LogPlatformInformation(ILogger logger)
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

        logger.LogInformation("Application start [Configuration: {Configuration}, OS: {OS}]", modeCheck, osCheck);
    }
}

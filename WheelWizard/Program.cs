using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Logging;
using Serilog;
using WheelWizard.Helpers;
using WheelWizard.Services.UrlProtocol;
using WheelWizard.Settings;
using WheelWizard.Shared.Services;
using WheelWizard.Views;

namespace WheelWizard;

// ReSharper disable once ClassNeverInstantiated.Global
public class Program : IDesignerEntryPoint
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Make sure this is the first action on startup!
        SetupWorkingDirectory();

        // Create a static logger instance for the application
        Logging.CreateStaticLogger();

        try
        {
            // Initialize the Avalonia application
            var builder = CreateWheelWizardApp(isDesigner: false);

            // Start the application
            builder.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            Log.Error(e, "Application start failed");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => CreateWheelWizardApp(isDesigner: true);

    /// <summary>
    /// Configures the WheelWizard application.
    /// </summary>
    private static AppBuilder CreateWheelWizardApp(bool isDesigner)
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();

        var services = new ServiceCollection();
        services.AddWheelWizardServices();

        var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Override the default TraceLogSink with our AvaloniaLoggerAdapter
        Logger.Sink = serviceProvider.GetRequiredService<AvaloniaLoggerAdapter>();

        // First, set up the application instance
        builder.AfterSetup(appBuilder =>
        {
            if (appBuilder.Instance is not App app)
                throw new InvalidOperationException("The application instance is not of type App.");

            // Set the service provider in the application instance
            app.SetServiceProvider(serviceProvider);

            // Make sure this comes AFTER setting the service provider
            // of the `App` instance! Otherwise, things like logging will not work
            // in `Setup`.
            Setup(serviceProvider);
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
            var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(executableDirectory))
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

    private static void Setup(IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<ISettingsStartupInitializer>().Initialize();
        UrlProtocolManager.SetWhWzScheme();
    }
}

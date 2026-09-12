using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Logging;
using Serilog;
using WheelWizard.ApplicationData;
using WheelWizard.ApplicationIntegration;
using WheelWizard.Settings;
using WheelWizard.Shared.Platform;
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
        var applicationData = ApplicationDataComposition.CreateLocation(
            new Testably.Abstractions.RealFileSystem(),
            new WheelWizard.Shared.Platform.RuntimeEnvironment()
        );
        Logging.CreateStaticLogger(applicationData.DirectoryPath);
        RegisterGlobalExceptionLogging();

        try
        {
            // Initialize the Avalonia application
            var services = new ServiceCollection();
            services.AddWheelWizardServices(applicationData);
            using var serviceProvider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            );
            var builder = CreateWheelWizardApp(serviceProvider);

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

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();

    private static void RegisterGlobalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled application exception.");
                return;
            }

            Log.Fatal("Unhandled application exception object: {ExceptionObject}", e.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };
    }

    /// <summary>
    /// Configures the WheelWizard application.
    /// </summary>
    private static AppBuilder CreateWheelWizardApp(IServiceProvider services)
    {
        Logger.Sink = services.GetRequiredService<AvaloniaLoggerAdapter>();
        return AppBuilder
            .Configure(() => new App(services.GetRequiredService<WheelWizard.Views.Startup.IDesktopStartup>()))
            .UsePlatformDetect()
            .WithInterFont()
            .AfterSetup(builder =>
            {
                Setup(services);
                services.GetRequiredService<WheelWizard.Views.Patterns.MiiControlThemes>().Install(builder.Instance!.Resources);
                services.GetRequiredService<WindowAppearance>().Install(builder.Instance.Resources);
            });
    }

    private static void SetupWorkingDirectory()
    {
        if (new WheelWizard.Shared.Platform.RuntimeEnvironment().IsFlatpakSandboxed(new Testably.Abstractions.RealFileSystem()))
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
        var registration = serviceProvider.GetRequiredService<IUrlProtocolRegistration>().EnsureRegistered(Environment.ProcessPath);
        if (registration.IsFailure)
            Log.Warning(registration.Error.Exception, "URL protocol registration failed: {Message}", registration.Error.Message);
    }
}

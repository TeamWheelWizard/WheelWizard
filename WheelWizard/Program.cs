using Avalonia;
using Avalonia.Logging;
using Serilog;
using Testably.Abstractions;
using WheelWizard.ApplicationData;
using WheelWizard.ApplicationIntegration;
using WheelWizard.ApplicationLifecycle.Logging;
using WheelWizard.Settings;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Services;
using WheelWizard.Views;
using WheelWizard.Views.Patterns;
using WheelWizard.Views.Startup;

namespace WheelWizard;

// ReSharper disable once ClassNeverInstantiated.Global
public class Program : IDesignerEntryPoint
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Make sure this is the first action on startup!
        SetupWorkingDirectory();

        // Logging and feature paths share the same application-data location.
        var applicationData = ApplicationDataComposition.CreateLocation(new RealFileSystem(), new RuntimeEnvironment());
        var logFiles = new ApplicationLogFiles(applicationData, new LogFileFactory(new RealFileSystem()));
        Log.Logger = CreateLoggerWithRecovery(applicationData, logFiles);
        ApplicationLogging.LogStartup(Log.Logger);
        RegisterGlobalExceptionLogging();

        try
        {
            // Initialize the Avalonia application
            var services = new ServiceCollection();
            services.AddWheelWizardServices(applicationData);
            services.AddSingleton(logFiles);
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

    /// <summary>
    /// Creates the logger, resetting the application data location once when its logs directory is unusable.
    /// </summary>
    private static Serilog.Core.Logger CreateLoggerWithRecovery(IApplicationDataLocation applicationData, ApplicationLogFiles logFiles)
    {
        try
        {
            return ApplicationLogging.CreateLogger(logFiles);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine("Resetting the Wheel Wizard directory due to an error");
            var resetWasSuccessful = applicationData.TryReset(out var errorMessage);
            if (!string.IsNullOrWhiteSpace(errorMessage))
                Console.WriteLine($"Error message recorded when the Wheel Wizard directory was reset: {errorMessage}");
            if (!resetWasSuccessful)
                throw;

            // Retry only once, against the location the reset restored.
            return ApplicationLogging.CreateLogger(logFiles);
        }
    }

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
        var builder = AppBuilder
            .Configure(() => new App(services.GetRequiredService<IDesktopStartup>()))
            .UsePlatformDetect()
            .WithInterFont();

        // https://docs.avaloniaui.net/docs/platform-specific-guides/linux#enabling-the-wayland-backend
        // NOTE: UseWayland() will prevent fallback to X11.
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null)
        {
            builder = builder.UseWayland();
        }

        return builder.AfterSetup(appBuilder =>
        {
            Setup(services);
            services.GetRequiredService<MiiControlThemes>().Install(appBuilder.Instance!.Resources);
            services.GetRequiredService<WindowAppearance>().Install(appBuilder.Instance.Resources);
        });
    }

    private static void SetupWorkingDirectory()
    {
        if (new RuntimeEnvironment().IsFlatpakSandboxed(new RealFileSystem()))
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

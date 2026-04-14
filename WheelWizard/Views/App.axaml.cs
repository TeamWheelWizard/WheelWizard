using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using WheelWizard.AutoUpdating;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Settings;
using WheelWizard.Services.Launcher;
using WheelWizard.Services;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.UrlProtocol;
using WheelWizard.Views.Behaviors;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Views;

public class App : Application
{
    private enum StartupLaunchTarget
    {
        None,
        RetroRewind,
    }

    /// <summary>
    /// Gets the service provider configured for this application.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the application is not initialized yet.</exception>
    public static IServiceProvider Services =>
        (Current as App)?._serviceProvider ?? throw new InvalidOperationException("The application is not initialized yet.");

    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Sets the service provider for this application.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the service provider has already been set.</exception>
    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        if (_serviceProvider != null)
            throw new InvalidOperationException("The service provider has already been set.");

        _serviceProvider = serviceProvider;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeBehaviorOverrides();
    }

    private void InitializeBehaviorOverrides()
    {
        //Behavior overrides are native components where we are overriding the behavior of
        ToolTipBubbleBehavior.Initialize();
    }

    private static string? GetLaunchProtocolArgument()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument.StartsWith("wheelwizard://", StringComparison.OrdinalIgnoreCase))
                return argument;
        }

        return null;
    }

    private static StartupLaunchTarget GetStartupLaunchTarget()
    {
        var args = Environment.GetCommandLineArgs();

        for (var i = 1; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument.Equals("--launch", StringComparison.OrdinalIgnoreCase) || argument.Equals("-l", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                    continue;

                var launchTarget = args[++i];
                if (launchTarget.Equals("rr", StringComparison.OrdinalIgnoreCase) ||
                    launchTarget.Equals("retrorewind", StringComparison.OrdinalIgnoreCase) ||
                    launchTarget.Equals("retro-rewind", StringComparison.OrdinalIgnoreCase))
                {
                    return StartupLaunchTarget.RetroRewind;
                }

                continue;
            }

            if (!argument.StartsWith("--launch=", StringComparison.OrdinalIgnoreCase))
                continue;

            var launchTargetFromEquals = argument["--launch=".Length..].Trim();
            if (launchTargetFromEquals.Equals("rr", StringComparison.OrdinalIgnoreCase) ||
                launchTargetFromEquals.Equals("retrorewind", StringComparison.OrdinalIgnoreCase) ||
                launchTargetFromEquals.Equals("retro-rewind", StringComparison.OrdinalIgnoreCase))
            {
                return StartupLaunchTarget.RetroRewind;
            }
        }

        return StartupLaunchTarget.None;
    }

    private static bool OpenGameBananaModWindow()
    {
        ModManager.Instance.ReloadAsync();
        var protocolArgument = GetLaunchProtocolArgument();
        if (string.IsNullOrWhiteSpace(protocolArgument))
            return false;

        _ = UrlProtocolManager.ShowPopupForLaunchUrlAsync(protocolArgument);
        return true;
    }

    private async void OnInitializedAsync()
    {
        try
        {
            var launchedFromProtocol = OpenGameBananaModWindow();

            var updateService = Services.GetRequiredService<IAutoUpdaterSingletonService>();
            var whWzDataService = Services.GetRequiredService<IWhWzDataSingletonService>();

            await updateService.CheckForUpdatesAsync();
            await whWzDataService.LoadBadgesAsync();
            InitializeManagers();

            var settingsManager = Services.GetRequiredService<ISettingsManager>();
            var requestedByCli = GetStartupLaunchTarget() == StartupLaunchTarget.RetroRewind;
            var shouldLaunchRrOnStartup = !launchedFromProtocol && settingsManager.Get<bool>(settingsManager.LAUNCH_RR_ON_STARTUP);
            if (requestedByCli || shouldLaunchRrOnStartup)
            {
                var rrLauncher = Services.GetRequiredService<RrLauncher>();
                await rrLauncher.Launch();
            }
        }
        catch (Exception e)
        {
            var logger = Services.GetRequiredService<ILogger<App>>();
            logger.LogError(e, "Failed to initialize application: {Message}", e.Message);
        }
    }

    private static void InitializeManagers()
    {
        WhWzStatusManager.Instance.Start();
        RRLiveRooms.Instance.Start();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = InitializeDesktopAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeDesktopAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var resourceInstaller = Services.GetRequiredService<IMiiRenderingResourceInstaller>();
            if (resourceInstaller.GetResolvedResourcePath().IsFailure)
            {
                var setupPopup = new MiiRenderingSetupPopup();
                var shouldContinue = await setupPopup.ShowAndAwaitCompletionAsync();
                if (!shouldContinue)
                {
                    desktop.Shutdown();
                    return;
                }
            }

            var layout = new Layout();
            desktop.MainWindow = layout;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            layout.Show();

            var gameDataService = Services.GetRequiredService<IGameLicenseSingletonService>();
            gameDataService.LoadLicense();
            OnInitializedAsync();
        }
        catch (Exception e)
        {
            var logger = Services.GetRequiredService<ILogger<App>>();
            logger.LogError(e, "Failed to initialize desktop application: {Message}", e.Message);
            desktop.Shutdown();
        }
    }
}

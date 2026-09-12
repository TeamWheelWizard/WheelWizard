using Microsoft.Extensions.Logging;
using WheelWizard.AutoUpdating;
using WheelWizard.GameBanana.InstallRequests;
using WheelWizard.Launching;
using WheelWizard.Mods;
using WheelWizard.Settings;
using WheelWizard.WheelWizardData;

namespace WheelWizard.ApplicationLifecycle;

public interface IApplicationStartup
{
    Task RunAsync(StartupOptions options, CancellationToken cancellationToken = default);
}

public sealed class ApplicationStartup(
    IModManager mods,
    IModInstallRequestHandler installRequests,
    IAutoUpdaterSingletonService updates,
    IWhWzDataSingletonService badges,
    IApplicationLiveUpdates liveUpdates,
    ISettingsManager settings,
    IRetroRewindLaunchService launch,
    ILogger<ApplicationStartup> logger
) : IApplicationStartup
{
    public async Task RunAsync(StartupOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var openedProtocol = !string.IsNullOrWhiteSpace(options.ProtocolArgument);
            if (openedProtocol)
            {
                var reload = await mods.ReloadAsync().WaitAsync(cancellationToken);
                if (reload.IsFailure)
                    logger.LogError(
                        reload.Error.Exception,
                        "Failed to reload mods before opening protocol window: {Message}",
                        reload.Error.Message
                    );
                await installRequests.HandleAsync(options.ProtocolArgument!).WaitAsync(cancellationToken);
            }

            await updates.CheckForUpdatesAsync().WaitAsync(cancellationToken);
            await badges.LoadBadgesAsync().WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            liveUpdates.Start();

            if (options.LaunchRetroRewind || (!openedProtocol && settings.Get<bool>(settings.LAUNCH_RR_ON_STARTUP)))
            {
                var result = await launch.LaunchAsync().WaitAsync(cancellationToken);
                if (result.IsFailure)
                    logger.LogError(result.Error.Exception, "Failed to launch Retro Rewind on startup: {Message}", result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to initialize application: {Message}", exception.Message);
        }
    }
}

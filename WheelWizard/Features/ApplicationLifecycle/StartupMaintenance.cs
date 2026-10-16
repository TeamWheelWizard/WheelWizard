using Microsoft.Extensions.Logging;
using WheelWizard.CustomDistributions;
using WheelWizard.DolphinInstaller;
using WheelWizard.GameFiles;
using WheelWizard.Onboarding;
using WheelWizard.Recomp;

namespace WheelWizard.ApplicationLifecycle;

/// <summary>One-time and every-start housekeeping that has to finish before the first page decides what to show.</summary>
public interface IStartupMaintenance
{
    /// <param name="showStatus">Receives a short status line for slow work, or <see langword="null"/> once it is done; may be called from any thread.</param>
    Task RunAsync(Action<string?> showStatus, CancellationToken cancellationToken = default);
}

public sealed class StartupMaintenance(
    IManagedDolphinService managedDolphin,
    IRetroRewindMigrationService migration,
    IOnboardingService onboarding,
    IGameFilesService gameFiles,
    IServiceProvider services,
    ILogger<StartupMaintenance> logger
) : IStartupMaintenance
{
    public async Task RunAsync(Action<string?> showStatus, CancellationToken cancellationToken = default)
    {
        try
        {
            // A relocated data folder moves the managed Dolphin with it; the settings follow before anything reads them.
            await Task.Run(managedDolphin.SyncSettings, cancellationToken);

            var progress = new InlineProgress(value => showStatus(t("onboarding.rr_moving", new { percent = (int)(value * 100) })));
            var result = await Task.Run(
                () => migration.Migrate(() => showStatus(t("onboarding.rr_moving", new { percent = 0 })), progress),
                cancellationToken
            );
            if (result.IsSuccess && result.Value == RetroRewindMigrationOutcome.Moved)
                ApplyRecompPaths();

            // An extraction interrupted by a crash or power cut is finished or cleaned up before anything looks at it.
            await Task.Run(gameFiles.RecoverInterruptedWork, cancellationToken);

            onboarding.MarkCompletedIfAlreadySetUp();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Startup maintenance failed: {Message}", exception.Message);
        }
        finally
        {
            showStatus(null);
        }
    }

    /// <summary>WiiCompiled's Config.toml still names the old Retro Rewind folder until it is rewritten.</summary>
    private void ApplyRecompPaths()
    {
        var result = services.GetService<IRecompDolphinDataService>()?.ApplyPathsToRecompConfig();
        if (result is { IsFailure: true })
            logger.LogWarning("Could not point WiiCompiled at the moved Retro Rewind: {Message}", result.Error.Message);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

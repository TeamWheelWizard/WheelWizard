using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.AutoUpdating;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Onboarding;
using WheelWizard.Views.Dialogs;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Views.Shell.Startup;

public interface IDesktopStartup
{
    Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, StartupOptions options, CancellationToken cancellationToken);
}

public interface IMiiSetupPresentation
{
    Task<bool> ShowAsync();
}

public sealed class MiiSetupPresentation(IModalFactory popups) : IMiiSetupPresentation
{
    public Task<bool> ShowAsync() => popups.Create<MiiRenderingSetupPopup>().ShowAndAwaitCompletionAsync();
}

public sealed class DesktopStartup(
    IBundleExtractionCleanupService cleanup,
    IMiiRenderingResourceInstaller resources,
    IMiiSetupPresentation setup,
    IMainWindowService windows,
    IGameLicenseSingletonService licenses,
    IApplicationStartup startup,
    IStartupMaintenance maintenance,
    IOnboardingService onboarding,
    IManagedDolphinUpdatePrompt dolphinUpdates,
    ILogger<DesktopStartup> logger
) : IDesktopStartup
{
    public async Task StartAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        StartupOptions options,
        CancellationToken cancellationToken
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = cleanup.CleanupStaleExtractionsAsync();
            var splash = desktop.MainWindow as SplashWindow;
            await maintenance.RunAsync(status => Dispatcher.UIThread.Post(() => splash?.SetStatus(status)), cancellationToken);
            await windows.ShowAsync(desktop, cancellationToken);
            if (resources.GetResolvedResourcePath().IsFailure)
            {
                var shouldContinue = await setup.ShowAsync().WaitAsync(cancellationToken);
                if (!shouldContinue)
                {
                    desktop.Shutdown();
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            licenses.LoadLicense();
            await startup.RunAsync(options, cancellationToken);
            if (!onboarding.IsRequired)
                await dolphinUpdates.OfferUpdateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to initialize desktop application: {Message}", exception.Message);
            desktop.Shutdown();
        }
    }
}

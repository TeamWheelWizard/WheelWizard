using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.AutoUpdating;
using WheelWizard.Onboarding;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Views.Shell.Startup;

public interface IDesktopStartup
{
    Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, StartupOptions options, CancellationToken cancellationToken);
}

public sealed class DesktopStartup(
    IBundleExtractionCleanupService cleanup,
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

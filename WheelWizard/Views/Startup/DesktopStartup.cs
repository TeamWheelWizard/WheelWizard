using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.AutoUpdating;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Views.Startup;

public interface IDesktopStartup
{
    Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, StartupOptions options, CancellationToken cancellationToken);
}

public interface IMiiSetupPresentation
{
    Task<bool> ShowAsync();
}

public sealed class MiiSetupPresentation(IPopupFactory popups) : IMiiSetupPresentation
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
            windows.Show(desktop);
            licenses.LoadLicense();
            await startup.RunAsync(options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to initialize desktop application: {Message}", exception.Message);
            desktop.Shutdown();
        }
    }
}

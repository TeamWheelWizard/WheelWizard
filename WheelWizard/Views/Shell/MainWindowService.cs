using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using WheelWizard.Views.Shell.Startup;

namespace WheelWizard.Views.Shell;

public interface IMainWindowService
{
    Task ShowAsync(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken = default);
}

public sealed class MainWindowService(Func<Layout> createWindow, TimeProvider time) : IMainWindowService
{
    /// <summary>Reveals initial content when ready, or after a 10-second recovery limit; shutdown still cancels startup.</summary>
    public async Task ShowAsync(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var splash = desktop.MainWindow as Startup.SplashWindow;
        var window = createWindow();
        desktop.MainWindow = window;
        var decorations = window.WindowDecorations;
        var transparency = window.TransparencyLevelHint;
        window.WindowDecorations = WindowDecorations.None;
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.Opacity = 0;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.IsHitTestVisible = false;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Show();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        try
        {
            await window.WaitForInitialContentAsync(waiting.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Serilog.Log.Warning("Initial content is still loading after 10 seconds; revealing the main window");
        }
        cancellationToken.ThrowIfCancellationRequested();
        window.WindowDecorations = decorations;
        window.TransparencyLevelHint = transparency;
        window.Opacity = 1;
        window.ShowInTaskbar = true;
        window.IsHitTestVisible = true;
        window.ShowActivated = true;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        splash?.Close();
        window.Activate();
    }
}

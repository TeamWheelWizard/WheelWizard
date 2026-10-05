using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace WheelWizard.Views;

public interface IMainWindowService
{
    Task ShowAsync(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken = default);
    void Refresh();
}

public sealed class MainWindowService(Func<Layout> createWindow, TimeProvider time) : IMainWindowService
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    /// <summary>Reveals initial content when ready, or after a 10-second recovery limit; shutdown still cancels startup.</summary>
    public async Task ShowAsync(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _desktop = desktop;
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

    public void Refresh()
    {
        if (_desktop?.MainWindow is not Layout oldWindow)
            return;

        var newWindow = createWindow();
        newWindow.Position = oldWindow.Position;
        var shutdownMode = _desktop.ShutdownMode;
        _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            _desktop.MainWindow = newWindow;
            newWindow.Show();
            oldWindow.Close();
            newWindow.UpdatePlayerAndRoomCount();
            newWindow.UpdateLiveAlert();
        }
        catch
        {
            _desktop.MainWindow = oldWindow;
            newWindow.Close();
            throw;
        }
        finally
        {
            _desktop.ShutdownMode = shutdownMode;
        }
    }
}

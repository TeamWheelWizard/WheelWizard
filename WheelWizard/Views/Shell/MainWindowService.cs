using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace WheelWizard.Views.Shell;

public interface IMainWindowService
{
    Task ShowAsync(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken = default);
}

public sealed class MainWindowService(Func<Layout> createWindow, TimeProvider time) : IMainWindowService
{
    /// <summary>Shows the shell with loading content until ready, or until the 10-second recovery limit.</summary>
    public async Task ShowAsync(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = createWindow();
        desktop.MainWindow = window;
        window.SetStartupLoading(true);
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        try
        {
            await window.WaitForInitialContentAsync(waiting.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Serilog.Log.Warning("Initial content is still loading after 10 seconds; revealing the main content");
        }
        cancellationToken.ThrowIfCancellationRequested();
        window.SetStartupLoading(false);
        window.Activate();
    }
}

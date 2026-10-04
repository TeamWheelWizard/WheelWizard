using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace WheelWizard.Views;

public interface IMainWindowService
{
    void Show(IClassicDesktopStyleApplicationLifetime desktop);
    void Refresh();
}

public sealed class MainWindowService(Func<Layout> createWindow) : IMainWindowService
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public void Show(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        desktop.MainWindow = createWindow();
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.MainWindow.Show();
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

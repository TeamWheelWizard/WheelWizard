using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Serilog;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.Views.Behaviors;
using WheelWizard.Views.Startup;

namespace WheelWizard.Views;

public class App : Application
{
    private readonly Func<Task<IDesktopStartup>>? _createStartup;

    /// <summary>Loads visual resources for the Avalonia previewer and headless UI tests.</summary>
    public App() { }

    public App(Func<Task<IDesktopStartup>> createStartup) => _createStartup = createStartup;

    public override void Initialize()
    {
        if (_createStartup is null)
            LoadVisualResources();
    }

    internal void LoadVisualResources()
    {
        AvaloniaXamlLoader.Load(this);
        ToolTipBubbleBehavior.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_createStartup is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new SplashWindow();
            desktop.MainWindow.Show();
            var shutdown = new CancellationTokenSource();
            var cancellationToken = shutdown.Token;
            desktop.Exit += (_, _) =>
            {
                shutdown.Cancel();
                shutdown.Dispose();
            };
            Dispatcher.UIThread.Post(
                async () =>
                {
                    try
                    {
                        var startup = await _createStartup();
                        cancellationToken.ThrowIfCancellationRequested();
                        await startup.StartAsync(desktop, StartupOptions.Parse(desktop.Args ?? []), cancellationToken);
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
                    catch (Exception exception)
                    {
                        Log.Error(exception, "Application start failed");
                        desktop.Shutdown(1);
                    }
                },
                DispatcherPriority.Background
            );
        }
        base.OnFrameworkInitializationCompleted();
    }
}

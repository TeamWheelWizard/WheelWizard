using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.Views.Behaviors;
using WheelWizard.Views.Startup;

namespace WheelWizard.Views;

public class App : Application
{
    private readonly IDesktopStartup? _startup;

    /// <summary>Loads visual resources for the Avalonia previewer and headless UI tests.</summary>
    public App() { }

    public App(IDesktopStartup startup) => _startup = startup;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ToolTipBubbleBehavior.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_startup is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shutdown = new CancellationTokenSource();
            desktop.Exit += (_, _) =>
            {
                shutdown.Cancel();
                shutdown.Dispose();
            };
            _ = _startup.StartAsync(desktop, StartupOptions.Parse(desktop.Args ?? []), shutdown.Token);
        }
        base.OnFrameworkInitializationCompleted();
    }
}

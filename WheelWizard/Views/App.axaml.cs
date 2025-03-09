using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WheelWizard.Services.Installation.AutoUpdater;

namespace WheelWizard.Views;

public class App : Application
{
    /// <summary>
    /// Gets the service provider configured for this application.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the application is not initialized yet.</exception>
    public static IServiceProvider Services =>
        (Current as App)?._serviceProvider ?? throw new InvalidOperationException("The application is not initialized yet.");

    private IServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AutoUpdater.CheckForUpdatesAsync();
            desktop.MainWindow = new Layout();
        }

        var services = new ServiceCollection();
        services.AddWheelWizardServices();

        _serviceProvider = services.BuildServiceProvider();

        base.OnFrameworkInitializationCompleted();
    }
}

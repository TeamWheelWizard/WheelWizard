using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using WheelWizard.AutoUpdating;
using WheelWizard.Services;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.UrlProtocol;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;

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

    /// <summary>
    /// Sets the service provider for this application.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the service provider has already been set.</exception>
    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        if (_serviceProvider != null)
            throw new InvalidOperationException("The service provider has already been set.");

        _serviceProvider = serviceProvider;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static void OpenGameBananaModWindow()
    {
        var args = Environment.GetCommandLineArgs();
        ModManager.Instance.ReloadAsync();
        if (args.Length <= 1)
            return;
        var protocolArgument = args[1];
        _ = UrlProtocolManager.ShowPopupForLaunchUrlAsync(protocolArgument);
    }

    private async void OnInitializedAsync()
    {
        try
        {
            OpenGameBananaModWindow();

            var updateService = Services.GetRequiredService<IAutoUpdaterSingletonService>();
            var whWzDataService = Services.GetRequiredService<IWhWzDataSingletonService>();

            await updateService.CheckForUpdatesAsync();
            await whWzDataService.LoadBadgesAsync();
            InitializeManagers();
        }
        catch (Exception e)
        {
            var logger = Services.GetRequiredService<ILogger<App>>();
            logger.LogError(e, "Failed to initialize application: {Message}", e.Message);
        }
    }

    private static void InitializeManagers()
    {
        WhWzStatusManager.Instance.Start();
        RRLiveRooms.Instance.Start();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Layout();
            var gameDataService = Services.GetRequiredService<IGameLicenseSingletonService>();
            gameDataService.LoadLicense();
            OnInitializedAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

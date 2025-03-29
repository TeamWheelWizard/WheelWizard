using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WheelWizard.AutoUpdating;
using WheelWizard.Services;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Settings;
using WheelWizard.Services.UrlProtocol;
using WheelWizard.Services.WiiManagement.SaveData;
using WheelWizard.WheelWizardData;

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
        var services = new ServiceCollection();
        services.AddWheelWizardServices();

        _serviceProvider = services.BuildServiceProvider();
        
        AvaloniaXamlLoader.Load(this);
    }

    private static void OpenGameBananaModWindow()
    {
        var args = Environment.GetCommandLineArgs();
        ModManager.Instance.ReloadAsync();
        if (args.Length <= 1) return; 
        var protocolArgument = args[1];
        _ = UrlProtocolManager.ShowPopupForLaunchUrlAsync(protocolArgument);
    }
    
    
    private static async void OnInitializedAsync()
    {
        OpenGameBananaModWindow();
            
        try
        {
            var updateService = Services.GetRequiredService<IAutoUpdaterSingletonService>();
            var whWzDataService = Services.GetRequiredService<IWhWzDataSingletonService>();

            await updateService.CheckForUpdatesAsync();
            await whWzDataService.LoadBadgesAsync();
            InitializeManagers();
        }
        catch (Exception e)
        {
            // TODO: Better logging using ILogger<T> and Serilog package
            Console.WriteLine($"Failed to initialize application: {e.Message}");
        }
    }
    
    private static void InitializeManagers()
    {
        WhWzStatusManager.Instance.Start();
        RRLiveRooms.Instance.Start();
        GameDataLoader.Instance.Start();
    }


    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Layout();

            OnInitializedAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

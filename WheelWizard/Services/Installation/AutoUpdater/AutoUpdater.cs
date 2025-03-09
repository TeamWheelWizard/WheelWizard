namespace WheelWizard.Services.Installation.AutoUpdater;

public class AutoUpdater
{
    public const string CurrentVersion = "2.0.1";
    public static async void CheckForUpdatesAsync()
    {
        IUpdaterPlatform autoUpdaterPlatform = new AutoUpdaterFallback();
#if WINDOWS
        autoUpdaterPlatform = new AutoUpdaterWindows();
#elif LINUX
        autoUpdaterPlatform = new AutoUpdaterLinux();
#elif MACOS
        // MacOS updater
#endif
        var updateHandler = new AutoUpdaterHandler(autoUpdaterPlatform);
        
        //todo: how to run this in a background thread?
        await updateHandler.CheckForUpdatesAsync();
    }
}

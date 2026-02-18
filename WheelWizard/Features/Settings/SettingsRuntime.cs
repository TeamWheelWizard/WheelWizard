namespace WheelWizard.Settings;

public static class SettingsRuntime
{
    private static readonly object SyncRoot = new();
    private static ISettingsManager? _current;

    public static ISettingsManager Current
    {
        get
        {
            lock (SyncRoot)
            {
                return _current ?? throw new InvalidOperationException("Settings runtime has not been initialized yet.");
            }
        }
    }

    public static void Initialize(ISettingsManager settingsManager)
    {
        lock (SyncRoot)
        {
            _current = settingsManager;
        }
    }
}

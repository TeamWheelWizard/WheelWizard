namespace WheelWizard.Settings;

// TODO: This file is temporary. 
//  it serves as a bridge for legacy code that cannot get constructor injection (like PathManager or NaviagtionManager).
//  Once those are all one day migrated or removed, we can and MUST remove this Runtime
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

using WheelWizard.Settings.Domain;

namespace WheelWizard.Settings;

// Legacy runtime bridge for static callers that cannot use constructor injection yet.
// Replace usage with injected services:
// 1) Inject `ISettingsManager` into classes that currently read `SettingsRuntime.Current`.
// 2) Inject `ISettingsSignalBus` for signal subscription/publish usage.
// 3) Remove runtime initialization from `SettingsStartupInitializer` after all static callers are gone.
[Obsolete("SettingsRuntime is deprecated. Use constructor injection for ISettingsManager instead.")]
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

[Obsolete("SettingsSignalRuntime is deprecated. Use constructor injection for ISettingsSignalBus instead.")]
public static class SettingsSignalRuntime
{
    private static readonly object SyncRoot = new();
    private static ISettingsSignalBus? _current;
    private static readonly List<Action<ISettingsSignalBus>> PendingInitializers = [];

    public static void Initialize(ISettingsSignalBus signalBus)
    {
        ArgumentNullException.ThrowIfNull(signalBus);

        List<Action<ISettingsSignalBus>> callbacksToRun;
        lock (SyncRoot)
        {
            _current = signalBus;
            callbacksToRun = [.. PendingInitializers];
            PendingInitializers.Clear();
        }

        foreach (var callback in callbacksToRun)
        {
            callback(signalBus);
        }
    }

    public static void OnInitialized(Action<ISettingsSignalBus> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        ISettingsSignalBus? signalBus;
        lock (SyncRoot)
        {
            signalBus = _current;
            if (signalBus == null)
            {
                PendingInitializers.Add(callback);
                return;
            }
        }

        callback(signalBus);
    }

    public static void Publish(Setting setting)
    {
        ISettingsSignalBus? signalBus;
        lock (SyncRoot)
        {
            signalBus = _current;
        }

        signalBus?.Publish(setting);
    }
}

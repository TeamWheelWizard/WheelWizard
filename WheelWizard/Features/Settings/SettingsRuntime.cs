using WheelWizard.Settings.Types;

namespace WheelWizard.Settings;

// Legacy runtime bridge for static callers that cannot use constructor injection yet.
// Replace usage with injected services:
// 1) Inject `ISettingsManager` into classes that currently read `SettingsRuntime.Current`.
// 2) Inject `ISettingsSignalBus` for signal subscription/publish usage.
// 3) Remove runtime initialization from `SettingsStartupInitializer` after all static callers are gone.
[Obsolete("SettingsRuntime is deprecated. Use constructor injection for ISettingsManager instead.")]
public static class SettingsRuntime
{
    private static ISettingsManager? _current;

    public static ISettingsManager Current
    {
        get { return _current ?? throw new InvalidOperationException("Settings runtime has not been initialized yet."); }
    }

    public static void Initialize(ISettingsManager settingsManager)
    {
        _current = settingsManager;
    }
}

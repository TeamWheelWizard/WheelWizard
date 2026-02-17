namespace WheelWizard.Settings;

public sealed class SettingsStartupInitializer(ISettingsManager settingsManager) : ISettingsStartupInitializer
{
    public void Initialize()
    {
        settingsManager.LoadSettings();
    }
}

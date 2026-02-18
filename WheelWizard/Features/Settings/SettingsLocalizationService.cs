using System.Globalization;
using WheelWizard.Models.Settings;

namespace WheelWizard.Settings;

public sealed class SettingsLocalizationService(ISettingsManager settingsManager) : ISettingsLocalizationService, ISettingListener
{
    private readonly object _syncRoot = new();
    private bool _initialized;

    public void Initialize()
    {
        lock (_syncRoot)
        {
            if (_initialized)
                return;

            settingsManager.WW_LANGUAGE.Subscribe(this);
            ApplyCulture();
            _initialized = true;
        }
    }

    public void OnSettingChanged(Setting setting)
    {
        if (setting == settingsManager.WW_LANGUAGE)
            ApplyCulture();
    }

    private void ApplyCulture()
    {
        var newCulture = new CultureInfo(settingsManager.WwLanguage.Get());
        CultureInfo.CurrentCulture = newCulture;
        CultureInfo.CurrentUICulture = newCulture;
    }
}

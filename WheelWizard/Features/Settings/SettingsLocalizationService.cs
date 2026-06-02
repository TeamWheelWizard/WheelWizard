using System.Globalization;
using WheelWizard.Localization;

namespace WheelWizard.Settings;

public sealed class SettingsLocalizationService(
    ISettingsManager settingsManager,
    ISettingsSignalBus settingsSignalBus,
    ILocalizationService localizationService
) : ISettingsLocalizationService
{
    private bool _initialized;
    private IDisposable? _subscription;

    public void Initialize()
    {
        if (_initialized)
            return;

        _subscription = settingsSignalBus.Subscribe(OnSignal);
        ApplyCurrentLanguage();
        _initialized = true;
    }

    private void OnSignal(SettingChangedSignal signal)
    {
        if (signal.Setting == settingsManager.WW_LANGUAGE)
            ApplyCurrentLanguage();
    }

    public void ApplyCurrentLanguage()
    {
        var languageCode = settingsManager.Get<string>(settingsManager.WW_LANGUAGE);
        var newCulture = new CultureInfo(languageCode);
        CultureInfo.DefaultThreadCurrentCulture = newCulture;
        CultureInfo.DefaultThreadCurrentUICulture = newCulture;
        CultureInfo.CurrentCulture = newCulture;
        CultureInfo.CurrentUICulture = newCulture;

        localizationService.SetLanguage(languageCode);
        LocalizationProvider.Use(localizationService);
    }
}

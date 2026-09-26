using System.Globalization;
using WheelWizard.Localization;

namespace WheelWizard.Settings;

public sealed class SettingsLocalizationService(
    ISettingsManager settingsManager,
    ISettingsSignalBus settingsSignalBus,
    ILocalizationService localizationService
) : ISettingsLocalizationService, IDisposable
{
    private bool _initialized;
    private bool _disposed;
    private IDisposable? _subscription;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;

        _subscription = settingsSignalBus.Subscribe(OnSignal);
        ApplyCurrentLanguage();
        _initialized = true;
    }

    private void OnSignal(SettingChangedSignal signal)
    {
        if (!_disposed && signal.Setting == settingsManager.WW_LANGUAGE)
            ApplyCurrentLanguage();
    }

    public void Dispose()
    {
        _disposed = true;
        _subscription?.Dispose();
        _subscription = null;
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

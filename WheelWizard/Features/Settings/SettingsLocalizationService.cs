using System.Globalization;

namespace WheelWizard.Settings;

public sealed class SettingsLocalizationService(ISettingsManager settingsManager, ISettingsSignalBus settingsSignalBus)
    : ISettingsLocalizationService
{
    private bool _initialized;
    private IDisposable? _subscription;

    public void Initialize()
    {
        if (_initialized)
            return;

        _subscription = settingsSignalBus.Subscribe(OnSignal);
        ApplyCulture();
        _initialized = true;
    }

    private void OnSignal(SettingChangedSignal signal)
    {
        if (signal.Setting == settingsManager.WW_LANGUAGE)
            ApplyCulture();
    }

    private void ApplyCulture()
    {
        var newCulture = new CultureInfo(settingsManager.Get<string>(settingsManager.WW_LANGUAGE));
        CultureInfo.CurrentCulture = newCulture;
        CultureInfo.CurrentUICulture = newCulture;
    }
}

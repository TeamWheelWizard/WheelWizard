using System.Globalization;
using CommonResource = WheelWizard.Resources.Languages.Common;
using PhrasesResource = WheelWizard.Resources.Languages.Phrases;
using SettingsResource = WheelWizard.Resources.Languages.Settings;

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
        var newCulture = new CultureInfo(settingsManager.Get<string>(settingsManager.WW_LANGUAGE));
        CultureInfo.DefaultThreadCurrentCulture = newCulture;
        CultureInfo.DefaultThreadCurrentUICulture = newCulture;
        CultureInfo.CurrentCulture = newCulture;
        CultureInfo.CurrentUICulture = newCulture;

        CommonResource.Culture = newCulture;
        PhrasesResource.Culture = newCulture;
        SettingsResource.Culture = newCulture;
    }
}

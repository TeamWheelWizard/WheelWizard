using System.Globalization;
using WheelWizard.Models.Settings;

namespace WheelWizard.Services.Settings;

// This class is meant for all the loose little helper methods regarding settings.
public class SettingsHelper : ISettingListener
{
    private SettingsHelper() { }
    private static readonly SettingsHelper Instance = new();
    
    public static void LoadExtraStuff()
    {
        SettingsManager.WW_LANGUAGE.Subscribe(Instance);
        Instance.OnWheelWizardLanguageChange();
    }
    
    public static bool PathsSetupCorrectly()
    {
        return SettingsManager.USER_FOLDER_PATH.IsValid() &&
               SettingsManager.DOLPHIN_LOCATION.IsValid() &&
               SettingsManager.GAME_LOCATION.IsValid();
    }

    public void OnSettingChanged(Setting setting)
    {
        if (setting == SettingsManager.WW_LANGUAGE)
            OnWheelWizardLanguageChange();
    }
    
    private void OnWheelWizardLanguageChange() {
        var lang = (string)SettingsManager.WW_LANGUAGE.Get();
        var newCulture = new CultureInfo(lang);
        CultureInfo.CurrentCulture = newCulture;
        CultureInfo.CurrentUICulture = newCulture;
    }
}

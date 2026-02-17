using WheelWizard.Models.Settings;

namespace WheelWizard.Settings;

public interface IWhWzSettingManager
{
    void RegisterSetting(WhWzSetting setting);
    void SaveSettings(WhWzSetting invokingSetting);
    void LoadSettings();
}

public interface IDolphinSettingManager
{
    void RegisterSetting(DolphinSetting setting);
    void SaveSettings(DolphinSetting invokingSetting);
    void ReloadSettings();
    void LoadSettings();
}

public interface ITypedSetting<T>
{
    string Name { get; }
    T Get();
    bool Set(T value, bool skipSave = false);
    bool IsValid();
    Setting RawSetting { get; }
}

public interface ISettingsManager
{
    Setting USER_FOLDER_PATH { get; }
    Setting DOLPHIN_LOCATION { get; }
    Setting GAME_LOCATION { get; }
    Setting FORCE_WIIMOTE { get; }
    Setting LAUNCH_WITH_DOLPHIN { get; }
    Setting PREFERS_MODS_ROW_VIEW { get; }
    Setting FOCUSSED_USER { get; }
    Setting ENABLE_ANIMATIONS { get; }
    Setting TESTING_MODE_ENABLED { get; }
    Setting SAVED_WINDOW_SCALE { get; }
    Setting REMOVE_BLUR { get; }
    Setting RR_REGION { get; }
    Setting WW_LANGUAGE { get; }

    Setting NAND_ROOT_PATH { get; }
    Setting LOAD_PATH { get; }
    Setting VSYNC { get; }
    Setting INTERNAL_RESOLUTION { get; }
    Setting SHOW_FPS { get; }
    Setting GFX_BACKEND { get; }
    Setting MACADDRESS { get; }
    Setting WINDOW_SCALE { get; }
    Setting RECOMMENDED_SETTINGS { get; }

    ITypedSetting<string> UserFolderPath { get; }
    ITypedSetting<string> DolphinLocation { get; }
    ITypedSetting<string> GameLocation { get; }
    ITypedSetting<bool> ForceWiimote { get; }
    ITypedSetting<bool> LaunchWithDolphin { get; }
    ITypedSetting<bool> PrefersModsRowView { get; }
    ITypedSetting<int> FocussedUser { get; }
    ITypedSetting<bool> EnableAnimations { get; }
    ITypedSetting<bool> TestingModeEnabled { get; }
    ITypedSetting<double> SavedWindowScale { get; }
    ITypedSetting<bool> RemoveBlur { get; }
    ITypedSetting<string> WwLanguage { get; }
    ITypedSetting<string> MacAddress { get; }

    T Get<T>(Setting setting);
    bool Set<T>(Setting setting, T value, bool skipSave = false);
    bool PathsSetupCorrectly();
    void LoadSettings();
}

public interface ISettingsStartupInitializer
{
    void Initialize();
}

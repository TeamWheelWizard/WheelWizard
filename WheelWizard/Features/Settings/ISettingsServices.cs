using WheelWizard.Settings.Types;

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

public interface IRecompSettingManager
{
    void RegisterSetting(RecompSetting setting);
    void SaveSettings(RecompSetting invokingSetting);
    void ReloadSettings();
    void LoadSettings();

    /// <summary>
    /// Deletes one key from one section of the recomp's <c>Config.toml</c>, leaving every other key,
    /// comment, and ordering untouched. A missing file or key is a no-op.
    /// </summary>
    void RemoveTomlSetting(string section, string settingToRemove);
}

public interface ISettingsProperties
{
    Setting USER_FOLDER_PATH { get; }
    Setting DOLPHIN_LOCATION { get; }
    Setting GAME_LOCATION { get; }
    Setting FORCE_WIIMOTE { get; }
    Setting LAUNCH_WITH_DOLPHIN { get; }
    Setting LAUNCH_RR_ON_STARTUP { get; }
    Setting ENABLE_RECOMP { get; }
    Setting RECOMP_USE_DOLPHIN_DATA { get; }
    Setting RECOMP_COPY_DOLPHIN_NAND { get; }
    Setting PREFERS_MODS_ROW_VIEW { get; }
    Setting USE_PATCHES_SYSTEM { get; }
    Setting FOCUSED_USER { get; }
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
    Setting RECOMP_RESOLUTION_MULTIPLIER { get; }
    Setting RECOMP_GRAPHICS_API { get; }
    Setting RECOMP_SHOW_FPS { get; }
    Setting RECOMP_PREVENT_STUTTERS { get; }
    Setting RECOMP_NAND_ROOT { get; }
}

public interface ISettingsManager : ISettingsProperties
{
    OperationResult<SettingsValidationReport> ValidateCorePathSettings();

    T Get<T>(Setting setting);
    bool Set<T>(Setting setting, T value, bool skipSave = false);
    bool PathsSetupCorrectly();
    bool DolphinPathsSetupCorrectly();

    /// <summary>
    /// Whether WiiCompiled is the active frontend instead of Dolphin/Retro Rewind. This is the single
    /// definition of that mode: it carries the Windows-only guard, so a stale <c>EnableRecomp</c>
    /// flag can never activate recomp behavior on a platform the recomp does not run on.
    /// </summary>
    bool IsRecompModeActive();

    void LoadSettings();
}

public interface ISettingsStartupInitializer
{
    void Initialize();
}

public interface ISettingsLocalizationService
{
    void Initialize();
    void ApplyCurrentLanguage();
}

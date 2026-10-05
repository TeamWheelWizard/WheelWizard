using WheelWizard.Models.Enums;
using WheelWizard.Settings.Types;

namespace WheelWizard.Settings;

public interface IWhWzSettingManager
{
    void RegisterSetting(IWhWzSetting setting);
    void SaveSettings(string configPath, IWhWzSetting invokingSetting);
    void LoadSettings(string configPath);
}

public interface IDolphinSettingManager
{
    void RegisterSetting(IDolphinSetting setting);
    void SaveSettings(string configDirectory, IDolphinSetting invokingSetting);
    void ReloadSettings(string configDirectory);
    void LoadSettings(string configDirectory);
}

public interface IRecompSettingManager
{
    void RegisterSetting(IRecompSetting setting);
    void SaveSettings(string configPath, IRecompSetting invokingSetting);
    void ReloadSettings(string configPath);
    void LoadSettings(string configPath);

    /// <summary>
    /// Deletes one key from one section of the recomp's <c>Config.toml</c>, leaving every other key,
    /// comment, and ordering untouched. A missing file or key is a no-op.
    /// </summary>
    void RemoveTomlSetting(string configPath, string section, string settingToRemove);
}

public interface ISettingsProperties
{
    Setting<string> USER_FOLDER_PATH { get; }
    Setting<string> DOLPHIN_LOCATION { get; }
    Setting<string> GAME_LOCATION { get; }
    Setting<bool> FORCE_WIIMOTE { get; }
    Setting<bool> LAUNCH_WITH_DOLPHIN { get; }
    Setting<bool> LAUNCH_RR_ON_STARTUP { get; }
    Setting<bool> ENABLE_RECOMP { get; }
    Setting<bool> RECOMP_USE_DOLPHIN_DATA { get; }
    Setting<bool> RECOMP_COPY_DOLPHIN_NAND { get; }
    Setting<bool> PREFERS_MODS_ROW_VIEW { get; }
    Setting<bool> USE_PATCHES_SYSTEM { get; }
    Setting<int> FOCUSED_USER { get; }
    Setting<bool> ENABLE_ANIMATIONS { get; }
    Setting<bool> SIDEBAR_COLLAPSED { get; }
    Setting<bool> TESTING_MODE_ENABLED { get; }
    Setting<double> SAVED_WINDOW_SCALE { get; }
    Setting<MarioKartWiiEnums.Regions> RR_REGION { get; }
    Setting<string> WW_LANGUAGE { get; }
    Setting<string> NAND_ROOT_PATH { get; }
    Setting<string> LOAD_PATH { get; }
    Setting<bool> VSYNC { get; }
    Setting<int> INTERNAL_RESOLUTION { get; }
    Setting<bool> SHOW_FPS { get; }
    Setting<string> GFX_BACKEND { get; }
    Setting<string> MACADDRESS { get; }
    Setting<double> WINDOW_SCALE { get; }
    Setting<bool> RECOMMENDED_SETTINGS { get; }
    Setting<double> RECOMP_RESOLUTION_MULTIPLIER { get; }
    Setting<string> RECOMP_GRAPHICS_API { get; }
    Setting<bool> RECOMP_SHOW_FPS { get; }
    Setting<bool> RECOMP_PREVENT_STUTTERS { get; }
    Setting<string> RECOMP_NAND_ROOT { get; }
}

public interface ISettingsManager : ISettingsProperties
{
    OperationResult<SettingsValidationReport> ValidateCorePathSettings();

    T Get<T>(Setting<T> setting);
    bool Set<T>(Setting<T> setting, T value, bool skipSave = false);
    ExtensionConfigurationInfo CheckExtensionConfiguration();
    bool PathsSetupCorrectly();
    bool DolphinPathsSetupCorrectly();

    /// <summary>
    /// Whether WiiCompiled is the active frontend instead of Dolphin/Retro Rewind. This is the single
    /// definition of that mode: it carries the platform guard, so a stale <c>EnableRecomp</c>
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

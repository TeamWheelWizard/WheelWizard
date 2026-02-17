using System.Globalization;
using System.Runtime.InteropServices;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Models.Settings;
using WheelWizard.Services;

namespace WheelWizard.Settings;

public class SettingsManager : ISettingsManager, ISettingListener
{
    private readonly IWhWzSettingManager _whWzSettingManager;
    private readonly IDolphinSettingManager _dolphinSettingManager;

    private readonly Setting _dolphinCompilationMode;
    private readonly Setting _dolphinCompileShadersAtStart;
    private readonly Setting _dolphinSsaa;
    private readonly Setting _dolphinMsaa;

    private bool _hasInitializedLanguageSync;
    private double _internalScale = -1.0;

    public SettingsManager(IWhWzSettingManager whWzSettingManager, IDolphinSettingManager dolphinSettingManager)
    {
        _whWzSettingManager = whWzSettingManager;
        _dolphinSettingManager = dolphinSettingManager;

        DOLPHIN_LOCATION = RegisterWhWz(
            CreateWhWzSetting(typeof(string), "DolphinLocation", "")
                .SetValidation(value =>
                {
                    var pathOrCommand = value as string ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(pathOrCommand))
                        return false;

                    if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            if (PathManager.IsFlatpakDolphinFilePath(pathOrCommand) && !LinuxDolphinInstaller.IsDolphinInstalledInFlatpak())
                            {
                                return false;
                            }
                        }

                        return EnvHelper.IsValidUnixCommand(pathOrCommand);
                    }

                    return FileHelper.FileExists(pathOrCommand);
                })
        );

        USER_FOLDER_PATH = RegisterWhWz(
            CreateWhWzSetting(typeof(string), "UserFolderPath", "")
                .SetValidation(value =>
                {
                    var userFolderPath = value as string ?? string.Empty;
                    if (!FileHelper.DirectoryExists(userFolderPath))
                        return false;

                    string dolphinLocation = Get<string>(DOLPHIN_LOCATION);

                    // We cannot determine the validity of the user folder path in that case
                    if (string.IsNullOrWhiteSpace(dolphinLocation))
                        return true;

                    // If we want to use a split XDG dolphin config,
                    // this only really works as expected if certain conditions are met
                    // (we cannot simply pass `-u` to Dolphin since that would put the `Config` directory
                    // inside the data directory and not use the XDG config directory, leading to two different configs).
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && PathManager.IsLinuxDolphinConfigSplit())
                    {
                        // In this case, Dolphin would use `EMBEDDED_USER_DIR` which is the portable `user` directory
                        // in the current directory (the directory of the WheelWizard executable).
                        // This means a split dolphin user folder and config cannot work...
                        if (FileHelper.DirectoryExists("user"))
                            return false;

                        // The Dolphin executable directory with `portable.txt` case
                        if (FileHelper.FileExists(Path.Combine(PathManager.GetDolphinExeDirectory(), "portable.txt")))
                            return false;

                        // The value of this environment variable would be used instead if it was somehow set
                        const string environmentVariableToAvoid = "DOLPHIN_EMU_USERPATH";

                        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariableToAvoid)))
                            return false;

                        if (dolphinLocation.Contains(environmentVariableToAvoid, StringComparison.Ordinal))
                            return false;

                        // `~/.dolphin-emu` would be used if it exists
                        if (!PathManager.IsFlatpakDolphinFilePath() && FileHelper.DirectoryExists(PathManager.LinuxDolphinLegacyFolderPath))
                            return false;
                    }

                    return true;
                })
        );

        GAME_LOCATION = RegisterWhWz(
            CreateWhWzSetting(typeof(string), "GameLocation", "")
                .SetValidation(value => FileHelper.FileExists(value as string ?? string.Empty))
        );
        FORCE_WIIMOTE = RegisterWhWz(CreateWhWzSetting(typeof(bool), "ForceWiimote", false));
        LAUNCH_WITH_DOLPHIN = RegisterWhWz(CreateWhWzSetting(typeof(bool), "LaunchWithDolphin", false));
        PREFERS_MODS_ROW_VIEW = RegisterWhWz(CreateWhWzSetting(typeof(bool), "PrefersModsRowView", true));
        FOCUSSED_USER = RegisterWhWz(
            CreateWhWzSetting(typeof(int), "FavoriteUser", 0).SetValidation(value => (int)(value ?? -1) >= 0 && (int)(value ?? -1) < 4)
        );

        ENABLE_ANIMATIONS = RegisterWhWz(CreateWhWzSetting(typeof(bool), "EnableAnimations", true));
        TESTING_MODE_ENABLED = RegisterWhWz(CreateWhWzSetting(typeof(bool), "TestingModeEnabled", false));
        SAVED_WINDOW_SCALE = RegisterWhWz(
            CreateWhWzSetting(typeof(double), "WindowScale", 1.0)
                .SetValidation(value => (double)(value ?? -1) >= 0.5 && (double)(value ?? -1) <= 2.0)
        );
        REMOVE_BLUR = RegisterWhWz(CreateWhWzSetting(typeof(bool), "REMOVE_BLUR", true));
        RR_REGION = RegisterWhWz(CreateWhWzSetting(typeof(MarioKartWiiEnums.Regions), "RR_Region", MarioKartWiiEnums.Regions.None));
        WW_LANGUAGE = RegisterWhWz(
            CreateWhWzSetting(typeof(string), "WW_Language", "en")
                .SetValidation(value => SettingValues.WhWzLanguages.ContainsKey((string)value!))
        );

        NAND_ROOT_PATH = RegisterDolphin(
            CreateDolphinSetting(typeof(string), ("Dolphin.ini", "General", "NANDRootPath"), "")
                .SetValidation(value => Directory.Exists(value as string ?? string.Empty))
        );

        LOAD_PATH = RegisterDolphin(
            CreateDolphinSetting(typeof(string), ("Dolphin.ini", "General", "LoadPath"), "")
                .SetValidation(value => Directory.Exists(value as string ?? string.Empty))
        );

        VSYNC = RegisterDolphin(CreateDolphinSetting(typeof(bool), ("GFX.ini", "Hardware", "VSync"), false));
        INTERNAL_RESOLUTION = RegisterDolphin(
            CreateDolphinSetting(typeof(int), ("GFX.ini", "Settings", "InternalResolution"), 1)
                .SetValidation(value => (int)(value ?? -1) >= 0)
        );
        SHOW_FPS = RegisterDolphin(CreateDolphinSetting(typeof(bool), ("GFX.ini", "Settings", "ShowFPS"), false));
        GFX_BACKEND = RegisterDolphin(
            CreateDolphinSetting(typeof(string), ("Dolphin.ini", "Core", "GFXBackend"), SettingValues.GFXRenderers.Values.First())
        );

        // recommended settings
        _dolphinCompilationMode = RegisterDolphin(
            CreateDolphinSetting(
                typeof(DolphinShaderCompilationMode),
                ("GFX.ini", "Settings", "ShaderCompilationMode"),
                DolphinShaderCompilationMode.Default
            )
        );
        _dolphinCompileShadersAtStart = RegisterDolphin(
            CreateDolphinSetting(typeof(bool), ("GFX.ini", "Settings", "WaitForShadersBeforeStarting"), false)
        );
        _dolphinSsaa = RegisterDolphin(CreateDolphinSetting(typeof(bool), ("GFX.ini", "Settings", "SSAA"), false));
        _dolphinMsaa = RegisterDolphin(
            CreateDolphinSetting(typeof(string), ("GFX.ini", "Settings", "MSAA"), "0x00000001")
                .SetValidation(value => (value?.ToString() ?? "") is "0x00000001" or "0x00000002" or "0x00000004" or "0x00000008")
        );

        // Readonly settings
        MACADDRESS = RegisterDolphin(CreateDolphinSetting(typeof(string), ("Dolphin.ini", "General", "WirelessMac"), "02:01:02:03:04:05"));

        WINDOW_SCALE = new VirtualSetting(
            typeof(double),
            value => _internalScale = (double)value!,
            () => _internalScale == -1.0 ? SAVED_WINDOW_SCALE.Get() : _internalScale
        ).SetDependencies(SAVED_WINDOW_SCALE);

        RECOMMENDED_SETTINGS = new VirtualSetting(
            typeof(bool),
            value =>
            {
                var newValue = (bool)value!;
                _dolphinCompilationMode.Set(
                    newValue ? DolphinShaderCompilationMode.HybridUberShaders : DolphinShaderCompilationMode.Default
                );
#if WINDOWS
                _dolphinCompileShadersAtStart.Set(newValue);
#endif
                _dolphinMsaa.Set(newValue ? "0x00000002" : "0x00000001");
                _dolphinSsaa.Set(false);
            },
            () =>
            {
                var value1 = (DolphinShaderCompilationMode)_dolphinCompilationMode.Get();
                var value2 = true;
#if WINDOWS
                value2 = (bool)_dolphinCompileShadersAtStart.Get();
#endif
                var value3 = (string)_dolphinMsaa.Get();
                var value4 = (bool)_dolphinSsaa.Get();
                return !value4 && value2 && value3 == "0x00000002" && value1 == DolphinShaderCompilationMode.HybridUberShaders;
            }
        ).SetDependencies(_dolphinCompilationMode, _dolphinCompileShadersAtStart, _dolphinMsaa, _dolphinSsaa);

        UserFolderPath = new TypedSetting<string>(USER_FOLDER_PATH);
        DolphinLocation = new TypedSetting<string>(DOLPHIN_LOCATION);
        GameLocation = new TypedSetting<string>(GAME_LOCATION);
        ForceWiimote = new TypedSetting<bool>(FORCE_WIIMOTE);
        LaunchWithDolphin = new TypedSetting<bool>(LAUNCH_WITH_DOLPHIN);
        PrefersModsRowView = new TypedSetting<bool>(PREFERS_MODS_ROW_VIEW);
        FocussedUser = new TypedSetting<int>(FOCUSSED_USER);
        EnableAnimations = new TypedSetting<bool>(ENABLE_ANIMATIONS);
        TestingModeEnabled = new TypedSetting<bool>(TESTING_MODE_ENABLED);
        SavedWindowScale = new TypedSetting<double>(SAVED_WINDOW_SCALE);
        RemoveBlur = new TypedSetting<bool>(REMOVE_BLUR);
        WwLanguage = new TypedSetting<string>(WW_LANGUAGE);
        MacAddress = new TypedSetting<string>(MACADDRESS);
    }

    public Setting USER_FOLDER_PATH { get; }
    public Setting DOLPHIN_LOCATION { get; }
    public Setting GAME_LOCATION { get; }
    public Setting FORCE_WIIMOTE { get; }
    public Setting LAUNCH_WITH_DOLPHIN { get; }
    public Setting PREFERS_MODS_ROW_VIEW { get; }
    public Setting FOCUSSED_USER { get; }
    public Setting ENABLE_ANIMATIONS { get; }
    public Setting TESTING_MODE_ENABLED { get; }
    public Setting SAVED_WINDOW_SCALE { get; }
    public Setting REMOVE_BLUR { get; }
    public Setting RR_REGION { get; }
    public Setting WW_LANGUAGE { get; }

    public Setting NAND_ROOT_PATH { get; }
    public Setting LOAD_PATH { get; }
    public Setting VSYNC { get; }
    public Setting INTERNAL_RESOLUTION { get; }
    public Setting SHOW_FPS { get; }
    public Setting GFX_BACKEND { get; }
    public Setting MACADDRESS { get; }
    public Setting WINDOW_SCALE { get; }
    public Setting RECOMMENDED_SETTINGS { get; }

    public ITypedSetting<string> UserFolderPath { get; }
    public ITypedSetting<string> DolphinLocation { get; }
    public ITypedSetting<string> GameLocation { get; }
    public ITypedSetting<bool> ForceWiimote { get; }
    public ITypedSetting<bool> LaunchWithDolphin { get; }
    public ITypedSetting<bool> PrefersModsRowView { get; }
    public ITypedSetting<int> FocussedUser { get; }
    public ITypedSetting<bool> EnableAnimations { get; }
    public ITypedSetting<bool> TestingModeEnabled { get; }
    public ITypedSetting<double> SavedWindowScale { get; }
    public ITypedSetting<bool> RemoveBlur { get; }
    public ITypedSetting<string> WwLanguage { get; }
    public ITypedSetting<string> MacAddress { get; }

    public T Get<T>(Setting setting)
    {
        var value = setting.Get();
        if (value is not T typedValue)
            throw new InvalidOperationException($"Setting '{setting.Name}' does not match expected type '{typeof(T).Name}'.");

        return typedValue;
    }

    public bool Set<T>(Setting setting, T value, bool skipSave = false)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        return setting.Set(value, skipSave);
    }

    public bool PathsSetupCorrectly()
    {
        return USER_FOLDER_PATH.IsValid() && DOLPHIN_LOCATION.IsValid() && GAME_LOCATION.IsValid();
    }

    public void LoadSettings()
    {
        _whWzSettingManager.LoadSettings();
        _dolphinSettingManager.LoadSettings();

        if (_hasInitializedLanguageSync)
            return;

        WW_LANGUAGE.Subscribe(this);
        OnWheelWizardLanguageChange();
        _hasInitializedLanguageSync = true;
    }

    public void OnSettingChanged(Setting setting)
    {
        if (setting == WW_LANGUAGE)
            OnWheelWizardLanguageChange();
    }

    private void OnWheelWizardLanguageChange()
    {
        var newCulture = new CultureInfo(WwLanguage.Get());
        CultureInfo.CurrentCulture = newCulture;
        CultureInfo.CurrentUICulture = newCulture;
    }

    private WhWzSetting CreateWhWzSetting(Type valueType, string name, object defaultValue)
    {
        return new(valueType, name, defaultValue, _whWzSettingManager.SaveSettings);
    }

    private DolphinSetting CreateDolphinSetting(Type valueType, (string, string, string) location, object defaultValue)
    {
        return new(valueType, location, defaultValue, _dolphinSettingManager.SaveSettings);
    }

    private Setting RegisterWhWz(WhWzSetting setting)
    {
        _whWzSettingManager.RegisterSetting(setting);
        return setting;
    }

    private Setting RegisterDolphin(DolphinSetting setting)
    {
        _dolphinSettingManager.RegisterSetting(setting);
        return setting;
    }
}

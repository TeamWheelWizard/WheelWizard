using System.IO.Abstractions;
using System.Runtime.InteropServices;
using WheelWizard.DolphinInstaller;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Services;
using WheelWizard.Settings.Domain;

namespace WheelWizard.Settings;

public class SettingsManager : ISettingsManager
{
    private readonly object _syncRoot = new();
    private readonly IWhWzSettingManager _whWzSettingManager;
    private readonly IDolphinSettingManager _dolphinSettingManager;
    private readonly ILinuxDolphinInstaller _linuxDolphinInstaller;
    private readonly IFileSystem _fileSystem;

    private readonly Setting _dolphinCompilationMode;
    private readonly Setting _dolphinCompileShadersAtStart;
    private readonly Setting _dolphinSsaa;
    private readonly Setting _dolphinMsaa;

    private bool _hasLoadedSettings;
    private double _internalScale = -1.0;

    #region Constructor
    public SettingsManager(
        IWhWzSettingManager whWzSettingManager,
        IDolphinSettingManager dolphinSettingManager,
        ILinuxDolphinInstaller linuxDolphinInstaller,
        IFileSystem fileSystem
    )
    {
        _whWzSettingManager = whWzSettingManager;
        _dolphinSettingManager = dolphinSettingManager;
        _linuxDolphinInstaller = linuxDolphinInstaller;
        _fileSystem = fileSystem;

        #region WhWz settings
        DOLPHIN_LOCATION = RegisterWhWz(
            "DolphinLocation",
            "",
            value =>
            {
                var pathOrCommand = value as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pathOrCommand))
                    return false;

                if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        return EnvHelper.IsValidUnixCommand(pathOrCommand);

                    if (PathManager.IsFlatpakDolphinFilePath(pathOrCommand) && !_linuxDolphinInstaller.IsDolphinInstalledInFlatpak())
                        return false;

                    return EnvHelper.IsValidUnixCommand(pathOrCommand);
                }

                return _fileSystem.File.Exists(pathOrCommand);
            }
        );

        USER_FOLDER_PATH = RegisterWhWz(
            "UserFolderPath",
            "",
            value =>
            {
                var userFolderPath = value as string ?? string.Empty;
                if (!_fileSystem.Directory.Exists(userFolderPath))
                    return false;

                var dolphinLocation = Get<string>(DOLPHIN_LOCATION);

                // We cannot determine the validity of the user folder path in that case
                if (string.IsNullOrWhiteSpace(dolphinLocation))
                    return true;

                // If we want to use a split XDG dolphin config,
                // this only really works as expected if certain conditions are met.
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || !PathManager.IsLinuxDolphinConfigSplit())
                    return true;

                // In this case, Dolphin would use `EMBEDDED_USER_DIR` (portable `user` directory).
                if (_fileSystem.Directory.Exists("user"))
                    return false;

                // The Dolphin executable directory with `portable.txt` case
                if (_fileSystem.File.Exists(Path.Combine(PathManager.GetDolphinExeDirectory(), "portable.txt")))
                    return false;

                // The value of this environment variable would be used instead if it was somehow set
                const string environmentVariableToAvoid = "DOLPHIN_EMU_USERPATH";

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariableToAvoid)))
                    return false;

                if (dolphinLocation.Contains(environmentVariableToAvoid, StringComparison.Ordinal))
                    return false;

                // `~/.dolphin-emu` would be used if it exists
                if (!PathManager.IsFlatpakDolphinFilePath() && _fileSystem.Directory.Exists(PathManager.LinuxDolphinLegacyFolderPath))
                    return false;

                return true;
            }
        );

        GAME_LOCATION = RegisterWhWz("GameLocation", "", value => _fileSystem.File.Exists(value as string ?? string.Empty));
        FORCE_WIIMOTE = RegisterWhWz("ForceWiimote", false);
        LAUNCH_WITH_DOLPHIN = RegisterWhWz("LaunchWithDolphin", false);
        PREFERS_MODS_ROW_VIEW = RegisterWhWz("PrefersModsRowView", true);
        FOCUSED_USER = RegisterWhWz("FavoriteUser", 0, value => (int)(value ?? -1) >= 0 && (int)(value ?? -1) < 4);

        ENABLE_ANIMATIONS = RegisterWhWz("EnableAnimations", true);
        TESTING_MODE_ENABLED = RegisterWhWz("TestingModeEnabled", false);
        SAVED_WINDOW_SCALE = RegisterWhWz("WindowScale", 1.0, value => (double)(value ?? -1) >= 0.5 && (double)(value ?? -1) <= 2.0);
        REMOVE_BLUR = RegisterWhWz("REMOVE_BLUR", true);
        RR_REGION = RegisterWhWz("RR_Region", MarioKartWiiEnums.Regions.None);
        WW_LANGUAGE = RegisterWhWz("WW_Language", "en", value => SettingValues.WhWzLanguages.ContainsKey((string)value!));
        #endregion

        #region Dolphin settings
        NAND_ROOT_PATH = RegisterDolphin(
            ("Dolphin.ini", "General", "NANDRootPath"),
            "",
            value => _fileSystem.Directory.Exists(value as string ?? string.Empty)
        );

        LOAD_PATH = RegisterDolphin(
            ("Dolphin.ini", "General", "LoadPath"),
            "",
            value => _fileSystem.Directory.Exists(value as string ?? string.Empty)
        );

        VSYNC = RegisterDolphin(("GFX.ini", "Hardware", "VSync"), false);
        INTERNAL_RESOLUTION = RegisterDolphin(("GFX.ini", "Settings", "InternalResolution"), 1, value => (int)(value ?? -1) >= 0);
        SHOW_FPS = RegisterDolphin(("GFX.ini", "Settings", "ShowFPS"), false);
        GFX_BACKEND = RegisterDolphin(("Dolphin.ini", "Core", "GFXBackend"), SettingValues.GFXRenderers.Values.First());

        // recommended settings
        _dolphinCompilationMode = RegisterDolphin(("GFX.ini", "Settings", "ShaderCompilationMode"), DolphinShaderCompilationMode.Default);
        _dolphinCompileShadersAtStart = RegisterDolphin(("GFX.ini", "Settings", "WaitForShadersBeforeStarting"), false);
        _dolphinSsaa = RegisterDolphin(("GFX.ini", "Settings", "SSAA"), false);
        _dolphinMsaa = RegisterDolphin(
            ("GFX.ini", "Settings", "MSAA"),
            "0x00000001",
            value => (value?.ToString() ?? "") is "0x00000001" or "0x00000002" or "0x00000004" or "0x00000008"
        );

        // Readonly settings
        MACADDRESS = RegisterDolphin(("Dolphin.ini", "General", "WirelessMac"), "02:01:02:03:04:05");
        #endregion

        #region Virtual settings
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
        #endregion
    }
    #endregion

    #region Settings Properties
    public Setting USER_FOLDER_PATH { get; }
    public Setting DOLPHIN_LOCATION { get; }
    public Setting GAME_LOCATION { get; }
    public Setting FORCE_WIIMOTE { get; }
    public Setting LAUNCH_WITH_DOLPHIN { get; }
    public Setting PREFERS_MODS_ROW_VIEW { get; }
    public Setting FOCUSED_USER { get; }
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
    #endregion

    #region Public API
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
        var reportResult = ValidateCorePathSettings();
        return reportResult.IsSuccess && reportResult.Value.IsValid;
    }

    public OperationResult<SettingsValidationReport> ValidateCorePathSettings()
    {
        try
        {
            var issues = new List<SettingsValidationIssue>();

            if (!USER_FOLDER_PATH.IsValid())
                issues.Add(new(SettingsValidationCode.InvalidUserFolderPath, USER_FOLDER_PATH.Name, "User folder path is invalid."));

            if (!DOLPHIN_LOCATION.IsValid())
                issues.Add(
                    new(SettingsValidationCode.InvalidDolphinLocation, DOLPHIN_LOCATION.Name, "Dolphin path or command is invalid.")
                );

            if (!GAME_LOCATION.IsValid())
                issues.Add(new(SettingsValidationCode.InvalidGameLocation, GAME_LOCATION.Name, "Game file path is invalid."));

            return Ok(new SettingsValidationReport(issues));
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public void LoadSettings()
    {
        lock (_syncRoot)
        {
            if (_hasLoadedSettings)
                return;

            _whWzSettingManager.LoadSettings();
            _dolphinSettingManager.LoadSettings();
            _hasLoadedSettings = true;
        }
    }
    #endregion

    #region Registration Helpers
    private WhWzSetting RegisterWhWz<T>(string name, T defaultValue, Func<object?, bool>? validation = null)
    {
        var setting = new WhWzSetting(typeof(T), name, defaultValue!, _whWzSettingManager.SaveSettings);
        if (validation != null)
            setting.SetValidation(validation);

        _whWzSettingManager.RegisterSetting(setting);
        return setting;
    }

    private DolphinSetting RegisterDolphin<T>((string, string, string) location, T defaultValue, Func<object?, bool>? validation = null)
    {
        var setting = new DolphinSetting(typeof(T), location, defaultValue!, _dolphinSettingManager.SaveSettings);
        if (validation != null)
            setting.SetValidation(validation);

        _dolphinSettingManager.RegisterSetting(setting);
        return setting;
    }
    #endregion
}

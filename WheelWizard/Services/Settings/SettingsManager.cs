using WheelWizard.Models.Enums;
using System.Runtime.InteropServices;
using WheelWizard.Helpers;
using WheelWizard.Models.Settings;
using WheelWizard.Services.Other;

namespace WheelWizard.Services.Settings;

public class SettingsManager
{
    #region Wheel Wizard Settings
    public static Setting USER_FOLDER_PATH = new WhWzSetting(typeof(string),"UserFolderPath", "").SetValidation(value => FileHelper.DirectoryExists(value as string ?? string.Empty));
    
    
    public static Setting DOLPHIN_LOCATION = new WhWzSetting(typeof(string), "DolphinLocation", "")
        .SetValidation(value =>
        {
            var pathOrCommand = value as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pathOrCommand))
                return false;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return FileHelper.FileExists(pathOrCommand) || LinuxDolphinInstaller.IsValidCommand(pathOrCommand);
            
            return FileHelper.FileExists(pathOrCommand);
        });
    
    
    public static Setting GAME_LOCATION = new WhWzSetting(typeof(string),"GameLocation", "").SetValidation(value => FileHelper.FileExists(value as string ?? string.Empty));
    public static Setting FORCE_WIIMOTE = new WhWzSetting(typeof(bool),"ForceWiimote", false);
    public static Setting LAUNCH_WITH_DOLPHIN = new WhWzSetting(typeof(bool),"LaunchWithDolphin", false);
    public static Setting PREFERS_MODS_ROW_VIEW = new WhWzSetting(typeof(bool),"PrefersModsRowView", true);
    public static Setting FOCUSSED_USER = new WhWzSetting(typeof(int), "FavoriteUser", 0).SetValidation(value => (int)(value ?? -1) >= 0 && (int)(value ?? -1) <= 4);

    public static Setting ENABLE_ANIMATIONS = new WhWzSetting(typeof(bool),"EnableAnimations", true);
    public static Setting SAVED_WINDOW_SCALE = new WhWzSetting(typeof(double), "WindowScale", 1.0).SetValidation(value => (double)(value ?? -1) >= 0.5 && (double)(value ?? -1) <= 2.0);
    public static Setting RR_LANGUAGE = new WhWzSetting(typeof(int), "RR_Language", 0).SetValidation(value => SettingValues.RrLanguages.ContainsKey((int)(value ?? -1)));
    public static Setting REMOVE_BLUR = new WhWzSetting(typeof(bool), "REMOVE_BLUR", true);
    public static Setting RR_REGION = new WhWzSetting(typeof(MarioKartWiiEnums.Regions), "RR_Region", RRRegionManager.GetValidRegions().First());
    public static Setting WW_LANGUAGE = new WhWzSetting(typeof(string), "WW_Language", "en").SetValidation(value => SettingValues.WhWzLanguages.ContainsKey((string)value!));
    #endregion
    
    #region Dolphin Settings
    public static Setting VSYNC = new DolphinSetting(typeof(bool), ("GFX.ini", "Hardware", "VSync"), false);
    public static Setting INTERNAL_RESOLUTION = new DolphinSetting(typeof(int), ("GFX.ini", "Settings", "InternalResolution"), 1)
        .SetValidation(value => (int)(value ?? -1) >= 0);
    public static Setting SHOW_FPS = new DolphinSetting(typeof(bool), ("GFX.ini", "Settings", "ShowFPS"), false);
    public static Setting GFX_BACKEND = new DolphinSetting(typeof(string), ("Dolphin.ini", "Core", "GFXBackend"), SettingValues.GFXRenderers.Values.First());
    
    //recommended settings
    private static Setting DOLPHIN_COMPILATION_MODE = new DolphinSetting(typeof(DolphinShaderCompilationMode), ("GFX.ini", "Settings", "ShaderCompilationMode"),
        DolphinShaderCompilationMode.Default);
    private static Setting DOLPHIN_COMPILE_SHADERS_AT_START = new DolphinSetting(typeof(bool), ("GFX.ini", "Settings", "WaitForShadersBeforeStarting"), false);
    private static Setting DOLPHIN_SSAA = new DolphinSetting(typeof(bool), ("GFX.ini", "Settings", "SSAA"), false);
    private static Setting DOLPHIN_MSAA = new DolphinSetting(typeof(string), ("GFX.ini", "Settings", "MSAA"), "0x00000001").SetValidation(
        value => ( value?.ToString() ?? "") is "0x00000001" or "0x00000002" or "0x00000004" or "0x00000008");
    #endregion
    
    #region Virtual Settings
    private static double _internalScale = -1.0;
    public static Setting WINDOW_SCALE = new VirtualSetting(typeof(double), 
                                                            value => _internalScale = (double)value!,
                                                            () => _internalScale == -1.0 ? SAVED_WINDOW_SCALE.Get() : _internalScale
                                                            ).SetDependencies(SAVED_WINDOW_SCALE);
    
    
    public static Setting RECOMMENDED_SETTINGS = new VirtualSetting(typeof(bool), value => {
                                                                var newValue = (bool)value!;
                                                                DOLPHIN_COMPILATION_MODE.Set(newValue ? DolphinShaderCompilationMode.HybridUberShaders : DolphinShaderCompilationMode.Default);
                                                            #if WINDOWS
                                                                DOLPHIN_COMPILE_SHADERS_AT_START.Set(newValue);
                                                            #endif
                                                                DOLPHIN_MSAA.Set(newValue ? "0x00000002" : "0x00000001");
                                                                DOLPHIN_SSAA.Set(false);
                                                            },
                                                            () => {
                                                                var value1 = (DolphinShaderCompilationMode)DOLPHIN_COMPILATION_MODE.Get();
                                                                var value2 = true;
                                                            #if WINDOWS
                                                                value2 = (bool)DOLPHIN_COMPILE_SHADERS_AT_START.Get();
                                                            #endif
                                                                var value3 = (string)DOLPHIN_MSAA.Get();
                                                                var value4 = (bool)DOLPHIN_SSAA.Get();
                                                                return !value4 && value2 && value3 == "0x00000002" && value1 == DolphinShaderCompilationMode.HybridUberShaders;
                                                            }).SetDependencies(DOLPHIN_COMPILATION_MODE, DOLPHIN_COMPILE_SHADERS_AT_START, DOLPHIN_MSAA, DOLPHIN_SSAA);

    private static RrGameMode _internalRrGameMode = RrGameMode.RETRO_TRACKS;
    #endregion

    
    #region Base Settings Manager 
    // dont ever make this a static class, it is required to be an instance class to ensure all settings are loaded
    public static SettingsManager Instance { get; } = new();
    private SettingsManager() { }
    // dont make this a static method
    public void LoadSettings()
    {
        WhWzSettingManager.Instance.LoadSettings();
        DolphinSettingManager.Instance.LoadSettings();
        SettingsHelper.LoadExtraStuff();
    }
    #endregion
}

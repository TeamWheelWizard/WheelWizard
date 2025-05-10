using System.Runtime.InteropServices;
using WheelWizard.Helpers;
using WheelWizard.Services.Settings;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace WheelWizard.Services;

public static class PathManager
{
    // IMPORTANT: To keep things consistent all paths should be Attrib expressions,
    //            and either end with `FilePath` or `FolderPath`

    // Portable WheelWizard config only makes sense on non-Flatpak WheelWizard
    private static readonly bool IsPortableWhWz = !IsFlatpakSandboxed() && File.Exists("portable-ww.txt");

    public static string HomeFolderPath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Paths set by the user
    public static string GameFilePath => (string)SettingsManager.GAME_LOCATION.Get();
    public static string DolphinFilePath => (string)SettingsManager.DOLPHIN_LOCATION.Get();
    public static string UserFolderPath => (string)SettingsManager.USER_FOLDER_PATH.Get();

    private static string AppDataFolder => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string LocalAppDataFolder => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // Wheel wizard's appdata paths  (dont have to be expressions since they dont depend on user input like the others)f
    public static readonly string WheelWizardAppdataPath = Path.Combine(IsPortableWhWz ? string.Empty : AppDataFolder, "CT-MKWII");
    public static readonly string WheelWizardConfigFilePath = Path.Combine(WheelWizardAppdataPath, "config.json");
    public static readonly string RrLaunchJsonFilePath = Path.Combine(WheelWizardAppdataPath, "RR.json");
    public static readonly string ModsFolderPath = Path.Combine(WheelWizardAppdataPath, "Mods");
    public static readonly string ModConfigFilePath = Path.Combine(ModsFolderPath, "modconfig.json");
    public static readonly string TempModsFolderPath = Path.Combine(ModsFolderPath, "Temp");
    public static readonly string RetroRewindTempFile = Path.Combine(TempModsFolderPath, "RetroRewind.zip");
    public static string RetroRewindVersionFile => Path.Combine(RetroRewind6FolderPath, "version.txt");
    public static string WiiDbFolder => Path.Combine(WiiFolderPath, "shared2", "menu", "FaceLib");
    public static string MiiDbFile => Path.Combine(WiiDbFolder, "RFL_DB.dat");

    //In case it is unclear, the mods folder is a folder with mods that are desired to be installed (if enabled)
    //When launching we want to move the mods from the Mods folder to the MyStuff folder since that is the folder the game uses
    //Also remember that mods may not be in a subfolder, all mod files must be located in /MyStuff directly

    // Helper paths for folders used across multiple files
    public static string MyStuffFolderPath => Path.Combine(RetroRewind6FolderPath, "MyStuff");

    public static string GetModDirectoryPath(string modName) => Path.Combine(ModsFolderPath, modName);

    public static string RiivolutionWhWzFolderPath => Path.Combine(LoadFolderPath, "Riivolution", "WheelWizard");
    public static string RetroRewind6FolderPath => Path.Combine(RiivolutionWhWzFolderPath, "RetroRewind6");

    // This is not the folder your save file is located in, but its the folder where every Region folder is, so the save file is in SaveFolderPath/Region
    public static string SaveFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "riivolution", "save", "RetroWFC");
    public static string XmlFilePath => Path.Combine(RiivolutionWhWzFolderPath, "riivolution", "RetroRewind6.xml");

    private static string PortableUserFolderPath =>
        Path.Combine(GetDolphinExeDirectory(), RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "user" : "User");

    private static string LinuxDolphinLegacyRelSubFolderPath => ".dolphin-emu";
    public static string LinuxDolphinLegacyFolderPath => Path.Combine(HomeFolderPath, LinuxDolphinLegacyRelSubFolderPath);
    private static string LinuxDolphinRelSubFolderPath => "dolphin-emu";

    private static string LinuxDolphinFlatpakAppDataFolderPath => Path.Combine(HomeFolderPath, ".var", "app", "org.DolphinEmu.dolphin-emu");
    public static string LinuxDolphinFlatpakDataDir =>
        Path.Combine(LinuxDolphinFlatpakAppDataFolderPath, "data", LinuxDolphinRelSubFolderPath);
    public static string LinuxDolphinFlatpakConfigDir =>
        Path.Combine(LinuxDolphinFlatpakAppDataFolderPath, "config", LinuxDolphinRelSubFolderPath);

    private static string? NullIfRelativeLinuxPath(string path)
    {
        return EnvHelper.NullIfRelativeLinuxPath(path);
    }

    private static bool IsFlatpakSandboxed()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        return EnvHelper.IsFlatpakSandboxed();
    }

    private static string LinuxXdgDataHome => LocalAppDataFolder;
    private static string LinuxXdgConfigHome => AppDataFolder;
    private static string LinuxHostXdgDataHome =>
        NullIfRelativeLinuxPath(Environment.GetEnvironmentVariable("HOST_XDG_DATA_HOME") ?? string.Empty)
        ?? Path.Combine(HomeFolderPath, ".local", "share");
    private static string LinuxHostXdgConfigHome =>
        NullIfRelativeLinuxPath(Environment.GetEnvironmentVariable("HOST_XDG_CONFIG_HOME") ?? string.Empty)
        ?? Path.Combine(HomeFolderPath, ".config");

    private static string LinuxDolphinHostNativeInstallConfigDir => Path.Combine(LinuxHostXdgConfigHome, LinuxDolphinRelSubFolderPath);
    private static string LinuxDolphinHostNativeInstallDataDir => Path.Combine(LinuxHostXdgDataHome, LinuxDolphinRelSubFolderPath);
    private static string LinuxDolphinNativeInstallConfigDir => Path.Combine(LinuxXdgConfigHome, LinuxDolphinRelSubFolderPath);
    private static string LinuxDolphinNativeInstallDataDir => Path.Combine(LinuxXdgDataHome, LinuxDolphinRelSubFolderPath);

    public static string SplitLinuxDolphinNativeConfigDir
    {
        get
        {
            if (IsFlatpakSandboxed())
            {
                if (LinuxDolphinHostNativeInstallDataDir.Equals(Path.GetFullPath(UserFolderPath), StringComparison.Ordinal))
                    return LinuxDolphinHostNativeInstallConfigDir;
            }
            else if (LinuxDolphinNativeInstallDataDir.Equals(Path.GetFullPath(UserFolderPath), StringComparison.Ordinal))
            {
                return LinuxDolphinNativeInstallConfigDir;
            }

            return string.Empty;
        }
    }

    public static string SplitLinuxDolphinConfigDir
    {
        get
        {
            if (IsFlatpakDolphinFilePath())
            {
                if (LinuxDolphinFlatpakDataDir.Equals(Path.GetFullPath(UserFolderPath), StringComparison.Ordinal))
                    return LinuxDolphinFlatpakConfigDir;

                return string.Empty;
            }
            else
            {
                return SplitLinuxDolphinNativeConfigDir;
            }
        }
    }

    public static bool IsLinuxDolphinConfigSplit()
    {
        return !string.IsNullOrWhiteSpace(SplitLinuxDolphinConfigDir);
    }

    public static string LoadFolderPath
    {
        get
        {
            if (SettingsManager.LOAD_PATH.IsValid())
            {
                return (string)SettingsManager.LOAD_PATH.Get();
            }
            return Path.Combine(UserFolderPath, "Load");
        }
    }

    public static string ConfigFolderPath
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    string determinedLinuxDolphinConfigDir = SplitLinuxDolphinConfigDir;
                    if (!string.IsNullOrWhiteSpace(determinedLinuxDolphinConfigDir))
                        return determinedLinuxDolphinConfigDir;
                }
                catch
                {
                    // Fall back to something that is likely not valid, will be checked later
                    return Path.Combine(UserFolderPath, "Config");
                }
            }
            return Path.Combine(UserFolderPath, "Config");
        }
    }

    public static string WiiFolderPath
    {
        get
        {
            if (SettingsManager.NAND_ROOT_PATH.IsValid())
            {
                return (string)SettingsManager.NAND_ROOT_PATH.Get();
            }
            return Path.Combine(UserFolderPath, "Wii");
        }
    }

    public static bool IsFlatpakDolphinFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            // Prioritize Flatpak Dolphin installation if no file path has been saved yet, so return true
            return true;
        }
        string flatpakRunCommand = "flatpak run";
        string dolphinAppId = "org.DolphinEmu.dolphin-emu";
        string[] possibleFlatpakDolphinCommands =
        [
            $"{flatpakRunCommand} {dolphinAppId}",
            $"{flatpakRunCommand} --system {dolphinAppId}",
            $"{flatpakRunCommand} --user {dolphinAppId}",
        ];
        foreach (string possibleFlatpakDolphinCommand in possibleFlatpakDolphinCommands)
        {
            if (possibleFlatpakDolphinCommand.Equals(filePath, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool IsFlatpakDolphinFilePath()
    {
        return IsFlatpakDolphinFilePath(DolphinFilePath);
    }

    private static string GetContainingBaseDirectorySafe(string path)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string GetDolphinExeDirectory()
    {
        return GetContainingBaseDirectorySafe(DolphinFilePath);
    }

    private static bool HasWindowsLocalUserConfigSet()
    {
#if WINDOWS
        try
        {
            string dolphinRegistryPath = @"Software\Dolphin Emulator";
            string localUserConfigValueName = "LocalUserConfig";
            bool local = false;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(dolphinRegistryPath))
            {
                if (key == null)
                    return local;

                object localUserConfigValue = key.GetValue(localUserConfigValueName);
                if (localUserConfigValue == null)
                    return local;

                if (localUserConfigValue is string localUserConfigValueString)
                {
                    if (localUserConfigValueString.Equals("1", StringComparison.Ordinal))
                        local = true;
                }
                else if (localUserConfigValue is int localUserConfigValueInt)
                {
                    if (localUserConfigValueInt == 1)
                        local = true;
                }
                else if (localUserConfigValue is long localUserConfigValueLong)
                {
                    if (localUserConfigValueLong == 1)
                        local = true;
                }
            }
            return local;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private static string TryFindRegistryUserConfigPath()
    {
#if WINDOWS
        try
        {
            string dolphinRegistryPath = @"Software\Dolphin Emulator";
            string userConfigPathValueName = "UserConfigPath";
            string userConfigPath = string.Empty;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(dolphinRegistryPath))
            {
                if (key == null)
                    return userConfigPath;

                string foundUserConfigPath = (string)key.GetValue(userConfigPathValueName);
                // We need to replace `/` with `\` here since Dolphin writes mismatching separators to the registry
                if (FileHelper.DirectoryExists(foundUserConfigPath))
                    userConfigPath = foundUserConfigPath.Replace(
                        Path.AltDirectorySeparatorChar.ToString(),
                        Path.DirectorySeparatorChar.ToString()
                    );
            }
            return userConfigPath;
        }
        catch
        {
            return string.Empty;
        }
#else
        return string.Empty;
#endif
    }

    private static string TryFindPortableUserFolderPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // In this case, Dolphin would use `EMBEDDED_USER_DIR` which is the portable `user` directory
            // in the current directory (the directory of the WheelWizard executable).
            // This is actually undocumented...
            string embeddedUserPath = Path.GetFullPath("user");
            if (FileHelper.DirectoryExists(embeddedUserPath))
                return embeddedUserPath;
        }

        string portableUserPath = PortableUserFolderPath;
        if (FileHelper.FileExists(Path.Combine(GetDolphinExeDirectory(), "portable.txt")))
        {
            if (FileHelper.DirectoryExists(portableUserPath))
                return portableUserPath;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && HasWindowsLocalUserConfigSet())
        {
            if (FileHelper.DirectoryExists(portableUserPath))
                return portableUserPath;
        }

        return string.Empty;
    }

    // This should return null if not found since functions above require it
    private static string? TryFindLinuxFlatpakUserFolderPath()
    {
        if (Directory.Exists(LinuxDolphinFlatpakAppDataFolderPath))
            return Path.Combine(LinuxDolphinFlatpakDataDir);

        // If not found, return null.
        return null;
    }

    private static string? TryFindLinuxNativeUserFolderPath()
    {
        if (Directory.Exists(LinuxDolphinLegacyFolderPath))
            return LinuxDolphinLegacyFolderPath;

        if (IsFlatpakSandboxed())
        {
            if (Directory.Exists(LinuxHostXdgConfigHome) && Directory.Exists(LinuxHostXdgDataHome))
            {
                if (Directory.Exists(LinuxDolphinHostNativeInstallConfigDir) && Directory.Exists(LinuxDolphinHostNativeInstallDataDir))
                    return LinuxDolphinHostNativeInstallDataDir;
            }
        }
        else
        {
            if (Directory.Exists(LinuxDolphinNativeInstallConfigDir) && Directory.Exists(LinuxDolphinNativeInstallDataDir))
                return LinuxDolphinNativeInstallDataDir;
        }

        // If not found, return null.
        return null;
    }

    public static string? TryFindUserFolderPath()
    {
        var portableUserFolderPath = TryFindPortableUserFolderPath();
        if (!string.IsNullOrWhiteSpace(portableUserFolderPath))
            return portableUserFolderPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string registryUserConfigPath = TryFindRegistryUserConfigPath();
            if (!string.IsNullOrWhiteSpace(registryUserConfigPath))
                return registryUserConfigPath;

            var documentsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dolphin Emulator");
            if (FileHelper.DirectoryExists(documentsPath))
                return documentsPath;

            var appDataPath = Path.Combine(AppDataFolder, "Dolphin Emulator");
            if (FileHelper.DirectoryExists(appDataPath))
                return appDataPath;

            if (FileHelper.DirectoryExists(PortableUserFolderPath))
                return PortableUserFolderPath;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var libraryPath = Path.Combine(AppDataFolder, "Dolphin");
            if (FileHelper.DirectoryExists(libraryPath))
                return libraryPath;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (IsFlatpakDolphinFilePath())
            {
                return TryFindLinuxFlatpakUserFolderPath();
            }
            else
            {
                return TryFindLinuxNativeUserFolderPath();
            }
        }

        return null;
    }

    public static string? TryToFindApplicationPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var dolphinApplicationPath = Path.Combine("Dolphin.app", "Contents", "MacOS", "Dolphin");
            // Try system wide install on MacOS
            var path = Path.Combine("/Applications", dolphinApplicationPath);
            if (FileHelper.FileExists(path))
                return path;
            // Try user install on MacOS
            path = Path.Combine(HomeFolderPath, "Applications", dolphinApplicationPath);
            if (FileHelper.FileExists(path))
                return path;
        }
        return null;
    }
}

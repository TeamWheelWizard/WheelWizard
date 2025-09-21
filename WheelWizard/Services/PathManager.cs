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

    private const string WheelWizardFolderName = "CT-MKWII";
#if WINDOWS
    private const string WindowsAppDataOverrideRegistryKeyPath = @"Software\\WheelWizard";
    private const string WindowsAppDataOverrideRegistryValueName = "AppDataLocation";
#endif
    private static readonly object WheelWizardAppdataLock = new();

    // Portable WheelWizard config only makes sense on non-Flatpak WheelWizard
    private static readonly bool IsPortableWhWz = !IsFlatpakSandboxed() && File.Exists("portable-ww.txt");
    private static readonly string DefaultWheelWizardAppdataPath = FileHelper.NormalizePath(
        FileHelper.Combine(IsPortableWhWz ? string.Empty : AppDataFolder, WheelWizardFolderName)
    );
    private static string? _wheelWizardAppdataOverride;

    static PathManager()
    {
        _wheelWizardAppdataOverride = LoadSavedWheelWizardAppdataOverride();
    }

    public static string HomeFolderPath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Paths set by the user
    public static string GameFilePath => (string)SettingsManager.GAME_LOCATION.Get();
    public static string DolphinFilePath => (string)SettingsManager.DOLPHIN_LOCATION.Get();
    public static string UserFolderPath => (string)SettingsManager.USER_FOLDER_PATH.Get();

    private static string AppDataFolder => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string LocalAppDataFolder => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string UnixAppDataOverrideFilePath =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(HomeFolderPath, "Library", "Preferences", "com.wheelwizard.appdata-location")
            : Path.Combine(HomeFolderPath, ".config", "wheelwizard-appdata-location");

    // Wheel wizard's appdata paths (don't have to be expressions since they don't depend on user input like the others)
    public static string WheelWizardAppdataPath
    {
        get
        {
            lock (WheelWizardAppdataLock)
            {
                return _wheelWizardAppdataOverride ?? DefaultWheelWizardAppdataPath;
            }
        }
    }

    public static string DefaultWheelWizardAppdataFolderPath => DefaultWheelWizardAppdataPath;
    public static bool IsUsingCustomWheelWizardAppdataPath
    {
        get
        {
            lock (WheelWizardAppdataLock)
            {
                return _wheelWizardAppdataOverride != null;
            }
        }
    }

    public static string WheelWizardConfigFilePath => Path.Combine(WheelWizardAppdataPath, "config.json");
    public static string RrLaunchJsonFilePath => Path.Combine(WheelWizardAppdataPath, "RR.json");
    public static string ModsFolderPath => Path.Combine(WheelWizardAppdataPath, "Mods");
    public static string ModConfigFilePath => Path.Combine(ModsFolderPath, "modconfig.json");
    public static string TempModsFolderPath => Path.Combine(ModsFolderPath, "Temp");
    public static string RetroRewindTempFile => Path.Combine(TempModsFolderPath, "RetroRewind.zip");
    public static string WiiDbFolder => Path.Combine(WiiFolderPath, "shared2", "menu", "FaceLib");
    public static string MiiDbFile => Path.Combine(WiiDbFolder, "RFL_DB.dat");

    #region Wheel Wizard Appdata Override

    private static string? LoadSavedWheelWizardAppdataOverride()
    {
        try
        {
            var storedPath = LoadPersistedWheelWizardAppdataOverride();
            if (string.IsNullOrWhiteSpace(storedPath))
                return null;

            var normalized = FileHelper.NormalizePath(storedPath);
            return FileHelper.PathsEqual(normalized, DefaultWheelWizardAppdataPath) ? null : normalized;
        }
        catch
        {
            return null;
        }
    }

    private static string? LoadPersistedWheelWizardAppdataOverride()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(WindowsAppDataOverrideRegistryKeyPath, writable: false);
                if (key != null)
                {
                    var value = key.GetValue(WindowsAppDataOverrideRegistryValueName) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            catch
            {
                // ignored; fall back to other persistence mechanisms
            }
        }
#endif

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            var storedPath = FileHelper.ReadAllTextSafe(UnixAppDataOverrideFilePath);
            if (!string.IsNullOrWhiteSpace(storedPath))
                return storedPath;
        }

        return null;
    }

    public static bool TrySetWheelWizardAppdataPath(string requestedPath, out string errorMessage, IProgress<double>? progress = null)
    {
        errorMessage = string.Empty;

        if (
            !TryValidateWheelWizardAppdataTarget(
                requestedPath,
                out var normalizedTarget,
                out var currentPath,
                out errorMessage,
                out var requiresMove
            )
        )
            return false;

        if (!requiresMove)
            return true;

        if (!FileHelper.DirectoryExists(normalizedTarget))
        {
            try
            {
                FileHelper.EnsureDirectory(normalizedTarget);
            }
            catch (Exception ex)
            {
                errorMessage = $"Unable to create the selected folder: {ex.Message}";
                return false;
            }
        }
        else if (!FileHelper.IsDirectoryEmpty(normalizedTarget))
        {
            errorMessage = "The selected folder must be empty. Please choose an empty folder.";
            return false;
        }

        var newOverrideValue = FileHelper.PathsEqual(normalizedTarget, DefaultWheelWizardAppdataPath) ? null : normalizedTarget;

        try
        {
            FileHelper.MoveDirectoryContents(currentPath, normalizedTarget, progress: progress);
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to move Wheel Wizard files: {ex.Message}";
            return false;
        }

        try
        {
            PersistWheelWizardAppdataOverride(newOverrideValue);
        }
        catch (Exception ex)
        {
            try
            {
                FileHelper.MoveDirectoryContents(normalizedTarget, currentPath);
            }
            catch (Exception rollbackEx)
            {
                errorMessage = $"Failed to persist the new Wheel Wizard data location: {ex.Message}. Rollback failed: {rollbackEx.Message}";
                return false;
            }

            errorMessage = $"Failed to persist the new Wheel Wizard data location: {ex.Message}";
            return false;
        }

        lock (WheelWizardAppdataLock)
        {
            _wheelWizardAppdataOverride = newOverrideValue;
        }

        return true;
    }

    public static bool TryResetWheelWizardAppdataPath(out string errorMessage) =>
        TrySetWheelWizardAppdataPath(DefaultWheelWizardAppdataPath, out errorMessage);

    public static bool TryValidateWheelWizardAppdataTarget(
        string requestedPath,
        out string normalizedTarget,
        out string currentPath,
        out string errorMessage,
        out bool requiresMove
    )
    {
        normalizedTarget = string.Empty;
        currentPath = string.Empty;
        errorMessage = string.Empty;
        requiresMove = false;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            errorMessage = "Please select a valid folder.";
            return false;
        }

        try
        {
            normalizedTarget = FileHelper.NormalizePath(requestedPath);
        }
        catch (Exception ex)
        {
            errorMessage = $"Invalid folder path: {ex.Message}";
            return false;
        }

        lock (WheelWizardAppdataLock)
        {
            currentPath = _wheelWizardAppdataOverride ?? DefaultWheelWizardAppdataPath;
        }

        if (FileHelper.PathsEqual(currentPath, normalizedTarget))
            return true;

        if (FileHelper.IsDescendantPath(normalizedTarget, currentPath))
        {
            errorMessage = "The selected folder is inside the current Wheel Wizard data folder. Please choose a different folder.";
            return false;
        }

        if (FileHelper.IsDescendantPath(currentPath, normalizedTarget))
        {
            errorMessage = "The selected folder contains the current Wheel Wizard data folder. Please choose a different folder.";
            return false;
        }

        if (FileHelper.FileExists(normalizedTarget))
        {
            errorMessage = "The selected path points to a file. Please choose an empty folder instead.";
            return false;
        }

        if (FileHelper.IsRootDirectory(normalizedTarget))
        {
            errorMessage = "Selecting a drive or root directory is not allowed. Please choose an empty folder.";
            return false;
        }

        if (FileHelper.DirectoryExists(normalizedTarget) && !FileHelper.IsDirectoryEmpty(normalizedTarget))
        {
            errorMessage = "The selected folder must be empty. Please choose an empty folder.";
            return false;
        }

        requiresMove = true;
        return true;
    }

    private static void PersistWheelWizardAppdataOverride(string? overridePath)
    {
        if (string.IsNullOrWhiteSpace(overridePath) || FileHelper.PathsEqual(overridePath, DefaultWheelWizardAppdataPath))
        {
            ClearWheelWizardAppdataOverride();
            return;
        }

        try
        {
            var normalizedOverride = FileHelper.NormalizePath(overridePath);
            SaveWheelWizardAppdataOverride(normalizedOverride);
        }
        catch
        {
            // ignored
        }
    }

    private static void ClearWheelWizardAppdataOverride()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(WindowsAppDataOverrideRegistryKeyPath, writable: true);
                key?.DeleteValue(WindowsAppDataOverrideRegistryValueName, throwOnMissingValue: false);
            }
            catch
            {
                // ignored
            }

            return;
        }
#endif

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            TryDeleteFileSilently(UnixAppDataOverrideFilePath);
        }
    }

    private static void SaveWheelWizardAppdataOverride(string overridePath)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(WindowsAppDataOverrideRegistryKeyPath, writable: true);
                key?.SetValue(WindowsAppDataOverrideRegistryValueName, overridePath, RegistryValueKind.String);
                return;
            }
            catch
            {
                // Fall back to other persistence mechanisms
            }
        }
#endif

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            FileHelper.WriteAllTextSafe(UnixAppDataOverrideFilePath, overridePath);
        }
    }

    private static void TryDeleteFileSilently(string path)
    {
        try
        {
            if (FileHelper.FileExists(path))
                File.Delete(path);
        }
        catch
        {
            // ignored
        }
    }

    private static void MoveWheelWizardAppdataContents(string sourcePath, string destinationPath) =>
        FileHelper.MoveDirectoryContents(sourcePath, destinationPath);

    #endregion

    //In case it is unclear, the mods folder is a folder with mods that are desired to be installed (if enabled)
    //When launching we want to move the mods from the Mods folder to the MyStuff folder since that is the folder the game uses
    //Also remember that mods may not be in a subfolder, all mod files must be located in /MyStuff directly

    // Helper paths for folders used across multiple files

    //todo: before we can actually add more distributions, we will have to rewrite the MyStuff as a service aswell
    public static string MyStuffFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "RetroRewind6", "MyStuff");

    public static string GetModDirectoryPath(string modName) => Path.Combine(ModsFolderPath, modName);

    public static string RiivolutionWhWzFolderPath => Path.Combine(LoadFolderPath, "Riivolution", "WheelWizard");

    // public static string RetroRewind6FolderPath => Path.Combine(RiivolutionWhWzFolderPath, "RetroRewind6");

    // This is not the folder your save file is located in, but its the folder where every Region folder is, so the save file is in SaveFolderPath/Region
    public static string SaveFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "riivolution", "save", "RetroWFC");
    public static string RiivolutionXmlFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "riivolution");
    public static string XmlFilePath => Path.Combine(RiivolutionXmlFolderPath, "RetroRewind6.xml");

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
                    var determinedLinuxDolphinConfigDir = SplitLinuxDolphinConfigDir;
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
        var flatpakRunCommand = "flatpak run";
        var dolphinAppId = "org.DolphinEmu.dolphin-emu";
        string[] possibleFlatpakDolphinCommands =
        [
            $"{flatpakRunCommand} {dolphinAppId}",
            $"{flatpakRunCommand} --system {dolphinAppId}",
            $"{flatpakRunCommand} --user {dolphinAppId}",
            $"{flatpakRunCommand} -p {dolphinAppId}",
            $"{flatpakRunCommand} --system -p {dolphinAppId}",
            $"{flatpakRunCommand} --user -p {dolphinAppId}",
        ];
        foreach (var possibleFlatpakDolphinCommand in possibleFlatpakDolphinCommands)
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
            return Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
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
            var dolphinRegistryPath = @"Software\Dolphin Emulator";
            var localUserConfigValueName = "LocalUserConfig";
            var local = false;
            using var key = Registry.CurrentUser.OpenSubKey(dolphinRegistryPath);
            if (key == null)
                return local;

            var localUserConfigValue = key.GetValue(localUserConfigValueName);
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
            var dolphinRegistryPath = @"Software\Dolphin Emulator";
            var userConfigPathValueName = "UserConfigPath";
            var userConfigPath = string.Empty;
            using var key = Registry.CurrentUser.OpenSubKey(dolphinRegistryPath);
            if (key == null)
                return userConfigPath;

            var foundUserConfigPath = key.GetValue(userConfigPathValueName) as string;
            // We need to replace `/` with `\` here since Dolphin writes mismatching separators to the registry
            if (!string.IsNullOrWhiteSpace(foundUserConfigPath) && FileHelper.DirectoryExists(foundUserConfigPath))
                userConfigPath = foundUserConfigPath.Replace(
                    Path.AltDirectorySeparatorChar.ToString(),
                    Path.DirectorySeparatorChar.ToString()
                );
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

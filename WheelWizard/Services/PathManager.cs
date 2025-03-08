using System.Runtime.InteropServices;
using WheelWizard.Helpers;
using WheelWizard.Services.Settings;

namespace WheelWizard.Services;

public static class PathManager
{
    // IMPORTANT: To keep things consistent all paths should be Attrib expressions,
    //            and either end with `FilePath` or `FolderPath`

    public static string HomeFolderPath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Paths set by the user
    public static string GameFilePath => (string)SettingsManager.GAME_LOCATION.Get();
    public static string DolphinFilePath => (string)SettingsManager.DOLPHIN_LOCATION.Get();
    public static string UserFolderPath => (string)SettingsManager.USER_FOLDER_PATH.Get();

    private static string LinuxDolphinLegacyRelSubFolderPath => ".dolphin-emu";
    private static string LinuxDolphinLegacyFolderPath => Path.Combine(HomeFolderPath, LinuxDolphinLegacyRelSubFolderPath);
    private static string LinuxDolphinRelSubFolderPath => "dolphin-emu";
    // The User Folder on Linux is the "data folder", so we just go up the hierarchy
    private static string LinuxDolphinFlatpakFullConfigSubFolderPath => Path.GetFullPath(
        Path.Combine(
            UserFolderPath,
            "..", "..", "config", LinuxDolphinRelSubFolderPath));
    private static string LinuxDolphinNativeFullConfigSubFolderPath => Path.GetFullPath(
        Path.Combine(
            UserFolderPath,
            "..", "..", "..", ".config", LinuxDolphinRelSubFolderPath));
    private static string LinuxDolphinNativeRelConfigSubFolderPath => Path.Combine(".config", LinuxDolphinRelSubFolderPath);
    private static string LinuxDolphinFlatpakRelDataSubFolderPath => Path.Combine("data", LinuxDolphinRelSubFolderPath);
    private static string LinuxDolphinNativeRelDataSubFolderPath => Path.Combine(".local", "share", LinuxDolphinRelSubFolderPath);

    // Wheel wizard's appdata paths  (dont have to be expressions since they dont depend on user input like the others)f
    public static readonly string WheelWizardAppdataPath = Path.Combine(GetAppDataFolder(), "CT-MKWII");
    public static readonly string WheelWizardConfigFilePath = Path.Combine(WheelWizardAppdataPath, "config.json");
    public static readonly string ModsFolderPath = Path.Combine(WheelWizardAppdataPath, "Mods");
    public static readonly string ModConfigFilePath = Path.Combine(ModsFolderPath, "modconfig.json");
    public static readonly string TempModsFolderPath = Path.Combine(ModsFolderPath, "Temp");
    public static readonly string RetroRewindTempFile = Path.Combine(TempModsFolderPath, "RetroRewind.zip");
    public static string RetroRewindVersionFile => Path.Combine(RetroRewind6FolderPath, "version.txt");
    public static string WiiDbFile => Path.Combine(WiiFolderPath, "shared2", "menu", "FaceLib", "RFL_DB.dat");


    //In case it is unclear, the mods folder is a folder with mods that are desired to be installed (if enabled)
    //When launching we want to move the mods from the Mods folder to the MyStuff folder since that is the folder the game uses
    //Also remember that mods may not be in a subfolder, all mod files must be located in /MyStuff directly


    // Keep config in ~/.config for macOS
    private static string GetAppDataFolder()
    {
        if (OperatingSystem.IsMacOS())
        {
            //return Path.Combine(HomeFolderPath, ".config"); // ~ is the home directory
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    // helper paths for folders used across multiple files
    public static string MyStuffFolderPath => Path.Combine(RetroRewind6FolderPath, "MyStuff");
    public static string GetModDirectoryPath(string modName) => Path.Combine(ModsFolderPath, modName);
    public static string RiivolutionWhWzFolderPath => Path.Combine(LoadFolderPath, "Riivolution", "WheelWizard");
    public static string RetroRewind6FolderPath => Path.Combine(RiivolutionWhWzFolderPath, "RetroRewind6");

    //this is not the folder your save file is located in, but its the folder where every Region folder is, so the save file is in SaveFolderPath/Region
    public static string SaveFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "riivolution", "Save" ,"RetroWFC");

    public static string LinuxDolphinFullConfigSubFolderPath
    {
        get
        {
            if (IsFlatpakDolphinFilePath())
            {
                return LinuxDolphinFlatpakFullConfigSubFolderPath;
            }
            else
            {
                return LinuxDolphinNativeFullConfigSubFolderPath;
            }
        }
    }

    public static string LoadFolderPath
    {
        get
        {
            return Path.Combine(UserFolderPath, "Load");
        }
    }

    public static string ConfigFolderPath
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !LinuxDolphinLegacyFolderPath.Equals(UserFolderPath) &&
                LinuxDolphinRelSubFolderPath.Equals(Path.GetFileName(UserFolderPath)))
            {
                try
                {
                    return LinuxDolphinFullConfigSubFolderPath;
                }
                catch (Exception ex)
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
            // TODO: Check if this path is valid on macOS
            return Path.Combine(UserFolderPath, "Wii");
        }
    }

    public static bool IsFlatpakDolphinFilePath(string filePath)
    {
        string[] flatpakFilePathSubStrings = { "flatpak", "run", "org.DolphinEmu.dolphin-emu" };
        if (string.IsNullOrWhiteSpace(filePath))
        {
            // Prioritize Flatpak Dolphin installation if no file path has been saved yet, so return true
            return true;
        }
        foreach (string substring in flatpakFilePathSubStrings)
        {
            if (!filePath.Contains(substring))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsFlatpakDolphinFilePath()
    {
        return IsFlatpakDolphinFilePath(DolphinFilePath);
    }

    //this should return null if not found since functions above require it
    private static string? TryFindLinuxFlatpakUserFolderPath()
    {
        // First, try the default Flatpak location.
        var flatpakUserFolder = Path.Combine(HomeFolderPath, ".var", "app", "org.DolphinEmu.dolphin-emu");
        if (Directory.Exists(flatpakUserFolder))
            return Path.Combine(flatpakUserFolder, LinuxDolphinFlatpakRelDataSubFolderPath);

        // Next, check if there's an override provided via FLATPAK_USER_DIR.
        var flatpakOverride = Environment.GetEnvironmentVariable("FLATPAK_USER_DIR");
        if (!string.IsNullOrEmpty(flatpakOverride))
        {
            // Often the structure remains the same.
            flatpakUserFolder = Path.Combine(flatpakOverride, "org.DolphinEmu.dolphin-emu");
            if (Directory.Exists(flatpakUserFolder))
                return Path.Combine(flatpakUserFolder, LinuxDolphinFlatpakRelDataSubFolderPath);
        }

        // If not found, return null.
        return null;
    }

    private static string? TryFindLinuxNativeUserFolderPath()
    {
        var manualInstallConfigDir = Path.Combine(HomeFolderPath, LinuxDolphinNativeRelConfigSubFolderPath);
        var manualInstallDataDir = Path.Combine(HomeFolderPath, LinuxDolphinNativeRelDataSubFolderPath);
        if (Directory.Exists(manualInstallConfigDir) && Directory.Exists(manualInstallDataDir))
            return manualInstallDataDir;

        if (Directory.Exists(LinuxDolphinLegacyFolderPath))
            return LinuxDolphinLegacyFolderPath;

        // If not found, return null.
        return null;
    }

    public static string? TryFindUserFolderPath()
    {
        var appDataPath = Path.Combine(GetAppDataFolder(), "Dolphin Emulator");
        if (FileHelper.DirectoryExists(appDataPath))
            return appDataPath;

        // Macos path
        var libraryPath = Path.Combine(GetAppDataFolder(), "Dolphin");
        if (FileHelper.DirectoryExists(libraryPath))
            return libraryPath;

        var documentsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Dolphin Emulator");
        if (FileHelper.DirectoryExists(documentsPath))
            return documentsPath;

        if (IsFlatpakDolphinFilePath())
        {
            return TryFindLinuxFlatpakUserFolderPath();
        }
        else
        {
            return TryFindLinuxNativeUserFolderPath();
        }
    }

    public static string? TryToFindApplicationPath() {

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

using System.Runtime.InteropServices;
using WheelWizard.Helpers;
using WheelWizard.Services.Settings;

namespace WheelWizard.Services;

public static class PathManager
{
    // IMPORTANT: To keep things consistent all paths should be Attrib expressions,
    //            and either end with `FilePath` or `FolderPath`
    
    // pats set by the user
    public static string GameFilePath => (string)SettingsManager.GAME_LOCATION.Get();
    public static string DolphinFilePath => (string)SettingsManager.DOLPHIN_LOCATION.Get();
    public static string UserFolderPath => (string)SettingsManager.USER_FOLDER_PATH.Get();
    
    // Wheel wizard's appdata paths  (dont have to be expressions since they dont depend on user input like the others)
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
            return Path.Combine("~", ".config"); // ~ is the home directory
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

    //todo: find a way to clean this up so its not just alot of if statements
    public static string LoadFolderPath
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (string.IsNullOrWhiteSpace(UserFolderPath)) return "";
                return Path.Combine(UserFolderPath, "Load");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(UserFolderPath, "data", "dolphin-emu", "Load");
            }
            throw new PlatformNotSupportedException("Unsupported operating system");
        }
    }

    public static string ConfigFolderPath
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(UserFolderPath, "Config");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(UserFolderPath, "config", "dolphin-emu");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "/", "Library", "Application Support", "Dolphin", "Config");
            }
            throw new PlatformNotSupportedException("Unsupported operating system");
        }
    }

    public static string WiiFolderPath
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(UserFolderPath, "Wii");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(UserFolderPath, "data", "dolphin-emu", "Wii");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(UserFolderPath, "Wii"); // TODO: Check this path
            }
            throw new PlatformNotSupportedException("Unsupported operating system");
        }
    }


    public static string? TryFindUserFolderPath()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Dolphin Emulator");
        if (FileHelper.DirectoryExists(appDataPath))
            return appDataPath;

        // Macos path
        var libraryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                       "Library", "Application Support", "Dolphin");
        if (FileHelper.DirectoryExists(libraryPath))
            return libraryPath;

        var documentsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Dolphin Emulator");
        if (FileHelper.DirectoryExists(documentsPath)) return documentsPath;

        //linux path returns The location until the paths split into Config and Data
        var linuxPath = LinuxDolphinInstaller.TryFindUserFolderPath();
        
        return linuxPath;
    }

    public static string? TryToFindApplicationPath() {

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Try system wide install on MacOS
            var path = "/Applications/Dolphin.app/Contents/MacOS/Dolphin";
            if (FileHelper.FileExists(path))
                return path;
            // Try user install on MacOS
            path = Path.Combine("~", "Applications", "Dolphin.app", "Contents", "MacOS", "Dolphin");
            if (FileHelper.FileExists(path))
                return path;
        }
        return null;
    }
}

using WheelWizard.ApplicationData;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Helpers;
using WheelWizard.Recomp;
using WheelWizard.Settings;
using WheelWizard.Shared.IO;

namespace WheelWizard.Services;

public static class PathManager
{
    private static ISettingsManager Settings => SettingsRuntime.Current;

    // IMPORTANT: To keep things consistent all paths should be Attrib expressions,
    //            and either end with `FilePath` or `FolderPath`

    // Shared only with legacy callers during migration; composition registers this exact instance.
    internal static IApplicationDataLocation ApplicationData { get; } = CreateApplicationDataLocation();

    private static IApplicationDataLocation CreateApplicationDataLocation()
    {
        var fileSystem = new Testably.Abstractions.RealFileSystem();
        var environment = new WheelWizard.Shared.Platform.RuntimeEnvironment();
        var directories = new ApplicationDataDirectories(fileSystem, environment);
        IApplicationDataLocationStore store = environment.IsWindows
            ? new WindowsApplicationDataLocationStore()
            : new FileApplicationDataLocationStore(fileSystem, directories);
        return new ApplicationDataLocation(fileSystem, directories, store, new DirectoryTransferService(fileSystem));
    }

    public static string HomeFolderPath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Paths set by the user
    public static string GameFilePath => Settings.Get<string>(Settings.GAME_LOCATION);
    public static string DolphinFilePath => EnvHelper.MaybeDolphinLocationOverride() ?? Settings.Get<string>(Settings.DOLPHIN_LOCATION);
    public static string UserFolderPath => Settings.Get<string>(Settings.USER_FOLDER_PATH);

    private static string LocalAppDataFolder => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string WheelWizardAppdataPath => ApplicationData.DirectoryPath;
    public static string DefaultWheelWizardAppdataFolderPath => ApplicationData.DefaultDirectoryPath;
    public static bool IsUsingCustomWheelWizardAppdataPath => ApplicationData.IsCustom;

    public static string WheelWizardConfigFilePath => Path.Combine(WheelWizardAppdataPath, "config.json");
    public static string RrLaunchJsonFilePath => Path.Combine(WheelWizardAppdataPath, "RR.json");
    public static string ModsFolderPath => Path.Combine(WheelWizardAppdataPath, "Mods");
    public static string TempModsFolderPath => Path.Combine(ModsFolderPath, "Temp");
    public static string RetroRewindTempFile => Path.Combine(TempModsFolderPath, "RetroRewind.zip");
    public static string RrBetaTempFolderPath => Path.Combine(TempModsFolderPath, "RRBetaTemp");
    public static string RrBetaTempFilePath => Path.Combine(RrBetaTempFolderPath, "Testers.zip");
    public static string RrBetaManifestFilePath => Path.Combine(WheelWizardAppdataPath, "RRBeta.manifest.json");
    public static string MiiRenderingFolderPath => Path.Combine(WheelWizardAppdataPath, "MiiRendering");
    public static string MiiRenderingResourceFilePath => Path.Combine(MiiRenderingFolderPath, "FFLResHigh.dat");

    // WiiCompiled is portable: <RecompFolderPath> is the portable root the backend owns
    // (portable.txt, Install\, UserData\), so the whole product travels with Wheel Wizard's data directory.
    public static string RecompFolderPath => Path.Combine(WheelWizardAppdataPath, "Recomp");

    /// <summary>The portable install directory inside the recomp's portable root.</summary>
    public static string PortableRecompInstallFolderPath => Path.Combine(RecompFolderPath, "Install");

    /// <summary>The backend install directory inside Wheel Wizard's portable recomp root.</summary>
    public static string RecompInstallFolderPath => PortableRecompInstallFolderPath;

    /// <summary>
    /// Whether the recomp uses the portable layout, which is what earns the setup's <c>--portable</c> flag.
    /// The Linux AppImage has no portable mode: it always installs into the user's XDG data directory.
    /// </summary>
    public static bool IsRecompInstallPortable => !RecompPlatform.IsLinux;

    public static string RecompCacheFolderPath => Path.Combine(RecompFolderPath, "Cache");
    public static string RecompInstallStateFilePath => Path.Combine(RecompInstallFolderPath, RecompInstallStateFileName);
    public static string RecompSetupFilePath => Path.Combine(RecompInstallFolderPath, RecompPlatform.SetupFileName);

    /// <summary>The backend-owned runtime user state (Config.toml, private NAND, caches) inside the portable root.</summary>
    public static string RecompUserDataFolderPath => Path.Combine(RecompFolderPath, "UserData");

    /// <summary>
    /// On Linux the AppImage owns this directory under <c>$XDG_DATA_HOME</c> (the same value .NET reports as
    /// local application data): its <c>install-state.json</c>, the built products under <c>Install/</c>,
    /// <c>Config.toml</c>, logs and the multi-gigabyte build workspace. Wheel Wizard reads it and removes it
    /// on uninstall, but never chooses it: a manual AppImage run installs to the very same place.
    /// </summary>
    public static string RecompLinuxBackendFolderPath => Path.Combine(LocalAppDataFolder, "WiiCompiled");

    /// <summary>The AppImage's own record of what it installed and where. Not the same schema as the Windows state file.</summary>
    public static string RecompLinuxBackendStateFilePath => Path.Combine(RecompLinuxBackendFolderPath, RecompInstallStateFileName);

    /// <summary>The recomp's own settings file, shared between Wheel Wizard and the in-game settings bar.</summary>
    public static string RecompConfigFilePath =>
        RecompPlatform.IsLinux
            ? Path.Combine(RecompLinuxBackendFolderPath, "Config.toml")
            : Path.Combine(RecompUserDataFolderPath, "Config.toml");

    /// <summary>The Wheel Wizard-owned copy of the Dolphin NAND, used when the user chose copying over sharing it in place.</summary>
    public static string RecompNandCopyFolderPath => Path.Combine(RecompFolderPath, "Nand");

    /// <summary>The marker file whose presence makes <see cref="RecompFolderPath"/> a portable root.</summary>
    public static string RecompPortableMarkerFilePath => Path.Combine(RecompFolderPath, "portable.txt");

    /// <summary>The recomp runtime's private NAND, used when no Dolphin NAND is linked.</summary>
    public static string RecompPrivateNandFolderPath =>
        RecompPlatform.IsLinux ? Path.Combine(RecompLinuxBackendFolderPath, "NAND") : Path.Combine(RecompUserDataFolderPath, "NAND");

    public static string GetWiiDbFolderPath(string nandFolderPath) => Path.Combine(nandFolderPath, "shared2", "menu", "FaceLib");

    public static string GetMiiDbFilePath(string nandFolderPath) => Path.Combine(GetWiiDbFolderPath(nandFolderPath), "RFL_DB.dat");

    public static string WiiDbFolder => GetWiiDbFolderPath(WiiFolderPath);
    public static string MiiDbFile => GetMiiDbFilePath(WiiFolderPath);
    public static string RRratingFilePath => Path.Combine(WiiFolderPath, "shared2", "Pulsar", "RetroRewind6", "RRRating.pul");

    /// <summary>The file the recomp setup writes to mark a directory as one of its installations.</summary>
    public const string RecompInstallStateFileName = "install-state.json";

    public static bool TrySetWheelWizardAppdataPath(
        string path,
        out string errorMessage,
        out DirectoryMoveContentsResult result,
        IProgress<double>? progress = null
    ) => ApplicationData.TryMove(path, out errorMessage, out result, progress);

    public static bool TryResetWheelWizardAppdataPath(out string errorMessage) => ApplicationData.TryReset(out errorMessage);

    // In case it is unclear, the mods folder is a folder with mods that are desired to be installed (if enabled).
    // When launching, enabled mods are synced into the active Patches runtime folder.

    // Helper paths for folders used across multiple files

    public static string PatchesFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "RetroRewind6", "Patches");
    public static string RrBetaFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "RRBeta");
    public static string RrBetaPatchesFolderPath => Path.Combine(RrBetaFolderPath, "Patches");

    public static string GetModDirectoryPath(string modName) => Path.Combine(ModsFolderPath, modName);

    // Retro Rewind lives in Dolphin's Load folder so Riivolution can find it. Recomp-only setups
    // have no Dolphin user folder, so the package falls back to a Wheel Wizard-owned location; both
    // frontends read this same property, so they always share one installation.
    public static string RiivolutionWhWzFolderPath
    {
        get
        {
            if (Settings.LOAD_PATH.IsValid() || !string.IsNullOrWhiteSpace(UserFolderPath))
                return Path.Combine(LoadFolderPath, "Riivolution", "WheelWizard");

            return Path.Combine(WheelWizardAppdataPath, "RetroRewind");
        }
    }

    public static string RetroRewind6FolderPath => Path.Combine(RiivolutionWhWzFolderPath, "RetroRewind6");

    // This is not the folder your save file is located in, but its the folder where every Region folder is, so the save file is in SaveFolderPath/Region
    public static string SaveFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "riivolution", "save", "RetroWFC");
    public static string RiivolutionXmlFolderPath => Path.Combine(RiivolutionWhWzFolderPath, "riivolution");
    public static string XmlFilePath => Path.Combine(RiivolutionXmlFolderPath, "RetroRewind6.xml");
    public static string RrBetaXmlFilePath => Path.Combine(RiivolutionXmlFolderPath, "RRBeta.xml");

    // Transitional facade for remaining static callers. New consumers inject IDolphinPathResolver.
    private static DolphinPathLayout ResolveDolphinLayout(string dolphinLocation, string userFolderPath) =>
        new DolphinPathResolver(new Testably.Abstractions.RealFileSystem(), new WheelWizard.Shared.Platform.RuntimeEnvironment()).Resolve(
            dolphinLocation,
            userFolderPath
        );

    private static DolphinPathLayout CurrentDolphinLayout => ResolveDolphinLayout(DolphinFilePath, UserFolderPath);
    private static DolphinPathLayout DefaultDolphinLayout => ResolveDolphinLayout("", "");

    public static string LinuxDolphinLegacyFolderPath => DefaultDolphinLayout.LinuxDolphinLegacyFolderPath;
    public static string LinuxDolphinFlatpakDataDir => CurrentDolphinLayout.LinuxDolphinFlatpakDataDir;
    public static string LinuxDolphinFlatpakConfigDir => CurrentDolphinLayout.LinuxDolphinFlatpakConfigDir;
    public static string LinuxXdgDataHome => DefaultDolphinLayout.LinuxXdgDataHome;
    public static string LinuxXdgConfigHome => DefaultDolphinLayout.LinuxXdgConfigHome;
    public static string LinuxDolphinNativeInstallConfigDir => DefaultDolphinLayout.LinuxDolphinNativeInstallConfigDir;
    public static string LinuxDolphinNativeInstallDataDir => DefaultDolphinLayout.LinuxDolphinNativeInstallDataDir;
    public static string LinuxFlatpakBundledDolphinXdgConfigDir => DefaultDolphinLayout.LinuxFlatpakBundledDolphinXdgConfigDir;
    public static string LinuxFlatpakBundledDolphinXdgDataDir => DefaultDolphinLayout.LinuxFlatpakBundledDolphinXdgDataDir;
    public static string[] LinuxFlatpakSandboxedDolphinUserFolderBlockList =>
        DefaultDolphinLayout.LinuxFlatpakSandboxedDolphinUserFolderBlockList;
    public static string SplitLinuxDolphinNativeConfigDir => CurrentDolphinLayout.SplitLinuxDolphinNativeConfigDir;
    public static string SplitLinuxDolphinConfigDir => CurrentDolphinLayout.SplitLinuxDolphinConfigDir;

    private static bool IsFlatpakSandboxed() => EnvHelper.IsFlatpakSandboxed();

    public static bool IsLinuxDolphinConfigSplit() => CurrentDolphinLayout.IsLinuxDolphinConfigSplit();

    public static string LoadFolderPath
    {
        get
        {
            if (Settings.LOAD_PATH.IsValid())
            {
                return Settings.Get<string>(Settings.LOAD_PATH);
            }
            return Path.Combine(UserFolderPath, "Load");
        }
    }

    public static string ConfigFolderPath => CurrentDolphinLayout.ConfigFolderPath;

    public static string WiiFolderPath
    {
        get
        {
            if (Settings.NAND_ROOT_PATH.IsValid())
            {
                return Settings.Get<string>(Settings.NAND_ROOT_PATH);
            }
            return Path.Combine(UserFolderPath, "Wii");
        }
    }

    public static bool IsFlatpakDolphinFilePath(string filePath) => DefaultDolphinLayout.IsFlatpakDolphinFilePath(filePath);

    public static string WheelWizardFlatpakAppId => DefaultDolphinLayout.WheelWizardFlatpakAppId;
    public const string DefaultDolphinFlatpakAppId = DolphinPathLayout.DefaultDolphinFlatpakAppId;

    public static string ExtractDolphinFlatpakAppId(string command) => DefaultDolphinLayout.ExtractDolphinFlatpakAppId(command);
}

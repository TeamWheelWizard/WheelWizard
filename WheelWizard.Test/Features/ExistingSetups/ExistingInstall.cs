using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.CustomDistributions;
using WheelWizard.DiscImages;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Dolphin.Paths;
using WheelWizard.DolphinInstaller;
using WheelWizard.GameFiles;
using WheelWizard.Launching;
using WheelWizard.Onboarding;
using WheelWizard.Recomp;
using WheelWizard.Settings;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Test.Features.ExistingSetups;

public enum HostPlatform
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>
/// An installation of Wheel Wizard as an earlier release left it on disk: a config.json, the user's Dolphin and
/// game files, and a Retro Rewind install inside Dolphin's Load folder. <see cref="Boot"/> then starts this
/// version of Wheel Wizard on top of it, with the real settings, path, migration, onboarding and startup
/// services, so a test states what an existing user has and what has to still be true after the update.
/// </summary>
internal sealed class ExistingInstall
{
    public const string RetroRewindVersion = "6.5.1";

    public HostPlatform Platform { get; }
    public MockFileSystem Fs { get; }
    public string Home { get; }
    public string DataDir { get; private set; }

    /// <summary>The value of the DolphinLocation setting as an earlier release wrote it.</summary>
    public string DolphinLocation { get; private set; } = "";

    /// <summary>The Dolphin user folder, or empty when none was configured.</summary>
    public string UserFolder { get; private set; } = "";

    public string GameFile { get; private set; } = "";
    public Dictionary<string, object?> Config { get; } = new();

    /// <summary>The file system as the app sees it; <see cref="Fs"/> is the same disk for seeding and asserting.</summary>
    public IFileSystem AppFs { get; }
    public HashSet<string> AvailableCommands { get; } = new();
    public bool IsFlatpakSandbox { get; private set; }
    public bool RecompInstalled { get; set; }

    public ExistingInstall(HostPlatform platform = HostPlatform.Windows, string? home = null, string? dataDir = null)
    {
        Platform = platform;
        Fs = new MockFileSystem(options =>
            options.SimulatingOperatingSystem(platform == HostPlatform.Windows ? SimulationMode.Windows : SimulationMode.Linux)
        );
        if (platform == HostPlatform.Windows)
        {
            Fs.WithDrive("D:");
            // Windows cannot rename a folder onto another drive.
            Fs.Intercept.Event(
                _ => throw new IOException("Source and destination path must have identical roots."),
                change =>
                    change.ChangeType == WatcherChangeTypes.Renamed
                    && change.FileSystemType == FileSystemTypes.Directory
                    && change.OldPath is not null
                    && !string.Equals(
                        Fs.Path.GetPathRoot(change.OldPath),
                        Fs.Path.GetPathRoot(change.Path),
                        StringComparison.OrdinalIgnoreCase
                    )
            );
        }

        Home =
            home
            ?? platform switch
            {
                HostPlatform.Windows => @"C:\Users\racer",
                HostPlatform.MacOS => "/Users/racer",
                _ => "/home/racer",
            };
        DataDir =
            dataDir
            ?? platform switch
            {
                HostPlatform.Windows => @"C:\Users\racer\AppData\Roaming\WheelWizard",
                HostPlatform.MacOS => "/Users/racer/Library/Application Support/WheelWizard",
                _ => "/home/racer/.local/share/WheelWizard",
            };
        Fs.Directory.CreateDirectory(DataDir);
        AppFs = RealisticFileSystem.Wrap(Fs);
    }

    /// <summary>Wheel Wizard itself runs from its Flatpak, with the Dolphin extension that bundles a Dolphin.</summary>
    public ExistingInstall WithFlatpakSandbox(bool dolphinExtension = true)
    {
        IsFlatpakSandbox = true;
        Fs.File.WriteAllText("/.flatpak-info", "");
        if (dolphinExtension)
        {
            Fs.Directory.CreateDirectory("/app/extensions/backends/dolphin-emu/bin");
            Fs.File.WriteAllText("/app/extensions/backends/dolphin-emu/bin/dolphin-emu-wrapper", "");
        }

        return this;
    }

    private readonly List<(string Original, string Stashed, bool IsDirectory)> _unplugged = [];

    /// <summary>Takes a folder or file away as if its drive was disconnected; <see cref="Replug"/> puts it all back.</summary>
    public ExistingInstall Unplug(string path)
    {
        var isDirectory = Fs.Directory.Exists(path);
        if (!isDirectory && !Fs.File.Exists(path))
            return this;

        var stashed = Combine(Fs.Path.GetPathRoot(path)!, ".unplugged", _unplugged.Count.ToString());
        Fs.Directory.CreateDirectory(Fs.Path.GetDirectoryName(stashed)!);
        if (isDirectory)
            Fs.Directory.Move(path, stashed);
        else
            Fs.File.Move(path, stashed);
        _unplugged.Add((path, stashed, isDirectory));
        return this;
    }

    public ExistingInstall Replug()
    {
        foreach (var (original, stashed, isDirectory) in Enumerable.Reverse(_unplugged))
        {
            Fs.Directory.CreateDirectory(Fs.Path.GetDirectoryName(original)!);
            if (isDirectory)
                Fs.Directory.Move(stashed, original);
            else
                Fs.File.Move(stashed, original);
        }

        _unplugged.Clear();
        return this;
    }

    /// <summary>The location of the folder that <see cref="Unplug"/> took away, while it is away.</summary>
    public string UnpluggedLocation(string original) => _unplugged.First(item => item.Original == original).Stashed;

    /// <summary>Settings > relocate data folder: everything moves and the next start uses the new location.</summary>
    public ExistingInstall RelocateDataFolder(string newDataDir)
    {
        Fs.Directory.CreateDirectory(newDataDir);
        foreach (var directory in Fs.Directory.GetDirectories(DataDir))
            Fs.Directory.Move(directory, Combine(newDataDir, Fs.Path.GetFileName(directory)));
        foreach (var file in Fs.Directory.GetFiles(DataDir))
            Fs.File.Move(file, Combine(newDataDir, Fs.Path.GetFileName(file)));
        DataDir = newDataDir;
        return this;
    }

    public string Combine(params string[] parts) => Fs.Path.Combine(parts);

    public string ConfigFilePath => Combine(DataDir, "config.json");
    public string LoadFolder => Combine(UserFolder, "Load");
    public string LegacyRoot => Combine(LoadFolder, "Riivolution", "WheelWizard");
    public string ManagedRoot => Combine(DataDir, "RetroRewind");
    public string DolphinIniPath => Combine(DolphinConfigFolder, "Dolphin.ini");
    public string DolphinConfigFolder => Resolver.Resolve(DolphinLocation, UserFolder).ConfigFolderPath;

    private IDolphinPathResolver Resolver => new DolphinPathResolver(AppFs, Environment());

    #region Seeding
    /// <summary>A Dolphin the user picked and a game file, the way the settings page wrote them.</summary>
    public ExistingInstall WithDolphin(string location, string userFolder, string? game = null, bool createFiles = true)
    {
        DolphinLocation = location;
        UserFolder = userFolder;
        Config["DolphinLocation"] = location;
        Config["UserFolderPath"] = userFolder;

        if (createFiles)
        {
            if (Platform == HostPlatform.Windows)
            {
                Fs.Directory.CreateDirectory(Fs.Path.GetDirectoryName(location)!);
                Fs.File.WriteAllText(location, "MZ");
            }
            else
            {
                AvailableCommands.Add(location);
            }

            Fs.Directory.CreateDirectory(userFolder);
            Fs.Directory.CreateDirectory(DolphinConfigFolder);
            Fs.Directory.CreateDirectory(Combine(userFolder, "Wii", "title"));
            Fs.Directory.CreateDirectory(LoadFolder);
            Fs.File.WriteAllText(DolphinIniPath, "[General]\nWirelessMac = 02:01:02:03:04:05\n[Core]\nGFXBackend = Vulkan\n");
        }

        if (game is not null)
            WithGame(game);
        return this;
    }

    public ExistingInstall WithGame(string path, bool createFile = true)
    {
        GameFile = path;
        Config["GameLocation"] = path;
        if (createFile)
        {
            Fs.Directory.CreateDirectory(Fs.Path.GetDirectoryName(path)!);
            Fs.File.WriteAllText(path, "RMCP01");
        }

        return this;
    }

    /// <summary>A real (synthetic) Mario Kart Wii image instead of a placeholder file, for setups that get extracted.</summary>
    public ExistingInstall WithRealGame(string path, string gameId = "RMCP01")
    {
        GameFile = path;
        Config["GameLocation"] = path;
        Fs.Directory.CreateDirectory(Fs.Path.GetDirectoryName(path)!);
        Fs.File.WriteAllBytes(path, new GameFiles.SyntheticWiiDisc { GameId = gameId }.Build("rvz"));
        return this;
    }

    public ExistingInstall WithConfig(string key, object? value)
    {
        Config[key] = value;
        return this;
    }

    /// <summary>Sets Dolphin's Load folder the way Dolphin records it in Dolphin.ini.</summary>
    public ExistingInstall WithCustomLoadFolder(string folder, bool exists = true)
    {
        if (exists)
            Fs.Directory.CreateDirectory(folder);
        var ini = Fs.File.ReadAllText(DolphinIniPath);
        Fs.File.WriteAllText(DolphinIniPath, ini.Replace("[General]\n", $"[General]\nLoadPath = {folder}\n"));
        return this;
    }

    /// <summary>An install a release before the managed folder left in <paramref name="root"/> (default: the legacy Load folder).</summary>
    public Dictionary<string, string> WithRetroRewind(string? root = null, bool withBeta = false, int extraFiles = 0)
    {
        root ??= LegacyRoot;
        var files = new Dictionary<string, string>
        {
            ["RetroRewind6/version.txt"] = RetroRewindVersion,
            ["RetroRewind6/Track/Luigi.szs"] = "luigi circuit",
            ["RetroRewind6/Patches/Main.bin"] = "patch",
            ["riivolution/RetroRewind6.xml"] = "<wiidisc/>",
            ["riivolution/save/RetroWFC/RMCP/rksys.dat"] = "my ghosts and 12000 VR",
        };
        if (withBeta)
        {
            files["RRBeta/version.txt"] = "6.6.0-beta";
            files["riivolution/RRBeta.xml"] = "<wiidisc beta/>";
        }

        for (var i = 0; i < extraFiles; i++)
            files[$"RetroRewind6/Track/Custom/{i / 20}/track{i}.szs"] = $"track {i}";

        foreach (var (relative, content) in files)
        {
            var path = Combine(root, relative.Replace('/', Fs.Path.DirectorySeparatorChar));
            Fs.Directory.CreateDirectory(Fs.Path.GetDirectoryName(path)!);
            Fs.File.WriteAllText(path, content);
        }

        return files;
    }

    public ExistingInstall FlushConfig()
    {
        Fs.File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
        return this;
    }
    #endregion

    #region Reading back
    /// <summary>Every file under <paramref name="folder"/> by relative path (forward slashes) with its content.</summary>
    public Dictionary<string, string> Snapshot(string folder)
    {
        var result = new Dictionary<string, string>();
        if (!Fs.Directory.Exists(folder))
            return result;
        foreach (var file in Fs.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Fs.Path.GetRelativePath(folder, file).Replace('\\', '/');
            result[relative] = Fs.File.ReadAllText(file);
        }

        return result;
    }

    public Dictionary<string, JsonElement> ReadConfig() =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Fs.File.ReadAllText(ConfigFilePath))!;
    #endregion

    #region Booting
    public IRuntimeEnvironment Environment()
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsWindows.Returns(Platform == HostPlatform.Windows);
        environment.IsLinux.Returns(Platform == HostPlatform.Linux);
        environment.IsMacOS.Returns(Platform == HostPlatform.MacOS);
        environment.OSArchitecture.Returns(Architecture.X64);
        environment
            .GetFolderPath(Arg.Any<System.Environment.SpecialFolder>())
            .Returns(call => FolderPath(call.Arg<System.Environment.SpecialFolder>()));
        environment.GetEnvironmentVariable(Arg.Any<string>()).Returns((string?)null);
        if (IsFlatpakSandbox)
            environment.GetEnvironmentVariable("FLATPAK_ID").Returns("io.github.TeamWheelWizard.WheelWizard");
        return environment;
    }

    private string FolderPath(System.Environment.SpecialFolder folder) =>
        (folder, Platform) switch
        {
            (System.Environment.SpecialFolder.UserProfile, _) => Home,
            (System.Environment.SpecialFolder.ApplicationData, HostPlatform.Windows) => Combine(Home, "AppData", "Roaming"),
            (System.Environment.SpecialFolder.LocalApplicationData, HostPlatform.Windows) => Combine(Home, "AppData", "Local"),
            (System.Environment.SpecialFolder.ApplicationData, HostPlatform.MacOS) => Combine(Home, "Library", "Application Support"),
            (System.Environment.SpecialFolder.LocalApplicationData, HostPlatform.MacOS) => Combine(Home, ".local", "share"),
            (System.Environment.SpecialFolder.ApplicationData, _) => Combine(Home, ".config"),
            (System.Environment.SpecialFolder.LocalApplicationData, _) => Combine(Home, ".local", "share"),
            _ => Home,
        };

    /// <summary>Starts Wheel Wizard on the current disk state, as a new process would, up to the first page deciding what to show.</summary>
    public Session Boot(bool runStartup = true)
    {
        var session = new Session(this);
        if (runStartup)
            session.RunStartup();
        return session;
    }
    #endregion
}

internal sealed class Session
{
    public ExistingInstall Install { get; }
    public SettingsManager Settings { get; }
    public CustomDistributionPaths Paths { get; }
    public RetroRewindMigrationService Migration { get; }
    public ManagedDolphinService ManagedDolphin { get; }
    public OnboardingService Onboarding { get; }
    public StartupMaintenance Maintenance { get; }
    public RecompDolphinDataService RecompData { get; }
    public RecompSettingManager RecompSettings { get; }
    public RecompPaths RecompPaths { get; }
    public RetroRewindLaunchDescriptor LaunchDescriptor { get; }
    public GameFilesPaths GamePaths { get; }
    public GameFilesService GameFiles { get; }
    public IDolphinPaths DolphinPaths { get; }
    public List<string?> Statuses { get; } = [];

    /// <summary>The user's files as they were before this start touched anything.</summary>
    public string? InitialConfigJson { get; }
    public string? InitialDolphinIni { get; }

    public Session(ExistingInstall install)
    {
        Install = install;
        var fs = install.AppFs;
        InitialConfigJson = fs.File.Exists(install.ConfigFilePath) ? fs.File.ReadAllText(install.ConfigFilePath) : null;
        InitialDolphinIni =
            install.UserFolder != "" && fs.File.Exists(install.DolphinIniPath) ? fs.File.ReadAllText(install.DolphinIniPath) : null;
        var environment = install.Environment();
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(install.DataDir);

        var commands = Substitute.For<IUnixCommandService>();
        commands.IsCommandAvailable(Arg.Any<string>()).Returns(call => install.AvailableCommands.Contains(call.Arg<string>()));

        var resolver = new DolphinPathResolver(fs, environment);
        RecompPaths = new RecompPaths(location, fs, environment);
        RecompSettings = new RecompSettingManager(fs);
        Settings = new SettingsManager(
            new WhWzSettingManager(NullLogger<WhWzSettingManager>.Instance, fs),
            new DolphinSettingManager(fs),
            RecompSettings,
            fs,
            new SettingsSignalBus(NullLogger<SettingsSignalBus>.Instance),
            resolver,
            location,
            RecompPaths,
            environment,
            commands
        );
        // Program.Setup: the settings are loaded before anything else looks at them.
        Settings.LoadSettings();

        DolphinPaths = new DolphinPaths(Settings, resolver, fs);
        Paths = new CustomDistributionPaths(location, DolphinPaths, fs);
        Migration = new RetroRewindMigrationService(
            Paths,
            fs,
            new DirectoryTransferService(fs),
            System.TimeProvider.System,
            NullLogger<RetroRewindMigrationService>.Instance
        );

        var discovery = Substitute.For<IDolphinDiscoveryService>();
        ManagedDolphin = new ManagedDolphinService(
            Settings,
            location,
            fs,
            environment,
            Substitute.For<IDolphinReleaseClient>(),
            Substitute.For<IDownloadService>(),
            Substitute.For<IUnixProcessService>(),
            Substitute.For<ILinuxDolphinInstaller>(),
            discovery,
            resolver,
            Substitute.For<IDolphinVersionService>(),
            NullLogger<ManagedDolphinService>.Instance
        );

        RecompData = new RecompDolphinDataService(Settings, RecompSettings, fs, discovery, RecompPaths, Paths);
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IRecompDolphinDataService)).Returns(RecompData);
        var recompInstall = Substitute.For<IRecompInstallService>();
        recompInstall.IsInstalled.Returns(install.RecompInstalled);
        services.GetService(typeof(IRecompInstallService)).Returns(recompInstall);
        Onboarding = new OnboardingService(
            Settings,
            fs,
            environment,
            Substitute.For<IDiscImageService>(),
            discovery,
            resolver,
            commands,
            Substitute.For<ILinuxDolphinInstaller>(),
            ManagedDolphin,
            Substitute.For<IMiiRepositoryService>(),
            Substitute.For<IGameLicenseSingletonService>(),
            Paths,
            services,
            NullLogger<OnboardingService>.Instance
        );
        GamePaths = new GameFilesPaths(location, fs);
        GameFiles = new GameFilesService(
            GamePaths,
            new DiscExtractor(fs),
            Settings,
            fs,
            Substitute.For<IProcessLauncher>(),
            DolphinPaths,
            new DiscImageService(fs),
            NullLogger<GameFilesService>.Instance
        );
        Maintenance = new StartupMaintenance(
            ManagedDolphin,
            Migration,
            Onboarding,
            GameFiles,
            services,
            NullLogger<StartupMaintenance>.Instance
        );
        LaunchDescriptor = new RetroRewindLaunchDescriptor(fs, Paths, GamePaths);
    }

    /// <summary>What Desktop startup runs before the main window decides which page to open.</summary>
    public void RunStartup() => Maintenance.RunAsync(Statuses.Add).GetAwaiter().GetResult();

    public string ReadLaunchRoot()
    {
        LaunchDescriptor.GenerateLaunchJson();
        using var document = JsonDocument.Parse(Install.Fs.File.ReadAllText(Paths.LaunchJsonFilePath));
        return document.RootElement.GetProperty("riivolution").GetProperty("patches")[0].GetProperty("root").GetString()!;
    }

    public string ReadLaunchXml()
    {
        LaunchDescriptor.GenerateLaunchJson();
        using var document = JsonDocument.Parse(Install.Fs.File.ReadAllText(Paths.LaunchJsonFilePath));
        return document.RootElement.GetProperty("riivolution").GetProperty("patches")[0].GetProperty("xml").GetString()!;
    }

    /// <summary>The Retro Rewind version Wheel Wizard would report; this is what decides between "Play" and "Install".</summary>
    public string? InstalledRetroRewindVersion()
    {
        var file = Install.Combine(Paths.RetroRewindFolderPath, "version.txt");
        return Install.Fs.File.Exists(file) ? Install.Fs.File.ReadAllText(file).Trim() : null;
    }
}

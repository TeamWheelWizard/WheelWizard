using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.DiscImages;
using WheelWizard.Dolphin.Paths;
using WheelWizard.GameFiles;
using WheelWizard.GameFiles.Domain;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Processes;

namespace WheelWizard.Test.Features.GameFiles;

/// <summary>
/// A data folder, some game images and the real game files service, over a mock disk or (for links, which the mock
/// does not follow) a temporary folder on the real one. Every image it writes is remembered, so a test can assert at
/// the end that none of them was changed in any way.
/// </summary>
internal sealed class GameFilesHarness : IDisposable
{
    private readonly string? _realRoot;
    private readonly Dictionary<string, (byte[] Bytes, DateTime LastWrite)> _images = new();

    public IFileSystem Fs { get; }
    public SimulationMode Mode { get; }
    public string DataDir { get; }
    public string GamesDir { get; }
    public IProcessLauncher Processes { get; } = Substitute.For<IProcessLauncher>();
    public WhWzSetting<string> GameLocation { get; } =
        (WhWzSetting<string>)new WhWzSetting<string>("GameLocation", "").SetRetainUnavailable();
    public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
    public GameFilesPaths Paths { get; }
    public GameFilesService Service { get; }
    public DiscExtractor Extractor { get; }

    public GameFilesHarness(SimulationMode mode = SimulationMode.Linux, string? dataDir = null, Action<MockFileSystem>? configure = null)
        : this(CreateMock(mode, configure), mode, dataDir, realRoot: null) { }

    private static MockFileSystem CreateMock(SimulationMode mode, Action<MockFileSystem>? configure)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(mode));
        configure?.Invoke(fs);
        return fs;
    }

    /// <summary>A harness on the real disk, in a fresh temporary folder that is removed again on dispose.</summary>
    public static GameFilesHarness OnRealDisk()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ww-gamefiles-" + Guid.NewGuid().ToString("N"));
        var mode =
            OperatingSystem.IsWindows() ? SimulationMode.Windows
            : OperatingSystem.IsMacOS() ? SimulationMode.MacOS
            : SimulationMode.Linux;
        return new GameFilesHarness(new Testably.Abstractions.RealFileSystem(), mode, System.IO.Path.Combine(root, "CT-MKWII"), root);
    }

    private GameFilesHarness(IFileSystem fs, SimulationMode mode, string? dataDir, string? realRoot)
    {
        Mode = mode;
        Fs = fs;
        _realRoot = realRoot;
        DataDir =
            dataDir ?? (mode == SimulationMode.Windows ? @"C:\Users\Player\AppData\Roaming\CT-MKWII" : "/home/player/.config/CT-MKWII");
        GamesDir =
            realRoot is not null ? Fs.Path.Combine(realRoot, "Wii Games")
            : mode == SimulationMode.Windows ? @"C:\Games\Wii Games"
            : "/home/player/Games/Wii Games";
        Fs.Directory.CreateDirectory(DataDir);
        Fs.Directory.CreateDirectory(GamesDir);

        Settings.GAME_LOCATION.Returns(GameLocation);
        Settings.Get(GameLocation).Returns(_ => (string)GameLocation.Get());
        Settings
            .Set(GameLocation, Arg.Any<string>(), Arg.Any<bool>())
            .Returns(call => GameLocation.Set(call.ArgAt<string>(1), skipSave: true));

        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(_ => DataDir);
        Paths = new GameFilesPaths(location, Fs);
        Extractor = new DiscExtractor(Fs);
        Service = new GameFilesService(
            Paths,
            Extractor,
            Settings,
            Fs,
            Processes,
            Substitute.For<IDolphinPaths>(),
            new DiscImageService(Fs),
            NullLogger<GameFilesService>.Instance
        );
    }

    public string Game => Paths.RootPath;

    /// <summary>A folder next to the data folder, standing in for another drive.</summary>
    public string Elsewhere(string name) => Fs.Path.Combine(_realRoot ?? "/mnt", "other drive", name);

    /// <summary>
    /// Creates a directory link: a symlink, or on Windows without the right to make one, a junction (which is what
    /// most Windows users make anyway). False only when neither can be made here.
    /// </summary>
    public bool TryLink(string link, string target)
    {
        try
        {
            Fs.Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            if (!OperatingSystem.IsWindows())
                return false;
        }

        var junction = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", link, target },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        )!;
        junction.WaitForExit();
        return junction.ExitCode == 0 && Fs.DirectoryInfo.New(link).LinkTarget is not null;
    }

    public void Dispose()
    {
        if (_realRoot is null || !Directory.Exists(_realRoot))
            return;
        // A recursive delete trips over junctions on Windows, so links are removed on their own first.
        RemoveLinks(_realRoot);
        Directory.Delete(_realRoot, recursive: true);
    }

    private static void RemoveLinks(string folder)
    {
        foreach (var directory in Directory.GetDirectories(folder))
        {
            if (new DirectoryInfo(directory).LinkTarget is null)
                RemoveLinks(directory);
            else if (OperatingSystem.IsWindows())
                Directory.Delete(directory);
            else
                File.Delete(directory);
        }
    }

    public string In(params string[] parts) => Fs.Path.Combine([Game, .. parts]);

    /// <summary>Writes a game image and remembers exactly what it looked like.</summary>
    public string WriteImage(SyntheticWiiDisc disc, string name = "Mario Kart Wii (Europe).rvz", string format = "rvz")
    {
        var path = Fs.Path.Combine(GamesDir, name);
        Fs.File.WriteAllBytes(path, disc.Build(format));
        _images[path] = (Fs.File.ReadAllBytes(path), Fs.File.GetLastWriteTimeUtc(path));
        return path;
    }

    public string WriteImage(string gameId = "RMCP01", string? name = null) =>
        WriteImage(new SyntheticWiiDisc { GameId = gameId }, name ?? $"{gameId}.rvz");

    /// <summary>The one promise above all others: Wheel Wizard never changes, moves or deletes a game image.</summary>
    public void AssertImagesUntouched()
    {
        foreach (var (path, (bytes, lastWrite)) in _images)
        {
            Assert.True(Fs.File.Exists(path), $"The game image {path} is gone.");
            Assert.Equal(bytes, Fs.File.ReadAllBytes(path));
            Assert.Equal(lastWrite, Fs.File.GetLastWriteTimeUtc(path));
        }
    }

    public async Task ExtractAsync(string image, bool replace = false)
    {
        var result = await Service.ExtractAsync(image, replace);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    public GameFilesManifest? Manifest(string? manifestPath = null)
    {
        var path = manifestPath ?? Paths.ManifestPath;
        return Fs.File.Exists(path) ? JsonSerializer.Deserialize<GameFilesManifest>(Fs.File.ReadAllText(path)) : null;
    }

    /// <summary>Lays a finished extraction into <see cref="GameFilesPaths.StagingPath"/>, as an interrupted swap leaves it.</summary>
    public void Stage(string image, string gameId)
    {
        var staging = Paths.StagingPath;
        Fs.Directory.CreateDirectory(staging);
        Fs.File.WriteAllText(Fs.Path.Combine(staging, GameFilesPaths.WorkMarkerFileName), "");
        var code = Extractor.Extract(image, staging);
        Assert.True(code.IsSuccess, code.IsFailure ? code.Error.Message : null);
        Fs.File.WriteAllText(
            Fs.Path.Combine(staging, GameFilesPaths.ManifestFileName),
            JsonSerializer.Serialize(
                new GameFilesManifest
                {
                    GameId = gameId,
                    SourcePath = image,
                    MainDolSha256 = code.Value.MainDolSha256,
                    StaticRelSha256 = code.Value.StaticRelSha256,
                }
            )
        );
    }

    public string[] Siblings() => Fs.Directory.GetFileSystemEntries(Fs.Path.GetDirectoryName(Paths.ResolvedRootPath)!);

    public void SetRunning(params string[] processNames)
    {
        Processes.GetProcessIds(Arg.Any<string>()).Returns(Array.Empty<int>());
        foreach (var name in processNames)
            Processes.GetProcessIds(name).Returns([4242]);
    }
}

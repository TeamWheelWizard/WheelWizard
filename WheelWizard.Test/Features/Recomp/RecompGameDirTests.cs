using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;
using WheelWizard.Models.Enums;
using WheelWizard.Recomp;
using WheelWizard.Recomp.Domain;
using WheelWizard.Settings;
using WheelWizard.Shared;

namespace WheelWizard.Test.Features.Recomp;

/// <summary>
/// WiiCompiled reads Wheel Wizard's extracted game in place. These pin the contract with its setup (the
/// capability probe and the link command) and what Wheel Wizard does with an installation that still reads a game
/// copy of its own.
/// </summary>
public class RecompGameDirTests
{
    [Theory]
    [InlineData("""{"type":"capabilities","capabilities":["game-dir"]}""", true)]
    [InlineData("""{"type":"capabilities","capabilities":["GAME-DIR","other"]}""", true)]
    [InlineData("""{"type":"capabilities","capabilities":[]}""", false)]
    [InlineData("""{"type":"capabilities"}""", false)]
    [InlineData("""{"type":"capabilities","capabilities":[1,null]}""", false)]
    public void Capabilities_AreParsed(string line, bool gameDir)
    {
        var parsed = Assert.IsType<RecompCapabilitiesEvent>(RecompSetupOutputParser.Parse(line));
        Assert.Equal(gameDir, parsed.Has(RecompCapabilitiesEvent.GameDir));
    }

    [Fact]
    public void LinkCommand_NamesTheInstallationAndTheExtractedGame()
    {
        Assert.Equal(
            ["--link-game-dir", "--install-dir", @"C:\WW\Recomp\Install", "--game-dir", @"C:\WW\GAME", "--progress-json"],
            RecompSetupCommandBuilder.BuildLinkGameDirArguments(@"C:\WW\Recomp\Install", @"C:\WW\GAME")
        );
        Assert.Equal(
            ["link-game-dir", "--game-dir", "/data/GAME", "--progress-json"],
            RecompLinuxSetupCommandBuilder.BuildLinkGameDirArguments("/data/GAME")
        );
        Assert.Equal(["--capabilities"], RecompSetupCommandBuilder.BuildCapabilitiesArguments());
    }

    [Fact]
    public void SilentInstall_NeverPassesAnImage()
    {
        var arguments = RecompSetupCommandBuilder.BuildSilentInstallArguments(
            new() { GameFolderPath = @"C:\WW\GAME", InstallFolderPath = @"C:\WW\Recomp\Install" }
        );

        Assert.Contains("--game-dir", arguments);
        Assert.DoesNotContain("--game", arguments);
    }

    [Fact]
    public async Task SupportsGameDir_AnOldSetupThatRejectsTheProbe_DoesNot()
    {
        var runner = Substitute.For<IRecompProcessRunner>();
        runner
            .RunAsync(
                "setup.exe",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Ok(2));
        var hosts = Hosts(runner);

        Assert.False(await hosts.SupportsGameDirAsync("setup.exe", CancellationToken.None));
    }

    [Fact]
    public async Task SupportsGameDir_ASetupThatSaysSo_Does()
    {
        var runner = Substitute.For<IRecompProcessRunner>();
        runner
            .RunAsync(
                "setup.exe",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                call.ArgAt<Action<string>?>(3)?.Invoke("""{"type":"capabilities","capabilities":["game-dir"]}""");
                return Ok(0);
            });

        Assert.True(await Hosts(runner).SupportsGameDirAsync("setup.exe", CancellationToken.None));
    }

    [Fact]
    public async Task Status_AnInstallationReadingItsOwnCopy_AsksForAnUpdate()
    {
        var fixture = new Fixture(supportsGameDir: false);

        Assert.Equal(WheelWizardStatus.OutOfDate, await fixture.Service.GetCurrentStatusAsync());
    }

    [Fact]
    public async Task Link_AnOldSetup_FailsAndChangesNothing()
    {
        var fixture = new Fixture(supportsGameDir: false);

        var result = await fixture.Service.LinkGameDataAsync();

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(fixture.Calls, call => call.Contains("--link-game-dir"));
        Assert.Null(fixture.State().GameDataDir);
    }

    [Fact]
    public async Task Link_ASetupThatCan_PointsItAtTheExtractedGame()
    {
        var fixture = new Fixture(supportsGameDir: true);

        var result = await fixture.Service.LinkGameDataAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(
            fixture.Calls,
            call => call.SequenceEqual(RecompSetupCommandBuilder.BuildLinkGameDirArguments(Fixture.Install, Fixture.Game))
        );
        Assert.Equal(Fixture.Game + "/DATA", fixture.State().GameDataDir!.Replace('\\', '/'));
    }

    [Fact]
    public async Task Link_AlreadyLinked_RunsNothing()
    {
        var fixture = new Fixture(supportsGameDir: true, linked: true);

        Assert.True((await fixture.Service.LinkGameDataAsync()).IsSuccess);
        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public async Task Link_WithoutAnExtractedGame_RunsNothing()
    {
        var fixture = new Fixture(supportsGameDir: true, gameReady: false);

        Assert.True((await fixture.Service.LinkGameDataAsync()).IsFailure);
        Assert.DoesNotContain(fixture.Calls, call => call.Contains("--link-game-dir"));
    }

    [Fact]
    public async Task Link_NothingInstalled_IsNothingToDo()
    {
        var fixture = new Fixture(supportsGameDir: true, installed: false);

        Assert.True((await fixture.Service.LinkGameDataAsync()).IsSuccess);
        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public async Task Launch_AnInstallationReadingItsOwnCopy_IsRefused()
    {
        var fixture = new Fixture(supportsGameDir: false);

        Assert.True((await fixture.Service.ReconcileForLaunchAsync()).IsFailure);
        Assert.DoesNotContain(fixture.Calls, call => call.Contains("--repair-products"));
    }

    private static RecompSetupHostAcquirer Hosts(IRecompProcessRunner runner)
    {
        var github = Substitute.For<IGitHubSingletonService>();
        github
            .GetReleasesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(new OperationResult<List<GithubRelease>>(new OperationError { Message = "offline" }));
        return new RecompSetupHostAcquirer(
            runner,
            Substitute.For<IRecompSetupDownloader>(),
            github,
            new MockFileSystem(),
            NullLogger<RecompSetupHostAcquirer>.Instance
        );
    }

    private sealed class Fixture
    {
        public const string Install = "/ww/Recomp/Install";
        public const string Game = "/ww/GAME";
        private const string Setup = Install + "/WiiCompiled-Setup.exe";
        private const string StateFile = Install + "/install-state.json";

        private readonly MockFileSystem _fs = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public RecompInstallService Service { get; }

        public Fixture(bool supportsGameDir, bool linked = false, bool gameReady = true, bool installed = true)
        {
            _fs.Directory.CreateDirectory(Install);
            if (installed)
            {
                _fs.File.WriteAllText(Setup, "setup");
                WriteState(linked ? Game + "/DATA" : null);
            }

            var environment = Substitute.For<IRecompEnvironment>();
            environment.InstallFolderPath.Returns(Install);
            environment.InstalledSetupFilePath.Returns(Setup);
            environment.InstallStateFilePath.Returns(StateFile);
            environment.BackendStateFilePath.Returns(StateFile);
            environment.GameFolderPath.Returns(Game);
            environment.IsGameReady.Returns(gameReady);
            environment.RetroRewindFolderPath.Returns("/ww/RetroRewind/RetroRewind6");
            environment.CacheFolderPath.Returns("/ww/Recomp/Cache");
            environment.GetExtensionConfigurationInfo.Returns(ExtensionConfigurationInfo.ExtensionFoundOrNotNeeded);

            var runner = Substitute.For<IRecompProcessRunner>();
            runner
                .RunAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<string?>(),
                    Arg.Any<Action<string>?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    var arguments = call.ArgAt<IReadOnlyList<string>>(1);
                    var output = call.ArgAt<Action<string>?>(3);
                    if (arguments[0] == "--version")
                    {
                        output?.Invoke("0.3.0");
                        return Ok(0);
                    }

                    if (arguments[0] == "--capabilities")
                    {
                        if (!supportsGameDir)
                            return Ok(1);
                        output?.Invoke("""{"type":"capabilities","capabilities":["game-dir"]}""");
                        return Ok(0);
                    }

                    Calls.Add(arguments);
                    if (arguments[0] == "--link-game-dir")
                    {
                        WriteState(arguments[^2] + "/DATA");
                        output?.Invoke("""{"type":"result","success":true,"version":"0.3.0","installDir":"/ww/Recomp/Install"}""");
                    }

                    return Ok(0);
                });

            Service = new RecompInstallService(
                environment,
                runner,
                Hosts(runner),
                Substitute.For<IRecompRetroWfcPayloadProbe>(),
                _fs,
                NullLogger<RecompInstallService>.Instance
            );
        }

        private void WriteState(string? gameDataDir) =>
            _fs.File.WriteAllText(
                StateFile,
                JsonSerializer.Serialize(
                    new RecompInstallState
                    {
                        SchemaVersion = 1,
                        SetupVersion = "0.3.0",
                        InstallDir = Install,
                        RetroRewindInstalled = true,
                        RetroWfcPayloadMode = "downloaded",
                        GameDataDir = gameDataDir,
                    }
                )
            );

        public RecompInstallState State() => JsonSerializer.Deserialize<RecompInstallState>(_fs.File.ReadAllText(StateFile))!;
    }
}

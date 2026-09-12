using System.Diagnostics;
using Testably.Abstractions.Testing;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.DolphinInstaller;
using WheelWizard.Launching;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;

namespace WheelWizard.Test.Features.Launching;

public class DolphinLaunchServiceTests
{
    [Theory]
    [InlineData(DolphinVersionAction.Cancel)]
    [InlineData(DolphinVersionAction.Update)]
    public async Task OutdatedVersion_BlocksLaunchUnlessPlayAnywayWasExplicit(DolphinVersionAction action)
    {
        var fixture = new Fixture();
        fixture.Versions.CheckConfiguredDolphin().Returns((DolphinVersionStatus.Outdated, "2603"));
        fixture.Presentation.ChooseOutdatedVersionActionAsync("2603").Returns(action);

        var result = await fixture.Service.LaunchDolphin();

        Assert.True(result.IsFailure);
        fixture.Processes.DidNotReceive().Start(Arg.Any<ProcessStartInfo>());
        if (action == DolphinVersionAction.Update)
            fixture.Presentation.Received().OpenUpdateInstructions(false);
    }

    [Fact]
    public async Task ExplicitPlayAnyway_StartsDolphin_WithoutUpdating()
    {
        var fixture = new Fixture();
        fixture.Versions.CheckConfiguredDolphin().Returns((DolphinVersionStatus.Outdated, "2603"));
        fixture.Presentation.ChooseOutdatedVersionActionAsync("2603").Returns(DolphinVersionAction.PlayAnyway);

        Assert.True((await fixture.Service.LaunchDolphin()).IsSuccess);

        fixture.Processes.Received(1).Start(Arg.Any<ProcessStartInfo>());
        fixture.Presentation.DidNotReceive().OpenUpdateInstructions(Arg.Any<bool>());
    }

    [Fact]
    public async Task UnknownVersion_WarnsBeforeStarting()
    {
        var fixture = new Fixture();
        fixture.Versions.CheckConfiguredDolphin().Returns((DolphinVersionStatus.Unknown, null));
        var warning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Presentation.ShowUnverifiedVersionAsync().Returns(warning.Task);
        var launch = fixture.Service.LaunchDolphin();
        fixture.Processes.DidNotReceive().Start(Arg.Any<ProcessStartInfo>());
        warning.SetResult();

        Assert.True((await launch).IsSuccess);
        await fixture.Presentation.Received(1).ShowUnverifiedVersionAsync();
    }

    [Fact]
    public async Task FailedPreflight_HasNoProcessOrPermissionSideEffects()
    {
        var fixture = new Fixture();

        Assert.True((await fixture.Service.LaunchDolphin(versionPreflightResult: Fail("cancelled"))).IsFailure);

        Assert.Empty(fixture.Processes.ReceivedCalls());
        Assert.Empty(fixture.LinuxProcesses.ReceivedCalls());
        fixture.Versions.DidNotReceive().CheckConfiguredDolphin();
    }

    [Fact]
    public async Task WindowsLaunch_PreservesArgumentsAndDoesNotRepeatPreflight()
    {
        var fixture = new Fixture(windows: true);

        Assert.True((await fixture.Service.LaunchDolphin("-b -e game.json", true, Ok())).IsSuccess);

        fixture
            .Processes.Received(1)
            .Start(
                Arg.Is<ProcessStartInfo>(info =>
                    info.FileName == @"C:\Dolphin\Dolphin.exe"
                    && info.Arguments == "-b -e game.json -u \"C:\\Dolphin User\""
                    && info.UseShellExecute
                )
            );
        fixture.Versions.DidNotReceive().CheckConfiguredDolphin();
    }

    [Fact]
    public async Task StartFailure_ReturnsErrorAndShowsReason()
    {
        var fixture = new Fixture();
        fixture.Processes.When(process => process.Start(Arg.Any<ProcessStartInfo>())).Do(_ => throw new IOException("missing executable"));

        var result = await fixture.Service.LaunchDolphin(versionPreflightResult: Ok());

        Assert.True(result.IsFailure);
        Assert.IsType<IOException>(result.Error.Exception);
        fixture.Presentation.Received().ShowLaunchFailure("missing executable");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task PortalExport_OnlySkipsFilesystemPermissionAfterSuccessfulExit(int exitCode, bool expectedFallback)
    {
        var fixture = new Fixture(command: "flatpak run org.DolphinEmu.dolphin-emu");
        fixture.GamePath.Set("/run/user/1000/doc/game/game.iso", skipSave: true);
        fixture
            .LinuxProcesses.Run("flatpak", Arg.Any<IEnumerable<string>>(), out Arg.Any<string>(), out Arg.Any<string>())
            .Returns(Ok(exitCode));
        ProcessStartInfo? captured = null;
        fixture.Processes.When(process => process.Start(Arg.Any<ProcessStartInfo>())).Do(call => captured = call.Arg<ProcessStartInfo>());

        Assert.True((await fixture.Service.LaunchDolphin(versionPreflightResult: Ok())).IsSuccess);

        Assert.NotNull(captured);
        Assert.Equal(expectedFallback, captured.ArgumentList[^1].Contains("--filesystem='/run/user/1000/doc/game/game.iso':ro"));
        fixture
            .LinuxProcesses.Received(1)
            .Run(
                "flatpak",
                Arg.Is<IEnumerable<string>>(args => args.Contains("/run/user/1000/doc/game/game.iso")),
                out Arg.Any<string>(),
                out Arg.Any<string>()
            );
    }

    [Fact]
    public async Task FlatpakUpdate_UsesConfiguredAppIdAndStillBlocksLaunch()
    {
        var fixture = new Fixture(command: "flatpak run org.example.DolphinFork");
        fixture.Versions.CheckConfiguredDolphin().Returns((DolphinVersionStatus.Outdated, "2603"));
        fixture.Presentation.ChooseOutdatedVersionActionAsync("2603").Returns(DolphinVersionAction.Update);
        fixture
            .Presentation.RunUpdateAsync(Arg.Any<Func<IProgress<int>, Task<OperationResult>>>())
            .Returns(call => call.Arg<Func<IProgress<int>, Task<OperationResult>>>()(new Progress<int>()));
        fixture.Installer.UpdateFlatpakDolphin(Arg.Any<string>(), Arg.Any<IProgress<int>>()).Returns(Ok());

        Assert.True((await fixture.Service.LaunchDolphin()).IsFailure);

        await fixture.Installer.Received(1).UpdateFlatpakDolphin("org.example.DolphinFork", Arg.Any<IProgress<int>>());
        fixture.Processes.DidNotReceive().Start(Arg.Any<ProcessStartInfo>());
    }

    private sealed class Fixture
    {
        public IProcessLauncher Processes { get; } = Substitute.For<IProcessLauncher>();
        public ILinuxProcessService LinuxProcesses { get; } = Substitute.For<ILinuxProcessService>();
        public IDolphinVersionService Versions { get; } = Substitute.For<IDolphinVersionService>();
        public ILinuxDolphinInstaller Installer { get; } = Substitute.For<ILinuxDolphinInstaller>();
        public IDolphinLaunchPresentation Presentation { get; } = Substitute.For<IDolphinLaunchPresentation>();
        public WhWzSetting GamePath { get; } = new(typeof(string), "GamePath", "/game.iso");
        public DolphinLaunchService Service { get; }

        public Fixture(bool windows = false, string command = "dolphin-emu")
        {
            var fs = new MockFileSystem(options =>
                options.SimulatingOperatingSystem(windows ? SimulationMode.Windows : SimulationMode.Linux)
            );
            var environment = Substitute.For<IRuntimeEnvironment>();
            environment.IsWindows.Returns(windows);
            environment.IsLinux.Returns(!windows);
            environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Returns("/home/test");
            environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Returns("/home/test/.config");
            environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Returns("/home/test/.local/share");
            var settings = Substitute.For<ISettingsManager>();
            settings.GAME_LOCATION.Returns(GamePath);
            settings.Get<string>(GamePath).Returns(_ => (string)GamePath.Get());
            var paths = Substitute.For<IDolphinPaths>();
            var user = windows ? @"C:\Dolphin User" : "/dolphin-user";
            paths.UserFolderPath.Returns(user);
            paths.ExecutablePath.Returns(windows ? @"C:\Dolphin\Dolphin.exe" : command);
            paths.Layout.Returns(new DolphinPathLayout(fs.Path, environment, false, command, user));
            var distribution = Substitute.For<ICustomDistributionPaths>();
            distribution.RootFolderPath.Returns("/distribution");
            distribution.LaunchJsonFilePath.Returns("/distribution/RR.json");
            Versions.CheckConfiguredDolphin().Returns((DolphinVersionStatus.Supported, "2606a"));
            Service = new(settings, paths, distribution, fs, environment, Processes, LinuxProcesses, Versions, Installer, Presentation);
        }
    }
}

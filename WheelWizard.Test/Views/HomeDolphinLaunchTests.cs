using System.Diagnostics;
using Testably.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.DolphinInstaller;
using WheelWizard.Launching;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;
using WheelWizard.Shared.Calendar;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.Views.Pages;
using TimeProvider = System.TimeProvider;

namespace WheelWizard.Test.Views;

public class HomeDolphinLaunchTests
{
    [Fact]
    public async Task UnexpectedException_IsReportedAndRestoresInteraction()
    {
        var dolphin = Substitute.For<IDolphinLaunchService>();
        var presentation = Substitute.For<IHomePresentation>();
        dolphin.LaunchDolphin().Returns(Task.FromException<OperationResult>(new IOException("unexpected failure")));
        using var model = CreateHomeModel(dolphin, presentation);
        await model.RefreshAsync();

        await model.LaunchDolphinAsync();

        presentation.Received(1).ShowError(Arg.Is<OperationError>(error => error.Exception is IOException));
        Assert.True(model.IsInteractable);
        Assert.True(model.CanLaunchDolphin);
    }

    private static HomeViewModel CreateHomeModel(IDolphinLaunchService dolphin, IHomePresentation presentation)
    {
        var launcher = Substitute.For<ILauncher>();
        launcher.GetCurrentStatus().Returns(WheelWizardStatus.Ready);
        var provider = Substitute.For<ILauncherProvider>();
        provider.GetActiveLauncher().Returns(launcher);
        var settings = Substitute.For<ISettingsManager>();
        settings.PathsSetupCorrectly().Returns(true);
        return new(
            provider,
            dolphin,
            settings,
            presentation,
            Substitute.For<ISeasonalCalendar>(),
            Substitute.For<IRandomSystem>(),
            new ElapsedTimeProvider()
        );
    }

    private sealed class ElapsedTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Interlocked.Add(ref _timestamp, 3000);
    }

    [Theory]
    [InlineData(DolphinVersionAction.Cancel)]
    [InlineData(DolphinVersionAction.Update)]
    public async Task DirectDolphin_HandledPreflightChoiceMustNotShowError(DolphinVersionAction choice)
    {
        var d = new Fixture();
        d.Versions.CheckConfiguredDolphin().Returns((DolphinVersionStatus.Outdated, "2603"));
        d.Presentation.ChooseOutdatedVersionActionAsync("2603").Returns(choice);
        var homePresentation = Substitute.For<IHomePresentation>();
        using var model = CreateHomeModel(d.Service, homePresentation);
        await model.RefreshAsync();
        await model.LaunchDolphinAsync();
        d.Processes.DidNotReceive().Start(Arg.Any<ProcessStartInfo>());
        homePresentation.DidNotReceive().ShowError(Arg.Any<OperationError>());
    }

    [Fact]
    public async Task DirectFlatpakDolphin_SuccessfulUpdateMustNotShowError()
    {
        var d = new Fixture(command: "flatpak run org.example.DolphinFork");
        d.Versions.CheckConfiguredDolphin().Returns((DolphinVersionStatus.Outdated, "2603"));
        d.Presentation.ChooseOutdatedVersionActionAsync("2603").Returns(DolphinVersionAction.Update);
        d.Presentation.RunUpdateAsync(Arg.Any<Func<IProgress<int>, Task<OperationResult>>>())
            .Returns(call => call.Arg<Func<IProgress<int>, Task<OperationResult>>>()(new Progress<int>()));
        d.Installer.UpdateFlatpakDolphin(Arg.Any<string>(), Arg.Any<IProgress<int>>()).Returns(Ok());
        var homePresentation = Substitute.For<IHomePresentation>();
        using var model = CreateHomeModel(d.Service, homePresentation);
        await model.RefreshAsync();
        await model.LaunchDolphinAsync();
        await d.Installer.Received(1).UpdateFlatpakDolphin("org.example.DolphinFork", Arg.Any<IProgress<int>>());
        homePresentation.DidNotReceive().ShowError(Arg.Any<OperationError>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectDolphin_StartFailureMustBeShownOnlyOnce(bool windows)
    {
        var d = new Fixture(windows);
        d.Processes.When(x => x.Start(Arg.Any<ProcessStartInfo>())).Do(_ => throw new IOException("missing executable"));
        var homePresentation = Substitute.For<IHomePresentation>();
        using var model = CreateHomeModel(d.Service, homePresentation);
        await model.RefreshAsync();
        await model.LaunchDolphinAsync();
        d.Presentation.Received(1).ShowLaunchFailure("missing executable");
        homePresentation.DidNotReceive().ShowError(Arg.Any<OperationError>());
    }

    private sealed class Fixture
    {
        public IProcessLauncher Processes { get; } = Substitute.For<IProcessLauncher>();
        public IUnixProcessService LinuxProcesses { get; } = Substitute.For<IUnixProcessService>();
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

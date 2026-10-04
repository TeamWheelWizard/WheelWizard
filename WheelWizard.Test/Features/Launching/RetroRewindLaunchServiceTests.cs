using Testably.Abstractions.Testing;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Launching;
using WheelWizard.Mods;
using WheelWizard.Recomp;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;
using WheelWizard.Shared.Platform;
using WheelWizard.WiiManagement.Controllers;

namespace WheelWizard.Test.Features.Launching;

public class RetroRewindLaunchServiceTests
{
    [Theory]
    [InlineData(false, "/stable-patches", "/stable.xml")]
    [InlineData(true, "/beta-patches", "/beta.xml")]
    public async Task Launch_UsesSelectedDistribution_AndOneSuccessfulPreflight(bool beta, string patches, string xml)
    {
        var fixture = new Fixture();
        fixture.Mods.ShouldAskToClearTargetFolder(patches).Returns(true);
        fixture.Prompts.ConfirmPatchCleanupAsync().Returns(true);

        Assert.True((await fixture.Service.LaunchAsync(beta)).IsSuccess);

        await fixture.Mods.Received(1).PrepareModsForLaunch(patches, true, Arg.Any<IProgress<ModOperationProgress>>());
        fixture.Descriptor.Received(1).GenerateLaunchJson(xml);
        fixture.Remotes.Received(1).SetVirtualRemoteEnabled("/dolphin/Config", false);
        await fixture.Dolphin.Received(1).PreflightDolphinVersionAsync();
        await fixture
            .Dolphin.Received(1)
            .LaunchDolphin(
                "-b -e '/app/RR.json' --config=Dolphin.Core.EnableCheats=False --config=Achievements.Achievements.Enabled=False",
                false,
                Arg.Is<OperationResult>(result => result.IsSuccess)
            );
    }

    [Fact]
    public async Task FailedModPreparation_DoesNotWriteDescriptorOrStartGame()
    {
        var fixture = new Fixture();
        fixture
            .Mods.PrepareModsForLaunch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IProgress<ModOperationProgress>>())
            .Returns(Fail("preparation failed"));

        Assert.True((await fixture.Service.LaunchAsync()).IsFailure);

        Assert.Empty(fixture.Descriptor.ReceivedCalls());
        await fixture.Dolphin.DidNotReceive().LaunchDolphin(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<OperationResult>());
    }

    [Fact]
    public async Task MissingBetaGame_StopsBeforeVersionCheckOrPreparation()
    {
        var fixture = new Fixture();
        fixture.Fs.File.Delete("/game.iso");

        Assert.True((await fixture.Service.LaunchAsync(true)).IsFailure);

        Assert.Empty(fixture.Dolphin.ReceivedCalls());
        Assert.Empty(fixture.Mods.ReceivedCalls());
    }

    [Fact]
    public void LauncherSelection_OnlyConstructsTheSelectedFrontend()
    {
        var settings = Substitute.For<ISettingsManager>();
        var distribution = Substitute.For<ICustomDistributionSingletonService>();
        var rr = new RrLauncher(
            Substitute.For<IRetroRewindLaunchService>(),
            distribution,
            Substitute.For<IDistributionOperationPresentation>()
        );
        var recomp = new RecompLauncher(
            Substitute.For<IRecompInstallService>(),
            distribution,
            Substitute.For<IModsLaunchService>(),
            Substitute.For<IRecompDolphinDataService>(),
            Substitute.For<ICustomDistributionPaths>(),
            new InlineModPresentation(),
            Substitute.For<IRecompPresentation>(),
            Substitute.For<ILaunchPrompts>()
        );
        var rrCount = 0;
        var recompCount = 0;
        var provider = new LauncherProvider(
            settings,
            () =>
            {
                rrCount++;
                return rr;
            },
            () =>
            {
                recompCount++;
                return recomp;
            }
        );

        Assert.Same(rr, provider.GetActiveLauncher());
        Assert.Equal(0, recompCount);
        settings.IsRecompModeActive().Returns(true);
        Assert.Same(recomp, provider.GetActiveLauncher());
        Assert.Equal(1, rrCount);
        Assert.Equal(1, recompCount);
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; } = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        public IDolphinLaunchService Dolphin { get; } = Substitute.For<IDolphinLaunchService>();
        public IModsLaunchService Mods { get; } = Substitute.For<IModsLaunchService>();
        public IRetroRewindLaunchDescriptor Descriptor { get; } = Substitute.For<IRetroRewindLaunchDescriptor>();
        public IWiiRemoteConfigurationService Remotes { get; } = Substitute.For<IWiiRemoteConfigurationService>();
        public ILaunchPrompts Prompts { get; } = Substitute.For<ILaunchPrompts>();
        public RetroRewindLaunchService Service { get; }

        public Fixture()
        {
            Fs.File.WriteAllText("/game.iso", "game");
            var settings = Substitute.For<ISettingsManager>();
            settings.GAME_LOCATION.Returns(new WhWzSetting(typeof(string), "Game", "/game.iso"));
            settings.FORCE_WIIMOTE.Returns(new WhWzSetting(typeof(bool), "Force", true));
            settings.LAUNCH_WITH_DOLPHIN.Returns(new WhWzSetting(typeof(bool), "Dolphin", false));
            settings.Get<string>(Arg.Any<Setting>()).Returns(call => (string)call.Arg<Setting>().Get());
            settings.Get<bool>(Arg.Any<Setting>()).Returns(call => (bool)call.Arg<Setting>().Get());
            var paths = Substitute.For<ICustomDistributionPaths>();
            paths.PatchesFolderPath.Returns("/stable-patches");
            paths.BetaPatchesFolderPath.Returns("/beta-patches");
            paths.XmlFilePath.Returns("/stable.xml");
            paths.BetaXmlFilePath.Returns("/beta.xml");
            paths.LaunchJsonFilePath.Returns("/app/RR.json");
            var dolphinPaths = Substitute.For<IDolphinPaths>();
            dolphinPaths.ConfigFolderPath.Returns("/dolphin/Config");
            Dolphin.PreflightDolphinVersionAsync().Returns(Ok());
            Dolphin.LaunchDolphin(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<OperationResult>()).Returns(Ok());
            Mods.PrepareModsForLaunch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IProgress<ModOperationProgress>>()).Returns(Ok());
            Service = new(
                settings,
                Fs,
                Dolphin,
                Remotes,
                dolphinPaths,
                paths,
                Mods,
                Descriptor,
                Substitute.For<IRuntimeEnvironment>(),
                Prompts,
                new InlineModPresentation()
            );
        }
    }
}

internal sealed class InlineModPresentation : IModOperationPresentation
{
    public Task<TResult> RunAsync<TResult>(
        Func<IProgress<ModOperationProgress>, CancellationToken, Task<TResult>> operation,
        bool canCancel = false
    ) => operation(new Progress<ModOperationProgress>(), CancellationToken.None);
}

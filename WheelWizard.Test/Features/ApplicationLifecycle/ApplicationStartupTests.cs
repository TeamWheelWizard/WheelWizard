using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.AutoUpdating;
using WheelWizard.GameBanana.InstallRequests;
using WheelWizard.Launching;
using WheelWizard.Mods;
using WheelWizard.Settings;
using WheelWizard.WheelWizardData;

namespace WheelWizard.Test.Features.ApplicationLifecycle;

public class ApplicationStartupTests
{
    [Theory]
    [InlineData("--launch", "rr")]
    [InlineData("-l", "RetroRewind")]
    [InlineData("--launch", "retro-rewind")]
    public void Options_ParseLaunchAliasesAndProtocolIndependently(string flag, string target)
    {
        var options = StartupOptions.Parse([flag, target, "wheelwizard://123"]);
        Assert.True(options.LaunchRetroRewind);
        Assert.Equal("wheelwizard://123", options.ProtocolArgument);
        Assert.True(StartupOptions.Parse(["--launch= RR "]).LaunchRetroRewind);
        Assert.False(StartupOptions.Parse(["--launch"]).LaunchRetroRewind);
        Assert.False(StartupOptions.Parse(["--launch=unknown"]).LaunchRetroRewind);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, true)]
    public async Task StartupLaunch_RespectsSavedPreferenceProtocolAndExplicitCli(bool saved, bool protocol, bool cli, bool expected)
    {
        var fixture = new Fixture();
        fixture.Settings.Get<bool>(fixture.Settings.LAUNCH_RR_ON_STARTUP).Returns(saved);
        await fixture.Startup.RunAsync(new StartupOptions(protocol ? "wheelwizard://123" : null, cli));

        await fixture.Launch.Received(expected ? 1 : 0).LaunchAsync(false);
        fixture.Live.Received(1).Start();
        await fixture.Requests.Received(protocol ? 1 : 0).HandleAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ProtocolReload_CompletesBeforeOpeningInstallRequest()
    {
        var fixture = new Fixture();
        var reload = new TaskCompletionSource<WheelWizard.Shared.OperationResult>();
        fixture.Mods.ReloadAsync().Returns(reload.Task);
        var run = fixture.Startup.RunAsync(new StartupOptions("wheelwizard://123", false));
        await fixture.Requests.DidNotReceiveWithAnyArgs().HandleAsync(default!);
        reload.SetResult(Ok());
        await run;
        await fixture.Requests.Received(1).HandleAsync("wheelwizard://123");
    }

    [Fact]
    public async Task ShutdownDuringUpdateCheck_DoesNotStartPollingOrLaunch()
    {
        var fixture = new Fixture();
        var check = new TaskCompletionSource();
        fixture.Updates.CheckForUpdatesAsync().Returns(check.Task);
        using var shutdown = new CancellationTokenSource();
        var run = fixture.Startup.RunAsync(new StartupOptions(null, true), shutdown.Token);
        shutdown.Cancel();
        await run;
        check.SetResult();

        fixture.Live.DidNotReceive().Start();
        await fixture.Badges.DidNotReceive().LoadBadgesAsync();
        await fixture.Launch.DidNotReceiveWithAnyArgs().LaunchAsync(default);
    }

    private sealed class Fixture
    {
        public IModManager Mods { get; } = Substitute.For<IModManager>();
        public IModInstallRequestHandler Requests { get; } = Substitute.For<IModInstallRequestHandler>();
        public IAutoUpdaterSingletonService Updates { get; } = Substitute.For<IAutoUpdaterSingletonService>();
        public IWhWzDataSingletonService Badges { get; } = Substitute.For<IWhWzDataSingletonService>();
        public IApplicationLiveUpdates Live { get; } = Substitute.For<IApplicationLiveUpdates>();
        public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
        public IRetroRewindLaunchService Launch { get; } = Substitute.For<IRetroRewindLaunchService>();
        public ApplicationStartup Startup { get; }

        public Fixture()
        {
            Mods.ReloadAsync().Returns(Ok());
            Requests.HandleAsync(Arg.Any<string>()).Returns(Ok());
            Badges.LoadBadgesAsync().Returns(Ok());
            Launch.LaunchAsync(Arg.Any<bool>()).Returns(Ok());
            Startup = new ApplicationStartup(
                Mods,
                Requests,
                Updates,
                Badges,
                Live,
                Settings,
                Launch,
                NullLogger<ApplicationStartup>.Instance
            );
        }
    }
}

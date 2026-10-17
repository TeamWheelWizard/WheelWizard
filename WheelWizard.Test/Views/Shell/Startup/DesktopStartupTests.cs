using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.AutoUpdating;
using WheelWizard.Onboarding;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Startup;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Test.Views.Shell.Startup;

public class DesktopStartupTests
{
    [Fact]
    public async Task ShowsTheWindowBeforeStartupActions()
    {
        var fixture = new Fixture();
        var sequence = new List<string>();
        fixture
            .Windows.ShowAsync(fixture.Desktop, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("window");
                return Task.CompletedTask;
            });
        fixture
            .Startup.RunAsync(Arg.Any<StartupOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("startup");
                return Task.CompletedTask;
            });

        await fixture.Coordinator.StartAsync(fixture.Desktop, new StartupOptions(null, false), CancellationToken.None);

        Assert.Equal(["window", "startup"], sequence);
    }

    [Fact]
    public async Task ShutdownWhileTheWindowOpens_DoesNotRunStartup()
    {
        var fixture = new Fixture();
        using var shutdown = new CancellationTokenSource();
        fixture
            .Windows.ShowAsync(fixture.Desktop, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                shutdown.Cancel();
                return Task.CompletedTask;
            });

        await fixture.Coordinator.StartAsync(fixture.Desktop, new StartupOptions(null, false), shutdown.Token);

        await fixture.Startup.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    private sealed class Fixture
    {
        public IClassicDesktopStyleApplicationLifetime Desktop { get; } = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        public IMainWindowService Windows { get; } = Substitute.For<IMainWindowService>();
        public IApplicationStartup Startup { get; } = Substitute.For<IApplicationStartup>();
        public DesktopStartup Coordinator { get; }

        public Fixture() =>
            Coordinator = new DesktopStartup(
                Substitute.For<IBundleExtractionCleanupService>(),
                Windows,
                Substitute.For<IGameLicenseSingletonService>(),
                Startup,
                Substitute.For<IStartupMaintenance>(),
                Substitute.For<IOnboardingService>(),
                Substitute.For<IManagedDolphinUpdatePrompt>(),
                NullLogger<DesktopStartup>.Instance
            );
    }
}

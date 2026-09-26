using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.ApplicationLifecycle;
using WheelWizard.AutoUpdating;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared;
using WheelWizard.Views;
using WheelWizard.Views.Startup;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Test.Views;

public class DesktopStartupTests
{
    [Fact]
    public async Task InstalledResources_ShowTheWindowBeforeStartupActions()
    {
        var fixture = new Fixture();
        fixture.Resources.GetResolvedResourcePath().Returns(Ok("rendering-resource"));
        var sequence = new List<string>();
        fixture.Windows.When(service => service.Show(fixture.Desktop)).Do(_ => sequence.Add("window"));
        fixture
            .Startup.RunAsync(Arg.Any<StartupOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("startup");
                return Task.CompletedTask;
            });

        await fixture.Coordinator.StartAsync(fixture.Desktop, new StartupOptions(null, false), CancellationToken.None);

        Assert.Equal(["window", "startup"], sequence);
        await fixture.Setup.DidNotReceive().ShowAsync();
    }

    [Fact]
    public async Task DeclinedResourceSetup_ShutsDownWithoutOpeningMainWindow()
    {
        var fixture = new Fixture();
        fixture.Resources.GetResolvedResourcePath().Returns(new OperationError { Message = "Missing" });
        fixture.Setup.ShowAsync().Returns(false);

        await fixture.Coordinator.StartAsync(fixture.Desktop, new StartupOptions(null, false), CancellationToken.None);

        fixture.Desktop.Received(1).Shutdown();
        fixture.Windows.DidNotReceiveWithAnyArgs().Show(default!);
        await fixture.Startup.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task ShutdownDuringResourceSetup_DoesNotOpenAWindowAfterCompletion()
    {
        var fixture = new Fixture();
        fixture.Resources.GetResolvedResourcePath().Returns(new OperationError { Message = "Missing" });
        var setup = new TaskCompletionSource<bool>();
        fixture.Setup.ShowAsync().Returns(setup.Task);
        using var shutdown = new CancellationTokenSource();
        var start = fixture.Coordinator.StartAsync(fixture.Desktop, new StartupOptions(null, false), shutdown.Token);
        shutdown.Cancel();
        await start;
        setup.SetResult(true);

        fixture.Windows.DidNotReceiveWithAnyArgs().Show(default!);
        await fixture.Startup.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    private sealed class Fixture
    {
        public IClassicDesktopStyleApplicationLifetime Desktop { get; } = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        public IMiiRenderingResourceInstaller Resources { get; } = Substitute.For<IMiiRenderingResourceInstaller>();
        public IMiiSetupPresentation Setup { get; } = Substitute.For<IMiiSetupPresentation>();
        public IMainWindowService Windows { get; } = Substitute.For<IMainWindowService>();
        public IApplicationStartup Startup { get; } = Substitute.For<IApplicationStartup>();
        public DesktopStartup Coordinator { get; }

        public Fixture() =>
            Coordinator = new DesktopStartup(
                Substitute.For<IBundleExtractionCleanupService>(),
                Resources,
                Setup,
                Windows,
                Substitute.For<IGameLicenseSingletonService>(),
                Startup,
                NullLogger<DesktopStartup>.Instance
            );
    }
}

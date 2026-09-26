using WheelWizard.CustomDistributions;
using WheelWizard.Launching;
using WheelWizard.Models.Enums;
using WheelWizard.Mods;
using WheelWizard.Recomp;
using WheelWizard.Recomp.Domain;
using WheelWizard.Shared;

namespace WheelWizard.Test.Features.Recomp;

public class RecompLauncherTests
{
    [Fact]
    public async Task CompletedDistributionUpdate_FinishesPairedRecompEvenWhenCancelRacesCompletion()
    {
        using var fixture = new Fixture();
        fixture.Distribution.GetCurrentStatusAsync().Returns(Ok(WheelWizardStatus.OutOfDate));
        fixture
            .Distribution.UpdateAsync(Arg.Any<DistributionOperation>())
            .Returns(_ =>
            {
                fixture.Cancellation.Cancel();
                return Ok();
            });

        Assert.True((await fixture.Launcher.Update()).IsSuccess);

        await fixture
            .Install.Received(1)
            .InstallAsync(Arg.Any<IProgress<RecompInstallProgress>>(), Arg.Any<Func<Task<bool>>>(), CancellationToken.None);
        Assert.Contains(fixture.Progress, value => value.CanCancel == false);
        fixture.Nand.Received(1).ApplyNandToRecompConfig();
    }

    [Fact]
    public async Task CancelledDistributionUpdate_DoesNotStartRecompOrApplyConfiguration()
    {
        using var fixture = new Fixture();
        fixture.Distribution.GetCurrentStatusAsync().Returns(Ok(WheelWizardStatus.OutOfDate));
        fixture
            .Distribution.UpdateAsync(Arg.Any<DistributionOperation>())
            .Returns(_ =>
            {
                fixture.Cancellation.Cancel();
                return Fail("cancelled");
            });

        Assert.True((await fixture.Launcher.Update()).IsFailure);

        await fixture
            .Install.DidNotReceive()
            .InstallAsync(Arg.Any<IProgress<RecompInstallProgress>>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        fixture.Nand.DidNotReceive().ApplyNandToRecompConfig();
    }

    [Fact]
    public async Task CurrentDistribution_KeepsRecompSetupCancellable_AndForwardsOfflineChoice()
    {
        using var fixture = new Fixture();
        fixture.Presentation.ConfirmOfflineInstallAsync().Returns(false);
        fixture
            .Install.InstallAsync(Arg.Any<IProgress<RecompInstallProgress>>(), Arg.Any<Func<Task<bool>>>(), fixture.Cancellation.Token)
            .Returns(async call =>
            {
                Assert.False(await call.Arg<Func<Task<bool>>>()());
                fixture.Cancellation.Cancel();
                return (OperationResult)Fail("cancelled");
            });

        Assert.True((await fixture.Launcher.Update()).IsFailure);

        await fixture.Presentation.Received(1).ConfirmOfflineInstallAsync();
        Assert.Contains(fixture.Progress, value => value.CanCancel == true);
    }

    [Fact]
    public async Task FailedNandCopy_DoesNotSwitchSettingsOrStartInstallation()
    {
        using var fixture = new Fixture();
        fixture.Nand.SourceNandFolderPath.Returns("dolphin/Wii");
        fixture.Presentation.ConfirmUseDolphinDataAsync().Returns(true);
        fixture.Presentation.ConfirmCopyDolphinDataAsync().Returns(true);
        fixture.Nand.CopyNandForRecomp().Returns(Fail("disk full"));

        Assert.True((await fixture.Launcher.Install()).IsFailure);

        fixture.Nand.DidNotReceive().SetCopyEnabled(Arg.Any<bool>());
        fixture.Nand.DidNotReceive().SetSharingEnabled(Arg.Any<bool>());
        await fixture
            .Install.DidNotReceive()
            .InstallAsync(Arg.Any<IProgress<RecompInstallProgress>>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulNandCopy_IsEnabledBeforeApplyingInstalledConfiguration()
    {
        using var fixture = new Fixture();
        fixture.Nand.SourceNandFolderPath.Returns("dolphin/Wii");
        fixture.Presentation.ConfirmUseDolphinDataAsync().Returns(true);
        fixture.Presentation.ConfirmCopyDolphinDataAsync().Returns(true);
        fixture.Nand.CopyNandForRecomp().Returns(Ok());

        Assert.True((await fixture.Launcher.Install()).IsSuccess);

        Received.InOrder(() =>
        {
            fixture.Nand.CopyNandForRecomp();
            fixture.Nand.SetCopyEnabled(true);
            fixture.Nand.SetSharingEnabled(false);
            fixture.Nand.ApplyNandToRecompConfig();
        });
    }

    [Fact]
    public async Task Launch_ClosesPreparationScopeBeforeStartingUncancellableGameSession()
    {
        using var fixture = new Fixture();
        fixture.Install.ReconcileForLaunchAsync(Arg.Any<IProgress<RecompInstallProgress>>(), fixture.Cancellation.Token).Returns(Ok());
        fixture
            .Install.LaunchAsync(CancellationToken.None)
            .Returns(_ =>
            {
                Assert.True(fixture.ScopeClosed);
                return Ok();
            });

        Assert.True((await fixture.Launcher.Launch()).IsSuccess);

        await fixture.Install.Received(1).LaunchAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CancelledPreparation_DoesNotLaunchGame()
    {
        using var fixture = new Fixture();
        fixture
            .Install.ReconcileForLaunchAsync(Arg.Any<IProgress<RecompInstallProgress>>(), fixture.Cancellation.Token)
            .Returns(_ =>
            {
                fixture.Cancellation.Cancel();
                return Ok();
            });

        Assert.True((await fixture.Launcher.Launch()).IsFailure);

        await fixture.Install.DidNotReceive().LaunchAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Fixture : IDisposable, IProgress<RecompOperationProgress>
    {
        public IDistribution Distribution { get; } = Substitute.For<IDistribution>();
        public IRecompInstallService Install { get; } = Substitute.For<IRecompInstallService>();
        public IRecompDolphinDataService Nand { get; } = Substitute.For<IRecompDolphinDataService>();
        public IRecompPresentation Presentation { get; } = Substitute.For<IRecompPresentation>();
        public CancellationTokenSource Cancellation { get; } = new();
        public List<RecompOperationProgress> Progress { get; } = [];
        public bool ScopeClosed { get; private set; }
        public RecompLauncher Launcher { get; }

        public Fixture()
        {
            var distributions = Substitute.For<ICustomDistributionSingletonService>();
            distributions.RetroRewind.Returns(Distribution);
            Distribution.GetCurrentStatusAsync().Returns(Ok(WheelWizardStatus.Ready));
            Install
                .InstallAsync(Arg.Any<IProgress<RecompInstallProgress>>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(Ok());
            Nand.ApplyNandToRecompConfig().Returns(Ok());
            Presentation
                .RunAsync(Arg.Any<RecompOperationKind>(), Arg.Any<Func<RecompOperation, Task<OperationResult>>>())
                .Returns(async call =>
                {
                    var result = await call.Arg<Func<RecompOperation, Task<OperationResult>>>()(new(this, Cancellation.Token));
                    ScopeClosed = true;
                    return result;
                });
            var mods = Substitute.For<IModsLaunchService>();
            mods.PrepareModsForLaunch(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IProgress<ModOperationProgress>>()).Returns(Ok());
            var paths = Substitute.For<ICustomDistributionPaths>();
            paths.PatchesFolderPath.Returns("patches");
            Launcher = new RecompLauncher(
                Install,
                distributions,
                mods,
                Nand,
                paths,
                new Launching.InlineModPresentation(),
                Presentation,
                Substitute.For<ILaunchPrompts>()
            );
        }

        public void Report(RecompOperationProgress value) => Progress.Add(value);

        public void Dispose() => Cancellation.Dispose();
    }
}

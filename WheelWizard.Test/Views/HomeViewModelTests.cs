using Testably.Abstractions;
using WheelWizard.Launching;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Calendar;
using WheelWizard.Views.Pages;

namespace WheelWizard.Test.Views;

public class HomeViewModelTests
{
    [Fact]
    public async Task OlderStatusResponse_CannotOverwriteNewerState()
    {
        using var fixture = new Fixture();
        var older = new TaskCompletionSource<WheelWizardStatus>();
        var newer = new TaskCompletionSource<WheelWizardStatus>();
        fixture.Launcher.GetCurrentStatus().Returns(older.Task, newer.Task);
        var first = fixture.Model.RefreshAsync();
        var second = fixture.Model.RefreshAsync();
        newer.SetResult(WheelWizardStatus.Ready);
        await second;
        older.SetResult(WheelWizardStatus.NotInstalled);
        await first;

        Assert.Equal(WheelWizardStatus.Ready, fixture.Model.Status);
        Assert.True(fixture.Model.CanExecuteMain);
    }

    [Fact]
    public async Task OverlappingClicks_StartOneOperation_AndRestoreInteraction()
    {
        using var fixture = new Fixture();
        fixture.Launcher.GetCurrentStatus().Returns(WheelWizardStatus.NotInstalled);
        var install = new TaskCompletionSource<OperationResult>();
        fixture.Launcher.Install().Returns(install.Task);
        await fixture.Model.RefreshAsync();

        var first = fixture.Model.ExecuteMainAsync();
        await fixture.Model.ExecuteMainAsync();
        Assert.True(fixture.Model.IsBusy);
        Assert.False(fixture.Model.CanExecuteMain);
        fixture.Presentation.Received(1).SetApplicationInteractable(false);
        install.SetResult(Ok());
        await first;

        await fixture.Launcher.Received(1).Install();
        fixture.Presentation.Received(1).SetApplicationInteractable(true);
        Assert.False(fixture.Model.IsBusy);
        Assert.True(fixture.Model.IsInteractable);
    }

    [Fact]
    public async Task ThrowingUpdate_RestoresNavigationAndShowsError()
    {
        using var fixture = new Fixture();
        fixture.Launcher.GetCurrentStatus().Returns(WheelWizardStatus.OutOfDate);
        fixture.Launcher.Update().Returns(Task.FromException<OperationResult>(new IOException("disk full")));
        await fixture.Model.RefreshAsync();

        await fixture.Model.ExecuteMainAsync();

        fixture.Presentation.Received(1).ShowError(Arg.Is<OperationError>(error => error.Exception is IOException));
        fixture.Presentation.Received(1).SetApplicationInteractable(true);
        Assert.True(fixture.Model.CanExecuteMain);
    }

    [Theory]
    [InlineData(WheelWizardStatus.Ready)]
    [InlineData(WheelWizardStatus.NoServerButInstalled)]
    public async Task PlayAndOfflinePlay_UseLauncherAndRefreshAfterCompletion(WheelWizardStatus status)
    {
        using var fixture = new Fixture();
        fixture.Launcher.GetCurrentStatus().Returns(status);
        fixture.Launcher.Launch().Returns(Ok());
        await fixture.Model.RefreshAsync();
        var actionStarted = false;
        fixture.Model.MainActionStarted += (_, _) => actionStarted = true;

        await fixture.Model.ExecuteMainAsync();

        Assert.True(actionStarted);
        await fixture.Launcher.Received(1).Launch();
        await fixture.Launcher.Received(2).GetCurrentStatus();
    }

    [Fact]
    public async Task IncompleteConfiguration_NavigatesToSettingsWithoutLaunching()
    {
        using var fixture = new Fixture();
        fixture.Launcher.GetCurrentStatus().Returns(WheelWizardStatus.ConfigNotFinished);
        await fixture.Model.RefreshAsync();

        await fixture.Model.ExecuteMainAsync();

        fixture.Presentation.Received(1).OpenSettings();
        await fixture.Launcher.DidNotReceive().Launch();
    }

    [Fact]
    public async Task RecompMode_HidesAndDisablesDirectDolphinLaunch()
    {
        using var fixture = new Fixture();
        fixture.Settings.IsRecompModeActive().Returns(true);
        await fixture.Model.RefreshAsync();

        await fixture.Model.LaunchDolphinAsync();

        Assert.False(fixture.Model.IsDolphinVisible);
        Assert.False(fixture.Model.CanLaunchDolphin);
        Assert.Empty(fixture.Dolphin.ReceivedCalls());
    }

    [Fact]
    public async Task AprilFirst_UsesCalendarAndShowsFourPromptsBeforeLaunch()
    {
        using var fixture = new Fixture();
        fixture.Calendar.IsAprilFirst.Returns(true);
        fixture.Launcher.Launch().Returns(Ok());
        await fixture.Model.RefreshAsync();

        await fixture.Model.ExecuteMainAsync();

        Assert.Equal("Retro Beefbai", fixture.Model.GameTitle);
        await fixture.Presentation.Received(4).ShowLaunchPromptAsync(Arg.Any<HomeLaunchPrompt>());
        await fixture.Launcher.Received(1).Launch();
    }

    [Fact]
    public async Task DisposedModel_IgnoresPendingStatusResponse()
    {
        using var fixture = new Fixture();
        var pending = new TaskCompletionSource<WheelWizardStatus>();
        fixture.Launcher.GetCurrentStatus().Returns(pending.Task);
        var refresh = fixture.Model.RefreshAsync();
        fixture.Model.Dispose();
        pending.SetResult(WheelWizardStatus.Ready);

        await refresh;

        Assert.Equal(WheelWizardStatus.Loading, fixture.Model.Status);
        Assert.False(fixture.Model.CanExecuteMain);
    }

    private sealed class Fixture : IDisposable
    {
        public ILauncher Launcher { get; } = Substitute.For<ILauncher>();
        public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
        public IDolphinLaunchService Dolphin { get; } = Substitute.For<IDolphinLaunchService>();
        public IHomePresentation Presentation { get; } = Substitute.For<IHomePresentation>();
        public ISeasonalCalendar Calendar { get; } = Substitute.For<ISeasonalCalendar>();
        public HomeViewModel Model { get; }

        public Fixture()
        {
            var provider = Substitute.For<ILauncherProvider>();
            provider.GetActiveLauncher().Returns(Launcher);
            Launcher.GameTitle.Returns("Retro Rewind");
            Launcher.GetCurrentStatus().Returns(WheelWizardStatus.Ready);
            Settings.PathsSetupCorrectly().Returns(true);
            Model = new HomeViewModel(
                provider,
                Dolphin,
                Settings,
                Presentation,
                Calendar,
                Substitute.For<IRandomSystem>(),
                new ElapsedTimeProvider()
            );
        }

        public void Dispose() => Model.Dispose();
    }

    // An operation has already exceeded the click cooldown when its completion timestamp is read.
    private sealed class ElapsedTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Interlocked.Add(ref _timestamp, 3000);
    }
}

using WheelWizard.AutoUpdating;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.Branding;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.AutoUpdating;

public class AutoUpdaterTests
{
    [Fact]
    public async Task FindsNewestCompatibleStableRelease_AndHonorsPostpone()
    {
        var fixture = new Fixture();
        fixture.Releases([Release("v4.0.0", prerelease: true), Release("v3.0.0", compatible: false), Release("v1.1.0"), Release("v2.0.0")]);

        await fixture.Service.CheckForUpdatesAsync();

        await fixture.Presentation.Received(1).ConfirmUpdateAsync("2.0.0", "1.0.0");
        await fixture
            .Platform.DidNotReceive()
            .ExecuteUpdateAsync(Arg.Any<string>(), Arg.Any<IProgress<DownloadProgress>>(), Arg.Any<CancellationToken>());
        await fixture
            .Presentation.DidNotReceive()
            .RunUpdateAsync(Arg.Any<Func<IProgress<DownloadProgress>, CancellationToken, Task<OperationResult>>>());
    }

    [Fact]
    public async Task ConfirmedUpdate_ReportsPlatformFailure()
    {
        var fixture = new Fixture();
        fixture.Releases([Release("v2.0.0")]);
        fixture.Presentation.ConfirmUpdateAsync("2.0.0", "1.0.0").Returns(true);
        fixture
            .Platform.ExecuteUpdateAsync(Arg.Any<string>(), Arg.Any<IProgress<DownloadProgress>>(), Arg.Any<CancellationToken>())
            .Returns(Fail("disk full"));
        fixture
            .Presentation.RunUpdateAsync(Arg.Any<Func<IProgress<DownloadProgress>, CancellationToken, Task<OperationResult>>>())
            .Returns(call =>
                call.Arg<Func<IProgress<DownloadProgress>, CancellationToken, Task<OperationResult>>>()(
                    new Progress<DownloadProgress>(),
                    CancellationToken.None
                )
            );

        await fixture.Service.CheckForUpdatesAsync();

        await fixture
            .Platform.Received(1)
            .ExecuteUpdateAsync("https://example.test/v2.0.0.exe", Arg.Any<IProgress<DownloadProgress>>(), CancellationToken.None);
        await fixture.Presentation.Received(1).ShowUpdateFailureAsync("disk full");
    }

    [Fact]
    public async Task UnsupportedPlatform_ShowsNewestManualUpdateOnce()
    {
        var fixture = new Fixture();
        fixture.Platform.SupportsAutomaticUpdate.Returns(false);
        fixture.Releases([Release("v2.0.0", compatible: false), Release("v3.0.0", compatible: false)]);

        await fixture.Service.CheckForUpdatesAsync();
        await fixture.Service.CheckForUpdatesAsync();

        await fixture.Presentation.Received(1).ShowManualUpdateAsync("3.0.0", "1.0.0");
        await fixture.Presentation.DidNotReceive().ConfirmUpdateAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ReleaseLookupFailure_IsPresentedWithoutStartingAnUpdate()
    {
        var fixture = new Fixture();
        fixture.GitHub.GetReleasesAsync().Returns(Fail("offline"));

        await fixture.Service.CheckForUpdatesAsync();

        await fixture.Presentation.Received(1).ShowCheckFailureAsync("offline");
        Assert.Empty(fixture.Platform.ReceivedCalls());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostponedUpdate_CanBeReopenedWithoutAnotherLookup(bool automatic)
    {
        var fixture = new Fixture();
        fixture.Platform.SupportsAutomaticUpdate.Returns(automatic);
        fixture.Releases([Release("v2.0.0")]);
        var notifications = 0;
        fixture.Service.UpdateAvailable += (_, _) => notifications++;

        Assert.False(fixture.Service.IsUpdateAvailable);
        await fixture.Service.ShowAvailableUpdateAsync();
        Assert.Empty(fixture.Presentation.ReceivedCalls());

        await fixture.Service.CheckForUpdatesAsync();
        Assert.True(fixture.Service.IsUpdateAvailable);
        Assert.Equal(1, notifications);
        await fixture.Service.ShowAvailableUpdateAsync();

        await fixture.GitHub.Received(1).GetReleasesAsync();
        if (automatic)
            await fixture.Presentation.Received(2).ConfirmUpdateAsync("2.0.0", "1.0.0");
        else
            await fixture.Presentation.Received(2).ShowManualUpdateAsync("2.0.0", "1.0.0");
    }

    [Fact]
    public async Task Linux_SelectsNewestReleasePastDelay()
    {
        var fixture = new Fixture();
        fixture.Platform.SupportsAutomaticUpdate.Returns(false);
        fixture.Environment.IsLinux.Returns(true);
        var recent = Release("v3.0.0");
        recent.PublishedAt = fixture.Now;
        var available = Release("v2.0.0");
        available.PublishedAt = fixture.Now.AddDays(-1);
        fixture.Releases([recent, Release("invalid"), available]);

        await fixture.Service.CheckForUpdatesAsync();

        Assert.True(fixture.Service.IsUpdateAvailable);
        await fixture.Presentation.Received(1).ShowManualUpdateAsync("2.0.0", "1.0.0");
        await fixture.Presentation.DidNotReceive().ConfirmUpdateAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LinuxReleaseWithoutPublicationDate_DoesNotSetBadgeOrShowPopup()
    {
        var fixture = new Fixture();
        fixture.Environment.IsLinux.Returns(true);
        fixture.Releases([Release("v2.0.0")]);
        var notifications = 0;
        fixture.Service.UpdateAvailable += (_, _) => notifications++;

        await fixture.Service.CheckForUpdatesAsync();
        await fixture.Service.ShowAvailableUpdateAsync();

        Assert.False(fixture.Service.IsUpdateAvailable);
        Assert.Equal(0, notifications);
        Assert.Empty(fixture.Presentation.ReceivedCalls());
    }

    [Theory]
    [InlineData(23.999, false, true)]
    [InlineData(24, true, true)]
    [InlineData(48, true, true)]
    [InlineData(-1, false, true)]
    [InlineData(23.999, false, false)]
    [InlineData(24, true, false)]
    [InlineData(48, true, false)]
    [InlineData(-1, false, false)]
    public async Task EveryLinuxBuild_Waits24HoursBeforeBadgeAndPopup(double hours, bool expected, bool automatic)
    {
        var fixture = new Fixture();
        fixture.Environment.IsLinux.Returns(true);
        fixture.Platform.SupportsAutomaticUpdate.Returns(automatic);
        var release = Release("v2.0.0");
        release.PublishedAt = fixture.Now.AddHours(-hours);
        fixture.Releases([release]);
        var notifications = 0;
        fixture.Service.UpdateAvailable += (_, _) => notifications++;

        await fixture.Service.CheckForUpdatesAsync();

        Assert.Equal(expected, fixture.Service.IsUpdateAvailable);
        Assert.Equal(expected ? 1 : 0, notifications);
        if (!expected)
        {
            await fixture.Service.ShowAvailableUpdateAsync();
            Assert.Empty(fixture.Presentation.ReceivedCalls());
        }
        else if (automatic)
            await fixture.Presentation.Received(1).ConfirmUpdateAsync("2.0.0", "1.0.0");
        else
            await fixture.Presentation.Received(1).ShowManualUpdateAsync("2.0.0", "1.0.0");
    }

    private static GithubRelease Release(string version, bool prerelease = false, bool compatible = true) =>
        new()
        {
            TagName = version,
            Prerelease = prerelease,
            Assets = compatible ? [new() { Name = "update.exe", BrowserDownloadUrl = $"https://example.test/{version}.exe" }] : [],
        };

    private sealed class Fixture
    {
        public IUpdatePlatform Platform { get; } = Substitute.For<IUpdatePlatform>();
        public IGitHubSingletonService GitHub { get; } = Substitute.For<IGitHubSingletonService>();
        public IUpdatePresentation Presentation { get; } = Substitute.For<IUpdatePresentation>();
        public IRuntimeEnvironment Environment { get; } = Substitute.For<IRuntimeEnvironment>();
        public DateTimeOffset Now { get; } = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public AutoUpdaterSingletonService Service { get; }

        public Fixture()
        {
            var branding = Substitute.For<IBrandingSingletonService>();
            branding.Branding.Returns(
                new Branding.Branding
                {
                    DisplayName = "WheelWizard",
                    Identifier = "WheelWizard",
                    Version = "1.0.0",
                    RepositoryUrl = new Uri("https://example.test"),
                    DiscordUrl = new Uri("https://example.test"),
                    SupportUrl = new Uri("https://example.test"),
                }
            );
            Platform.SupportsAutomaticUpdate.Returns(true);
            Platform
                .GetAssetForCurrentPlatform(Arg.Any<GithubRelease>())
                .Returns(call => call.Arg<GithubRelease>().Assets.FirstOrDefault());
            var time = Substitute.For<TimeProvider>();
            time.GetUtcNow().Returns(Now);
            Service = new AutoUpdaterSingletonService(Platform, branding, GitHub, Presentation, Environment, time);
        }

        public void Releases(List<GithubRelease> releases) => GitHub.GetReleasesAsync().Returns(Ok(releases));
    }
}

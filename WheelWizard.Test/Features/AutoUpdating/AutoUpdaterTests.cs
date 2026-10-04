using WheelWizard.AutoUpdating;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.Branding;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared;
using WheelWizard.Shared.Downloads;

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
            Service = new AutoUpdaterSingletonService(Platform, branding, GitHub, Presentation);
        }

        public void Releases(List<GithubRelease> releases) => GitHub.GetReleasesAsync().Returns(Ok(releases));
    }
}

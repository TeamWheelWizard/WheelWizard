using System.Linq.Expressions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;

namespace WheelWizard.Test.Features.AutoUpdating;

public class ReleaseNotesTests
{
    [Fact]
    public async Task Notes_AreLoadedOnlyWhenRequested_AndCachedPerRelease()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var api = Substitute.For<IApiCaller<IGitHubApi>>();
        api.CallApiAsync(Arg.Any<Expression<Func<IGitHubApi, Task<GithubRelease>>>>())
            .Returns(Ok(new GithubRelease { TagName = "v2.5.9", BodyHtml = "<h2>Changes</h2><p><strong>Fixed</strong></p>" }));
        var service = new GitHubSingletonService(api, cache);
        Assert.Empty(api.ReceivedCalls());

        var first = await service.GetReleaseNotesAsync("v2.5.9");
        var again = await service.GetReleaseNotesAsync("v2.5.9");
        Assert.Equal("<h2>Changes</h2><p><strong>Fixed</strong></p>", first.Value);
        Assert.Equal(first.Value, again.Value);
        Assert.Single(api.ReceivedCalls());

        await service.GetReleaseNotesAsync("v2.5.8");
        Assert.Equal(2, api.ReceivedCalls().Count());
    }

    [Fact]
    public async Task FailedNotes_CanBeRetried()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var api = Substitute.For<IApiCaller<IGitHubApi>>();
        api.CallApiAsync(Arg.Any<Expression<Func<IGitHubApi, Task<GithubRelease>>>>())
            .Returns(Fail("offline"), Ok(new GithubRelease { TagName = "v2.5.9", BodyHtml = "<p>Fixed</p>" }));
        var service = new GitHubSingletonService(api, cache);

        Assert.True((await service.GetReleaseNotesAsync("v2.5.9")).IsFailure);
        Assert.Equal("<p>Fixed</p>", (await service.GetReleaseNotesAsync("v2.5.9")).Value);
        Assert.Equal(2, api.ReceivedCalls().Count());
    }
}

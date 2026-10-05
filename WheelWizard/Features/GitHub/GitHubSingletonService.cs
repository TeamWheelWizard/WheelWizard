using Microsoft.Extensions.Caching.Memory;
using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Services;

namespace WheelWizard.GitHub;

public interface IGitHubSingletonService
{
    /// <summary>
    /// Get the releases for a GitHub repository.
    /// </summary>
    Task<OperationResult<List<GithubRelease>>> GetReleasesAsync();
    Task<OperationResult<string>> GetReleaseNotesAsync(string tag);

    /// <summary>
    /// Get the releases for any GitHub repository.
    /// </summary>
    Task<OperationResult<List<GithubRelease>>> GetReleasesAsync(string owner, string repository, int count = 3);
}

public class GitHubSingletonService(IApiCaller<IGitHubApi> apiService, IMemoryCache cache) : IGitHubSingletonService
{
    public async Task<OperationResult<List<GithubRelease>>> GetReleasesAsync() =>
        await GetReleasesAsync("TeamWheelWizard", "WheelWizard", count: 30);

    public async Task<OperationResult<List<GithubRelease>>> GetReleasesAsync(string owner, string repository, int count = 3)
    {
        return await apiService.CallApiAsync(gitHubApi => gitHubApi.GetReleasesAsync(owner, repository, count));
    }

    public async Task<OperationResult<string>> GetReleaseNotesAsync(string tag)
    {
        var key = $"github:release-notes:{tag}";
        if (cache.TryGetValue<string>(key, out var html))
            return Ok(html!);

        var result = await apiService.CallApiAsync(api => api.GetReleaseNotesAsync(tag));
        if (result.IsFailure)
            return Fail(result.Error.Message);

        html = string.IsNullOrWhiteSpace(result.Value.BodyHtml)
            ? "<p>No changelog was provided for this release.</p>"
            : result.Value.BodyHtml;
        cache.Set(key, html, TimeSpan.FromHours(1));
        return Ok(html);
    }
}

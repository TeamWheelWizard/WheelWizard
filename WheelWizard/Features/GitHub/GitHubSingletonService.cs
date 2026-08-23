using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Services;

namespace WheelWizard.GitHub;

public interface IGitHubSingletonService
{
    /// <summary>
    /// Get the releases for a GitHub repository.
    /// </summary>
    Task<OperationResult<List<GithubRelease>>> GetReleasesAsync();

    /// <summary>
    /// Get the releases for any GitHub repository.
    /// </summary>
    Task<OperationResult<List<GithubRelease>>> GetReleasesAsync(string owner, string repository, int count = 3);
}

public class GitHubSingletonService(IApiCaller<IGitHubApi> apiService) : IGitHubSingletonService
{
    public async Task<OperationResult<List<GithubRelease>>> GetReleasesAsync() => await GetReleasesAsync("TeamWheelWizard", "WheelWizard");

    public async Task<OperationResult<List<GithubRelease>>> GetReleasesAsync(string owner, string repository, int count = 3)
    {
        return await apiService.CallApiAsync(gitHubApi => gitHubApi.GetReleasesAsync(owner, repository, count));
    }
}

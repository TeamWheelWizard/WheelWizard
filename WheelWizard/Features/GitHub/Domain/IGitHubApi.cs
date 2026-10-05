using Refit;

namespace WheelWizard.GitHub.Domain;

public interface IGitHubApi
{
    /// <summary>
    /// Get the releases for a GitHub repository.
    /// </summary>
    /// <param name="owner">The owner of the repository.</param>
    /// <param name="repository">The name of the repository.</param>
    /// <param name="count">The number of releases to get.</param>
    /// <returns>A list of releases for the repository.</returns>
    [Get("/repos/{owner}/{repository}/releases")]
    Task<List<GithubRelease>> GetReleasesAsync(string owner, string repository, [AliasAs("per_page")] int count = 3);

    [Get("/repos/TeamWheelWizard/WheelWizard/releases/tags/{tag}")]
    [Headers("Accept: application/vnd.github.html+json")]
    Task<GithubRelease> GetReleaseNotesAsync(string tag);
}

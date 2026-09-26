using System.Text.Json;
using WheelWizard.GitHub.Domain;

namespace WheelWizard.GitHub;

public static class GitHubExtensions
{
    public static IServiceCollection AddGitHub(this IServiceCollection services)
    {
        services.AddWhWzRefitApi<IGitHubApi>("https://api.github.com", new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        services.AddSingleton<IGitHubSingletonService, GitHubSingletonService>();
        return services;
    }
}

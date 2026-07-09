using WheelWizard.GameBanana.Domain;
using WheelWizard.Shared.Services;

namespace WheelWizard.GameBanana;

public interface IGameBananaSingletonService
{
    /// <summary>
    /// Get the mods catalog from GameBanana. If you don't provide a search term, it will return the featured mods.
    /// </summary>
    Task<OperationResult<GameBananaSearchResults>> GetModSearchResults(string searchTerm, int page = 1);

    /// <summary>
    /// Gets all the details of a mod from the GameBanana API.
    /// </summary>
    Task<OperationResult<GameBananaModDetails>> GetModDetails(int modId);

    GameBananaModPreview GetLoadingPreview();
}

public class GameBananaSingletonService(IApiCaller<IGameBananaApi> apiService) : IGameBananaSingletonService
{
    private const int MkGameId = 5896;

    public async Task<OperationResult<GameBananaSearchResults>> GetModSearchResults(string searchTerm, int page = 1)
    {
        // If there is no search term, we still want the user to see something. so we set the term to "mod" as our own featured list.
        if (string.IsNullOrWhiteSpace(searchTerm))
            searchTerm = "Mod";

        return await apiService.CallApiAsync(gitHubApi => gitHubApi.GetModSearchResults(searchTerm, MkGameId, "Mod", page));
    }

    public async Task<OperationResult<GameBananaModDetails>> GetModDetails(int modId)
    {
        return await apiService.CallApiAsync(gitHubApi => gitHubApi.GetModDetails(modId));
    }

    public GameBananaModPreview GetLoadingPreview()
    {
        // Name in both the mod and the author have to be "LOADING". This to ensure that they visually also show up as a loading icon.
        return new()
        {
            Id = 0,
            Name = "LOADING",
            Version = "",
            ModelName = "",
            Tags = [],
            ProfileUrl = "",
            LikeCount = 0,
            ViewCount = 0,
            DateAdded = 0,
            DateModified = 0,
            Game = new()
            {
                Name = "",
                ProfileUrl = "",
                IconUrl = "",
            },
            RootCategory = new()
            {
                Name = "",
                ProfileUrl = "",
                IconUrl = "",
            },
            Author = new()
            {
                Name = "LOADING",
                ProfileUrl = "",
                AvatarUrl = "",
            },
            PreviewMedia = new(),
            HasContentRatings = false,
        };
    }
}

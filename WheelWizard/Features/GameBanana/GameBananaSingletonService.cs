using WheelWizard.GameBanana.Domain;
using WheelWizard.Shared.Services;

namespace WheelWizard.GameBanana;

public interface IGameBananaSingletonService
{
    /// <summary>
    /// Get the mods catalog from GameBanana. Without a search term, returns the latest mods in the requested category.
    /// </summary>
    Task<OperationResult<GameBananaSearchResults>> GetModSearchResults(string searchTerm, int page = 1, int? categoryId = null);

    /// <summary>
    /// Gets all the details of a mod from the GameBanana API.
    /// </summary>
    Task<OperationResult<GameBananaModDetails>> GetModDetails(int modId);
}

public class GameBananaSingletonService(IApiCaller<IGameBananaApi> apiService) : IGameBananaSingletonService
{
    private const int MkGameId = 5896;

    public async Task<OperationResult<GameBananaSearchResults>> GetModSearchResults(string searchTerm, int page = 1, int? categoryId = null)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return await apiService.CallApiAsync(api => api.GetMods(MkGameId, categoryId, page, 12));
        // Full-text search also matches tags. It ignores category parameters, so the browser filters its results.
        return await apiService.CallApiAsync(gitHubApi => gitHubApi.GetModSearchResults(searchTerm, MkGameId, "Mod", page));
    }

    public async Task<OperationResult<GameBananaModDetails>> GetModDetails(int modId)
    {
        var result = await apiService.CallApiAsync(gitHubApi => gitHubApi.GetModDetails(modId));
        if (result.IsSuccess && result.Value is not { Author: not null, Category: not null })
            return Fail("GameBanana returned incomplete mod details.");
        return result;
    }
}

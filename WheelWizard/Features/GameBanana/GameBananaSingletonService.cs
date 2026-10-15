using Microsoft.Extensions.Caching.Memory;
using WheelWizard.GameBanana.Domain;
using WheelWizard.Shared.Services;

namespace WheelWizard.GameBanana;

public interface IGameBananaSingletonService
{
    /// <summary>
    /// Get the mods catalog from GameBanana. If you don't provide a search term, it will return the featured mods.
    /// </summary>
    Task<OperationResult<GameBananaSearchResults>> GetModSearchResults(string searchTerm, int page = 1, int? categoryId = null);

    Task<OperationResult<List<GameBananaModType>>> GetModTypes();

    /// <summary>
    /// Gets all the details of a mod from the GameBanana API.
    /// </summary>
    Task<OperationResult<GameBananaModDetails>> GetModDetails(int modId);
}

public class GameBananaSingletonService(IApiCaller<IGameBananaApi> apiService, IMemoryCache cache) : IGameBananaSingletonService
{
    private const int MkGameId = 5896;

    public async Task<OperationResult<GameBananaSearchResults>> GetModSearchResults(string searchTerm, int page = 1, int? categoryId = null)
    {
        if (categoryId.HasValue && string.IsNullOrWhiteSpace(searchTerm))
            return await apiService.CallApiAsync(api => api.GetModsByCategory(MkGameId, categoryId.Value, page, 12));
        // Search ignores category parameters; the browser filters those results before displaying them.
        // If there is no search term, we still want the user to see something. so we set the term to "mod" as our own featured list.
        if (string.IsNullOrWhiteSpace(searchTerm))
            searchTerm = "Mod";

        return await apiService.CallApiAsync(gitHubApi => gitHubApi.GetModSearchResults(searchTerm, MkGameId, "Mod", page));
    }

    public async Task<OperationResult<List<GameBananaModType>>> GetModTypes()
    {
        const string key = "GameBanana.ModTypes";
        if (cache.TryGetValue<List<GameBananaModType>>(key, out var types))
            return OperationResult.Ok(types!);
        var response = await apiService.CallApiAsync(api => api.GetModTypes(MkGameId));
        if (response.IsSuccess)
            cache.Set(key, response.Value, TimeSpan.FromHours(6));
        return response;
    }

    public async Task<OperationResult<GameBananaModDetails>> GetModDetails(int modId)
    {
        return await apiService.CallApiAsync(gitHubApi => gitHubApi.GetModDetails(modId));
    }
}

using Refit;

namespace WheelWizard.GameBanana.Domain;

public interface IGameBananaApi
{
    [Get("/Mod/Categories")]
    Task<List<GameBananaModType>> GetModTypes([AliasAs("_idGameRow")] int gameId);

    [Get("/Mod/Index")]
    Task<GameBananaSearchResults> GetModsByCategory(
        [AliasAs("_aFilters[Generic_Game]")] int gameId,
        [AliasAs("_aFilters[Generic_Category]")] int categoryId,
        [AliasAs("_nPage")] int page,
        [AliasAs("_nPerpage")] int perPage = 12
    );

    [Get("/Mod/{modId}/ProfilePage")]
    Task<GameBananaModDetails> GetModDetails(int modId);

    [Get("/Util/Search/Results")]
    Task<GameBananaSearchResults> GetModSearchResults(
        [AliasAs("_sSearchString")] string searchString,
        [AliasAs("_idGameRow")] int gameId,
        [AliasAs("_sModelName")] string modelName,
        [AliasAs("_nPage")] int page = 1
    );
}

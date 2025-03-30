using System.Text.Json;
using System.Text.Json.Serialization;
using WheelWizard.Helpers;
using WheelWizard.Models;
using WheelWizard.Models.GameBanana;

namespace WheelWizard.Services.GameBanana;

public class GamebananaSearchHandler
{
    private static readonly JsonSerializerOptions? JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string BaseUrl = Endpoints.GameBananaBaseUrl;
    private const int GameId = 5896;

    public static async Task<HttpClientResult<GameBananaSearchResults>> SearchModsAsync(string searchString, int page = 1, int perPage = 20)
    {
        if (string.IsNullOrWhiteSpace(searchString)) searchString = "mod";
        if (page < 1) page = 1;
        if (perPage is < 1 or > 50) perPage = 20;
        var searchUrl =
            $"{BaseUrl}/Util/Search/Results?_sSearchString={searchString}&_nPage={page}&_nPerpage={perPage}&_idGameRow={GameId}";

        var result = await HttpClientHelper.GetAsync<GameBananaSearchResults>(searchUrl, JsonSerializerOptions);

        if (!result.Succeeded)
            Console.WriteLine($"Error: {result.StatusMessage} (HTTP {result.StatusCode})");

        return result;
    }

    public static async Task<HttpClientResult<GameBananaModDetails>> GetModDetailsAsync(int modId)
    {
        var modDetailUrl = $"{BaseUrl}/Mod/{modId}/ProfilePage";
        var result = await HttpClientHelper.GetAsync<GameBananaModDetails>(modDetailUrl, JsonSerializerOptions);
        return result;
    }

    public static async Task<HttpClientResult<GameBananaSearchResults>> GetFeaturedModsAsync()
    {
        var featuredUrl = $"{BaseUrl}/Util/List/Featured?_idGameRow={GameId}";
        return await HttpClientHelper.GetAsync<GameBananaSearchResults>(featuredUrl, JsonSerializerOptions);
    }

    public static async Task<HttpClientResult<GameBananaSearchResults>> GetLatestModsAsync(int page = 1)
    {
        if (page < 1) page = 1;
        var latestModsUrl = $"{BaseUrl}/Game/{GameId}/Subfeed?_nPage={page}";
        return await HttpClientHelper.GetAsync<GameBananaSearchResults>(latestModsUrl, JsonSerializerOptions);
    }
}

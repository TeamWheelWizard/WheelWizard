using System.Text.Json;
using WheelWizard.Models;
using Serilog;

namespace WheelWizard.Services;

public class GhostLeaderboardService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://rwfc.net/api/timetrial/leaderboard";

    public GhostLeaderboardService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "WheelWizard/1.0");
    }

    public async Task<GhostLeaderboardResponse?> GetLeaderboardAsync(
        int trackId, 
        bool glitchAllowed, 
        GhostCc cc, 
        GhostLeaderboardType leaderboardType,
        int page = 1,
        int pageSize = 10)
    {
        try
        {
            var endpoint = leaderboardType == GhostLeaderboardType.Flap ? $"{_baseUrl}/flap" : _baseUrl;
            var url = $"{endpoint}?glitchAllowed={glitchAllowed.ToString().ToLower()}&trackId={trackId}&cc={((int)cc)}&page={page}&pageSize={pageSize}";
            
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            return JsonSerializer.Deserialize<GhostLeaderboardResponse>(json, options);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to load track info from API: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
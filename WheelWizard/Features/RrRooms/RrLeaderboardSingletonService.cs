using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WheelWizard.Models;
using WheelWizard.Shared.Services;

namespace WheelWizard.RrRooms;

public interface IRrLeaderboardSingletonService
{
    Task<OperationResult<List<RwfcLeaderboardEntry>>> GetTopPlayersAsync(int limit = 50);

    /// <summary>A player's leaderboard data, cached for a few minutes (failures too, so they are not retried on every call).</summary>
    Task<OperationResult<PlayerProfileResponse>> GetPlayerAsync(string friendCode);
}

public class RrLeaderboardSingletonService(
    IApiCaller<IRwfcApi> apiCaller,
    IMemoryCache cache,
    ILogger<RrLeaderboardSingletonService> logger
) : IRrLeaderboardSingletonService
{
    private const string FreshCacheKey = "rrrooms:leaderboard:fresh";
    private const string StaleCacheKey = "rrrooms:leaderboard:stale";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PlayerCacheLifetime = TimeSpan.FromMinutes(5);

    public async Task<OperationResult<List<RwfcLeaderboardEntry>>> GetTopPlayersAsync(int limit = 50)
    {
        var boundedLimit = Math.Clamp(limit, 1, 200);

        if (TryGetCached(FreshCacheKey, boundedLimit, out var freshCache))
            return freshCache;

        var fetchResult = await apiCaller.CallApiAsync(api => api.GetTopLeaderboardAsync(boundedLimit));
        if (fetchResult.IsFailure)
        {
            if (!TryGetCached(StaleCacheKey, boundedLimit, out var staleCache))
                return fetchResult;

            logger.LogWarning("RWFC leaderboard fetch failed; returning stale cached leaderboard for top {Limit}.", boundedLimit);
            return staleCache;
        }

        var fetchedEntries = fetchResult.Value.ToList();

        cache.Set(FreshCacheKey, fetchedEntries, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheLifetime });
        cache.Set(StaleCacheKey, fetchedEntries);

        return TrimToLimit(fetchedEntries, boundedLimit);
    }

    // The request itself is cached, so asking for a player that is still loading does not fetch it again.
    public Task<OperationResult<PlayerProfileResponse>> GetPlayerAsync(string friendCode) =>
        cache.GetOrCreate(
            $"rrrooms:player:{friendCode}",
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PlayerCacheLifetime;
                return apiCaller.CallApiAsync(api => api.GetPlayerProfileAsync(friendCode));
            }
        )!;

    private bool TryGetCached(string cacheKey, int limit, out OperationResult<List<RwfcLeaderboardEntry>> result)
    {
        if (
            !cache.TryGetValue(cacheKey, out List<RwfcLeaderboardEntry>? cachedEntries)
            || cachedEntries == null
            || cachedEntries.Count < limit
        )
        {
            result = default!;
            return false;
        }

        result = TrimToLimit(cachedEntries, limit);
        return true;
    }

    private static OperationResult<List<RwfcLeaderboardEntry>> TrimToLimit(IEnumerable<RwfcLeaderboardEntry> entries, int limit)
    {
        return entries.Take(limit).ToList();
    }
}

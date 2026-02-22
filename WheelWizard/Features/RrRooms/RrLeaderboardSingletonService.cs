using Microsoft.Extensions.Logging;
using WheelWizard.Shared.Services;

namespace WheelWizard.RrRooms;

public interface IRrLeaderboardSingletonService
{
    Task<OperationResult<List<RwfcLeaderboardEntry>>> GetTopPlayersAsync(int limit = 50);
}

public class RrLeaderboardSingletonService(IApiCaller<IRwfcApi> apiCaller, ILogger<RrLeaderboardSingletonService> logger)
    : IRrLeaderboardSingletonService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(90);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _cacheLock = new();

    private DateTimeOffset _cacheFetchedAt = DateTimeOffset.MinValue;
    private List<RwfcLeaderboardEntry> _cachedEntries = [];

    public async Task<OperationResult<List<RwfcLeaderboardEntry>>> GetTopPlayersAsync(int limit = 50)
    {
        var boundedLimit = Math.Clamp(limit, 1, 200);

        if (TryGetFreshCache(boundedLimit, out var freshCache))
            return freshCache;

        await _refreshGate.WaitAsync();

        try
        {
            if (TryGetFreshCache(boundedLimit, out freshCache))
                return freshCache;

            var fetchResult = await apiCaller.CallApiAsync(api => api.GetTopLeaderboardAsync(boundedLimit));
            if (fetchResult.IsFailure)
            {
                if (TryGetAnyCache(boundedLimit, out var staleCache))
                {
                    logger.LogWarning("RWFC leaderboard fetch failed; returning stale cached leaderboard for top {Limit}.", boundedLimit);
                    return staleCache;
                }

                return fetchResult;
            }

            lock (_cacheLock)
            {
                _cachedEntries = fetchResult.Value.ToList();
                _cacheFetchedAt = DateTimeOffset.UtcNow;
            }

            return TrimToLimit(fetchResult.Value, boundedLimit);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private bool TryGetFreshCache(int limit, out OperationResult<List<RwfcLeaderboardEntry>> result)
    {
        lock (_cacheLock)
        {
            var hasFreshCache = DateTimeOffset.UtcNow - _cacheFetchedAt <= CacheLifetime;
            if (!hasFreshCache || _cachedEntries.Count < limit)
            {
                result = default!;
                return false;
            }

            result = TrimToLimit(_cachedEntries, limit);
            return true;
        }
    }

    private bool TryGetAnyCache(int limit, out OperationResult<List<RwfcLeaderboardEntry>> result)
    {
        lock (_cacheLock)
        {
            if (_cachedEntries.Count < limit)
            {
                result = default!;
                return false;
            }

            result = TrimToLimit(_cachedEntries, limit);
            return true;
        }
    }

    private static OperationResult<List<RwfcLeaderboardEntry>> TrimToLimit(IEnumerable<RwfcLeaderboardEntry> entries, int limit)
    {
        return entries.Take(limit).ToList();
    }
}

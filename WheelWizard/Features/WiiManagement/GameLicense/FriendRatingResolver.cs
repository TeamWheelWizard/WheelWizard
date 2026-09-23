using WheelWizard.Models;
using WheelWizard.Models.RRInfo;
using WheelWizard.WiiManagement.GameLicense.Domain;

namespace WheelWizard.WiiManagement.GameLicense;

// rksys.dat VR/BR is unreliable, so prefer live rooms, then the API (VR only)
public class FriendRatingResolver
{
    public const uint DefaultFriendRating = 5000;
    public const uint MaxRksysFriendRating = 1_000_000 / 100;
    public static readonly TimeSpan ApiVrCacheDuration = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, (uint? Vr, DateTime FetchedAt)> _apiVrCache = [];
    private readonly HashSet<string> _pendingApiVrRequests = [];
    private readonly Dictionary<string, uint> _liveBrCache = [];

    // The game stores BR / 100; WheelWizard only ever writes the default 5000
    public static uint BrFromRksys(uint rawBr) => rawBr == DefaultFriendRating || rawBr > MaxRksysFriendRating ? rawBr : rawBr * 100;

    public static uint? VrFromApiProfile(PlayerProfileResponse? profile) => profile is { Vr: > 0 } ? (uint)profile.Vr : null;

    /// <summary>
    /// Applies the most accurate known VR/BR to each friend.
    /// </summary>
    /// <returns>Friend codes whose VR should be fetched from the API.</returns>
    public List<string> Apply(IEnumerable<FriendProfile> friends, IEnumerable<RrPlayer> onlinePlayers, DateTime now)
    {
        var onlineByFriendCode = onlinePlayers
            .Where(player => !string.IsNullOrWhiteSpace(player.FriendCode))
            .GroupBy(player => player.FriendCode)
            .ToDictionary(group => group.Key, group => group.First());

        var friendCodesToFetch = new List<string>();
        foreach (var friend in friends)
        {
            if (string.IsNullOrWhiteSpace(friend.FriendCode))
                continue;

            if (onlineByFriendCode.TryGetValue(friend.FriendCode, out var livePlayer) && livePlayer.Vr.HasValue)
            {
                friend.Vr = (uint)Math.Max(livePlayer.Vr.Value, 0);
                if (livePlayer.Br.HasValue)
                {
                    friend.Br = (uint)Math.Max(livePlayer.Br.Value, 0);
                    _liveBrCache[friend.FriendCode] = friend.Br;
                }
                _apiVrCache[friend.FriendCode] = (friend.Vr, now);
                continue;
            }

            if (_liveBrCache.TryGetValue(friend.FriendCode, out var cachedBr))
                friend.Br = cachedBr;

            if (_apiVrCache.TryGetValue(friend.FriendCode, out var cached))
            {
                if (cached.Vr.HasValue)
                    friend.Vr = cached.Vr.Value;
                if (now - cached.FetchedAt < ApiVrCacheDuration)
                    continue;
            }

            if (_pendingApiVrRequests.Add(friend.FriendCode))
                friendCodesToFetch.Add(friend.FriendCode);
        }

        return friendCodesToFetch;
    }

    /// <summary>
    /// Stores the result of an API request. A null VR marks a failed request, which is cached too
    /// to avoid retrying on every update.
    /// </summary>
    public void StoreApiVr(string friendCode, uint? vr, DateTime now)
    {
        _apiVrCache[friendCode] = (vr, now);
        _pendingApiVrRequests.Remove(friendCode);
    }
}

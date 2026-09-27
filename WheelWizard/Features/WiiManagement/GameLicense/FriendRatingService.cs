using Microsoft.Extensions.Logging;
using WheelWizard.Models.RRInfo;
using WheelWizard.RrRooms;
using WheelWizard.Shared.Services;
using WheelWizard.WiiManagement.GameLicense.Domain;

namespace WheelWizard.WiiManagement.GameLicense;

/// <summary>Keeps friend ratings and API requests alive across page navigation.</summary>
public sealed class FriendRatingService(IApiCaller<IRwfcApi> apiCaller, TimeProvider timeProvider, ILogger<FriendRatingService> logger)
{
    private readonly FriendRatingResolver _resolver = new();
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public event Action? RatingsChanged;

    public void Refresh(IEnumerable<FriendProfile> friends, IEnumerable<RrPlayer> onlinePlayers)
    {
        List<string> missing;
        lock (_gate)
            missing = _resolver.Apply(friends, onlinePlayers, timeProvider.GetUtcNow().UtcDateTime);

        if (missing.Count > 0)
            _ = FetchApiVrAsync(missing);
    }

    private async Task FetchApiVrAsync(List<string> friendCodes)
    {
        await _requestGate.WaitAsync();
        try
        {
            foreach (var friendCode in friendCodes)
            {
                uint? vr = null;
                try
                {
                    var result = await apiCaller.CallApiAsync(api => api.GetPlayerProfileAsync(friendCode));
                    if (result.IsSuccess)
                        vr = FriendRatingResolver.VrFromApiProfile(result.Value);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Could not fetch VR for friend {FriendCode}", friendCode);
                }

                lock (_gate)
                    _resolver.StoreApiVr(friendCode, vr, timeProvider.GetUtcNow().UtcDateTime);
                if (vr.HasValue)
                {
                    try
                    {
                        RatingsChanged?.Invoke();
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "Could not notify listeners about updated friend ratings");
                    }
                }
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }
}

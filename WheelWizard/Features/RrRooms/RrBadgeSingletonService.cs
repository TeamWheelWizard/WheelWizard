using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.FriendCodes;

namespace WheelWizard.RrRooms;

public interface IRrBadgeSingletonService
{
    Task<OperationResult> LoadBadgesAsync();
    BadgeVariant[] GetBadges(string friendCode);
}

public class RrBadgeSingletonService(IApiCaller<IRwfcApi> apiCaller) : IRrBadgeSingletonService
{
    private Dictionary<uint, BadgeVariant[]> BadgeData { get; set; } = new();

    public async Task<OperationResult> LoadBadgesAsync()
    {
        var badgeResult = await apiCaller.CallApiAsync(rwfcApi => rwfcApi.GetAllBadgesAsync());
        if (badgeResult.IsFailure)
            return badgeResult;

        var badgeData = new Dictionary<uint, BadgeVariant[]>();
        foreach (var (pid, badgeIds) in badgeResult.Value.Badges ?? [])
        {
            if (!uint.TryParse(pid, out var profileId) || profileId == 0)
                continue;

            // Badges the app does not know yet are skipped instead of rendering as blank.
            var badges = (badgeIds ?? [])
                .Where(id => id >= 0 && Enum.IsDefined(typeof(BadgeVariant), id))
                .Select(id => (BadgeVariant)id)
                .Distinct()
                .Order()
                .ToArray();
            if (badges.Length != 0)
                badgeData[profileId] = badges;
        }

        BadgeData = badgeData;
        return Ok();
    }

    public BadgeVariant[] GetBadges(string friendCode)
    {
        var profileId = FriendCode.FriendCodeToProfileId(friendCode);
        return profileId != 0 && BadgeData.TryGetValue(profileId, out var badges) ? badges : [];
    }
}

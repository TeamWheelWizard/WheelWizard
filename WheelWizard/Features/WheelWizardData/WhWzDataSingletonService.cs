using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData;

public interface IWhWzDataSingletonService
{
    Task<OperationResult<WhWzStatus>> GetStatusAsync();
    Task<OperationResult> LoadBadgesAsync();
    BadgeVariant[] GetBadges(string friendCode);
}

public class WhWzDataSingletonService(IApiCaller<IWhWzDataApi> apiCaller)
    : IWhWzDataSingletonService
{
    private Dictionary<string, BadgeVariant[]> BadgeData { get; set; } = new();

    public async Task<OperationResult<WhWzStatus>> GetStatusAsync()
    {
        return await apiCaller.CallApiAsync(whWzDataApi => whWzDataApi.GetStatusAsync());
    }

    public async Task<OperationResult> LoadBadgesAsync()
    {
        var badgeResult = await apiCaller.CallApiAsync(whWzDataApi => whWzDataApi.GetBadgesAsync());
        if(badgeResult.IsFailure)
            return badgeResult;

        BadgeData = badgeResult.Value.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Where(b => b != BadgeVariant.None).ToArray()
        );

        return Ok();
    }

    public BadgeVariant[] GetBadges(string friendCode) =>
        BadgeData.TryGetValue(friendCode, out var variants) ? variants : [];
}

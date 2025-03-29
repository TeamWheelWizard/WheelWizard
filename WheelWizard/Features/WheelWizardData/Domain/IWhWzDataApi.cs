using Refit;

namespace WheelWizard.WheelWizardData.Domain;

public interface IWhWzDataApi
{
    [Get("/badges.json")]
    Task<Dictionary<string,BadgeVariant[]>> GetBadgesAsync();
    
    [Get("/status.json")]
    Task<WhWzStatus> GetStatusAsync();
}

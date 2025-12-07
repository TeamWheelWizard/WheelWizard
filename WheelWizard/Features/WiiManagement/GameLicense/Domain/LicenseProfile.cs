using WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

namespace WheelWizard.WiiManagement.GameLicense.Domain;

public class LicenseProfile : PlayerProfileBase
{
    public required uint TotalRaceCount { get; set; }
    public required uint TotalWinCount { get; set; }
    public List<FriendProfile> Friends { get; set; } = [];
    public LicenseStatistics Statistics { get; set; }
}

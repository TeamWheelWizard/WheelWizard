using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.WiiManagement.GameLicense.Domain;

public class FriendProfile : PlayerProfileBase
{
    public bool IsPending { get; set; }

    public required uint Wins { get; set; }
    public required uint Losses { get; set; }

    public required byte CountryCode { get; set; }
    public string CountryName => GameLicenseDisplay.GetCountryEmoji(CountryCode);
}

using WheelWizard.Helpers;

namespace WheelWizard.WiiManagement.GameLicense.Domain;

public class FriendProfile : PlayerProfileBase
{
    public required uint Wins { get; set; }
    public required uint Losses { get; set; }

    public required byte CountryCode { get; set; }
    public string CountryName => Humanizer.GetCountryEmoji(CountryCode);
}

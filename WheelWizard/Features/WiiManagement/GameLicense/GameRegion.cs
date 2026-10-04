using WheelWizard.Models.Enums;

namespace WheelWizard.WiiManagement.GameLicense;

public static class GameRegion
{
    public static string GetGameId(MarioKartWiiEnums.Regions region) =>
        region switch
        {
            MarioKartWiiEnums.Regions.None => "",
            MarioKartWiiEnums.Regions.America => "RMCE",
            MarioKartWiiEnums.Regions.Europe => "RMCP",
            MarioKartWiiEnums.Regions.Japan => "RMCJ",
            MarioKartWiiEnums.Regions.Korea => "RMCK",
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, null),
        };
}

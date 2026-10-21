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

    /// <summary>The Mario Kart Wii region of a disc's game ID, or <c>None</c> when the disc is not Mario Kart Wii.</summary>
    public static MarioKartWiiEnums.Regions FromGameId(string? gameId)
    {
        if (gameId is not { Length: >= 4 } || !gameId.StartsWith("RMC", StringComparison.Ordinal))
            return MarioKartWiiEnums.Regions.None;

        return gameId[3] switch
        {
            'E' => MarioKartWiiEnums.Regions.America,
            'P' => MarioKartWiiEnums.Regions.Europe,
            'J' => MarioKartWiiEnums.Regions.Japan,
            'K' => MarioKartWiiEnums.Regions.Korea,
            _ => MarioKartWiiEnums.Regions.None,
        };
    }

    public static string GetDisplayName(MarioKartWiiEnums.Regions region) =>
        region switch
        {
            MarioKartWiiEnums.Regions.Europe => t("region.europe"),
            MarioKartWiiEnums.Regions.America => t("region.america"),
            MarioKartWiiEnums.Regions.Korea => t("region.south_korea"),
            MarioKartWiiEnums.Regions.Japan => t("region.japan"),
            _ => t("state.unknown"),
        };
}

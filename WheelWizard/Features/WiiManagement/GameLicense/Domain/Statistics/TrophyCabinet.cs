namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public class TrophyCabinet
{
    /// <summary>
    /// Key = “Mushroom Cup 50cc”, Value = TrophyInfo
    /// </summary>
    public Dictionary<string, TrophyInfo> PerCup { get; init; } = new();
}

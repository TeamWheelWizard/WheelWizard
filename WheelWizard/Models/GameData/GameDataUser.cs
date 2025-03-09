namespace WheelWizard.Models.GameData;

public class GameDataUser : GameDataPlayer
{
    public required uint TotalRaceCount { get; set; }
    public required uint TotalWinCount { get; set; }
    public List<GameDataFriend> Friends { get; set; } = new List<GameDataFriend>();
}

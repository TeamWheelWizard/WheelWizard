namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public class RaceTotals
{
    public uint AllRacesCount { get; set; } // total amount of races completed

    public uint OnlineRacesCount
    {
        get
        {
            return WinsVsLosses.OnlineVs.Wins
                + WinsVsLosses.OnlineVs.Losses
                + WinsVsLosses.OnlineBattle.Wins
                + WinsVsLosses.OnlineBattle.Losses;
        }
    }
    public uint BattleMatches { get; set; } // total amount of battle matches completed
    public WinsVsLosses WinsVsLosses { get; set; } = new();
    public int GhostChallengesSent { get; set; } // total amount of ghost challenges sent
    public int GhostChallengesReceived { get; set; } // total amount of ghost challenges received
}

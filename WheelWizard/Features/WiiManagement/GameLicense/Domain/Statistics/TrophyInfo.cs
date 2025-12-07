namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

/// <summary>
/// Single cup’s result: which trophy and what star rank.
/// </summary>
public class TrophyInfo
{
    public CupTrophyType CupType { get; set; } // Gold, Silver, Bronze, None
    public CupRank Rank { get; set; } // ThreeStars, TwoStars, OneStar, A, B, C, D, E, F
    public bool Completed { get; set; } // bit 0x52: 1 = completed
}

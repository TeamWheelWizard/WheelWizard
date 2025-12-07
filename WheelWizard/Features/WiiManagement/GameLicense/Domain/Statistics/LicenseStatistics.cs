namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public class LicenseStatistics
{
    public RaceTotals RaceTotals { get; set; } = new();
    public PreferredControls Controls { get; set; } = new();
    public Performance Performance { get; init; } = new();
    public RaceCompletions RaceCompletions { get; init; } = new();
    public PreferredControls PreferredControls { get; init; } = new();
    public BattleCompletions BattleCompletions { get; init; } = new();
    public ushort TotalCompetitions { get; init; }
    public TrophyCabinet? Trophies { get; init; }
}

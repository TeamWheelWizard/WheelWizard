namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public class BattleCompletions
{
    /// <summary>
    /// Key = Battle Stage, Value = Amount of times the stage was played
    /// </summary>
    public Dictionary<Stage, int> Stage { get; set; } = new();
}

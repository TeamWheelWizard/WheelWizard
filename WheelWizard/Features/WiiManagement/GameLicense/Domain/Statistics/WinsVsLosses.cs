namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public class WinsVsLosses
{
    public WinLoss OfflineVs { get; set; } = new();
    public WinLoss OfflineBattle { get; set; } = new();
    public WinLoss OnlineVs { get; set; } = new();
    public WinLoss OnlineBattle { get; set; } = new();
}

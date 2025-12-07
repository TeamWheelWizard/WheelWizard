namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public class PreferredControls
{
    public int WiiWheelRaces { get; set; } // amount of matched played with the Wii Wheel
    public int WiiWheelBattles { get; set; } // amount of matched played with the Wii Wheel in battles
    public float WheelWheelUsageRatio
    {
        get
        {
            if (WiiWheelRaces + WiiWheelBattles == 0)
                return 0f;

            return (float)WiiWheelRaces / (WiiWheelRaces + WiiWheelBattles);
        }
    }
    public DriftType PreferredDriftType { get; set; } = DriftType.Standard; // preferred drift type, default is standard
}

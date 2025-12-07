namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public class Performance
{
    public int TricksPerformed { get; set; } // total amount of tricks performed
    public int ItemHitsDealt { get; set; } // total amount of item hits dealt
    public int ItemHitsReceived { get; set; } // total amount of item hits received
    public int FirstPlaces { get; set; } // total amount of first places achieved
    public float DistanceTotal { get; set; } // Total Distance driven
    public float DistanceInFirstPlace { get; set; } // Total Distance driven in first place
    public float DistanceVsRaces { get; set; }

    // percentage of time in 1st = floor(DistanceInFirstPlace / DistanceVsRaces * 100).

    public int PercentTimeInFirstPlace
    {
        get
        {
            if (DistanceVsRaces == 0)
                return 0;

            return (int)Math.Floor(DistanceInFirstPlace / DistanceVsRaces * 100);
        }
    }
}

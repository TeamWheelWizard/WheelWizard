using WheelWizard.Helpers;
using WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

namespace WheelWizard.WiiManagement.GameLicense;

public static class StatisticsSerializer
{
    private static readonly string[] CupNames = { "Mushroom", "Flower", "Star", "Special", "Shell", "Banana", "Leaf", "Lightning" };

    // On retro rewind these are different, but for the original game these are the engine classes.
    // So whenever we display we should check if the game is retro rewind or not.
    private static readonly string[] EngineClasses = { "50cc", "100cc", "150cc", "Mirror" };

    public static LicenseStatistics ParseStatistics(byte[] rksysData, int rkpdOffset)
    {
        return new()
        {
            RaceTotals = ParseRaceTotals(rksysData, rkpdOffset),
            Performance = ParsePerformance(rksysData, rkpdOffset),
            PreferredControls = ParsePreferredControls(rksysData, rkpdOffset),
            RaceCompletions = ParseRaceCompletions(rksysData, rkpdOffset),
            BattleCompletions = ParseBattleCompletions(rksysData, rkpdOffset),
            TotalCompetitions = (ushort)BigEndianBinaryHelper.BufferToUint16(rksysData, rkpdOffset + 0xE8),
            Trophies = ParseTrophyCabinet(rksysData, rkpdOffset),
        };
    }

    private static RaceTotals ParseRaceTotals(byte[] rksysData, int rkpdOffset)
    {
        return new()
        {
            AllRacesCount = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xB4),
            BattleMatches = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xB8),
            GhostChallengesSent = (int)BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xC8),
            GhostChallengesReceived = (int)BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xCC),
            WinsVsLosses = new()
            {
                OfflineVs = new()
                {
                    Wins = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0x88),
                    Losses = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0x8C),
                },
                OfflineBattle = new()
                {
                    Wins = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0x90),
                    Losses = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0x94),
                },
                OnlineVs = new()
                {
                    Wins = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0x98),
                    Losses = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0x9C),
                },
                OnlineBattle = new()
                {
                    Wins = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xA0),
                    Losses = BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xA4),
                },
            },
        };
    }

    private static Performance ParsePerformance(byte[] rksysData, int rkpdOffset)
    {
        return new()
        {
            ItemHitsDealt = (int)BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xD0),
            ItemHitsReceived = (int)BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xD4),
            TricksPerformed = (int)BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xD8),
            FirstPlaces = (int)BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xDC),
            DistanceTotal = BigEndianBinaryHelper.BufferToFloat(rksysData, rkpdOffset + 0xC4),
            DistanceInFirstPlace = BigEndianBinaryHelper.BufferToFloat(rksysData, rkpdOffset + 0xE0),
            DistanceVsRaces = BigEndianBinaryHelper.BufferToFloat(rksysData, rkpdOffset + 0xE4),
        };
    }

    private static PreferredControls ParsePreferredControls(byte[] rksysData, int rkpdOffset)
    {
        var driftByte = rksysData[rkpdOffset + 0xEA];
        var driftValue = (driftByte >> 0) & 0x03; // Get first 2 bits
        var driftType = driftValue switch
        {
            1 => DriftType.Manual,
            2 => DriftType.Automatic,
            _ => DriftType.Standard,
        };

        return new PreferredControls
        {
            WiiWheelRaces = (int)BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xBC),
            WiiWheelBattles = (int)BigEndianBinaryHelper.BufferToUint32(rksysData, rkpdOffset + 0xC0),
            PreferredDriftType = driftType,
        };
    }

    private static RaceCompletions ParseRaceCompletions(byte[] rksysData, int rkpdOffset)
    {
        var completions = new RaceCompletions();

        // Favorite Character (0xEC, 24 entries)
        var charBaseOffset = rkpdOffset + 0xEC;
        foreach (Character character in Enum.GetValues(typeof(Character)))
        {
            var offset = charBaseOffset + (int)character * 2;
            completions.CharacterCompletions[character] = (int)BigEndianBinaryHelper.BufferToUint16(rksysData, offset);
        }

        // Favorite Vehicle (0x11E, 36 entries)
        var vehicleBaseOffset = rkpdOffset + 0x11E;
        foreach (Vehicle vehicle in Enum.GetValues(typeof(Vehicle)))
        {
            var offset = vehicleBaseOffset + (int)vehicle * 2;
            completions.Vehicle[vehicle] = (int)BigEndianBinaryHelper.BufferToUint16(rksysData, offset);
        }

        // Favorite Course (0x166, 32 entries)
        var courseBaseOffset = rkpdOffset + 0x166;
        foreach (Course course in Enum.GetValues(typeof(Course)))
        {
            var offset = courseBaseOffset + (int)course * 2;
            completions.Course[course] = (int)BigEndianBinaryHelper.BufferToUint16(rksysData, offset);
        }

        return completions;
    }

    private static BattleCompletions ParseBattleCompletions(byte[] rksysData, int rkpdOffset)
    {
        var completions = new BattleCompletions();

        // Favorite Stage (0x1A6, 10 entries)
        var stageBaseOffset = rkpdOffset + 0x1A6;
        foreach (Stage stage in Enum.GetValues(typeof(Stage)))
        {
            var offset = stageBaseOffset + (int)stage * 2;
            completions.Stage[stage] = (int)BigEndianBinaryHelper.BufferToUint16(rksysData, offset);
        }
        return completions;
    }

    private static TrophyCabinet ParseTrophyCabinet(byte[] rksysData, int rkpdOffset)
    {
        var cabinet = new TrophyCabinet();
        var cupDataStartOffset = rkpdOffset + 0x1C0;
        const int cupBlockSize = 0x60;

        var cupIndex = 0;
        foreach (var engineClass in EngineClasses)
        {
            foreach (var cupName in CupNames)
            {
                var cupOffset = cupDataStartOffset + (cupIndex * cupBlockSize);

                var trophyByte = rksysData[cupOffset + 0x4F];
                var rankByte = rksysData[cupOffset + 0x51];
                var completedByte = rksysData[cupOffset + 0x52];

                var info = new TrophyInfo
                {
                    CupType = (CupTrophyType)(trophyByte & 0x03),
                    Rank = (CupRank)((rankByte >> 4) & 0x0F),
                    Completed = (completedByte & 0x01) == 1,
                };

                var key = $"{cupName} Cup ({engineClass})";
                cabinet.PerCup[key] = info;

                cupIndex++;
            }
        }
        return cabinet;
    }
}

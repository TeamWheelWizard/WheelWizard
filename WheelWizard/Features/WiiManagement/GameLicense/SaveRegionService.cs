using System.IO.Abstractions;
using WheelWizard.GameFiles;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;

namespace WheelWizard.WiiManagement.GameLicense;

public interface ISaveRegionService
{
    List<MarioKartWiiEnums.Regions> GetAvailableRegions(string saveDirectory);

    /// <summary>The region of the extracted game (or of the picked image before that), or <c>None</c> when it can't be read.</summary>
    MarioKartWiiEnums.Regions GetGameFileRegion();

    /// <summary>
    /// The region whose save Dolphin will use: the game file's region when it can be read,
    /// otherwise the last used region, otherwise the first region that has a save.
    /// </summary>
    MarioKartWiiEnums.Regions ResolveActiveRegion(string saveDirectory);
}

public sealed class SaveRegionService(IFileSystem fileSystem, ISettingsManager settings, IGameFilesService gameFiles) : ISaveRegionService
{
    public List<MarioKartWiiEnums.Regions> GetAvailableRegions(string saveDirectory)
    {
        var validRegions = new List<MarioKartWiiEnums.Regions>();

        foreach (var region in Enum.GetValues<MarioKartWiiEnums.Regions>())
        {
            if (region == MarioKartWiiEnums.Regions.None)
                continue;

            // Build the folder path using the region's game ID
            var regionFolderName = GameRegion.GetGameId(region);
            var regionFolderPath = fileSystem.Path.Combine(saveDirectory, regionFolderName);

            // Check if the directory exists
            if (!fileSystem.Directory.Exists(regionFolderPath))
                continue;

            // Check if there is at least one .rksys file in the folder
            var rksysFiles = fileSystem.Directory.EnumerateFiles(regionFolderPath, "rksys.dat", SearchOption.TopDirectoryOnly);
            if (rksysFiles.Any())
                validRegions.Add(region);
        }
        //if no valid regions are found, add the none region
        if (validRegions.Count == 0)
            validRegions.Add(MarioKartWiiEnums.Regions.None);
        return validRegions;
    }

    public MarioKartWiiEnums.Regions GetGameFileRegion() => gameFiles.GetRegion();

    public MarioKartWiiEnums.Regions ResolveActiveRegion(string saveDirectory)
    {
        var gameRegion = GetGameFileRegion();
        if (gameRegion != MarioKartWiiEnums.Regions.None)
            return gameRegion;

        var lastRegion = settings.Get<MarioKartWiiEnums.Regions>(settings.RR_REGION);
        return lastRegion != MarioKartWiiEnums.Regions.None ? lastRegion : GetAvailableRegions(saveDirectory)[0];
    }
}

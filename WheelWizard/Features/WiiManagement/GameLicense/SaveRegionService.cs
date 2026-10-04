using System.IO.Abstractions;
using WheelWizard.Models.Enums;

namespace WheelWizard.WiiManagement.GameLicense;

public interface ISaveRegionService
{
    List<MarioKartWiiEnums.Regions> GetAvailableRegions(string saveDirectory);
}

public sealed class SaveRegionService(IFileSystem fileSystem) : ISaveRegionService
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
}

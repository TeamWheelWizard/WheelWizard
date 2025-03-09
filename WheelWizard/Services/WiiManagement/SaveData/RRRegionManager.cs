using WheelWizard.Models.Enums;

namespace WheelWizard.Services.Other;

public class RRRegionManager
{
    public static List<MarioKartWiiEnums.Regions> GetValidRegions()
    {
        var validRegions = new List<MarioKartWiiEnums.Regions>();
        
        foreach (var region in Enum.GetValues<MarioKartWiiEnums.Regions>())
        {
            if (region == MarioKartWiiEnums.Regions.None)
                continue;

            // Build the folder path using the region's game ID
            var regionFolderName = ConvertRegionToGameId(region);
            var regionFolderPath = Path.Combine(PathManager.SaveFolderPath, regionFolderName);

            // Check if the directory exists
            if (!Directory.Exists(regionFolderPath))
                continue;

            // Check if there is at least one .rksys file in the folder
            var rksysFiles = Directory.EnumerateFiles(regionFolderPath, "rksys.dat", SearchOption.TopDirectoryOnly);
            if (rksysFiles.Any())
                validRegions.Add(region);
        }
        //if no valid regions are found, add the none region
        if (validRegions.Count == 0)
            validRegions.Add(MarioKartWiiEnums.Regions.None);
        return validRegions;
    }
    public static string ConvertRegionToGameId(MarioKartWiiEnums.Regions region) => region switch
    {
        MarioKartWiiEnums.Regions.None => "",
        MarioKartWiiEnums.Regions.America => "RMCE",
        MarioKartWiiEnums.Regions.Europe => "RMCP",
        MarioKartWiiEnums.Regions.Japan => "RMCJ",
        MarioKartWiiEnums.Regions.Korea => "RMCK",
        _ => throw new ArgumentOutOfRangeException(nameof(region), region, null)
    };
}


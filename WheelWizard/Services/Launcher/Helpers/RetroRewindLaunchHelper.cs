using System.Text.Json;
using System.Text.Json.Serialization;
using WheelWizard.Models.RRLaunchModels;

namespace WheelWizard.Services.Launcher.Helpers;

public static class RetroRewindLaunchHelper
{
    private static string XmlFilePath => PathManager.XmlFilePath;
    private static string JsonFilePath => PathManager.RrLaunchJsonFilePath;

    public static void GenerateLaunchJson()
    {
        GenerateLaunchJson(XmlFilePath);
    }

    public static void GenerateLaunchJson(string xmlFilePath)
    {
        var launchInfo = GetLaunchInfo(xmlFilePath);
        GenerateLaunchJson(
            xmlFilePath,
            PathManager.RiivolutionWhWzFolderPath,
            launchInfo.SectionName,
            launchInfo.MyStuffChoice,
            launchInfo.EnableSeparateSave
        );
    }

    private static void GenerateLaunchJson(
        string xmlFilePath,
        string rootFolderPath,
        string sectionName,
        int myStuffChoice,
        bool enableSeparateSave
    )
    {
        var launchConfig = new LaunchConfig
        {
            BaseFile = Path.GetFullPath(PathManager.GameFilePath),
            DisplayName = "RR",
            Riivolution = new()
            {
                Patches =
                [
                    new()
                    {
                        Options = BuildOptions(sectionName, myStuffChoice, enableSeparateSave).ToArray(),
                        Root = Path.GetFullPath(rootFolderPath),
                        Xml = Path.GetFullPath(xmlFilePath),
                    },
                ],
            },
            Type = "dolphin-game-mod-descriptor",
            Version = 1,
        };

        var jsonString = JsonSerializer.Serialize(
            launchConfig,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }
        );

        File.WriteAllText(JsonFilePath, jsonString);
    }

    private static List<OptionConfig> BuildOptions(string sectionName, int myStuffChoice, bool enableSeparateSave)
    {
        var options = new List<OptionConfig>
        {
            new()
            {
                Choice = 1,
                OptionName = "Pack",
                SectionName = sectionName,
            },
            new()
            {
                Choice = myStuffChoice,
                OptionName = "My Stuff",
                SectionName = sectionName,
            },
        };

        if (enableSeparateSave)
        {
            options.Add(
                new()
                {
                    Choice = 1,
                    OptionName = "Seperate Savegame",
                    SectionName = sectionName,
                }
            );
        }

        return options;
    }

    private static (string SectionName, int MyStuffChoice, bool EnableSeparateSave) GetLaunchInfo(string xmlFilePath)
    {
        var fileName = Path.GetFileName(xmlFilePath);
        if (fileName.Equals("RRBeta.xml", StringComparison.OrdinalIgnoreCase))
            return ("Retro Rewind Beta", 2, true);

        return ("Retro Rewind", 2, false);
    }
}

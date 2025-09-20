using System.Text.Json;
using System.Text.Json.Serialization;
using WheelWizard.Models.RRLaunchModels;
using WheelWizard.Services.Settings;

namespace WheelWizard.Services.Launcher.Helpers;

public static class RetroRewindLaunchHelper
{
    private static string XmlFilePath => PathManager.XmlFilePath;
    private static string JsonFilePath => PathManager.RrLaunchJsonFilePath;

    public static void GenerateLaunchJson(string baseFilePath)
    {
        var removeBlur = (bool)SettingsManager.REMOVE_BLUR.Get();

        var launchConfig = new LaunchConfig
        {
            BaseFile = Path.GetFullPath(baseFilePath),
            DisplayName = "RR",
            Riivolution = new()
            {
                Patches =
                [
                    new()
                    {
                        Options =
                        [
                            new()
                            {
                                Choice = 1,
                                OptionName = "Pack",
                                SectionName = "Retro Rewind",
                            },
                            new()
                            {
                                Choice = 2,
                                OptionName = "My Stuff",
                                SectionName = "Retro Rewind",
                            },
                            new()
                            {
                                Choice = removeBlur ? 1 : 0,
                                OptionName = "Remove Blur",
                                SectionName = "Retro Rewind",
                            },
                        ],
                        Root = Path.GetFullPath(PathManager.RiivolutionWhWzFolderPath),
                        Xml = Path.GetFullPath(XmlFilePath),
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
}

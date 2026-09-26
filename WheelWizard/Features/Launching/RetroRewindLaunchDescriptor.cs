using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using WheelWizard.CustomDistributions;
using WheelWizard.Models.RRLaunchModels;
using WheelWizard.Settings;

namespace WheelWizard.Launching;

public interface IRetroRewindLaunchDescriptor
{
    void GenerateLaunchJson();
    void GenerateLaunchJson(string xmlFilePath);
}

public sealed class RetroRewindLaunchDescriptor(IFileSystem fileSystem, ISettingsManager settings, ICustomDistributionPaths paths)
    : IRetroRewindLaunchDescriptor
{
    private string XmlFilePath => paths.XmlFilePath;
    private string JsonFilePath => paths.LaunchJsonFilePath;

    public void GenerateLaunchJson()
    {
        GenerateLaunchJson(XmlFilePath);
    }

    public void GenerateLaunchJson(string xmlFilePath)
    {
        var launchInfo = GetLaunchInfo(xmlFilePath);
        GenerateLaunchJson(
            xmlFilePath,
            paths.RootFolderPath,
            launchInfo.SectionName,
            launchInfo.MyStuffChoice,
            launchInfo.EnableSeparateSave
        );
    }

    private void GenerateLaunchJson(
        string xmlFilePath,
        string rootFolderPath,
        string sectionName,
        int myStuffChoice,
        bool enableSeparateSave
    )
    {
        var launchConfig = new LaunchConfig
        {
            BaseFile = fileSystem.Path.GetFullPath(settings.Get<string>(settings.GAME_LOCATION)),
            DisplayName = "RR",
            Riivolution = new()
            {
                Patches =
                [
                    new()
                    {
                        Options = BuildOptions(sectionName, myStuffChoice, enableSeparateSave).ToArray(),
                        Root = fileSystem.Path.GetFullPath(rootFolderPath),
                        Xml = fileSystem.Path.GetFullPath(xmlFilePath),
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

        fileSystem.File.WriteAllText(JsonFilePath, jsonString);
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
            return ("Retro Rewind Beta", 0, true);

        return ("Retro Rewind", 0, false);
    }
}

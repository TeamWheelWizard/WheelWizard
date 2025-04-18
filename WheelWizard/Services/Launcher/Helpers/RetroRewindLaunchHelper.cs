﻿using System.Text.Json;
using System.Text.Json.Serialization;
using WheelWizard.Models.RRLaunchModels;
using WheelWizard.Services.Settings;

namespace WheelWizard.Services.Launcher.Helpers;

public static class RetroRewindLaunchHelper
{
    private static string XmlFilePath => PathManager.XmlFilePath;
    private static string JsonFilePath => PathManager.RrLaunchJsonFilePath;

    public static void GenerateLaunchJson()
    {
        var language = (int)SettingsManager.RR_LANGUAGE.Get();
        var removeBlur = (bool)SettingsManager.REMOVE_BLUR.Get();

        var launchConfig = new LaunchConfig
        {
            BaseFile = Path.GetFullPath(PathManager.GameFilePath),
            DisplayName = "RR",
            Riivolution = new RiivolutionConfig
            {
                Patches = new[]
                {
                    new PatchConfig
                    {
                        Options = new[]
                        {
                            new OptionConfig { Choice = 1, OptionName = "Pack", SectionName = "Retro Rewind" },
                            new OptionConfig { Choice = 2, OptionName = "My Stuff", SectionName = "Retro Rewind" },
                            new OptionConfig { Choice = language, OptionName = "Language", SectionName = "Retro Rewind" },
                            new OptionConfig { Choice = removeBlur ? 1 : 0, OptionName = "Remove Blur", SectionName = "Retro Rewind" }
                        },
                        Root = Path.GetFullPath(PathManager.RiivolutionWhWzFolderPath),
                        Xml = Path.GetFullPath(XmlFilePath)
                    }
                }
            },
            Type = "dolphin-game-mod-descriptor",
            Version = 1
        };

        var jsonString = JsonSerializer.Serialize(launchConfig, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        File.WriteAllText(JsonFilePath, jsonString);
    }
}

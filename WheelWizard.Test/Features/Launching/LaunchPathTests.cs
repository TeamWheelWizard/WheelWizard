using System.Text.Json;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Launching;
using WheelWizard.Mods;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Platform;
using WheelWizard.WiiManagement.Controllers;

namespace WheelWizard.Test.Features.Launching;

public class LaunchPathTests
{
    [Theory]
    [InlineData(false, "Retro Rewind", 2)]
    [InlineData(true, "Retro Rewind Beta", 3)]
    public void Descriptor_PreservesGameRootAndDistributionOptions(bool beta, string section, int optionCount)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        fs.Directory.CreateDirectory("/output");
        var settings = CreateSettings();
        var paths = Substitute.For<ICustomDistributionPaths>();
        paths.LaunchJsonFilePath.Returns("/output/RR.json");
        paths.RootFolderPath.Returns("/distribution");
        paths.XmlFilePath.Returns("/distribution/RR.xml");
        var descriptor = new RetroRewindLaunchDescriptor(fs, settings, paths);

        if (beta)
            descriptor.GenerateLaunchJson("/distribution/RRBeta.xml");
        else
            descriptor.GenerateLaunchJson();

        using var json = JsonDocument.Parse(fs.File.ReadAllText("/output/RR.json"));
        var root = json.RootElement;
        Assert.Equal("/game.iso", root.GetProperty("base-file").GetString());
        Assert.Equal("dolphin-game-mod-descriptor", root.GetProperty("type").GetString());
        var patch = root.GetProperty("riivolution").GetProperty("patches")[0];
        Assert.Equal("/distribution", patch.GetProperty("root").GetString());
        Assert.Equal(beta ? "/distribution/RRBeta.xml" : "/distribution/RR.xml", patch.GetProperty("xml").GetString());
        var options = patch.GetProperty("options").EnumerateArray().ToArray();
        Assert.Equal(optionCount, options.Length);
        Assert.All(options, option => Assert.Equal(section, option.GetProperty("section-name").GetString()));
        Assert.Equal(0, options[1].GetProperty("choice").GetInt32());
        if (beta)
        {
            // This spelling is part of the distribution XML contract.
            Assert.Equal("Seperate Savegame", options[2].GetProperty("option-name").GetString());
            Assert.Equal(1, options[2].GetProperty("choice").GetInt32());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedDistributionPreflight_DoesNotKillPrepareOrWrite(bool beta)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        var settings = CreateSettings();
        fs.File.WriteAllText("/game.iso", "game");
        var dolphin = Substitute.For<IDolphinLaunchService>();
        dolphin.PreflightDolphinVersionAsync().Returns(Fail("cancelled"));
        var mods = Substitute.For<IModsLaunchService>();
        var remotes = Substitute.For<IWiiRemoteConfigurationService>();
        var descriptor = Substitute.For<IRetroRewindLaunchDescriptor>();
        var distributions = Substitute.For<ICustomDistributionSingletonService>();
        var dolphinPaths = Substitute.For<IDolphinPaths>();
        var paths = Substitute.For<ICustomDistributionPaths>();
        var environment = Substitute.For<IRuntimeEnvironment>();
        var launcher = new RetroRewindLaunchService(
            settings,
            fs,
            dolphin,
            remotes,
            dolphinPaths,
            paths,
            mods,
            descriptor,
            environment,
            Substitute.For<ILaunchPrompts>()
        );
        Assert.True((await launcher.LaunchAsync(beta)).IsFailure);

        dolphin.DidNotReceive().KillDolphin();
        Assert.Empty(mods.ReceivedCalls());
        Assert.Empty(remotes.ReceivedCalls());
        Assert.Empty(descriptor.ReceivedCalls());
    }

    private static ISettingsManager CreateSettings()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GAME_LOCATION.Returns(new WhWzSetting(typeof(string), "GamePath", "/game.iso"));
        settings.Get<string>(Arg.Any<Setting>()).Returns(call => (string)call.Arg<Setting>().Get());
        return settings;
    }
}

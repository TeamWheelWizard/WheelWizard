using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.Dolphin;

public class FeaturePathOwnershipTests
{
    [Fact]
    public void DolphinPaths_FollowTheirOwnSettings_WhenUserAndCustomFoldersChange()
    {
        var fs = NewFileSystem();
        var (settings, paths) = CreateDolphinPaths(fs, "/first-user");

        Assert.Equal("/first-user/Config", paths.ConfigFolderPath);
        Assert.Equal("/first-user/Load", paths.LoadFolderPath);
        Assert.Equal("/first-user/Wii", paths.WiiFolderPath);

        settings.USER_FOLDER_PATH.Set("/second-user", skipSave: true);
        fs.Directory.CreateDirectory("/custom-load");
        fs.Directory.CreateDirectory("/custom-nand");
        settings.LOAD_PATH.Set("/custom-load", skipSave: true);
        settings.NAND_ROOT_PATH.Set("/custom-nand", skipSave: true);

        Assert.Equal("/second-user/Config", paths.ConfigFolderPath);
        Assert.Equal("/custom-load", paths.LoadFolderPath);
        Assert.Equal("/custom-nand", paths.WiiFolderPath);

        fs.Directory.Delete("/custom-load");
        fs.Directory.Delete("/custom-nand");

        Assert.Equal("/second-user/Load", paths.LoadFolderPath);
        Assert.Equal("/second-user/Wii", paths.WiiFolderPath);
    }

    [Fact]
    public void SeparateDolphinOwners_DoNotReadEachOthersSettings()
    {
        var fs = NewFileSystem();
        var (firstSettings, first) = CreateDolphinPaths(fs, "/first");
        var (_, second) = CreateDolphinPaths(fs, "/second");

        firstSettings.USER_FOLDER_PATH.Set("/changed", skipSave: true);

        Assert.Equal("/changed/Config", first.ConfigFolderPath);
        Assert.Equal("/second/Config", second.ConfigFolderPath);
    }

    [Fact]
    public void DistributionWithoutDolphin_UsesApplicationData_AndFollowsRelocation()
    {
        var fs = NewFileSystem();
        var (_, dolphin) = CreateDolphinPaths(fs, "");
        var location = Substitute.For<IApplicationDataLocation>();
        var root = "/launcher-data";
        location.DirectoryPath.Returns(_ => root);
        var paths = new CustomDistributionPaths(location, dolphin, fs);

        Assert.Equal("/launcher-data/RetroRewind/RetroRewind6", paths.RetroRewindFolderPath);

        root = "/relocated";

        Assert.Equal("/relocated/RetroRewind/RetroRewind6", paths.RetroRewindFolderPath);
        Assert.Equal("/relocated/RetroRewind/riivolution/save/RetroWFC", paths.SaveFolderPath);
        Assert.Equal("/relocated/Mods/Temp/RRBetaTemp/Testers.zip", paths.BetaArchivePath);
        Assert.Equal("/relocated/RRBeta.manifest.json", paths.BetaManifestFilePath);
    }

    [Fact]
    public void DistributionWithDolphin_UsesEffectiveLoadDirectory_WhileDownloadsFollowAppData()
    {
        var fs = NewFileSystem();
        var (settings, dolphin) = CreateDolphinPaths(fs, "/dolphin-user");
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns("/launcher-data");
        var paths = new CustomDistributionPaths(location, dolphin, fs);

        Assert.Equal("/dolphin-user/Load/Riivolution/WheelWizard", paths.RootFolderPath);
        fs.Directory.CreateDirectory("/custom-load");
        settings.LOAD_PATH.Set("/custom-load", skipSave: true);
        location.DirectoryPath.Returns("/relocated");

        Assert.Equal("/custom-load/Riivolution/WheelWizard", paths.RootFolderPath);
        Assert.Equal("/custom-load/Riivolution/WheelWizard/RRBeta/Patches", paths.BetaPatchesFolderPath);
        Assert.Equal("/relocated/Mods/Temp/RetroRewind.zip", paths.RetroRewindArchivePath);
    }

    private static MockFileSystem NewFileSystem() => new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));

    private static (ISettingsManager, DolphinPaths) CreateDolphinPaths(MockFileSystem fs, string userFolder)
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.USER_FOLDER_PATH.Returns(new WhWzSetting(typeof(string), "UserFolderPath", userFolder));
        settings.DOLPHIN_LOCATION.Returns(new WhWzSetting(typeof(string), "DolphinLocation", "dolphin-emu"));
        settings.LOAD_PATH.Returns(
            new WhWzSetting(typeof(string), "LoadPath", "").SetValidation(value =>
                value is string directory && !string.IsNullOrWhiteSpace(directory) && fs.Directory.Exists(directory)
            )
        );
        settings.NAND_ROOT_PATH.Returns(
            new WhWzSetting(typeof(string), "NandRootPath", "").SetValidation(value =>
                value is string directory && !string.IsNullOrWhiteSpace(directory) && fs.Directory.Exists(directory)
            )
        );
        settings.Get<string>(Arg.Any<Setting>()).Returns(call => (string)call.Arg<Setting>().Get());
        return (settings, new DolphinPaths(settings, new DolphinPathResolver(fs, Substitute.For<IRuntimeEnvironment>()), fs));
    }
}

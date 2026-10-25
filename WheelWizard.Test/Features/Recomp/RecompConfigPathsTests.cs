using Testably.Abstractions.Testing;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Recomp;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.Test.Features.Recomp;

public class RecompConfigPathsTests
{
    [Fact]
    public void ApplyPaths_WritesTheCurrentRetroRewindAndNandFolders()
    {
        var fixture = new Fixture();
        fixture.Fs.Directory.CreateDirectory("/data/RetroRewind/RetroRewind6");
        fixture.Fs.Directory.CreateDirectory("/dolphin/Wii");
        fixture.UseDolphinData.Set(true, skipSave: true);

        Assert.True(fixture.Service.ApplyPathsToRecompConfig().IsSuccess);

        Assert.Equal("/data/RetroRewind/RetroRewind6", fixture.RetroRewindRoot.Get());
        Assert.Equal("/dolphin/Wii", fixture.NandRoot.Get());
    }

    [Fact]
    public void ApplyPaths_FollowsAMovedRetroRewind_AndKeepsTheSetupValueWhenNothingIsInstalled()
    {
        var fixture = new Fixture();
        fixture.RetroRewindRoot.Set("/old/Load/Riivolution/WheelWizard/RetroRewind6", skipSave: true);

        Assert.True(fixture.Service.ApplyPathsToRecompConfig().IsSuccess);
        Assert.Equal("/old/Load/Riivolution/WheelWizard/RetroRewind6", fixture.RetroRewindRoot.Get());

        fixture.Fs.Directory.CreateDirectory("/data/RetroRewind/RetroRewind6");
        Assert.True(fixture.Service.ApplyPathsToRecompConfig().IsSuccess);
        Assert.Equal("/data/RetroRewind/RetroRewind6", fixture.RetroRewindRoot.Get());
    }

    [Fact]
    public void NandChoices_ShareCopyAndPrivate_SetTheMatchingFlags()
    {
        var data = Substitute.For<IRecompDolphinDataService>();
        data.CopyNandForRecomp().Returns(Ok());

        data.ApplyNandChoice(RecompNandChoice.Share);
        data.Received(1).SetSharingEnabled(true);

        data.ApplyNandChoice(RecompNandChoice.Copy);
        Received.InOrder(() =>
        {
            data.CopyNandForRecomp();
            data.SetCopyEnabled(true);
            data.SetSharingEnabled(false);
        });

        data.CopyNandForRecomp().Returns(Fail("disk full"));
        data.ClearReceivedCalls();
        Assert.True(data.ApplyNandChoice(RecompNandChoice.Copy).IsFailure);
        data.DidNotReceive().SetCopyEnabled(Arg.Any<bool>());
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; } = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        public WhWzSetting<string> NandRoot { get; } = new("nand_root", "");
        public WhWzSetting<string> RetroRewindRoot { get; } = new("retro_rewind_root", "");
        public WhWzSetting<bool> UseDolphinData { get; } = new("UseDolphinData", false);
        public RecompDolphinDataService Service { get; }

        public Fixture()
        {
            var settings = Substitute.For<ISettingsManager>();
            var userFolder = new WhWzSetting<string>("UserFolderPath", "/dolphin");
            var nandOverride = new WhWzSetting<string>("NandRootPath", "");
            var copy = new WhWzSetting<bool>("CopyNand", false);
            settings.RECOMP_NAND_ROOT.Returns(NandRoot);
            settings.RECOMP_RETRO_REWIND_ROOT.Returns(RetroRewindRoot);
            settings.RECOMP_USE_DOLPHIN_DATA.Returns(UseDolphinData);
            settings.RECOMP_COPY_DOLPHIN_NAND.Returns(copy);
            settings.USER_FOLDER_PATH.Returns(userFolder);
            settings.NAND_ROOT_PATH.Returns(nandOverride);
            settings.Get(Arg.Any<Setting<string>>()).Returns(call => call.Arg<Setting<string>>().Get());
            settings.Get(Arg.Any<Setting<bool>>()).Returns(call => call.Arg<Setting<bool>>().Get());
            settings
                .Set(Arg.Any<Setting<string>>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(call => call.Arg<Setting<string>>().Set(call.ArgAt<string>(1), skipSave: true));

            var paths = Substitute.For<IRecompPaths>();
            paths.ConfigFilePath.Returns("/data/Recomp/UserData/Config.toml");
            Fs.Directory.CreateDirectory("/data/Recomp/UserData");
            Fs.File.WriteAllText("/data/Recomp/UserData/Config.toml", "[paths]\n");

            var distribution = Substitute.For<ICustomDistributionPaths>();
            distribution.RetroRewindFolderPath.Returns("/data/RetroRewind/RetroRewind6");

            Service = new RecompDolphinDataService(
                settings,
                Substitute.For<IRecompSettingManager>(),
                Fs,
                Substitute.For<IDolphinDiscoveryService>(),
                paths,
                distribution
            );
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.CustomDistributions;
using WheelWizard.DiscImages;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Dolphin.Paths;
using WheelWizard.DolphinInstaller;
using WheelWizard.Onboarding;
using WheelWizard.Recomp;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Test.Features.Onboarding;

public class OnboardingServiceTests
{
    [Fact]
    public void IsRequired_OnlyWhenNothingWorks_AndNeverAfterCompletion()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Service.IsRequired);

        fixture.Completed.Set(true, skipSave: true);
        Assert.False(fixture.Service.IsRequired);
    }

    [Fact]
    public void AWorkingDolphinSetup_IsMarkedAsOnboarded()
    {
        var fixture = new Fixture();
        fixture.Settings.DolphinPathsSetupCorrectly().Returns(true);

        Assert.False(fixture.Service.IsRequired);
        fixture.Service.MarkCompletedIfAlreadySetUp();

        Assert.True(fixture.Completed.Get());
    }

    [Fact]
    public void AnInstalledWiiCompiledWithAGame_CountsAsWorking()
    {
        var fixture = new Fixture();
        var install = Substitute.For<IRecompInstallService>();
        install.IsInstalled.Returns(true);
        fixture.Services.GetService(typeof(IRecompInstallService)).Returns(install);
        fixture.Fs.File.WriteAllText("/game.rvz", "");
        fixture.Game.Set("/game.rvz", skipSave: true);

        Assert.False(fixture.Service.IsRequired);
    }

    [Fact]
    public void AnEarlierSetupThatIsBrokenToday_GetsRepairedInsteadOfOnboarded()
    {
        // The Dolphin user folder is on a drive that is not plugged in on the first start of this version.
        var fixture = new Fixture();
        fixture.UserFolder.Set("/unplugged/Dolphin Emulator", skipSave: true);
        fixture.Location.Set("/unplugged/Dolphin.exe", skipSave: true);

        Assert.False(fixture.Service.IsRequired);
        fixture.Service.MarkCompletedIfAlreadySetUp();

        Assert.True(fixture.Completed.Get());
    }

    [Fact]
    public void PathsSavedByAnUnfinishedOnboarding_DoNotCountAsAnEarlierSetup()
    {
        // The app was closed after picking a Dolphin and a game, before the onboarding finished.
        var fixture = new Fixture();
        fixture.Service.MarkStarted();
        fixture.Location.Set("/own/Dolphin.exe", skipSave: true);
        fixture.UserFolder.Set("/own/User", skipSave: true);
        fixture.Settings.DolphinPathsSetupCorrectly().Returns(true);

        fixture.Service.MarkCompletedIfAlreadySetUp();

        Assert.False(fixture.Completed.Get());
        Assert.True(fixture.Service.IsRequired);
    }

    [Fact]
    public void StartingTheOnboarding_AfterItWasCompleted_ChangesNothing()
    {
        var fixture = new Fixture();
        fixture.Completed.Set(true, skipSave: true);

        fixture.Service.MarkStarted();

        Assert.False(fixture.Started.Get());
        Assert.False(fixture.Service.IsRequired);
    }

    [Fact]
    public void TheFlatpaksDefaultDolphinLocation_DoesNotCountAsSetUp()
    {
        var fixture = new Fixture(defaultLocation: "/app/extensions/backends/dolphin-emu/bin/dolphin-emu-wrapper");

        Assert.True(fixture.Service.IsRequired);
        fixture.Service.MarkCompletedIfAlreadySetUp();

        Assert.False(fixture.Completed.Get());
    }

    [Fact]
    public void RestoringFrontendSettings_UndoesAPickedDolphin_EvenToPathsTheValidatorsReject()
    {
        // Picking a Dolphin and then backing out to the choice must leave nothing behind, but an empty
        // location is not a valid one to set while the Dolphin frontend is active.
        var fixture = new Fixture();
        var snapshot = fixture.Service.CaptureFrontendSettings();
        fixture.Location.Set("/managed/Dolphin.exe", skipSave: true);
        fixture.UserFolder.Set("/managed/User", skipSave: true);
        fixture.Managed.Set(true, skipSave: true);
        fixture.ShareNand.Set(true, skipSave: true);

        fixture.Service.RestoreFrontendSettings(snapshot);

        Assert.Equal("", fixture.Location.Get());
        Assert.Equal("", fixture.UserFolder.Get());
        Assert.False(fixture.Managed.Get());
        Assert.False(fixture.ShareNand.Get());
    }

    [Fact]
    public void RestoringFrontendSettings_PutsBackValuesThatWereSetBefore()
    {
        var fixture = new Fixture();
        fixture.Location.Set("/own/Dolphin.exe", skipSave: true);
        fixture.CopyNand.Set(true, skipSave: true);
        var snapshot = fixture.Service.CaptureFrontendSettings();
        fixture.Location.Set("/managed/Dolphin.exe", skipSave: true);
        fixture.CopyNand.Set(false, skipSave: true);

        fixture.Service.RestoreFrontendSettings(snapshot);

        Assert.Equal("/own/Dolphin.exe", fixture.Location.Get());
        Assert.True(fixture.CopyNand.Get());
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; } = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
        public IServiceProvider Services { get; } = Substitute.For<IServiceProvider>();
        public WhWzSetting<bool> Completed { get; } = new("OnboardingCompleted", false);
        public WhWzSetting<bool> Started { get; } = new("OnboardingStarted", false);
        public WhWzSetting<string> Game { get; }
        public WhWzSetting<string> Location { get; }
        public WhWzSetting<string> UserFolder { get; } = new("UserFolderPath", "");
        public WhWzSetting<bool> Managed { get; } = new("DolphinManaged", false);
        public WhWzSetting<bool> ShareNand { get; } = new("RecompUseDolphinData", false);
        public WhWzSetting<bool> CopyNand { get; } = new("RecompCopyDolphinNand", false);
        public OnboardingService Service { get; }

        public Fixture(string defaultLocation = "")
        {
            Game = new WhWzSetting<string>("GameLocation", "").SetValidation(value => Fs.File.Exists(value));
            Location = new WhWzSetting<string>("DolphinLocation", defaultLocation).SetValidation(value => value.Length > 0);
            Settings.ONBOARDING_COMPLETED.Returns(Completed);
            Settings.ONBOARDING_STARTED.Returns(Started);
            Settings.GAME_LOCATION.Returns(Game);
            Settings.DOLPHIN_LOCATION.Returns(Location);
            Settings.USER_FOLDER_PATH.Returns(UserFolder);
            Settings.DOLPHIN_MANAGED.Returns(Managed);
            Settings.RECOMP_USE_DOLPHIN_DATA.Returns(ShareNand);
            Settings.RECOMP_COPY_DOLPHIN_NAND.Returns(CopyNand);
            Settings.Get(Arg.Any<Setting<string>>()).Returns(call => call.Arg<Setting<string>>().Get());
            Settings.Get(Arg.Any<Setting<bool>>()).Returns(call => call.Arg<Setting<bool>>().Get());
            Settings
                .Set(Arg.Any<Setting<bool>>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(call => call.Arg<Setting<bool>>().Set(call.ArgAt<bool>(1), skipSave: true));
            Settings
                .Set(Arg.Any<Setting<string>>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(call => call.Arg<Setting<string>>().Set(call.ArgAt<string>(1), skipSave: true));

            Service = new OnboardingService(
                Settings,
                Fs,
                Substitute.For<IRuntimeEnvironment>(),
                Substitute.For<IDiscImageService>(),
                Substitute.For<IDolphinDiscoveryService>(),
                Substitute.For<IDolphinPathResolver>(),
                Substitute.For<IUnixCommandService>(),
                Substitute.For<ILinuxDolphinInstaller>(),
                Substitute.For<IManagedDolphinService>(),
                Substitute.For<IMiiRepositoryService>(),
                Substitute.For<IGameLicenseSingletonService>(),
                Substitute.For<ICustomDistributionPaths>(),
                Services,
                NullLogger<OnboardingService>.Instance
            );
        }
    }
}

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
    public void TheFlatpaksDefaultDolphinLocation_DoesNotCountAsSetUp()
    {
        var fixture = new Fixture(defaultLocation: "/app/extensions/backends/dolphin-emu/bin/dolphin-emu-wrapper");

        Assert.True(fixture.Service.IsRequired);
        fixture.Service.MarkCompletedIfAlreadySetUp();

        Assert.False(fixture.Completed.Get());
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; } = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
        public IServiceProvider Services { get; } = Substitute.For<IServiceProvider>();
        public WhWzSetting<bool> Completed { get; } = new("OnboardingCompleted", false);
        public WhWzSetting<string> Game { get; }
        public WhWzSetting<string> Location { get; }
        public WhWzSetting<string> UserFolder { get; } = new("UserFolderPath", "");
        public OnboardingService Service { get; }

        public Fixture(string defaultLocation = "")
        {
            Game = new WhWzSetting<string>("GameLocation", "").SetValidation(value => Fs.File.Exists(value));
            Location = new WhWzSetting<string>("DolphinLocation", defaultLocation);
            Settings.ONBOARDING_COMPLETED.Returns(Completed);
            Settings.GAME_LOCATION.Returns(Game);
            Settings.DOLPHIN_LOCATION.Returns(Location);
            Settings.USER_FOLDER_PATH.Returns(UserFolder);
            Settings.Get(Arg.Any<Setting<string>>()).Returns(call => call.Arg<Setting<string>>().Get());
            Settings.Get(Arg.Any<Setting<bool>>()).Returns(call => call.Arg<Setting<bool>>().Get());
            Settings
                .Set(Arg.Any<Setting<bool>>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(call => call.Arg<Setting<bool>>().Set(call.ArgAt<bool>(1), skipSave: true));

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

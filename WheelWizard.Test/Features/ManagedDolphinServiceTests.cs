using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomDistributions;
using WheelWizard.DiscImages;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Dolphin.Paths;
using WheelWizard.DolphinInstaller;
using WheelWizard.Onboarding;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Test.Features;

public class ManagedDolphinServiceTests
{
    private const string DataFolder = "/launcher-data";
    private const string ManagedUser = "/launcher-data/Dolphin/User";

    [Fact]
    public void Remove_KeepsTheUserFolder_WhenTheUsersOwnDolphinWasGivenIt()
    {
        var fixture = new Fixture();
        fixture.InstallManagedDolphin();

        // A Dolphin that never ran has no user folder of its own, so onboarding hands it the managed one.
        Assert.True(fixture.Onboarding.UseOwnDolphin("/own/Dolphin.exe").IsSuccess);
        Assert.Equal(ManagedUser, fixture.UserFolder.Get());
        Assert.False(fixture.Managed.IsActive);
        fixture.Fs.Directory.CreateDirectory($"{ManagedUser}/Wii/title/00010004/524d4345/data");
        fixture.Fs.File.WriteAllText($"{ManagedUser}/Wii/title/00010004/524d4345/data/rksys.dat", "save");

        Assert.False(fixture.Managed.CanRemove);
        var result = fixture.Managed.Remove();

        Assert.True(fixture.Fs.File.Exists($"{ManagedUser}/Wii/title/00010004/524d4345/data/rksys.dat"));
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Remove_DeletesEverything_WhenNothingUsesTheManagedFolders()
    {
        var fixture = new Fixture();
        fixture.InstallManagedDolphin();
        fixture.Fs.Directory.CreateDirectory("/own/User");
        fixture.UserFolder.Set("/own/User", skipSave: true);
        fixture.Location.Set("/own/Dolphin.exe", skipSave: true);
        fixture.Managed.Deactivate();

        Assert.True(fixture.Managed.CanRemove);
        Assert.True(fixture.Managed.Remove().IsSuccess);
        Assert.False(fixture.Fs.Directory.Exists($"{DataFolder}/Dolphin"));
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; } = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
        public WhWzSetting<string> Location { get; } = new("DolphinLocation", "");
        public WhWzSetting<string> UserFolder { get; } = new("UserFolderPath", "");
        public WhWzSetting<bool> Managed_ { get; } = new("DolphinManaged", false);
        public ManagedDolphinService Managed { get; }
        public OnboardingService Onboarding { get; }

        public Fixture()
        {
            Settings.DOLPHIN_LOCATION.Returns(Location);
            Settings.USER_FOLDER_PATH.Returns(UserFolder);
            Settings.DOLPHIN_MANAGED.Returns(Managed_);
            Settings.ONBOARDING_COMPLETED.Returns(new WhWzSetting<bool>("OnboardingCompleted", false));
            Settings.Get(Arg.Any<Setting<bool>>()).Returns(call => call.Arg<Setting<bool>>().Get());
            Settings.Get(Arg.Any<Setting<string>>()).Returns(call => call.Arg<Setting<string>>().Get());
            Settings
                .Set(Arg.Any<Setting<bool>>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(call => call.Arg<Setting<bool>>().Set(call.ArgAt<bool>(1), skipSave: true));
            Settings
                .Set(Arg.Any<Setting<string>>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(call => call.Arg<Setting<string>>().Set(call.ArgAt<string>(1), skipSave: true));

            var environment = Substitute.For<IRuntimeEnvironment>();
            environment.IsWindows.Returns(true);
            var applicationData = Substitute.For<IApplicationDataLocation>();
            applicationData.DirectoryPath.Returns(DataFolder);
            var discovery = Substitute.For<IDolphinDiscoveryService>();
            discovery.FindUserDirectory(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

            Managed = new ManagedDolphinService(
                Settings,
                applicationData,
                Fs,
                environment,
                Substitute.For<IDolphinReleaseClient>(),
                Substitute.For<IDownloadService>(),
                Substitute.For<IUnixProcessService>(),
                Substitute.For<ILinuxDolphinInstaller>(),
                discovery,
                Substitute.For<IDolphinPathResolver>(),
                Substitute.For<IDolphinVersionService>(),
                NullLogger<ManagedDolphinService>.Instance
            );
            Onboarding = new OnboardingService(
                Settings,
                Fs,
                environment,
                Substitute.For<IDiscImageService>(),
                discovery,
                Substitute.For<IDolphinPathResolver>(),
                Substitute.For<IUnixCommandService>(),
                Substitute.For<ILinuxDolphinInstaller>(),
                Managed,
                Substitute.For<IMiiRepositoryService>(),
                Substitute.For<IGameLicenseSingletonService>(),
                Substitute.For<ICustomDistributionPaths>(),
                Substitute.For<IServiceProvider>(),
                NullLogger<OnboardingService>.Instance
            );
        }

        /// <summary>The state after the onboarding installed the managed Dolphin and the user went back to pick their own.</summary>
        public void InstallManagedDolphin()
        {
            Fs.Directory.CreateDirectory($"{DataFolder}/Dolphin/App/Dolphin-x64");
            Fs.File.WriteAllText($"{DataFolder}/Dolphin/App/Dolphin-x64/Dolphin.exe", "");
            Assert.True(Managed.Activate().IsSuccess);
            Assert.True(Managed.IsInstalled);
        }
    }
}

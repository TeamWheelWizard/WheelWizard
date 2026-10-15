using static WheelWizard.Test.Features.ExistingSetups.ExistingSetupAssertions;

namespace WheelWizard.Test.Features.ExistingSetups;

/// <summary>
/// The onboarding is for somebody who has nothing set up. Anybody who set Wheel Wizard up with an earlier
/// release, working or not at this very moment, must land on Home (with its repair hint), never in the wizard.
/// </summary>
public class ExistingSetupOnboardingGateTests
{
    public static TheoryData<HostPlatform> Platforms => new() { HostPlatform.Windows, HostPlatform.Linux, HostPlatform.MacOS };

    private static ExistingInstall WorkingSetup(HostPlatform platform) =>
        platform switch
        {
            HostPlatform.Windows => new ExistingInstall(platform).WithDolphin(
                @"C:\Dolphin\Dolphin.exe",
                @"C:\Users\racer\Documents\Dolphin Emulator",
                @"C:\Games\Mario Kart Wii.iso"
            ),
            HostPlatform.MacOS => new ExistingInstall(platform).WithDolphin(
                "'/Applications/Dolphin.app/Contents/MacOS/Dolphin'",
                "/Users/racer/Library/Application Support/Dolphin",
                "/Users/racer/Games/mkw.iso"
            ),
            _ => new ExistingInstall(platform).WithDolphin(
                "dolphin-emu",
                "/home/racer/.local/share/dolphin-emu",
                "/home/racer/Games/mkw.iso"
            ),
        };

    private static void Unplug(ExistingInstall install, string path)
    {
        if (install.Fs.Directory.Exists(path))
            install.Fs.Directory.Delete(path, recursive: true);
        else if (install.Fs.File.Exists(path))
            install.Fs.File.Delete(path);
    }

    #region Control: somebody who really has nothing
    [Theory]
    [MemberData(nameof(Platforms))]
    public void ABrandNewInstall_IsOnboarded(HostPlatform platform)
    {
        var install = new ExistingInstall(platform);

        var session = install.Boot();

        Assert.True(session.Onboarding.IsRequired);
        Assert.False(install.Fs.File.Exists(install.ConfigFilePath), "starting up must not write a config for somebody who has not set up");
    }

    [Fact]
    public void ARetroRewindFolderAloneIsNotASetup()
    {
        // e.g. a data folder carried over from somewhere: nothing was ever picked in this Wheel Wizard.
        var install = new ExistingInstall();
        install.WithRetroRewind(install.ManagedRoot);

        Assert.True(install.Boot().Onboarding.IsRequired);
    }
    #endregion

    #region Working setups
    [Theory]
    [MemberData(nameof(Platforms))]
    public void AWorkingSetup_IsNeverOnboarded_AndIsRememberedAsOnboarded(HostPlatform platform)
    {
        var install = WorkingSetup(platform);
        install.WithRetroRewind();
        install.WithConfig("LaunchRrOnStartup", true).WithConfig("WW_Language", "nl").WithConfig("SomeFutureSetting", 42).FlushConfig();

        var session = install.Boot();

        Assert.False(session.Onboarding.IsRequired);
        Assert.True(session.Settings.ONBOARDING_COMPLETED.Get());
        AssertConfigKeepsWhatTheUserHad(session);
        Assert.True(install.ReadConfig()["OnboardingCompleted"].GetBoolean());
        Assert.Equal(42, install.ReadConfig()["SomeFutureSetting"].GetInt32());
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void AWorkingSetup_WithoutRetroRewindYet_IsNotOnboarded(HostPlatform platform)
    {
        var install = WorkingSetup(platform).FlushConfig();

        Assert.False(install.Boot().Onboarding.IsRequired);
    }

    [Fact]
    public void ASetupWrittenByAVeryOldRelease_WithOnlyThePathsInConfig_IsNotOnboarded()
    {
        var install = WorkingSetup(HostPlatform.Windows);
        install.Config.Clear();
        install
            .WithConfig("DolphinLocation", install.DolphinLocation)
            .WithConfig("UserFolderPath", install.UserFolder)
            .WithConfig("GameLocation", install.GameFile);
        install.FlushConfig();

        Assert.False(install.Boot().Onboarding.IsRequired);
    }

    [Fact]
    public void AFlatpakWheelWizardWithItsBundledDolphin_IsNotOnboarded()
    {
        var install = new ExistingInstall(HostPlatform.Linux).WithFlatpakSandbox();
        var userFolder = "/home/racer/.var/app/io.github.TeamWheelWizard.WheelWizard/data/dolphin-emu";
        install.Fs.Directory.CreateDirectory(userFolder);
        install.WithGame("/home/racer/Games/mkw.iso");
        install.Config["UserFolderPath"] = userFolder;
        install.FlushConfig();

        var session = install.Boot();

        Assert.False(session.Onboarding.IsRequired);
    }

    [Fact]
    public void AFlatpakWheelWizardThatKeptTheDefaultDolphinLocation_AndOnlyPickedAGame_IsNotOnboarded()
    {
        var install = new ExistingInstall(HostPlatform.Linux).WithFlatpakSandbox();
        install.WithGame("/home/racer/Games/mkw.iso").FlushConfig();

        Assert.False(install.Boot().Onboarding.IsRequired);
    }
    #endregion

    #region A path that does not resolve right now (unplugged drive, moved or renamed folder)
    [Theory]
    [MemberData(nameof(Platforms))]
    public void TheDolphinWasMovedAway_ButTheRestIsThere(HostPlatform platform)
    {
        var install = WorkingSetup(platform);
        install.FlushConfig();
        install.AvailableCommands.Clear();
        if (platform == HostPlatform.Windows)
            Unplug(install, install.DolphinLocation);

        var session = install.Boot();

        Assert.False(session.Onboarding.IsRequired);
        Assert.Equal(install.UserFolder, session.Settings.USER_FOLDER_PATH.Get());
        Assert.Equal(install.GameFile, session.Settings.GAME_LOCATION.Get());
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void TheUserFolderIsOnAnUnpluggedDrive(HostPlatform platform)
    {
        var install = WorkingSetup(platform);
        install.WithRetroRewind();
        install.FlushConfig();
        Unplug(install, install.UserFolder);

        var session = install.Boot();

        Assert.False(session.Onboarding.IsRequired);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void TheGameFileWasMoved_ButDolphinIsThere(HostPlatform platform)
    {
        var install = WorkingSetup(platform);
        install.FlushConfig();
        Unplug(install, install.GameFile);

        var session = install.Boot();

        Assert.False(session.Onboarding.IsRequired);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void EverythingLivesOnADriveThatIsNotThereToday(HostPlatform platform)
    {
        var install = WorkingSetup(platform);
        install.WithRetroRewind();
        install.FlushConfig();
        install.AvailableCommands.Clear();
        Unplug(install, install.UserFolder);
        Unplug(install, install.GameFile);
        if (platform == HostPlatform.Windows)
            Unplug(install, install.DolphinLocation);

        var session = install.Boot();

        Assert.False(session.Onboarding.IsRequired);
    }

    [Fact]
    public void ABrokenSetup_StaysNotOnboarded_OnEveryStart()
    {
        var install = WorkingSetup(HostPlatform.Windows);
        install.FlushConfig();
        Unplug(install, install.UserFolder);
        Unplug(install, install.GameFile);

        Assert.False(install.Boot().Onboarding.IsRequired);
        Assert.False(install.Boot().Onboarding.IsRequired);
    }

    [Fact]
    public void AnUnpluggedUserFolder_IsNotForgottenInConfigJson_ByTheStartup()
    {
        var install = WorkingSetup(HostPlatform.Windows);
        install.FlushConfig();
        Unplug(install, install.UserFolder);

        var session = install.Boot();

        AssertConfigKeepsWhatTheUserHad(session);
    }
    #endregion

    #region WiiCompiled
    [Fact]
    public void AnInstalledWiiCompiled_WithItsGame_IsNotOnboarded()
    {
        var install = new ExistingInstall(HostPlatform.Windows).WithGame(@"C:\Games\Mario Kart Wii.iso").WithConfig("EnableRecomp", true);
        install.RecompInstalled = true;
        install.FlushConfig();

        Assert.False(install.Boot().Onboarding.IsRequired);
    }

    [Fact]
    public void AnInstalledWiiCompiled_WhoseGameIsOnAnUnpluggedDrive_IsNotOnboarded()
    {
        var install = new ExistingInstall(HostPlatform.Windows).WithGame(@"D:\Games\Mario Kart Wii.iso").WithConfig("EnableRecomp", true);
        install.RecompInstalled = true;
        install.FlushConfig();
        Unplug(install, install.GameFile);

        Assert.False(install.Boot().Onboarding.IsRequired);
    }
    #endregion

    #region The settings file itself
    [Fact]
    public void ASettingsFileThatCannotBeRead_IsNotAFreshInstall()
    {
        var install = WorkingSetup(HostPlatform.Windows);
        install.FlushConfig();
        const string broken = "{ \"DolphinLocation\": \"C:\\\\Dolphin\\\\Dolphin.exe\", ";
        install.Fs.File.WriteAllText(install.ConfigFilePath, broken);

        var session = install.Boot();

        Assert.False(
            session.Onboarding.IsRequired,
            "the user's paths are in the unreadable file, the onboarding would only fail to save over it"
        );
        Assert.Equal(broken, install.Fs.File.ReadAllText(install.ConfigFilePath));
    }
    #endregion

    [Fact]
    public void TheOnboardingFlagSurvivesRestarts_EvenWhenEverythingIsLostLater()
    {
        var install = WorkingSetup(HostPlatform.Windows).FlushConfig();
        install.Boot();

        Unplug(install, install.UserFolder);
        Unplug(install, install.GameFile);
        Unplug(install, install.DolphinLocation);

        Assert.False(install.Boot().Onboarding.IsRequired);
    }
}

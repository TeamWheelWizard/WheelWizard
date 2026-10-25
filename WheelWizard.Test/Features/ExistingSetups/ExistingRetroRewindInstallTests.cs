using WheelWizard.CustomDistributions;
using static WheelWizard.Test.Features.ExistingSetups.ExistingSetupAssertions;

namespace WheelWizard.Test.Features.ExistingSetups;

/// <summary>
/// Retro Rewind used to live inside Dolphin's Load folder; this version keeps it in Wheel Wizard's own data
/// folder and moves older installs there on start. Every setup that worked before has to come out of that
/// with the same files, the same version and the same save, and without being asked to set anything up.
/// </summary>
public class ExistingRetroRewindInstallTests
{
    #region Where Dolphin and the data folder live
    [Fact]
    public void Windows_DolphinAndDataOnTheSameDrive_RenamesTheInstall()
    {
        var install = new ExistingInstall().WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"C:\Users\racer\Documents\Dolphin Emulator",
            @"C:\Games\Mario Kart Wii.iso"
        );
        var files = install.WithRetroRewind();
        install.FlushConfig();
        Assert.Equal(install.LegacyRoot, install.Boot(runStartup: false).Paths.RootFolderPath);

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.False(install.Fs.Directory.Exists(install.LegacyRoot));
        AssertConfigKeepsWhatTheUserHad(session);
        Assert.True(install.ReadConfig()["OnboardingCompleted"].GetBoolean());
    }

    [Fact]
    public void Windows_DolphinOnAnotherDrive_CopiesTheInstallAcrossDrives()
    {
        var install = new ExistingInstall().WithDolphin(
            @"D:\Dolphin\Dolphin.exe",
            @"D:\Dolphin\Dolphin Emulator",
            @"D:\Games\Mario Kart Wii.iso"
        );
        var files = install.WithRetroRewind(withBeta: true);
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.False(install.Fs.Directory.Exists(install.LegacyRoot));
        Assert.Contains(session.Statuses, status => status is not null);
        Assert.Null(session.Statuses[^1]);
    }

    [Fact]
    public void Windows_DataFolderOnAnotherDrive_CopiesTheInstallAcrossDrives()
    {
        var install = new ExistingInstall(dataDir: @"D:\WheelWizard").WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"C:\Users\racer\Documents\Dolphin Emulator",
            @"C:\Games\Mario Kart Wii.iso"
        );
        var files = install.WithRetroRewind();
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, @"D:\WheelWizard\RetroRewind");
    }

    [Fact]
    public void Windows_PortableDolphinWithItsUserFolderNextToTheExecutable()
    {
        var install = new ExistingInstall().WithDolphin(
            @"C:\Games\Dolphin-x64\Dolphin.exe",
            @"C:\Games\Dolphin-x64\User",
            @"C:\Games\Mario Kart Wii.iso"
        );
        install.Fs.File.WriteAllText(@"C:\Games\Dolphin-x64\portable.txt", "");
        var files = install.WithRetroRewind();
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.True(install.Fs.File.Exists(@"C:\Games\Dolphin-x64\portable.txt"));
    }

    [Fact]
    public void Windows_CustomLoadFolder_IsTheFolderTheInstallIsMovedFrom()
    {
        var install = new ExistingInstall()
            .WithDolphin(@"C:\Dolphin\Dolphin.exe", @"C:\Users\racer\Documents\Dolphin Emulator", @"C:\Games\Mario Kart Wii.iso")
            .WithCustomLoadFolder(@"D:\Mods\Dolphin Load");
        var customLegacy = @"D:\Mods\Dolphin Load\Riivolution\WheelWizard";
        var files = install.WithRetroRewind(customLegacy);
        install.FlushConfig();
        Assert.Equal(customLegacy, install.Boot(runStartup: false).Paths.RootFolderPath);

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.False(install.Fs.Directory.Exists(customLegacy));
        Assert.True(install.Fs.Directory.Exists(@"D:\Mods\Dolphin Load"), "the user's Load folder itself stays");
    }

    [Fact]
    public void Windows_NamesWithSpacesAndAccents_SurviveTheMove()
    {
        var install = new ExistingInstall(
            home: @"C:\Users\José Müller",
            dataDir: @"C:\Users\José Müller\AppData\Roaming\Wheel Wizard"
        ).WithDolphin(
            @"C:\Spiele\Dolphin 2603\Dolphin.exe",
            @"C:\Users\José Müller\Documents\Dolphin Emulator",
            @"C:\Spiele\Mario Kart Wii (Europe).rvz"
        );
        var files = install.WithRetroRewind();
        install.FlushConfig();

        AssertStillWorks(install.Boot(), files, install.ManagedRoot);
    }

    [Fact]
    public void Linux_NativeDolphin_WithSplitConfigAndDataFolders()
    {
        var install = new ExistingInstall(HostPlatform.Linux).WithDolphin(
            "dolphin-emu",
            "/home/racer/.local/share/dolphin-emu",
            "/home/racer/Games/mkw.iso"
        );
        var files = install.WithRetroRewind();
        install.FlushConfig();
        Assert.Equal("/home/racer/.config/dolphin-emu", install.DolphinConfigFolder);

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
    }

    [Fact]
    public void Linux_FlatpakDolphin_KeepsItsDataFolderAndGetsItsInstallMoved()
    {
        var install = new ExistingInstall(HostPlatform.Linux).WithDolphin(
            "flatpak run org.DolphinEmu.dolphin-emu",
            "/home/racer/.var/app/org.DolphinEmu.dolphin-emu/data/dolphin-emu",
            "/home/racer/Games/mkw.iso"
        );
        var files = install.WithRetroRewind();
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
    }

    [Fact]
    public void MacOs_DolphinApp_WithQuotedLocation()
    {
        var install = new ExistingInstall(HostPlatform.MacOS).WithDolphin(
            "'/Applications/Dolphin.app/Contents/MacOS/Dolphin'",
            "/Users/racer/Library/Application Support/Dolphin",
            "/Users/racer/Games/mkw.iso"
        );
        var files = install.WithRetroRewind();
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
    }
    #endregion

    #region What was installed
    [Fact]
    public void StableAndBetaTogether_AreMovedTogether()
    {
        var install = WindowsInstall();
        var files = install.WithRetroRewind(withBeta: true);
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.Equal("6.6.0-beta", install.Fs.File.ReadAllText(install.Combine(session.Paths.BetaFolderPath, "version.txt")));
        Assert.True(install.Fs.File.Exists(session.Paths.BetaXmlFilePath));
    }

    [Fact]
    public void ALargeInstall_WithManyCustomTracks_ArrivesComplete()
    {
        var install = WindowsInstall();
        var files = install.WithRetroRewind(extraFiles: 400);
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.Equal(405, files.Count);
    }

    [Fact]
    public void ABrokenInstallWithOnlyItsXml_IsStillMoved_AndStillCountsAsNotInstalled()
    {
        var install = WindowsInstall();
        install.Fs.Directory.CreateDirectory(install.Combine(install.LegacyRoot, "riivolution"));
        install.Fs.File.WriteAllText(install.Combine(install.LegacyRoot, "riivolution", "RetroRewind6.xml"), "<wiidisc/>");
        install.FlushConfig();

        var session = install.Boot();

        Assert.Equal(install.ManagedRoot, session.Paths.RootFolderPath);
        Assert.Equal("<wiidisc/>", install.Fs.File.ReadAllText(session.Paths.XmlFilePath));
        Assert.Null(session.InstalledRetroRewindVersion());
        AssertUserSettingsUntouched(session);
        Assert.False(session.Onboarding.IsRequired);
    }

    [Fact]
    public void AVersionFileTheServerHasNotShippedYet_IsKeptByteForByte()
    {
        var install = WindowsInstall();
        var files = install.WithRetroRewind();
        files["RetroRewind6/version.txt"] = "6.5.1\r\n";
        install.Fs.File.WriteAllText(install.Combine(install.LegacyRoot, "RetroRewind6", "version.txt"), "6.5.1\r\n");
        install.FlushConfig();

        var session = install.Boot();

        Assert.Equal("6.5.1\r\n", install.Fs.File.ReadAllText(install.Combine(session.Paths.RetroRewindFolderPath, "version.txt")));
    }

    [Fact]
    public void FilesNextToWheelWizardsFolder_InDolphinsLoadFolder_AreNeverTouched()
    {
        var install = WindowsInstall();
        install.WithRetroRewind();
        var others = new Dictionary<string, string>
        {
            [install.Combine(install.LoadFolder, "Riivolution", "SomeOtherMod.xml")] = "<wiidisc other/>",
            [install.Combine(install.LoadFolder, "Riivolution", "OtherMod", "data.bin")] = "other",
            [install.Combine(install.LoadFolder, "GameSettings", "RMCP01.ini")] = "[Core]",
            [install.Combine(install.LoadFolder, "Textures", "RMC", "tex1_64x64.png")] = "png",
        };
        foreach (var (path, content) in others)
        {
            install.Fs.Directory.CreateDirectory(install.Fs.Path.GetDirectoryName(path)!);
            install.Fs.File.WriteAllText(path, content);
        }

        install.FlushConfig();
        install.Boot();

        foreach (var (path, content) in others)
            Assert.Equal(content, install.Fs.File.ReadAllText(path));
    }
    #endregion

    #region Nothing to move
    [Fact]
    public void NeverInstalledRetroRewind_StaysUninstalled_AndNothingIsInvented()
    {
        var install = WindowsInstall();
        install.FlushConfig();

        var session = install.Boot();

        Assert.Equal(install.ManagedRoot, session.Paths.RootFolderPath);
        Assert.Null(session.InstalledRetroRewindVersion());
        Assert.Empty(install.Snapshot(install.ManagedRoot).Keys.Where(key => key != CustomDistributionPaths.MigratedMarkerFileName));
        AssertUserSettingsUntouched(session);
        Assert.False(session.Onboarding.IsRequired);
    }

    [Fact]
    public void AnEmptyLeftoverFolder_IsNotAnInstall()
    {
        var install = WindowsInstall();
        install.Fs.Directory.CreateDirectory(install.LegacyRoot);
        install.FlushConfig();

        var session = install.Boot();

        Assert.Equal(install.ManagedRoot, session.Paths.RootFolderPath);
        Assert.True(install.Fs.Directory.Exists(install.LegacyRoot), "an empty folder of the user's is left alone");
    }

    [Fact]
    public void WiiCompiledOnlyUser_WithoutAnyDolphin_KeepsTheirInstallWhereItIs()
    {
        var install = new ExistingInstall().WithGame(@"C:\Games\Mario Kart Wii.iso");
        var files = install.WithRetroRewind(install.ManagedRoot);
        install.FlushConfig();

        var session = install.Boot();

        Assert.Equal(install.ManagedRoot, session.Paths.RootFolderPath);
        Assert.Equal(
            files,
            install.Snapshot(install.ManagedRoot).Where(pair => pair.Key != CustomDistributionPaths.MigratedMarkerFileName).ToDictionary()
        );
        Assert.False(session.Onboarding.IsRequired);
    }

    [Fact]
    public void AnInstallAlreadyInTheManagedFolder_IsNotTouched_EvenWithADolphinConfigured()
    {
        var install = WindowsInstall();
        var files = install.WithRetroRewind(install.ManagedRoot);
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.Empty(install.Fs.Directory.GetDirectories(install.DataDir, "RetroRewind.old-*"));
    }
    #endregion

    #region Both a legacy and a managed install
    [Fact]
    public void LegacyInstallWins_AndTheManagedOneIsKeptNextToIt()
    {
        var install = WindowsInstall();
        var legacy = install.WithRetroRewind();
        install.Fs.Directory.CreateDirectory(install.Combine(install.ManagedRoot, "RetroRewind6"));
        install.Fs.File.WriteAllText(install.Combine(install.ManagedRoot, "RetroRewind6", "version.txt"), "6.0.0");
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, legacy, install.ManagedRoot);
        var backup = Assert.Single(install.Fs.Directory.GetDirectories(install.DataDir, "RetroRewind.old-*"));
        Assert.Equal("6.0.0", install.Fs.File.ReadAllText(install.Combine(backup, "RetroRewind6", "version.txt")));
    }
    #endregion

    #region Later starts
    [Fact]
    public void StartingAgainAfterTheMove_ChangesNothing()
    {
        var install = WindowsInstall();
        var files = install.WithRetroRewind(withBeta: true);
        install.FlushConfig();
        install.Boot();
        var afterFirst = install.Snapshot(install.DataDir);

        var second = install.Boot();
        var third = install.Boot();

        Assert.Equal(afterFirst.OrderBy(pair => pair.Key), install.Snapshot(install.DataDir).OrderBy(pair => pair.Key));
        AssertStillWorks(third, files, install.ManagedRoot);
        Assert.Equal(install.ManagedRoot, second.Paths.RootFolderPath);
    }

    [Fact]
    public void ADolphinPickedLaterThatHasItsOwnOldInstall_NeverTakesOver()
    {
        var install = WindowsInstall();
        var files = install.WithRetroRewind();
        install.FlushConfig();
        install.Boot();

        // The user points Wheel Wizard at a second Dolphin that still has an old Retro Rewind in its Load folder.
        var other = @"C:\Users\racer\Documents\Other Dolphin";
        install.Fs.Directory.CreateDirectory(other);
        install.WithRetroRewind(install.Combine(other, "Load", "Riivolution", "WheelWizard")).Clear();
        install.Config["UserFolderPath"] = other;
        install.FlushConfig();

        var session = install.Boot();

        Assert.Equal(install.ManagedRoot, session.Paths.RootFolderPath);
        Assert.Equal(
            files["riivolution/save/RetroWFC/RMCP/rksys.dat"],
            install.Fs.File.ReadAllText(install.Combine(install.ManagedRoot, "riivolution", "save", "RetroWFC", "RMCP", "rksys.dat"))
        );
    }

    [Fact]
    public void ADolphinOnAnUnpluggedDrive_IsRetriedOnTheNextStart_WithoutLosingAnything()
    {
        var install = new ExistingInstall().WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"E:\Dolphin\Dolphin Emulator",
            @"C:\Games\Mario Kart Wii.iso",
            createFiles: false
        );
        install.Fs.Directory.CreateDirectory(@"C:\Dolphin");
        install.Fs.File.WriteAllText(@"C:\Dolphin\Dolphin.exe", "MZ");
        install.FlushConfig();

        var unplugged = install.Boot();

        Assert.False(unplugged.Onboarding.IsRequired);
        Assert.False(
            install.Fs.File.Exists(unplugged.Paths.MigratedMarkerFilePath),
            "an unreadable folder must not be written off as having no install"
        );
        Assert.Null(unplugged.InstalledRetroRewindVersion());

        // The drive comes back with the install on it.
        install.Fs.WithDrive("E:");
        install.Fs.Directory.CreateDirectory(@"E:\Dolphin\Dolphin Emulator\Config");
        var files = install.WithRetroRewind(@"E:\Dolphin\Dolphin Emulator\Load\Riivolution\WheelWizard");
        install.Fs.File.WriteAllText(@"E:\Dolphin\Dolphin Emulator\Config\Dolphin.ini", "[General]\n");

        var plugged = install.Boot();

        Assert.Equal(install.ManagedRoot, plugged.Paths.RootFolderPath);
        Assert.Equal(ExistingInstall.RetroRewindVersion, plugged.InstalledRetroRewindVersion());
        Assert.Equal(
            files["riivolution/save/RetroWFC/RMCP/rksys.dat"],
            install.Fs.File.ReadAllText(install.Combine(install.ManagedRoot, "riivolution", "save", "RetroWFC", "RMCP", "rksys.dat"))
        );
    }
    #endregion

    #region More shapes
    [Fact]
    public void ChangingAnotherSetting_WhileTheDriveIsAway_DoesNotForgetTheirPaths()
    {
        var install = new ExistingInstall().WithDolphin(
            @"D:\Dolphin\Dolphin.exe",
            @"D:\Dolphin\Dolphin Emulator",
            @"D:\Games\Mario Kart Wii.iso"
        );
        install.WithRetroRewind();
        install.FlushConfig();
        install.Unplug(@"D:\Dolphin");
        install.Unplug(@"D:\Games");

        var session = install.Boot();
        Assert.True(session.Settings.Set(session.Settings.LAUNCH_RR_ON_STARTUP, true));

        var config = install.ReadConfig();
        Assert.Equal(@"D:\Dolphin\Dolphin.exe", config["DolphinLocation"].GetString());
        Assert.Equal(@"D:\Dolphin\Dolphin Emulator", config["UserFolderPath"].GetString());
        Assert.Equal(@"D:\Games\Mario Kart Wii.iso", config["GameLocation"].GetString());

        // The drive comes back: everything is exactly as it was, with the install moved in.
        install.Replug();
        var back = install.Boot();
        Assert.True(back.Settings.PathsSetupCorrectly());
        Assert.Equal(install.ManagedRoot, back.Paths.RootFolderPath);
        Assert.Equal(ExistingInstall.RetroRewindVersion, back.InstalledRetroRewindVersion());
    }

    [Fact]
    public void PickingAnotherFolderInSettings_WhileTheOldOneIsAway_ReplacesIt()
    {
        var install = new ExistingInstall().WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"D:\Dolphin\Dolphin Emulator",
            @"C:\Games\Mario Kart Wii.iso"
        );
        install.FlushConfig();
        install.Unplug(@"D:\Dolphin");
        var replacement = @"C:\Users\racer\Documents\Dolphin Emulator";
        install.Fs.Directory.CreateDirectory(replacement);

        var session = install.Boot();
        Assert.True(session.Settings.Set(session.Settings.USER_FOLDER_PATH, replacement));

        Assert.Equal(replacement, install.ReadConfig()["UserFolderPath"].GetString());
        Assert.False(install.Boot().Onboarding.IsRequired);
    }

    [Fact]
    public void FlatpakWheelWizard_WithItsBundledDolphin_BringsItsInstallAlong()
    {
        var install = new ExistingInstall(HostPlatform.Linux).WithFlatpakSandbox();
        var userFolder = "/home/racer/.var/app/io.github.TeamWheelWizard.WheelWizard/data/dolphin-emu";
        install.Fs.Directory.CreateDirectory(userFolder);
        install.WithGame("/home/racer/Games/mkw.iso");
        install.Config["UserFolderPath"] = userFolder;
        var legacy = install.Combine(userFolder, "Load", "Riivolution", "WheelWizard");
        var files = install.WithRetroRewind(legacy);
        install.FlushConfig();

        var session = install.Boot();

        Assert.False(session.Onboarding.IsRequired);
        Assert.Equal(install.ManagedRoot, session.Paths.RootFolderPath);
        Assert.Equal(
            files,
            install
                .Snapshot(session.Paths.RootFolderPath)
                .Where(pair => pair.Key != CustomDistributionPaths.MigratedMarkerFileName)
                .ToDictionary()
        );
        Assert.False(install.Fs.Directory.Exists(legacy));
    }

    [Fact]
    public void ABetaOnlyInstall_IsMovedAndStaysBetaOnly()
    {
        var install = new ExistingInstall().WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"C:\Users\racer\Documents\Dolphin Emulator",
            @"C:\Games\Mario Kart Wii.iso"
        );
        var beta = new Dictionary<string, string>
        {
            ["RRBeta/version.txt"] = "6.6.0-beta",
            ["RRBeta/Patches/Main.bin"] = "beta patch",
            ["riivolution/RRBeta.xml"] = "<wiidisc beta/>",
            ["riivolution/save/RetroWFC/RMCP/rksys.dat"] = "beta save",
        };
        foreach (var (relative, content) in beta)
        {
            var path = install.Combine(install.LegacyRoot, relative.Replace('/', install.Fs.Path.DirectorySeparatorChar));
            install.Fs.Directory.CreateDirectory(install.Fs.Path.GetDirectoryName(path)!);
            install.Fs.File.WriteAllText(path, content);
        }

        install.FlushConfig();

        var session = install.Boot();

        Assert.Equal(
            beta.OrderBy(pair => pair.Key),
            install
                .Snapshot(session.Paths.RootFolderPath)
                .Where(pair => pair.Key != CustomDistributionPaths.MigratedMarkerFileName)
                .OrderBy(pair => pair.Key)
        );
        Assert.Null(session.InstalledRetroRewindVersion());
        Assert.True(install.Fs.File.Exists(session.Paths.BetaXmlFilePath));
    }

    [Fact]
    public async Task AnExistingDolphinSetup_IsNeverOfferedAManagedDolphinUpdate()
    {
        var install = new ExistingInstall().WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"C:\Users\racer\Documents\Dolphin Emulator",
            @"C:\Games\Mario Kart Wii.iso"
        );
        install.FlushConfig();

        var session = install.Boot();

        Assert.Null(await session.ManagedDolphin.CheckForUpdateAsync());
        Assert.False(session.ManagedDolphin.IsActive);
        Assert.Equal(@"C:\Dolphin\Dolphin.exe", session.Settings.DOLPHIN_LOCATION.Get());
    }

    [Fact]
    public void ACustomLoadFolderOnAnUnpluggedDrive_IsRetriedWhenItIsBack()
    {
        var install = new ExistingInstall()
            .WithDolphin(@"C:\Dolphin\Dolphin.exe", @"C:\Users\racer\Documents\Dolphin Emulator", @"C:\Games\Mario Kart Wii.iso")
            .WithCustomLoadFolder(@"E:\Mods\Load", exists: false);
        install.Fs.WithDrive("E:");
        var legacy = @"E:\Mods\Load\Riivolution\WheelWizard";
        var files = install.WithRetroRewind(legacy);
        install.FlushConfig();
        install.Unplug(@"E:\Mods");

        var away = install.Boot();

        Assert.False(away.Onboarding.IsRequired);
        Assert.False(install.Fs.File.Exists(away.Paths.MigratedMarkerFilePath));

        install.Replug();
        var back = install.Boot();

        AssertStillWorks(back, files, install.ManagedRoot);
    }
    #endregion

    private static ExistingInstall WindowsInstall() =>
        new ExistingInstall().WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"C:\Users\racer\Documents\Dolphin Emulator",
            @"C:\Games\Mario Kart Wii.iso"
        );
}

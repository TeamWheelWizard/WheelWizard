using WheelWizard.CustomDistributions;
using static WheelWizard.Test.Features.ExistingSetups.ExistingSetupAssertions;

namespace WheelWizard.Test.Features.ExistingSetups;

/// <summary>
/// WiiCompiled reads Retro Rewind's and the Wii data's location from its own Config.toml. This version
/// rewrites those two keys before every launch, so every earlier way of having them set has to keep working.
/// </summary>
public class ExistingRecompSetupTests
{
    private static ExistingInstall RecompUser(out string toml, bool dolphinConfigured = true, bool recompEnabled = true)
    {
        var install = new ExistingInstall(HostPlatform.Windows);
        if (dolphinConfigured)
            install.WithDolphin(@"C:\Dolphin\Dolphin.exe", @"C:\Users\racer\Documents\Dolphin Emulator", @"C:\Games\Mario Kart Wii.iso");
        else
            install.WithGame(@"C:\Games\Mario Kart Wii.iso");
        install.WithConfig("EnableRecomp", recompEnabled);
        install.RecompInstalled = true;
        toml = install.Combine(install.DataDir, "Recomp", "UserData", "Config.toml");
        return install;
    }

    private static void WriteToml(ExistingInstall install, string path, string content)
    {
        install.Fs.Directory.CreateDirectory(install.Fs.Path.GetDirectoryName(path)!);
        install.Fs.File.WriteAllText(path, content);
    }

    [Fact]
    public void ADolphinUserWhoSwitchedToWiiCompiled_GetsTheirRetroRewindFollowedToTheNewFolder()
    {
        var install = RecompUser(out var toml);
        var files = install.WithRetroRewind();
        var wii = install.Combine(install.UserFolder, "Wii");
        WriteToml(
            install,
            toml,
            $"# my settings\n[paths]\nnand_root = \"{Escape(wii)}\"\nretro_rewind_root = \"{Escape(install.Combine(install.LegacyRoot, "RetroRewind6"))}\"\n\n[video]\nresolution_multiplier = 2.0\nshow_fps = false\n"
        );
        install.WithConfig("RecompUseDolphinData", true).FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot, expectPlayable: true);
        var after = install.Fs.File.ReadAllText(toml);
        var reread = install.Boot(runStartup: false);
        Assert.Equal(install.Combine(install.ManagedRoot, "RetroRewind6"), reread.Settings.RECOMP_RETRO_REWIND_ROOT.Get());
        Assert.Equal(wii, reread.Settings.RECOMP_NAND_ROOT.Get());
        Assert.Contains("# my settings", after);
        Assert.Contains("resolution_multiplier = 2.0", after);
        Assert.Contains("show_fps = false", after);
    }

    [Fact]
    public void AWiiCompiledOnlyUser_KeepsTheirConfigWorkingBeforeEveryLaunch()
    {
        var install = RecompUser(out var toml, dolphinConfigured: false);
        var files = install.WithRetroRewind(install.ManagedRoot);
        var retroRewind = install.Combine(install.ManagedRoot, "RetroRewind6");
        WriteToml(install, toml, $"[paths]\nretro_rewind_root = \"{Escape(retroRewind)}\"\n[video]\nshow_fps = true\n");
        install.FlushConfig();

        var session = install.Boot();
        Assert.True(session.RecompData.ApplyPathsToRecompConfig().IsSuccess);
        Assert.True(session.RecompData.ApplyPathsToRecompConfig().IsSuccess);

        Assert.False(session.Onboarding.IsRequired);
        var reread = install.Boot(runStartup: false);
        Assert.Equal(retroRewind, reread.Settings.RECOMP_RETRO_REWIND_ROOT.Get());
        Assert.Equal("", reread.Settings.RECOMP_NAND_ROOT.Get());
        Assert.Equal(
            files,
            install.Snapshot(install.ManagedRoot).Where(pair => pair.Key != CustomDistributionPaths.MigratedMarkerFileName).ToDictionary()
        );
    }

    [Fact]
    public void PrivateMode_NeverGainsANandRoot()
    {
        var install = RecompUser(out var toml);
        install.WithRetroRewind();
        WriteToml(install, toml, "[paths]\n[video]\nshow_fps = true\n");
        install.FlushConfig();

        var session = install.Boot();
        Assert.True(session.RecompData.ApplyPathsToRecompConfig().IsSuccess);

        Assert.DoesNotContain("nand_root", install.Fs.File.ReadAllText(toml));
    }

    [Fact]
    public void ACopiedNand_StaysTheNandTheRuntimeUses()
    {
        var install = RecompUser(out var toml);
        install.WithRetroRewind();
        var copy = install.Combine(install.DataDir, "Recomp", "Nand");
        install.Fs.Directory.CreateDirectory(install.Combine(copy, "title"));
        WriteToml(install, toml, $"[paths]\nnand_root = \"{Escape(copy)}\"\n");
        install.WithConfig("RecompCopyDolphinNand", true).FlushConfig();

        var session = install.Boot();
        Assert.True(session.RecompData.ApplyPathsToRecompConfig().IsSuccess);

        Assert.Equal(copy, install.Boot(runStartup: false).Settings.RECOMP_NAND_ROOT.Get());
    }

    [Fact]
    public void SharingDolphinsNand_WhileDolphinsFolderIsOnAnUnpluggedDrive_KeepsTheNandRoot()
    {
        // Applying must not turn "the Wii data is on a drive that is not there right now" into "use a new,
        // empty private NAND": the next time the drive is back the user expects their saves and Miis.
        var install = RecompUser(out var toml);
        install.WithRetroRewind();
        var wii = install.Combine(install.UserFolder, "Wii");
        WriteToml(install, toml, $"[paths]\nnand_root = \"{Escape(wii)}\"\n");
        install.WithConfig("RecompUseDolphinData", true).FlushConfig();
        install.Fs.Directory.Delete(install.UserFolder, recursive: true);

        var session = install.Boot();
        Assert.True(session.RecompData.ApplyPathsToRecompConfig().IsSuccess);

        Assert.Contains("nand_root", install.Fs.File.ReadAllText(toml));
        Assert.Equal(wii, install.Boot(runStartup: false).Settings.RECOMP_NAND_ROOT.Get());
    }

    [Fact]
    public void WithoutAConfigFileYet_NothingIsCreated()
    {
        var install = RecompUser(out var toml);
        install.WithRetroRewind();
        install.FlushConfig();

        var session = install.Boot();
        Assert.True(session.RecompData.ApplyPathsToRecompConfig().IsSuccess);

        Assert.False(install.Fs.File.Exists(toml));
    }

    [Fact]
    public void WhenRetroRewindIsNotInstalled_TheRuntimesOwnValueIsKept()
    {
        var install = RecompUser(out var toml);
        const string custom = @"D:\My RR\RetroRewind6";
        WriteToml(install, toml, $"[paths]\nretro_rewind_root = \"{Escape(custom)}\"\n");
        install.FlushConfig();

        var session = install.Boot();
        Assert.True(session.RecompData.ApplyPathsToRecompConfig().IsSuccess);

        Assert.Equal(custom, install.Boot(runStartup: false).Settings.RECOMP_RETRO_REWIND_ROOT.Get());
    }

    [Fact]
    public void ADolphinUserWhoAlsoHasWiiCompiledInstalled_GetsItFollowedToo_ButStaysOnDolphin()
    {
        var install = RecompUser(out var toml, recompEnabled: false);
        var files = install.WithRetroRewind();
        WriteToml(
            install,
            toml,
            $"[paths]\nretro_rewind_root = \"{Escape(install.Combine(install.LegacyRoot, "RetroRewind6"))}\"\n[video]\nshow_fps = true\n"
        );
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.False(session.Settings.IsRecompModeActive());
        Assert.Equal(
            install.Combine(install.ManagedRoot, "RetroRewind6"),
            install.Boot(runStartup: false).Settings.RECOMP_RETRO_REWIND_ROOT.Get()
        );
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}

using WheelWizard.CustomDistributions;
using static WheelWizard.Test.Features.ExistingSetups.ExistingSetupAssertions;

namespace WheelWizard.Test.Features.ExistingSetups;

public enum LoadFolder
{
    /// <summary>Dolphin's own Load folder inside its user folder.</summary>
    Default,
    CustomAvailable,
    CustomUnplugged,
}

public enum RetroRewindState
{
    None,
    Stable,
    StableAndBeta,
    OnlyTheXml,
}

public sealed record SetupShape(
    HostPlatform Platform,
    bool UserFolderUnplugged,
    LoadFolder Load,
    RetroRewindState RetroRewind,
    bool ManagedFolderAlreadyThere,
    bool DolphinMissing,
    bool GameMissing
)
{
    public override string ToString() =>
        $"{Platform}, userFolder {(UserFolderUnplugged ? "unplugged" : "there")}, load {Load}, RR {RetroRewind}, "
        + $"{(ManagedFolderAlreadyThere ? "managed folder present" : "no managed folder")}, "
        + $"dolphin {(DolphinMissing ? "missing" : "there")}, game {(GameMissing ? "missing" : "there")}";
}

/// <summary>
/// Every combination of the things that differ between players who set Wheel Wizard up before the update.
/// Whatever the combination, nobody is onboarded, nothing of theirs is lost or overwritten, and a drive that
/// is away on this start is simply picked up on the next one.
/// </summary>
public class ExistingSetupSweepTests
{
    public static TheoryData<SetupShape> Shapes()
    {
        var data = new TheoryData<SetupShape>();
        foreach (var platform in Enum.GetValues<HostPlatform>())
        foreach (var userFolderUnplugged in new[] { false, true })
        foreach (var load in Enum.GetValues<LoadFolder>())
        foreach (var retroRewind in Enum.GetValues<RetroRewindState>())
        foreach (var managed in new[] { false, true })
        foreach (var dolphinMissing in new[] { false, true })
        foreach (var gameMissing in new[] { false, true })
            data.Add(new SetupShape(platform, userFolderUnplugged, load, retroRewind, managed, dolphinMissing, gameMissing));
        return data;
    }

    private sealed record Built(
        ExistingInstall Install,
        Dictionary<string, string> Files,
        string LegacyRoot,
        bool Reachable,
        string CustomLoad
    );

    private static Built Build(SetupShape shape)
    {
        var install = shape.Platform switch
        {
            HostPlatform.Windows => new ExistingInstall(shape.Platform).WithDolphin(
                @"C:\Dolphin\Dolphin.exe",
                @"D:\Dolphin\Dolphin Emulator",
                @"D:\Games\Mario Kart Wii.iso"
            ),
            HostPlatform.MacOS => new ExistingInstall(shape.Platform).WithDolphin(
                "'/Applications/Dolphin.app/Contents/MacOS/Dolphin'",
                "/Users/racer/Library/Application Support/Dolphin",
                "/Users/racer/Games/mkw.iso"
            ),
            _ => new ExistingInstall(shape.Platform).WithDolphin(
                "dolphin-emu",
                "/home/racer/.local/share/dolphin-emu",
                "/home/racer/Games/mkw.iso"
            ),
        };

        var customLoad = shape.Platform switch
        {
            HostPlatform.Windows => @"D:\Mods\Dolphin Load",
            HostPlatform.MacOS => "/Volumes/Mods/Load",
            _ => "/mnt/mods/Load",
        };
        if (shape.Load != LoadFolder.Default)
            install.WithCustomLoadFolder(customLoad);
        var legacy = shape.Load == LoadFolder.Default ? install.LegacyRoot : install.Combine(customLoad, "Riivolution", "WheelWizard");

        var files = new Dictionary<string, string>();
        switch (shape.RetroRewind)
        {
            case RetroRewindState.Stable:
                files = install.WithRetroRewind(legacy);
                break;
            case RetroRewindState.StableAndBeta:
                files = install.WithRetroRewind(legacy, withBeta: true);
                break;
            case RetroRewindState.OnlyTheXml:
                var xml = install.Combine(legacy, "riivolution", "RetroRewind6.xml");
                install.Fs.Directory.CreateDirectory(install.Fs.Path.GetDirectoryName(xml)!);
                install.Fs.File.WriteAllText(xml, "<wiidisc/>");
                files = new() { ["riivolution/RetroRewind6.xml"] = "<wiidisc/>" };
                break;
        }

        if (shape.ManagedFolderAlreadyThere)
        {
            var version = install.Combine(install.ManagedRoot, "RetroRewind6", "version.txt");
            install.Fs.Directory.CreateDirectory(install.Fs.Path.GetDirectoryName(version)!);
            install.Fs.File.WriteAllText(version, "6.0.0");
        }

        install.WithConfig("LaunchRrOnStartup", true).WithConfig("FavoriteUser", 2).WithConfig("SomethingFromTheFuture", "kept");
        install.FlushConfig();

        // Whatever is unplugged takes what lives on it along, including the Retro Rewind install.
        if (shape.UserFolderUnplugged)
            install.Unplug(install.UserFolder);
        if (shape.Load == LoadFolder.CustomUnplugged)
            install.Unplug(customLoad);
        if (shape.GameMissing)
            install.Unplug(install.GameFile);
        if (shape.DolphinMissing)
        {
            install.AvailableCommands.Clear();
            if (shape.Platform == HostPlatform.Windows)
                install.Unplug(install.DolphinLocation);
        }

        var reachable = !shape.UserFolderUnplugged && shape.Load != LoadFolder.CustomUnplugged;
        return new Built(install, files, legacy, reachable, customLoad);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void NoSetup_IsOnboarded_LosesFilesOrIsRewrittenBehindItsBack(SetupShape shape)
    {
        var (install, files, legacy, reachable, customLoad) = Build(shape);
        var managedVersionBefore = shape.ManagedFolderAlreadyThere ? "6.0.0" : null;

        var session = install.Boot();

        // The user is never asked to set anything up.
        Assert.False(session.Onboarding.IsRequired);
        AssertConfigKeepsWhatTheUserHad(session);
        var config = install.ReadConfig();
        Assert.True(config["LaunchRrOnStartup"].GetBoolean());
        Assert.Equal(2, config["FavoriteUser"].GetInt32());
        Assert.Equal("kept", config["SomethingFromTheFuture"].GetString());
        Assert.Equal(install.DolphinLocation, config["DolphinLocation"].GetString());
        Assert.Equal(install.UserFolder, config["UserFolderPath"].GetString());
        Assert.Equal(install.GameFile, config["GameLocation"].GetString());
        Assert.False(session.ManagedDolphin.IsActive);

        var hasLegacyFiles = files.Count > 0;
        if (!reachable)
        {
            // Nothing can be known about a folder that is away: no marker, nothing moved, nothing invented.
            Assert.False(install.Fs.File.Exists(session.Paths.MigratedMarkerFilePath));
            Assert.False(install.Fs.Directory.Exists(install.ManagedRoot + ".migrating"));
            if (managedVersionBefore is not null)
                Assert.Equal(
                    managedVersionBefore,
                    install.Fs.File.ReadAllText(install.Combine(install.ManagedRoot, "RetroRewind6", "version.txt"))
                );
            if (hasLegacyFiles && shape.Load == LoadFolder.CustomUnplugged)
            {
                var stashed = install.UnpluggedLocation(customLoad);
                Assert.Equal(files, install.Snapshot(install.Combine(stashed, "Riivolution", "WheelWizard")));
            }

            // The drive is back on the next start.
            install.Replug();
            if (shape.DolphinMissing && shape.Platform != HostPlatform.Windows)
                install.AvailableCommands.Add(install.DolphinLocation);
            session = install.Boot();
            Assert.False(session.Onboarding.IsRequired);
        }

        // From here on the drives are all there, so the install has been found and brought along, whole.
        if (hasLegacyFiles)
        {
            var current = install.Snapshot(session.Paths.RootFolderPath);
            current.Remove(CustomDistributionPaths.MigratedMarkerFileName);
            Assert.Equal(install.ManagedRoot, session.Paths.RootFolderPath);
            Assert.Equal(files.OrderBy(pair => pair.Key), current.OrderBy(pair => pair.Key));
            Assert.False(install.Fs.Directory.Exists(legacy), "the old folder is gone once its content is safely elsewhere");
            if (managedVersionBefore is not null)
            {
                // The one that was already there is kept next to it, not deleted.
                var backup = Assert.Single(install.Fs.Directory.GetDirectories(install.DataDir, "RetroRewind.old-*"));
                Assert.Equal(managedVersionBefore, install.Fs.File.ReadAllText(install.Combine(backup, "RetroRewind6", "version.txt")));
            }
        }
        else
        {
            Assert.Equal(install.ManagedRoot, session.Paths.RootFolderPath);
            if (managedVersionBefore is not null)
                Assert.Equal(
                    managedVersionBefore,
                    install.Fs.File.ReadAllText(install.Combine(install.ManagedRoot, "RetroRewind6", "version.txt"))
                );
        }

        // And it stays that way.
        var settled = install.Snapshot(install.DataDir);
        var again = install.Boot();
        Assert.Equal(settled.OrderBy(pair => pair.Key), install.Snapshot(install.DataDir).OrderBy(pair => pair.Key));
        Assert.Equal(install.ManagedRoot, again.Paths.RootFolderPath);
        Assert.False(again.Onboarding.IsRequired);
    }
}

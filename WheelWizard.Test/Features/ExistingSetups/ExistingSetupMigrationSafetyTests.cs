using Testably.Abstractions.Testing;
using WheelWizard.CustomDistributions;
using static WheelWizard.Test.Features.ExistingSetups.ExistingSetupAssertions;

namespace WheelWizard.Test.Features.ExistingSetups;

/// <summary>
/// The Retro Rewind folder holds what a player cannot download again: their save (VR, ghosts, license).
/// Whatever goes wrong while it is being moved, the original has to stay usable and the next start has to
/// finish the job.
/// </summary>
public class ExistingSetupMigrationSafetyTests
{
    private static ExistingInstall CrossDriveInstall() =>
        new ExistingInstall().WithDolphin(@"D:\Dolphin\Dolphin.exe", @"D:\Dolphin\Dolphin Emulator", @"D:\Games\Mario Kart Wii.iso");

    private static ExistingInstall SameDriveInstall() =>
        new ExistingInstall().WithDolphin(
            @"C:\Dolphin\Dolphin.exe",
            @"C:\Users\racer\Documents\Dolphin Emulator",
            @"C:\Games\Mario Kart Wii.iso"
        );

    [Fact]
    public void ACopyThatFailsHalfway_LeavesTheOriginalActiveAndComplete_AndTheNextStartFinishesTheMove()
    {
        var install = CrossDriveInstall();
        var files = install.WithRetroRewind(extraFiles: 30);
        install.FlushConfig();
        using var failing = install.Fs.Intercept.Event(
            _ => throw new IOException("There is not enough space on the disk."),
            change =>
                change.ChangeType == WatcherChangeTypes.Changed
                && change.FileSystemType == FileSystemTypes.File
                && change.Path.EndsWith("rksys.dat", StringComparison.OrdinalIgnoreCase)
                && change.Path.Contains(".migrating", StringComparison.OrdinalIgnoreCase)
        );

        var failed = install.Boot();

        AssertStillWorks(failed, files, install.LegacyRoot);
        Assert.False(install.Fs.File.Exists(failed.Paths.MigratedMarkerFilePath));
        Assert.False(install.Fs.Directory.Exists(install.ManagedRoot), "an unfinished copy must never become the active install");
        Assert.False(install.Fs.Directory.Exists(install.ManagedRoot + ".migrating"));

        failing.Dispose();
        var retried = install.Boot();

        AssertStillWorks(retried, files, install.ManagedRoot);
        Assert.False(install.Fs.Directory.Exists(install.LegacyRoot));
    }

    [Fact]
    public void ARenameThatIsRefused_BecauseTheFolderIsInUse_FallsBackToACopy()
    {
        var install = SameDriveInstall();
        var files = install.WithRetroRewind();
        install.FlushConfig();
        using var busy = install.Fs.Intercept.Event(
            _ => throw new IOException("The process cannot access the file because it is being used by another process."),
            change =>
                change.ChangeType == WatcherChangeTypes.Renamed
                && change.FileSystemType == FileSystemTypes.Directory
                && change.OldPath is not null
                && change.OldPath.EndsWith("WheelWizard", StringComparison.OrdinalIgnoreCase)
        );

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
    }

    [Fact]
    public void AMoveThatIsDeniedOutright_KeepsTheOriginalWorkingAndTriesAgainNextStart()
    {
        var install = SameDriveInstall();
        var files = install.WithRetroRewind();
        install.FlushConfig();
        using var denied = install.Fs.Intercept.Event(
            _ => throw new UnauthorizedAccessException("Access to the path is denied."),
            change =>
                change.ChangeType == WatcherChangeTypes.Renamed
                && change.FileSystemType == FileSystemTypes.Directory
                && change.OldPath is not null
                && change.OldPath.EndsWith("WheelWizard", StringComparison.OrdinalIgnoreCase)
        );

        var blocked = install.Boot();

        AssertStillWorks(blocked, files, install.LegacyRoot);
        Assert.False(install.Fs.File.Exists(blocked.Paths.MigratedMarkerFilePath));

        denied.Dispose();
        AssertStillWorks(install.Boot(), files, install.ManagedRoot);
    }

    [Fact]
    public void AnOldFolderThatCannotBeDeletedAfterTheCopy_IsNeverPickedAgain_AndTheCopyIsComplete()
    {
        var install = CrossDriveInstall();
        var files = install.WithRetroRewind();
        install.FlushConfig();
        using var stuck = install.Fs.Intercept.Event(
            _ => throw new IOException("The directory is not empty."),
            change =>
                change.ChangeType == WatcherChangeTypes.Deleted
                && change.Path.StartsWith(@"D:\Dolphin\Dolphin Emulator", StringComparison.OrdinalIgnoreCase)
        );

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
        Assert.True(install.Fs.Directory.Exists(install.LegacyRoot), "the folder that could not be removed is simply left behind");
        // Starting again must not copy it over the managed install, nor make it the active one.
        var again = install.Boot();
        AssertStillWorks(again, files, install.ManagedRoot);
        Assert.Empty(install.Fs.Directory.GetDirectories(install.DataDir, "RetroRewind.old-*"));
    }

    [Fact]
    public void AStagingFolderLeftByAnAppThatWasClosedMidCopy_IsDiscardedAndNeverUsed()
    {
        var install = CrossDriveInstall();
        var files = install.WithRetroRewind();
        install.Fs.Directory.CreateDirectory(install.Combine(install.ManagedRoot + ".migrating", "RetroRewind6"));
        install.Fs.File.WriteAllText(install.Combine(install.ManagedRoot + ".migrating", "RetroRewind6", "version.txt"), "6.0.0-partial");
        install.FlushConfig();

        var session = install.Boot();

        AssertStillWorks(session, files, install.ManagedRoot);
    }

    [Fact]
    public void AnUnwritableDataFolder_LeavesTheOriginalInPlace()
    {
        var install = CrossDriveInstall();
        var files = install.WithRetroRewind();
        install.FlushConfig();
        using var readOnly = install.Fs.Intercept.Event(
            _ => throw new UnauthorizedAccessException("Access to the path is denied."),
            change =>
                change.ChangeType == WatcherChangeTypes.Created
                && change.Path.StartsWith(install.ManagedRoot, StringComparison.OrdinalIgnoreCase)
        );

        var session = install.Boot();

        AssertStillWorks(session, files, install.LegacyRoot);
    }

    [Fact]
    public void ACancelledStartup_MovesNothing()
    {
        var install = SameDriveInstall();
        var files = install.WithRetroRewind();
        install.FlushConfig();
        var session = install.Boot(runStartup: false);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => session.Maintenance.RunAsync(_ => { }, cancelled.Token).GetAwaiter().GetResult()
        );

        Assert.Equal(files, install.Snapshot(install.LegacyRoot));
        Assert.False(install.Fs.Directory.Exists(install.ManagedRoot));
    }

    [Fact]
    public void RelocatingTheDataFolderAfterTheMove_TakesRetroRewindAlongAndKeepsUsingIt()
    {
        var install = SameDriveInstall();
        var files = install.WithRetroRewind();
        install.FlushConfig();
        install.Boot();

        install.RelocateDataFolder(@"C:\Users\racer\Games\WheelWizardData");
        var session = install.Boot();

        AssertStillWorks(session, files, @"C:\Users\racer\Games\WheelWizardData\RetroRewind");
    }

    [Fact]
    public void RelocatingTheDataFolderBeforeTheMove_StillMovesTheOldInstallIntoTheNewFolder()
    {
        var install = SameDriveInstall();
        var files = install.WithRetroRewind();
        install.FlushConfig();
        install.RelocateDataFolder(@"D:\WheelWizardData");

        var session = install.Boot();

        AssertStillWorks(session, files, @"D:\WheelWizardData\RetroRewind");
    }

    [Fact]
    public void WhileTheMoveIsPending_EveryPathWheelWizardUsesStillAddressesTheOldInstall_AndAfterwardsTheNewOne()
    {
        var install = SameDriveInstall();
        install.WithRetroRewind(withBeta: true);
        install.FlushConfig();

        var before = install.Boot(runStartup: false).Paths;
        Assert.True(before.IsLegacyRootActive);
        foreach (
            var path in new[]
            {
                before.RetroRewindFolderPath,
                before.BetaFolderPath,
                before.PatchesFolderPath,
                before.SaveFolderPath,
                before.XmlFilePath,
                before.BetaXmlFilePath,
            }
        )
            Assert.StartsWith(install.LegacyRoot, path, StringComparison.OrdinalIgnoreCase);

        var after = install.Boot().Paths;
        foreach (
            var path in new[]
            {
                after.RetroRewindFolderPath,
                after.BetaFolderPath,
                after.PatchesFolderPath,
                after.SaveFolderPath,
                after.XmlFilePath,
                after.BetaXmlFilePath,
            }
        )
            Assert.StartsWith(install.ManagedRoot, path, StringComparison.OrdinalIgnoreCase);
        Assert.False(after.IsLegacyRootActive);
    }

    [Fact]
    public void TheMigrationMarkerNeverEndsUpInTheGamesSight()
    {
        // The marker lives in the managed root, next to RetroRewind6 and riivolution, which Dolphin is told about
        // explicitly; it must not sit inside either of them.
        var install = SameDriveInstall();
        install.WithRetroRewind();
        install.FlushConfig();

        var session = install.Boot();

        var marker = session.Paths.MigratedMarkerFilePath;
        Assert.Equal(install.ManagedRoot, install.Fs.Path.GetDirectoryName(marker));
        Assert.False(
            install.Fs.File.Exists(install.Combine(session.Paths.RetroRewindFolderPath, CustomDistributionPaths.MigratedMarkerFileName))
        );
    }
}

using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.Shared.IO;

namespace WheelWizard.CustomDistributions;

public enum RetroRewindMigrationOutcome
{
    /// <summary>There was no legacy install to move.</summary>
    NotNeeded,
    Moved,

    /// <summary>The move failed; the legacy install stays active and the next start tries again.</summary>
    Failed,

    /// <summary>The Dolphin folder that may hold a legacy install cannot be read right now; the next start checks again.</summary>
    Deferred,
}

public interface IRetroRewindMigrationService
{
    bool IsPending { get; }

    /// <summary>
    /// Moves a Retro Rewind install out of Dolphin's Load folder into the managed folder, or records that there
    /// was none once that folder could actually be read, so this only ever happens once. A same-volume move
    /// is a rename; otherwise <paramref name="copyStarted"/> fires once before the (slow) verified copy, which
    /// is staged next to the managed folder and only takes its place when complete.
    /// </summary>
    OperationResult<RetroRewindMigrationOutcome> Migrate(Action? copyStarted = null, IProgress<double>? progress = null);
}

public sealed class RetroRewindMigrationService(
    ICustomDistributionPaths paths,
    IFileSystem fileSystem,
    IDirectoryTransferService transfer,
    TimeProvider time,
    ILogger<RetroRewindMigrationService> logger
) : IRetroRewindMigrationService
{
    public bool IsPending => paths.IsLegacyRootActive;

    public OperationResult<RetroRewindMigrationOutcome> Migrate(Action? copyStarted = null, IProgress<double>? progress = null)
    {
        var destination = paths.ManagedRootFolderPath;
        var staging = $"{destination}.migrating";
        // Left behind by a copy that was cut off (the app closed on the splash). The source is only removed once
        // the copy is in place, so this is never the only copy.
        TryDelete(staging);

        if (!paths.IsLegacyRootActive)
        {
            if (!paths.CanRuleOutLegacyRoot)
            {
                logger.LogInformation("The Dolphin folder that may hold Retro Rewind cannot be read; checking again next start");
                return RetroRewindMigrationOutcome.Deferred;
            }

            MarkChecked();
            return RetroRewindMigrationOutcome.NotNeeded;
        }

        var source = paths.LegacyRootFolderPath!;
        try
        {
            SetAsideExistingManagedInstall(destination);

            var parent = fileSystem.Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
                fileSystem.Directory.CreateDirectory(parent);

            if (TryRename(source, destination))
            {
                fileSystem.File.WriteAllText(paths.MigratedMarkerFilePath, source);
                logger.LogInformation("Moved Retro Rewind from {Source} to {Destination}", source, destination);
                return RetroRewindMigrationOutcome.Moved;
            }

            // Another drive: copy next to the managed root first, so an interrupted copy never becomes the active install.
            copyStarted?.Invoke();
            var result = transfer.MoveContents(source, staging, deleteSource: false, progress);
            if (!result.IsSuccessful)
            {
                TryDelete(staging);
                logger.LogError(result.Exception, "Moving Retro Rewind failed: {Message}", result.ErrorMessage);
                return RetroRewindMigrationOutcome.Failed;
            }

            // The marker goes in before the rename, so the copy is never active while the old one could still win.
            fileSystem.File.WriteAllText(fileSystem.Path.Combine(staging, CustomDistributionPaths.MigratedMarkerFileName), source);
            fileSystem.Directory.Move(staging, destination);
            logger.LogInformation("Moved Retro Rewind from {Source} to {Destination}", source, destination);

            // The copy is complete and verified; the marker keeps a leftover from ever being picked again.
            if (!TryDelete(source))
                logger.LogWarning("Moved Retro Rewind, but the old folder {Source} could not be removed", source);
            return RetroRewindMigrationOutcome.Moved;
        }
        catch (Exception exception)
        {
            TryDelete(staging);
            logger.LogError(exception, "Moving Retro Rewind from {Source} failed", source);
            return RetroRewindMigrationOutcome.Failed;
        }
    }

    /// <summary>
    /// Only an install that predates the managed folder may take over; once the first check found nothing to
    /// move, a Retro Rewind left in some Dolphin's Load folder later on is never picked up again.
    /// </summary>
    private void MarkChecked()
    {
        try
        {
            if (fileSystem.File.Exists(paths.MigratedMarkerFilePath))
                return;
            fileSystem.Directory.CreateDirectory(paths.ManagedRootFolderPath);
            fileSystem.File.WriteAllText(paths.MigratedMarkerFilePath, string.Empty);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not mark the Retro Rewind folder as checked");
        }
    }

    /// <summary>
    /// A WiiCompiled-only install may already sit in the managed folder. The legacy install is the one Wheel
    /// Wizard has been using, so it wins; the other one is kept next to it instead of being deleted.
    /// </summary>
    private void SetAsideExistingManagedInstall(string destination)
    {
        if (!fileSystem.Directory.Exists(destination))
            return;
        if (!fileSystem.Directory.EnumerateFileSystemEntries(destination).Any())
        {
            fileSystem.Directory.Delete(destination);
            return;
        }

        var backup = $"{destination}.old-{time.GetLocalNow():yyyyMMdd-HHmmss}";
        fileSystem.Directory.Move(destination, backup);
        logger.LogInformation("Kept the existing managed Retro Rewind as {Backup}", backup);
    }

    private bool TryRename(string source, string destination)
    {
        try
        {
            fileSystem.Directory.Move(source, destination);
            return true;
        }
        catch (IOException)
        {
            // Different volumes (or a busy file); fall back to a verified copy.
            return false;
        }
    }

    private bool TryDelete(string folder)
    {
        try
        {
            if (fileSystem.Directory.Exists(folder))
                fileSystem.Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove {Folder}", folder);
            return false;
        }
    }
}

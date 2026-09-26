using System.IO.Abstractions;
using WheelWizard.Shared.IO;

namespace WheelWizard.ApplicationData;

public interface IApplicationDataLocation
{
    string DirectoryPath { get; }
    string DefaultDirectoryPath { get; }
    bool IsCustom { get; }
    bool TryMove(
        string requestedPath,
        out string errorMessage,
        out DirectoryMoveContentsResult moveResult,
        IProgress<double>? progress = null
    );
    bool TryReset(out string errorMessage);
    bool TryRevertMove(string previousPath, string newPath, out string errorMessage);
    bool TryCleanupPartialMove(string destinationPath, out string errorMessage);
    bool TryValidateTarget(
        string requestedPath,
        out string normalizedTarget,
        out string currentPath,
        out string errorMessage,
        out bool requiresMove
    );
}

public sealed class ApplicationDataLocation : IApplicationDataLocation
{
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationDataLocationStore _store;
    private readonly IDirectoryTransferService _transfer;
    private readonly object _stateLock = new();
    private string? _override;
    public string DefaultDirectoryPath { get; }

    public ApplicationDataLocation(
        IFileSystem fileSystem,
        ApplicationDataDirectories directories,
        IApplicationDataLocationStore store,
        IDirectoryTransferService transfer
    )
    {
        _fileSystem = fileSystem;
        DefaultDirectoryPath = directories.DefaultDirectoryPath;
        _store = store;
        _transfer = transfer;
        _override = LoadSavedLocation();
    }

    public string DirectoryPath
    {
        get
        {
            lock (_stateLock)
            {
                return _override ?? DefaultDirectoryPath;
            }
        }
    }

    public bool IsCustom
    {
        get
        {
            lock (_stateLock)
            {
                return _override != null;
            }
        }
    }

    private string? LoadSavedLocation()
    {
        try
        {
            var storedPath = _store.Load();
            if (string.IsNullOrWhiteSpace(storedPath))
                return null;

            var normalized = _fileSystem.Path.NormalizePath(storedPath);
            if (_fileSystem.Path.PathsEqual(normalized, DefaultDirectoryPath))
                return null;

            // If a previously selected custom location is no longer available (for example,
            // when an external drive letter changes), fall back to the default path.
            if (!TryEnsureAccessible(normalized))
                return null;

            return normalized;
        }
        catch
        {
            return null;
        }
    }

    private bool TryEnsureAccessible(string normalizedPath)
    {
        try
        {
            return _fileSystem.EnsureDirectory(normalizedPath).IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    public bool TryMove(
        string requestedPath,
        out string errorMessage,
        out DirectoryMoveContentsResult moveResult,
        IProgress<double>? progress = null
    )
    {
        errorMessage = string.Empty;
        moveResult = new DirectoryMoveContentsResult(
            DirectoryMoveOutcome.NoOp,
            string.Empty,
            string.Empty,
            copyAttempted: false,
            verificationAttempted: false,
            deleteSourceRequested: false,
            sourceDeletionSucceeded: true
        );

        if (!TryValidateTarget(requestedPath, out var normalizedTarget, out var currentPath, out errorMessage, out var requiresMove))
            return false;

        if (!requiresMove)
        {
            moveResult = new DirectoryMoveContentsResult(
                DirectoryMoveOutcome.NoOp,
                currentPath,
                normalizedTarget,
                copyAttempted: false,
                verificationAttempted: false,
                deleteSourceRequested: false,
                sourceDeletionSucceeded: true
            );
            return true;
        }

        if (!_fileSystem.Directory.Exists(normalizedTarget))
        {
            try
            {
                _fileSystem.Directory.CreateDirectory(normalizedTarget);
            }
            catch (Exception ex)
            {
                errorMessage = $"Unable to create the selected folder: {ex.Message}";
                return false;
            }
        }
        else if (!_fileSystem.IsDirectoryEmpty(normalizedTarget))
        {
            errorMessage = "The selected folder must be empty. Please choose an empty folder.";
            return false;
        }

        var newOverrideValue = _fileSystem.Path.PathsEqual(normalizedTarget, DefaultDirectoryPath) ? null : normalizedTarget;

        try
        {
            moveResult = _transfer.MoveContents(currentPath, normalizedTarget, progress: progress);
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to move Wheel Wizard files: {ex.Message}";
            return false;
        }

        switch (moveResult.Outcome)
        {
            case DirectoryMoveOutcome.CopyFailed:
                errorMessage = moveResult.ErrorMessage ?? "Failed to copy Wheel Wizard files.";
                return false;
            case DirectoryMoveOutcome.VerificationFailed:
                var failureMessage = moveResult.ErrorMessage ?? "Failed to verify Wheel Wizard files.";
                if (moveResult.VerificationFailures.Count > 0)
                    failureMessage += $"\n{string.Join("\n", moveResult.VerificationFailures)}";
                errorMessage = failureMessage;
                return false;
            case DirectoryMoveOutcome.NoOp:
            case DirectoryMoveOutcome.Success:
            case DirectoryMoveOutcome.SourceDeletionFailed:
                break;
            default:
                errorMessage = "Unknown outcome while moving Wheel Wizard files.";
                return false;
        }

        // Update the setting even if persistence fails, since files were successfully moved or intentionally skipped
        lock (_stateLock)
        {
            _override = newOverrideValue;
        }

        try
        {
            PersistLocation(newOverrideValue);
        }
        catch (Exception ex)
        {
            // Log the persistence failure but don't fail the operation since files are already moved
            // and the in-memory setting is updated
            errorMessage = $"Warning: Files were moved successfully, but failed to persist the setting: {ex.Message}";
            // Still return true since the operation succeeded where it matters
        }

        if (moveResult.Outcome == DirectoryMoveOutcome.SourceDeletionFailed)
        {
            var deletionWarning =
                moveResult.ErrorMessage
                ?? $"Files were moved successfully, but the old folder '{currentPath}' could not be deleted. You may need to remove it manually.";

            errorMessage = string.IsNullOrEmpty(errorMessage) ? deletionWarning : $"{errorMessage} {deletionWarning}";
        }

        return true;
    }

    public bool TryReset(out string errorMessage) => TryMove(DefaultDirectoryPath, out errorMessage, out _);

    public bool TryRevertMove(string previousPath, string newPath, out string errorMessage)
    {
        errorMessage = string.Empty;

        string normalizedPrevious;
        string normalizedNew;

        try
        {
            normalizedPrevious = _fileSystem.Path.NormalizePath(previousPath);
            normalizedNew = _fileSystem.Path.NormalizePath(newPath);
        }
        catch (Exception ex)
        {
            errorMessage = $"Invalid folder path: {ex.Message}";
            return false;
        }

        var previousOverrideValue = _fileSystem.Path.PathsEqual(normalizedPrevious, DefaultDirectoryPath) ? null : normalizedPrevious;

        // Source deletion can fail after removing some files. Do not discard the complete destination
        // unless the previous folder still contains every copied file with matching contents.
        try
        {
            if (
                !_fileSystem.Directory.Exists(normalizedPrevious)
                || _fileSystem.Path.PathsEqual(normalizedPrevious, normalizedNew)
                || _fileSystem.Path.IsDescendantPath(normalizedPrevious, normalizedNew)
                || _fileSystem.Path.IsDescendantPath(normalizedNew, normalizedPrevious)
                || !PreviousCopyIsComplete(normalizedPrevious, normalizedNew)
            )
            {
                errorMessage =
                    "Cannot revert because the previous folder no longer contains a complete copy. Keep using the new data folder.";
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Could not verify the previous data folder: {ex.Message}";
            return false;
        }

        lock (_stateLock)
        {
            _override = previousOverrideValue;
        }

        try
        {
            PersistLocation(previousOverrideValue);
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to persist Wheel Wizard data folder setting: {ex.Message}";
            return false;
        }

        if (_fileSystem.Directory.Exists(normalizedNew))
        {
            try
            {
                _fileSystem.Directory.Delete(normalizedNew, true);
            }
            catch (Exception ex)
            {
                errorMessage = $"Reverted to the previous folder, but failed to remove the new folder '{normalizedNew}': {ex.Message}";
                return false;
            }
        }

        return true;
    }

    public bool TryCleanupPartialMove(string destinationPath, out string errorMessage)
    {
        errorMessage = string.Empty;

        string normalizedDestination;
        try
        {
            normalizedDestination = _fileSystem.Path.NormalizePath(destinationPath);
        }
        catch (Exception ex)
        {
            errorMessage = $"Invalid folder path: {ex.Message}";
            return false;
        }

        if (!_fileSystem.Directory.Exists(normalizedDestination))
            return true;

        try
        {
            _fileSystem.Directory.Delete(normalizedDestination, true);
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to remove the partially copied folder '{normalizedDestination}': {ex.Message}";
            return false;
        }

        return true;
    }

    public bool TryValidateTarget(
        string requestedPath,
        out string normalizedTarget,
        out string currentPath,
        out string errorMessage,
        out bool requiresMove
    )
    {
        normalizedTarget = string.Empty;
        currentPath = string.Empty;
        errorMessage = string.Empty;
        requiresMove = false;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            errorMessage = "Please select a valid folder.";
            return false;
        }

        try
        {
            normalizedTarget = _fileSystem.Path.NormalizePath(requestedPath);
        }
        catch (Exception ex)
        {
            errorMessage = $"Invalid folder path: {ex.Message}";
            return false;
        }

        lock (_stateLock)
        {
            currentPath = _override ?? DefaultDirectoryPath;
        }

        if (_fileSystem.Path.PathsEqual(currentPath, normalizedTarget))
            return true;

        if (_fileSystem.Path.IsDescendantPath(normalizedTarget, currentPath))
        {
            errorMessage = "The selected folder is inside the current Wheel Wizard data folder. Please choose a different folder.";
            return false;
        }

        if (_fileSystem.Path.IsDescendantPath(currentPath, normalizedTarget))
        {
            errorMessage = "The selected folder contains the current Wheel Wizard data folder. Please choose a different folder.";
            return false;
        }

        if (_fileSystem.File.Exists(normalizedTarget))
        {
            errorMessage = "The selected path points to a file. Please choose an empty folder instead.";
            return false;
        }

        if (_fileSystem.Path.IsRootDirectory(normalizedTarget))
        {
            errorMessage = "Selecting a drive or root directory is not allowed. Please choose an empty folder.";
            return false;
        }

        if (_fileSystem.Directory.Exists(normalizedTarget) && !_fileSystem.IsDirectoryEmpty(normalizedTarget))
        {
            errorMessage = "The selected folder must be empty. Please choose an empty folder.";
            return false;
        }

        requiresMove = true;
        return true;
    }

    private bool PreviousCopyIsComplete(string previousPath, string newPath)
    {
        if (!_fileSystem.Directory.Exists(newPath))
            return true;

        foreach (var copiedFile in _fileSystem.Directory.EnumerateFiles(newPath, "*", SearchOption.AllDirectories))
        {
            var relative = _fileSystem.Path.GetRelativePath(newPath, copiedFile);
            var previousFile = _fileSystem.Path.Combine(previousPath, relative);
            if (
                !_fileSystem.File.Exists(previousFile)
                || _fileSystem.FileInfo.New(previousFile).Length != _fileSystem.FileInfo.New(copiedFile).Length
            )
                return false;

            using var previous = _fileSystem.File.OpenRead(previousFile);
            using var copied = _fileSystem.File.OpenRead(copiedFile);
            if (
                !System
                    .Security.Cryptography.SHA256.HashData(previous)
                    .AsSpan()
                    .SequenceEqual(System.Security.Cryptography.SHA256.HashData(copied))
            )
                return false;
        }

        return true;
    }

    private void PersistLocation(string? overridePath) =>
        _store.Save(
            string.IsNullOrWhiteSpace(overridePath) || _fileSystem.Path.PathsEqual(overridePath, DefaultDirectoryPath)
                ? null
                : _fileSystem.Path.NormalizePath(overridePath)
        );
}

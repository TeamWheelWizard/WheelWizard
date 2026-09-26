using System.IO.Abstractions;

namespace WheelWizard.Shared.IO;

public interface IDirectoryTransferService
{
    DirectoryMoveContentsResult MoveContents(
        string sourcePath,
        string destinationPath,
        bool deleteSource = true,
        IProgress<double>? progress = null
    );
}

public sealed class DirectoryTransferService(IFileSystem fileSystem) : IDirectoryTransferService
{
    public DirectoryMoveContentsResult MoveContents(
        string sourcePath,
        string destinationPath,
        bool deleteSource = true,
        IProgress<double>? progress = null
    )
    {
        var normalizedSource = fileSystem.Path.NormalizePath(sourcePath);
        var normalizedDestination = fileSystem.Path.NormalizePath(destinationPath);

        DirectoryMoveContentsResult CreateResult(
            DirectoryMoveOutcome outcome,
            bool copyAttempted = false,
            bool verificationAttempted = false,
            bool sourceDeletionSucceeded = false,
            string? errorMessage = null,
            Exception? exception = null,
            IReadOnlyList<string>? verificationFailures = null
        ) =>
            new(
                outcome,
                normalizedSource,
                normalizedDestination,
                copyAttempted,
                verificationAttempted,
                deleteSource,
                sourceDeletionSucceeded,
                errorMessage,
                exception,
                verificationFailures
            );

        DirectoryMoveContentsResult? CreateEnsureDirectoryFailureResult(OperationResult ensureDirectoryResult, bool copyAttempted = false)
        {
            if (ensureDirectoryResult.IsSuccess)
                return null;

            return CreateResult(
                DirectoryMoveOutcome.CopyFailed,
                copyAttempted: copyAttempted,
                errorMessage: ensureDirectoryResult.Error.Message,
                exception: ensureDirectoryResult.Error.Exception
            );
        }

        if (fileSystem.Path.PathsEqual(normalizedSource, normalizedDestination))
        {
            var ensureResult = CreateEnsureDirectoryFailureResult(fileSystem.EnsureDirectory(normalizedDestination));
            if (ensureResult is not null)
            {
                progress?.Report(1.0);
                return ensureResult;
            }

            progress?.Report(1.0);
            return CreateResult(DirectoryMoveOutcome.NoOp, sourceDeletionSucceeded: true);
        }

        if (
            fileSystem.Path.IsDescendantPath(normalizedDestination, normalizedSource)
            || fileSystem.Path.IsDescendantPath(normalizedSource, normalizedDestination)
        )
        {
            progress?.Report(1.0);
            return CreateResult(
                DirectoryMoveOutcome.CopyFailed,
                errorMessage: "Source and destination folders must not contain each other."
            );
        }

        var destinationEnsureResult = CreateEnsureDirectoryFailureResult(fileSystem.EnsureDirectory(normalizedDestination));
        if (destinationEnsureResult is not null)
        {
            progress?.Report(1.0);
            return destinationEnsureResult;
        }

        if (!fileSystem.Directory.Exists(normalizedSource))
        {
            progress?.Report(1.0);
            return CreateResult(DirectoryMoveOutcome.NoOp, sourceDeletionSucceeded: true);
        }

        progress?.Report(0.0);

        var directories = fileSystem.Directory.GetDirectories(normalizedSource, "*", SearchOption.AllDirectories);
        var files = fileSystem.Directory.GetFiles(normalizedSource, "*", SearchOption.AllDirectories);
        var totalSteps = directories.Length + files.Length;
        var processedSteps = 0;

        void ReportProgress()
        {
            if (progress == null)
                return;

            if (totalSteps <= 0)
            {
                progress.Report(1.0);
                return;
            }

            progress.Report(Math.Clamp((double)processedSteps / totalSteps, 0.0, 1.0));
        }

        var copyAttempted = directories.Length > 0 || files.Length > 0;

        try
        {
            foreach (var directory in directories)
            {
                var relative = fileSystem.Path.GetRelativePath(normalizedSource, directory);
                var ensureResult = CreateEnsureDirectoryFailureResult(
                    fileSystem.EnsureDirectory(fileSystem.Path.Combine(normalizedDestination, relative)),
                    copyAttempted
                );
                if (ensureResult is not null)
                {
                    progress?.Report(1.0);
                    return ensureResult;
                }

                processedSteps++;
                ReportProgress();
            }

            foreach (var file in files)
            {
                var relative = fileSystem.Path.GetRelativePath(normalizedSource, file);
                var destinationFile = fileSystem.Path.Combine(normalizedDestination, relative);
                var destinationDirectory = fileSystem.Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    var ensureResult = CreateEnsureDirectoryFailureResult(fileSystem.EnsureDirectory(destinationDirectory), copyAttempted);
                    if (ensureResult is not null)
                    {
                        progress?.Report(1.0);
                        return ensureResult;
                    }
                }

                if (fileSystem.File.Exists(destinationFile))
                    fileSystem.File.Delete(destinationFile);

                fileSystem.File.Copy(file, destinationFile, overwrite: true);
                processedSteps++;
                ReportProgress();
            }
        }
        catch (Exception ex)
        {
            progress?.Report(1.0);
            return CreateResult(
                DirectoryMoveOutcome.CopyFailed,
                copyAttempted: copyAttempted,
                errorMessage: $"Failed to copy data: {ex.Message}",
                exception: ex
            );
        }

        var verificationAttempted = true;
        var verificationFailures = new List<string>();

        foreach (var file in files)
        {
            var relative = fileSystem.Path.GetRelativePath(normalizedSource, file);
            var destinationFile = fileSystem.Path.Combine(normalizedDestination, relative);

            if (!fileSystem.File.Exists(destinationFile))
            {
                verificationFailures.Add($"{relative} (missing in destination)");
                continue;
            }

            var sourceInfo = fileSystem.FileInfo.New(file);
            var destinationInfo = fileSystem.FileInfo.New(destinationFile);
            if (sourceInfo.Length != destinationInfo.Length)
            {
                verificationFailures.Add(
                    $"{relative} (size mismatch: expected {sourceInfo.Length} bytes, found {destinationInfo.Length} bytes)"
                );
            }
        }

        if (verificationFailures.Count > 0)
        {
            progress?.Report(1.0);
            return CreateResult(
                DirectoryMoveOutcome.VerificationFailed,
                copyAttempted: copyAttempted,
                verificationAttempted: verificationAttempted,
                errorMessage: "One or more files failed verification after copying.",
                verificationFailures: verificationFailures
            );
        }

        if (deleteSource && fileSystem.Directory.Exists(normalizedSource))
        {
            try
            {
                fileSystem.Directory.Delete(normalizedSource, true);
            }
            catch (Exception ex)
            {
                progress?.Report(1.0);
                return CreateResult(
                    DirectoryMoveOutcome.SourceDeletionFailed,
                    copyAttempted: copyAttempted,
                    verificationAttempted: verificationAttempted,
                    sourceDeletionSucceeded: false,
                    errorMessage: $"Failed to delete source folder '{normalizedSource}': {ex.Message}",
                    exception: ex
                );
            }
        }

        progress?.Report(1.0);
        return CreateResult(
            DirectoryMoveOutcome.Success,
            copyAttempted: copyAttempted,
            verificationAttempted: verificationAttempted,
            sourceDeletionSucceeded: true
        );
    }
}

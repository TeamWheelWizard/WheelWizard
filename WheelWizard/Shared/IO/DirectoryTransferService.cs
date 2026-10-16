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

        // A linked folder (e.g. a game folder the user put on another drive) moves as a link; only when the link can't
        // be recreated here are its contents copied instead. Deleting the source then removes the link, never its target.
        var directories = new List<string>();
        var files = new List<string>();
        var sourceLinks = new List<string>();
        try
        {
            Collect(normalizedSource, normalizedSource, normalizedDestination, directories, files, sourceLinks, insideLink: false);
        }
        catch (Exception ex)
        {
            progress?.Report(1.0);
            return CreateResult(DirectoryMoveOutcome.CopyFailed, errorMessage: $"Failed to copy data: {ex.Message}", exception: ex);
        }

        var totalSteps = directories.Count + files.Count;
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

        var copyAttempted = directories.Count > 0 || files.Count > 0;

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
                // Links (recreated at the destination, or copied through when that wasn't possible) go first, on their
                // own: that keeps the recursive delete away from their targets and from junctions, which Windows
                // refuses to delete that way. Deepest first, in case a linked folder held links of its own.
                foreach (var link in sourceLinks.OrderByDescending(link => link.Length))
                    DeleteLink(link);
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

    private void Collect(
        string sourceRoot,
        string folder,
        string destinationRoot,
        List<string> directories,
        List<string> files,
        List<string> sourceLinks,
        bool insideLink
    )
    {
        foreach (var directory in fileSystem.Directory.EnumerateDirectories(folder))
        {
            var link = fileSystem.DirectoryInfo.New(directory).LinkTarget;
            var isLink = !string.IsNullOrEmpty(link);
            if (isLink)
            {
                // Only links in the folder itself are removed later; one inside a linked folder is not ours to touch.
                if (!insideLink)
                    sourceLinks.Add(directory);
                if (TryRecreateLink(directory, link!, sourceRoot, destinationRoot))
                    continue;
            }

            directories.Add(directory);
            Collect(sourceRoot, directory, destinationRoot, directories, files, sourceLinks, insideLink || isLink);
        }

        files.AddRange(fileSystem.Directory.EnumerateFiles(folder));
    }

    private void DeleteLink(string path)
    {
        if (fileSystem.Path.DirectorySeparatorChar == '\\')
            fileSystem.Directory.Delete(path, recursive: false);
        else
            fileSystem.File.Delete(path);
    }

    private bool TryRecreateLink(string linkPath, string target, string sourceRoot, string destinationRoot)
    {
        try
        {
            var destination = fileSystem.Path.Combine(destinationRoot, fileSystem.Path.GetRelativePath(sourceRoot, linkPath));
            if (fileSystem.Directory.Exists(destination) || fileSystem.File.Exists(destination))
                return false;
            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(destination)!);
            // A relative target keeps pointing at the same place only when it is resolved from the old folder.
            var absoluteTarget = fileSystem.Path.IsPathRooted(target)
                ? target
                : fileSystem.Path.GetFullPath(fileSystem.Path.Combine(fileSystem.Path.GetDirectoryName(linkPath)!, target));
            fileSystem.Directory.CreateSymbolicLink(destination, absoluteTarget);
            return true;
        }
        catch
        {
            // Windows only lets some accounts create links; the contents are copied instead.
            return false;
        }
    }
}

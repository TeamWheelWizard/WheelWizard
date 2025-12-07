using System.Collections.Generic;

namespace WheelWizard.Helpers;

public enum DirectoryMoveOutcome
{
    NoOp,
    Success,
    CopyFailed,
    VerificationFailed,
    SourceDeletionFailed,
}

public sealed class DirectoryMoveContentsResult
{
    public DirectoryMoveContentsResult(
        DirectoryMoveOutcome outcome,
        string sourcePath,
        string destinationPath,
        bool copyAttempted,
        bool verificationAttempted,
        bool deleteSourceRequested,
        bool sourceDeletionSucceeded,
        string? errorMessage = null,
        Exception? exception = null,
        IReadOnlyList<string>? verificationFailures = null
    )
    {
        Outcome = outcome;
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        CopyAttempted = copyAttempted;
        VerificationAttempted = verificationAttempted;
        DeleteSourceRequested = deleteSourceRequested;
        SourceDeletionSucceeded = deleteSourceRequested ? sourceDeletionSucceeded : true;
        ErrorMessage = errorMessage;
        Exception = exception;
        VerificationFailures = verificationFailures ?? Array.Empty<string>();
    }

    public DirectoryMoveOutcome Outcome { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public bool CopyAttempted { get; }
    public bool VerificationAttempted { get; }
    public bool DeleteSourceRequested { get; }
    public bool SourceDeletionSucceeded { get; }
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }
    public IReadOnlyList<string> VerificationFailures { get; }

    public bool CopyCompleted => CopyAttempted && Outcome != DirectoryMoveOutcome.CopyFailed;
    public bool VerificationSucceeded => !VerificationAttempted || Outcome != DirectoryMoveOutcome.VerificationFailed;
    public bool IsSuccessful => Outcome is DirectoryMoveOutcome.Success or DirectoryMoveOutcome.NoOp;
    public bool RequiresUserDecision => Outcome == DirectoryMoveOutcome.SourceDeletionFailed;
}

// From now on we to have this FileHelper as a middle man whenever we do anything file related. This makes
// it easier to create helper methods, mock data, and most importantly, easy to make it multi-platform later on
public static class FileHelper
{
    public static bool FileExists(string path) => File.Exists(path);

    public static bool DirectoryExists(string path) => Directory.Exists(path);

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public static string Combine(params string[] paths) => Path.Combine(paths);

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    public static bool IsRootDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalized;
        try
        {
            normalized = NormalizePath(path);
        }
        catch
        {
            return false;
        }

        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root))
            return false;

        try
        {
            return PathsEqual(normalized, root);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDirectoryEmpty(string path)
    {
        if (!DirectoryExists(path))
            return true;

        foreach (var _ in Directory.EnumerateFileSystemEntries(path))
            return false;

        return true;
    }

    public static string GetRelativePath(string relativeTo, string path) => Path.GetRelativePath(relativeTo, path);

    public static bool PathsEqual(string pathA, string pathB)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(NormalizePath(pathA), NormalizePath(pathB), comparison);
    }

    public static bool IsDescendantPath(string potentialDescendant, string potentialAncestor)
    {
        if (string.IsNullOrWhiteSpace(potentialDescendant) || string.IsNullOrWhiteSpace(potentialAncestor))
            return false;

        if (PathsEqual(potentialDescendant, potentialAncestor))
            return false;

        var normalizedAncestor = NormalizePath(potentialAncestor);
        var normalizedDescendant = NormalizePath(potentialDescendant);
        var relative = Path.GetRelativePath(normalizedAncestor, normalizedDescendant);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    public static string ReadAllText(string path) => File.ReadAllText(path);

    public static string? ReadAllTextSafe(string path) => FileExists(path) ? ReadAllText(path) : null;

    public static string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public static string[]? ReadAllLinesSafe(string path) => FileExists(path) ? ReadAllLines(path) : null;

    public static void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public static void WriteAllTextSafe(string path, string contents)
    {
        var directoryPath = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directoryPath) && !DirectoryExists(directoryPath))
            Directory.CreateDirectory(directoryPath);

        WriteAllText(path, contents);
    }

    public static void WriteAllLines(string path, IEnumerable<string> contents) => File.WriteAllLines(path, contents);

    public static void WriteAllLinesSafe(string path, IEnumerable<string> contents)
    {
        var directoryPath = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directoryPath) && !DirectoryExists(directoryPath))
            Directory.CreateDirectory(directoryPath);

        WriteAllLines(path, contents);
    }

    public static void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    public static DirectoryMoveContentsResult MoveDirectoryContents(
        string sourcePath,
        string destinationPath,
        bool deleteSource = true,
        IProgress<double>? progress = null
    )
    {
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDestination = NormalizePath(destinationPath);

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

        if (PathsEqual(normalizedSource, normalizedDestination))
        {
            EnsureDirectory(normalizedDestination);
            progress?.Report(1.0);
            return CreateResult(DirectoryMoveOutcome.NoOp, sourceDeletionSucceeded: true);
        }

        EnsureDirectory(normalizedDestination);

        if (!DirectoryExists(normalizedSource))
        {
            progress?.Report(1.0);
            return CreateResult(DirectoryMoveOutcome.NoOp, sourceDeletionSucceeded: true);
        }

        progress?.Report(0.0);

        var directories = Directory.GetDirectories(normalizedSource, "*", SearchOption.AllDirectories);
        var files = Directory.GetFiles(normalizedSource, "*", SearchOption.AllDirectories);
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
                var relative = GetRelativePath(normalizedSource, directory);
                EnsureDirectory(Path.Combine(normalizedDestination, relative));
                processedSteps++;
                ReportProgress();
            }

            foreach (var file in files)
            {
                var relative = GetRelativePath(normalizedSource, file);
                var destinationFile = Path.Combine(normalizedDestination, relative);
                var destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(destinationDirectory))
                    EnsureDirectory(destinationDirectory);

                if (FileExists(destinationFile))
                    File.Delete(destinationFile);

                File.Copy(file, destinationFile, overwrite: true);
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
            var relative = GetRelativePath(normalizedSource, file);
            var destinationFile = Path.Combine(normalizedDestination, relative);

            if (!FileExists(destinationFile))
            {
                verificationFailures.Add($"{relative} (missing in destination)");
                continue;
            }

            var sourceInfo = new FileInfo(file);
            var destinationInfo = new FileInfo(destinationFile);
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

        if (deleteSource && DirectoryExists(normalizedSource))
        {
            try
            {
                Directory.Delete(normalizedSource, true);
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

    public static void Touch(string path, string defaultValue = "")
    {
        if (DirectoryExists(path))
            TouchDirectory(path);
        else if (FileExists(path))
            TouchFile(path, defaultValue);
        else
        {
            var likelyDirectory =
                (path.EndsWith(Path.DirectorySeparatorChar.ToString()) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                || !Path.HasExtension(path);
            if (likelyDirectory)
                TouchDirectory(path);
            else
                TouchFile(path, defaultValue);
        }
    }

    public static void TouchFile(string path, string defaultValue = "")
    {
        if (FileExists(path))
            File.SetLastAccessTime(path, DateTime.Now);
        else
            WriteAllTextSafe(path, defaultValue);
    }

    public static void TouchDirectory(string path)
    {
        if (DirectoryExists(path))
        {
            Directory.SetLastAccessTime(path, DateTime.Now);
            Directory.SetLastWriteTime(path, DateTime.Now);
        }
        else
            Directory.CreateDirectory(path);
    }
}

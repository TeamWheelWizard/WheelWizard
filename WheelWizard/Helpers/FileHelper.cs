namespace WheelWizard.Helpers;

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

    public static void MoveDirectoryContents(
        string sourcePath,
        string destinationPath,
        bool deleteSource = true,
        IProgress<double>? progress = null
    )
    {
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDestination = NormalizePath(destinationPath);

        if (PathsEqual(normalizedSource, normalizedDestination))
        {
            EnsureDirectory(normalizedDestination);
            progress?.Report(1.0);
            return;
        }

        EnsureDirectory(normalizedDestination);

        if (!DirectoryExists(normalizedSource))
        {
            progress?.Report(1.0);
            return;
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

            File.Move(file, destinationFile);
            processedSteps++;
            ReportProgress();
        }

        if (deleteSource && DirectoryExists(normalizedSource))
            Directory.Delete(normalizedSource, true);

        progress?.Report(1.0);
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

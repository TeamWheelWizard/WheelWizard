namespace WheelWizard.Shared.IO;

public static class PathSafety
{
    private static readonly System.IO.Abstractions.IPath NativePath = new Testably.Abstractions.RealFileSystem().Path;

    public static bool TryGetPathWithinDirectory(string directory, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;

        if (!TryNormalizeRelativePath(relativePath, out var normalizedRelativePath))
            return false;

        var destinationPath = NativePath.Combine(directory, normalizedRelativePath);
        var fullDirectory = NativePath.GetFullPath(directory);
        var candidatePath = NativePath.GetFullPath(destinationPath);

        if (!IsPathWithinDirectory(fullDirectory, candidatePath))
            return false;

        fullPath = candidatePath;
        return true;
    }

    public static bool IsPathWithinDirectory(string directory, string path)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(path))
            return false;

        var fullDirectory = NativePath.GetFullPath(directory);
        var fullPath = NativePath.GetFullPath(path);
        var relativePath = NativePath.GetRelativePath(fullDirectory, fullPath);

        return relativePath == "."
            || (
                !NativePath.IsPathRooted(relativePath)
                && !relativePath.Equals("..", StringComparison.Ordinal)
                && !relativePath.StartsWith($"..{NativePath.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relativePath.StartsWith($"..{NativePath.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            );
    }

    public static bool TryNormalizeRelativePath(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        var trimmedPath = path.Trim();
        if (NativePath.IsPathFullyQualified(trimmedPath) || trimmedPath.StartsWith('/') || trimmedPath.StartsWith('\\'))
            return false;

        var segments = trimmedPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != ".");

        var safeSegments = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".." || segment.Contains(NativePath.VolumeSeparatorChar))
                return false;

            safeSegments.Add(segment);
        }

        if (safeSegments.Count == 0)
            return false;

        normalizedPath = NativePath.Combine(safeSegments.ToArray());
        return true;
    }

    public static bool TryGetSafeFileName(string fileName, out string safeFileName)
    {
        safeFileName = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var leafName = fileName.Trim().Trim('"').Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(leafName) || leafName is "." or "..")
            return false;

        if (leafName.IndexOfAny(NativePath.GetInvalidFileNameChars()) >= 0)
            return false;

        safeFileName = leafName;
        return true;
    }
}

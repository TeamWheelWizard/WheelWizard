namespace WheelWizard.Helpers;

public static class PathSafetyHelper
{
    public static bool TryGetPathWithinDirectory(string directory, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;

        if (!TryNormalizeRelativePath(relativePath, out var normalizedRelativePath))
            return false;

        var destinationPath = Path.Combine(directory, normalizedRelativePath);
        var fullDirectory = Path.GetFullPath(directory);
        var candidatePath = Path.GetFullPath(destinationPath);

        if (!IsPathWithinDirectory(fullDirectory, candidatePath))
            return false;

        fullPath = candidatePath;
        return true;
    }

    public static bool IsPathWithinDirectory(string directory, string path)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(path))
            return false;

        var fullDirectory = Path.GetFullPath(directory);
        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(fullDirectory, fullPath);

        return relativePath == "."
            || (
                !Path.IsPathRooted(relativePath)
                && !relativePath.Equals("..", StringComparison.Ordinal)
                && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            );
    }

    public static bool TryNormalizeRelativePath(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        var trimmedPath = path.Trim();
        if (Path.IsPathFullyQualified(trimmedPath) || trimmedPath.StartsWith('/') || trimmedPath.StartsWith('\\'))
            return false;

        var segments = trimmedPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != ".");

        var safeSegments = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".." || segment.Contains(Path.VolumeSeparatorChar))
                return false;

            safeSegments.Add(segment);
        }

        if (safeSegments.Count == 0)
            return false;

        normalizedPath = Path.Combine(safeSegments.ToArray());
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

        if (leafName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        safeFileName = leafName;
        return true;
    }
}

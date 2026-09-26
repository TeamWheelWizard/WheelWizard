using System.IO.Abstractions;

namespace WheelWizard.Shared.IO;

public static class PathExtensions
{
    public static string NormalizePath(this IPath paths, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        var fullPath = paths.GetFullPath(path);
        var root = paths.GetPathRoot(fullPath) ?? "";

        while (fullPath.Length > root.Length)
        {
            var trimmedPath = paths.TrimEndingDirectorySeparator(fullPath);

            if (trimmedPath.Equals(fullPath, StringComparison.Ordinal))
            {
                break;
            }

            fullPath = trimmedPath;
        }

        return fullPath;
    }

    public static bool IsRootDirectory(this IPath paths, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalized;
        try
        {
            normalized = paths.NormalizePath(path);
        }
        catch
        {
            return false;
        }

        var root = paths.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root))
            return false;

        try
        {
            return paths.PathsEqual(normalized, root);
        }
        catch
        {
            return false;
        }
    }

    public static bool PathsEqual(this IPath paths, string pathA, string pathB)
    {
        var comparison = paths.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(paths.NormalizePath(pathA), paths.NormalizePath(pathB), comparison);
    }

    public static bool IsDescendantPath(this IPath paths, string potentialDescendant, string potentialAncestor)
    {
        if (string.IsNullOrWhiteSpace(potentialDescendant) || string.IsNullOrWhiteSpace(potentialAncestor))
            return false;

        if (paths.PathsEqual(potentialDescendant, potentialAncestor))
            return false;

        var normalizedAncestor = paths.NormalizePath(potentialAncestor);
        var normalizedDescendant = paths.NormalizePath(potentialDescendant);
        var relative = paths.GetRelativePath(normalizedAncestor, normalizedDescendant);
        return relative != ".."
            && !relative.StartsWith($"..{paths.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{paths.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            && !paths.IsPathRooted(relative);
    }
}

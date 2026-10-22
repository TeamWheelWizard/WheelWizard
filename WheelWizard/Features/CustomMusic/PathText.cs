namespace WheelWizard.CustomMusic;

/// <summary>
/// File-name text helpers for display and matching. They work on the text alone, accept both '/' and '\' on every
/// platform, and never touch the disk.
/// </summary>
public static class PathText
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>Characters Windows does not allow in file names (the strictest platform we write for).</summary>
    private static readonly char[] InvalidFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>"C:\Music\Song.mp3" -> "Song.mp3".</summary>
    public static string FileName(string path) => path[(path.LastIndexOfAny(Separators) + 1)..];

    /// <summary>"C:\Music\Song.mp3" -> "Song".</summary>
    public static string FileNameWithoutExtension(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>"C:\Music\Song.MP3" -> ".mp3" (lower case), or "" when there is none.</summary>
    public static string Extension(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[dot..].ToLowerInvariant() : string.Empty;
    }

    /// <summary>Whether <paramref name="name"/> can be used as a file name on Windows, macOS and Linux.</summary>
    public static bool IsValidFileName(string name) =>
        name.Length > 0
        && name.IndexOfAny(InvalidFileNameChars) < 0
        && name.All(c => c >= ' ')
        && name is not ("." or "..")
        && !name.EndsWith(' ')
        && !name.EndsWith('.');
}

using System.IO.Abstractions;

namespace WheelWizard.Shared.IO;

public static class DriveExtensions
{
    /// <summary>
    /// The drive a folder is on. On Linux and macOS every path's root is "/", but /home or /Volumes/X is often its
    /// own disk, so the deepest mount point that contains the folder is the one that counts.
    /// </summary>
    public static IDriveInfo DriveContaining(this IFileSystem fileSystem, string path)
    {
        var fullPath = fileSystem.Path.GetFullPath(path);
        var root = fileSystem.Path.GetPathRoot(fullPath)!;
        if (fileSystem.Path.DirectorySeparatorChar == '\\')
            return fileSystem.DriveInfo.New(root);

        var withSlash = fullPath.TrimEnd('/') + "/";
        return fileSystem
                .DriveInfo.GetDrives()
                .Select(drive => (Drive: drive, Mount: drive.RootDirectory.FullName.TrimEnd('/') + "/"))
                .Where(entry => withSlash.StartsWith(entry.Mount, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.Mount.Length)
                .Select(entry => entry.Drive)
                .FirstOrDefault() ?? fileSystem.DriveInfo.New(root);
    }
}

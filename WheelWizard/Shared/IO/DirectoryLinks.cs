using System.IO.Abstractions;

namespace WheelWizard.Shared.IO;

public static class DirectoryLinks
{
    public static bool IsSymlink(this IFileSystem fileSystem, string path)
    {
        var file = fileSystem.FileInfo.New(path);
        return file.LinkTarget != null;
    }

    public static void EnsureRelativeSymlink(
        this IFileSystem fileSystem,
        string symlinkPath,
        string targetDirectoryPath,
        bool createTarget = false
    )
    {
        symlinkPath = fileSystem.Path.NormalizePath(symlinkPath);
        targetDirectoryPath = fileSystem.Path.NormalizePath(targetDirectoryPath);
        if (createTarget)
        {
            fileSystem.Directory.CreateDirectory(targetDirectoryPath);
        }

        var symlinkParentDirectoryPath =
            fileSystem.Path.GetDirectoryName(symlinkPath)
            ?? throw new ArgumentException($"Symlink must have a valid parent directory: '{symlinkPath}'");

        fileSystem.Directory.CreateDirectory(symlinkParentDirectoryPath);

        var relativeTargetDirectoryPath = fileSystem.Path.GetRelativePath(symlinkParentDirectoryPath, targetDirectoryPath);

        if (fileSystem.IsSymlink(symlinkPath))
        {
            fileSystem.File.Delete(symlinkPath);
        }
        else if (fileSystem.File.Exists(symlinkPath))
        {
            throw new IOException($"Should have created a symlink at '{symlinkPath}', but a file already existed at this path!");
        }
        else if (fileSystem.Directory.Exists(symlinkPath))
        {
            throw new IOException($"Should have created a symlink at '{symlinkPath}', but a directory already existed at this path!");
        }

        fileSystem.Directory.CreateSymbolicLink(symlinkPath, relativeTargetDirectoryPath);
    }
}

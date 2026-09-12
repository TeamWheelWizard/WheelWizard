using System.IO.Abstractions;

namespace WheelWizard.Shared.IO;

public static class FileSystemOperations
{
    public static bool IsDirectoryEmpty(this IFileSystem fileSystem, string path)
    {
        if (!fileSystem.Directory.Exists(path))
            return true;

        foreach (var _ in fileSystem.Directory.EnumerateFileSystemEntries(path))
            return false;

        return true;
    }

    public static OperationResult<string[]> FindFilesByExtension(this IFileSystem fileSystem, string folderPath, string searchPattern)
    {
        try
        {
            return fileSystem.Directory.GetFiles(folderPath, searchPattern, SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to find files: {ex.Message}", Exception = ex };
        }
    }

    public static OperationResult TryDeleteFile(this IFileSystem fileSystem, string filePath)
    {
        return TryCatch(() => fileSystem.File.Delete(filePath), $"Failed to delete file: {filePath}");
    }

    public static void WriteAllTextCreatingDirectory(this IFileSystem fileSystem, string path, string contents)
    {
        var directoryPath = fileSystem.Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directoryPath) && !fileSystem.Directory.Exists(directoryPath))
            fileSystem.Directory.CreateDirectory(directoryPath);

        fileSystem.File.WriteAllText(path, contents);
    }

    public static OperationResult DeleteDirectoryIfExists(this IFileSystem fileSystem, string directory)
    {
        try
        {
            if (fileSystem.Directory.Exists(directory))
                fileSystem.Directory.Delete(directory, true);

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"{ex.Message}", Exception = ex };
        }
    }

    public static OperationResult EnsureDirectory(this IFileSystem fileSystem, string path)
    {
        return TryCatch(() => fileSystem.Directory.CreateDirectory(path), "Failed to ensure directory exists: " + path);
    }
}

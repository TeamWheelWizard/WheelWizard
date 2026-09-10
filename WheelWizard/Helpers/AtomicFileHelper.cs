using System.IO.Abstractions;

namespace WheelWizard.Helpers;

/// <summary>
/// Helpers for writing files that must never end up half written (Wii save files for example).
/// The new contents are written to a temporary file first, flushed to disk, and only then swapped
/// in place, keeping a backup of the previous file.
/// </summary>
public static class AtomicFileHelper
{
    /// <summary>
    /// The extension appended to the file that is being written before it is swapped in.
    /// </summary>
    public const string TempExtension = ".tmp";

    /// <summary>
    /// The extension appended to the backup of the previous version of the file.
    /// </summary>
    public const string BackupExtension = ".bak";

    /// <summary>
    /// Writes the given bytes to the given path without ever leaving the destination truncated.
    /// The data is written to a temporary file, flushed to disk, and then atomically swapped in.
    /// When the destination already exists, the previous version is kept as a <c>.bak</c> file.
    /// </summary>
    /// <param name="fileSystem">The file system to write with.</param>
    /// <param name="filePath">The final path of the file.</param>
    /// <param name="contents">The complete contents of the file.</param>
    /// <param name="errorMessage">The error message to return when writing fails.</param>
    public static OperationResult WriteAllBytesAtomic(
        this IFileSystem fileSystem,
        string filePath,
        byte[] contents,
        string? errorMessage = null
    )
    {
        return TryCatch(
            () =>
            {
                var directory = fileSystem.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !fileSystem.Directory.Exists(directory))
                    fileSystem.Directory.CreateDirectory(directory);

                var tempPath = filePath + TempExtension;
                var backupPath = filePath + BackupExtension;

                using (var stream = fileSystem.File.Create(tempPath))
                {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(flushToDisk: true);
                }

                // File.Replace requires the destination to already exist, so for a brand new file
                // there is nothing to replace (or to back up) and a plain move is already atomic.
                if (!fileSystem.File.Exists(filePath))
                {
                    fileSystem.File.Move(tempPath, filePath);
                    return;
                }

                fileSystem.File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
            },
            errorMessage ?? $"Failed to write file: {filePath}"
        );
    }
}

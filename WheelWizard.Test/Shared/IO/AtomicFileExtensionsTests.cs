using Testably.Abstractions.Testing;
using WheelWizard.Shared.IO;

namespace WheelWizard.Test.Shared.IO;

public class AtomicFileExtensionsTests
{
    private const string FilePath = "/save/rksys.dat";

    [Fact]
    public void WriteAllBytesAtomic_CreatesFileAndDirectory_WhenFileDoesNotExist()
    {
        var fileSystem = new MockFileSystem();
        var contents = new byte[] { 1, 2, 3, 4 };

        var result = fileSystem.WriteAllBytesAtomic(FilePath, contents);

        Assert.True(result.IsSuccess);
        Assert.Equal(contents, fileSystem.File.ReadAllBytes(FilePath));
        Assert.False(fileSystem.File.Exists(FilePath + AtomicFileExtensions.TempExtension));
        Assert.False(fileSystem.File.Exists(FilePath + AtomicFileExtensions.BackupExtension));
    }

    [Fact]
    public void WriteAllBytesAtomic_ReplacesFileAndKeepsBackup_WhenFileAlreadyExists()
    {
        var fileSystem = new MockFileSystem();
        var oldContents = new byte[] { 9, 9, 9 };
        var newContents = new byte[] { 1, 2, 3, 4 };
        fileSystem.Directory.CreateDirectory("/save");
        fileSystem.File.WriteAllBytes(FilePath, oldContents);

        var result = fileSystem.WriteAllBytesAtomic(FilePath, newContents);

        Assert.True(result.IsSuccess);
        Assert.Equal(newContents, fileSystem.File.ReadAllBytes(FilePath));
        Assert.Equal(oldContents, fileSystem.File.ReadAllBytes(FilePath + AtomicFileExtensions.BackupExtension));
        Assert.False(fileSystem.File.Exists(FilePath + AtomicFileExtensions.TempExtension));
    }

    [Fact]
    public void WriteAllBytesAtomic_CreatesThenReplacesRepeatedly_KeepingPreviousContents()
    {
        var fileSystem = new Testably.Abstractions.RealFileSystem();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"wheelwizard-atomic-{Guid.NewGuid():N}");
        var filePath = Path.Combine(temporaryDirectory, "save.dat");
        try
        {
            for (byte version = 1; version <= 3; version++)
            {
                var result = fileSystem.WriteAllBytesAtomic(filePath, [version]);

                Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Exception?.ToString() ?? result.Error.Message : null);
                Assert.Equal(new byte[] { version }, fileSystem.File.ReadAllBytes(filePath));
                Assert.False(fileSystem.File.Exists(filePath + AtomicFileExtensions.TempExtension));
                if (version > 1)
                    Assert.Equal(
                        new byte[] { (byte)(version - 1) },
                        fileSystem.File.ReadAllBytes(filePath + AtomicFileExtensions.BackupExtension)
                    );
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllBytesAtomic_LeavesOriginalIntact_WhenWriteFails()
    {
        var fileSystem = new MockFileSystem();
        var oldContents = new byte[] { 9, 9, 9 };
        fileSystem.Directory.CreateDirectory("/save");
        fileSystem.File.WriteAllBytes(FilePath, oldContents);

        // A directory on the temp path makes writing the temp file fail before anything is swapped in.
        fileSystem.Directory.CreateDirectory(FilePath + AtomicFileExtensions.TempExtension);

        var result = fileSystem.WriteAllBytesAtomic(FilePath, [1, 2, 3, 4], "Failed to save rksys.dat.");

        Assert.True(result.IsFailure);
        Assert.Equal("Failed to save rksys.dat.", result.Error.Message);
        Assert.Equal(oldContents, fileSystem.File.ReadAllBytes(FilePath));
    }
}

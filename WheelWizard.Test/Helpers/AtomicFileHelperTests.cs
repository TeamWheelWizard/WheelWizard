using Testably.Abstractions.Testing;
using WheelWizard.Helpers;

namespace WheelWizard.Test.Helpers;

public class AtomicFileHelperTests
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
        Assert.False(fileSystem.File.Exists(FilePath + AtomicFileHelper.TempExtension));
        Assert.False(fileSystem.File.Exists(FilePath + AtomicFileHelper.BackupExtension));
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
        Assert.Equal(oldContents, fileSystem.File.ReadAllBytes(FilePath + AtomicFileHelper.BackupExtension));
        Assert.False(fileSystem.File.Exists(FilePath + AtomicFileHelper.TempExtension));
    }

    [Fact]
    public void WriteAllBytesAtomic_LeavesOriginalIntact_WhenWriteFails()
    {
        var fileSystem = new MockFileSystem();
        var oldContents = new byte[] { 9, 9, 9 };
        fileSystem.Directory.CreateDirectory("/save");
        fileSystem.File.WriteAllBytes(FilePath, oldContents);

        // A directory on the temp path makes writing the temp file fail before anything is swapped in.
        fileSystem.Directory.CreateDirectory(FilePath + AtomicFileHelper.TempExtension);

        var result = fileSystem.WriteAllBytesAtomic(FilePath, [1, 2, 3, 4], "Failed to save rksys.dat.");

        Assert.True(result.IsFailure);
        Assert.Equal("Failed to save rksys.dat.", result.Error.Message);
        Assert.Equal(oldContents, fileSystem.File.ReadAllBytes(FilePath));
    }
}

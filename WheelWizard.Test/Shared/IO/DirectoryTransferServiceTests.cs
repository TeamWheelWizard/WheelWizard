using System.IO.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.Shared.IO;

namespace WheelWizard.Test.Shared.IO;

public class DirectoryTransferServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuccessfulTransfer_PreservesNestedFilesAndEmptyDirectories(bool deleteSource)
    {
        var fs = CreateSource();
        var result = new DirectoryTransferService(fs).MoveContents("/source", "/destination", deleteSource);

        Assert.Equal(DirectoryMoveOutcome.Success, result.Outcome);
        Assert.True(result.VerificationSucceeded);
        Assert.Equal("saved-data", fs.File.ReadAllText("/destination/nested/save.dat"));
        Assert.True(fs.Directory.Exists("/destination/empty"));
        Assert.Equal(!deleteSource, fs.Directory.Exists("/source"));
    }

    [Fact]
    public void CopyFailure_KeepsSourceData()
    {
        var fs = CreateSource();
        var io = Substitute.For<IFileSystem>();
        io.Path.Returns(fs.Path);
        io.Directory.Returns(fs.Directory);
        var files = Substitute.For<IFile>();
        files.Exists(Arg.Any<string>()).Returns(call => fs.File.Exists(call.Arg<string>()));
        files.When(file => file.Copy(Arg.Any<string>(), Arg.Any<string>(), true)).Do(_ => throw new IOException("Disk full"));
        io.File.Returns(files);

        var result = new DirectoryTransferService(io).MoveContents("/source", "/destination");

        Assert.Equal(DirectoryMoveOutcome.CopyFailed, result.Outcome);
        Assert.Equal("saved-data", fs.File.ReadAllText("/source/nested/save.dat"));
        Assert.False(result.VerificationAttempted);
    }

    [Fact]
    public void VerificationFailure_KeepsSourceData()
    {
        var fs = CreateSource();
        var io = Substitute.For<IFileSystem>();
        io.Path.Returns(fs.Path);
        io.File.Returns(fs.File);
        io.Directory.Returns(fs.Directory);
        var fileInfoFactory = Substitute.For<IFileInfoFactory>();
        fileInfoFactory.New(Arg.Any<string>()).Returns(call => fs.FileInfo.New(call.Arg<string>()));
        var truncatedFile = Substitute.For<IFileInfo>();
        truncatedFile.Length.Returns(1L);
        fileInfoFactory.New(fs.Path.GetFullPath("/destination/nested/save.dat")).Returns(truncatedFile);
        io.FileInfo.Returns(fileInfoFactory);

        var result = new DirectoryTransferService(io).MoveContents("/source", "/destination");

        Assert.Equal(DirectoryMoveOutcome.VerificationFailed, result.Outcome);
        Assert.Single(result.VerificationFailures);
        Assert.Equal("saved-data", fs.File.ReadAllText("/source/nested/save.dat"));
    }

    [Fact]
    public void SourceDeletionFailure_ReportsRecoverableCopy_AndKeepsBothCopies()
    {
        var fs = CreateSource();
        fs.Intercept.Deleting(FileSystemTypes.Directory, _ => throw new UnauthorizedAccessException("Source locked"));

        var result = new DirectoryTransferService(fs).MoveContents("/source", "/destination");

        Assert.Equal(DirectoryMoveOutcome.SourceDeletionFailed, result.Outcome);
        Assert.True(result.RequiresUserDecision);
        Assert.True(result.CopyCompleted);
        Assert.True(result.VerificationSucceeded);
        Assert.Equal("saved-data", fs.File.ReadAllText("/source/nested/save.dat"));
        Assert.Equal("saved-data", fs.File.ReadAllText("/destination/nested/save.dat"));
    }

    [Fact]
    public void SameDirectory_IsNoOp_AndKeepsContents()
    {
        var fs = CreateSource();

        var result = new DirectoryTransferService(fs).MoveContents("/source", "/source/");

        Assert.Equal(DirectoryMoveOutcome.NoOp, result.Outcome);
        Assert.Equal("saved-data", fs.File.ReadAllText("/source/nested/save.dat"));
    }

    private static MockFileSystem CreateSource()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/source/nested");
        fs.Directory.CreateDirectory("/source/empty");
        fs.File.WriteAllText("/source/nested/save.dat", "saved-data");
        return fs;
    }
}

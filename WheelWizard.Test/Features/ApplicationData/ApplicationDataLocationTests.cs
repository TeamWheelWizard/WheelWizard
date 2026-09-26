using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.ApplicationData;

public class ApplicationDataLocationTests
{
    [Fact]
    public void SuccessfulMove_UpdatesLocationAndPersistence_AfterTransferringData()
    {
        var (fs, store, location) = Create();
        fs.Directory.CreateDirectory(location.DirectoryPath);
        fs.File.WriteAllText(fs.Path.Combine(location.DirectoryPath, "config.json"), "saved settings");
        var original = location.DirectoryPath;

        Assert.True(location.TryMove("/relocated", out var error, out var result));

        Assert.Empty(error);
        Assert.Equal(DirectoryMoveOutcome.Success, result.Outcome);
        Assert.Equal("/relocated", location.DirectoryPath);
        Assert.True(location.IsCustom);
        Assert.Equal("saved settings", fs.File.ReadAllText("/relocated/config.json"));
        Assert.False(fs.Directory.Exists(original));
        store.Received(1).Save("/relocated");
    }

    [Fact]
    public void PersistenceFailure_ReportsWarning_AndKeepsMovedDataActive()
    {
        var (fs, store, location) = Create();
        fs.Directory.CreateDirectory(location.DirectoryPath);
        fs.File.WriteAllText(fs.Path.Combine(location.DirectoryPath, "config.json"), "saved settings");
        store.When(value => value.Save(Arg.Any<string>())).Do(_ => throw new IOException("Read-only settings store"));

        Assert.True(location.TryMove("/relocated", out var warning, out _));

        Assert.Contains("failed to persist", warning);
        Assert.Equal("/relocated", location.DirectoryPath);
        Assert.Equal("saved settings", fs.File.ReadAllText("/relocated/config.json"));
    }

    [Theory]
    [InlineData(DirectoryMoveOutcome.CopyFailed)]
    [InlineData(DirectoryMoveOutcome.VerificationFailed)]
    public void TransferFailure_DoesNotChangeActiveOrPersistedLocation(DirectoryMoveOutcome outcome)
    {
        var transfer = Substitute.For<IDirectoryTransferService>();
        transfer
            .MoveContents(Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<IProgress<double>?>())
            .Returns(new DirectoryMoveContentsResult(outcome, "/appdata/CT-MKWII", "/relocated", true, true, true, false));
        var (_, store, location) = Create(transfer: transfer);
        var original = location.DirectoryPath;

        Assert.False(location.TryMove("/relocated", out var error, out _));

        Assert.NotEmpty(error);
        Assert.Equal(original, location.DirectoryPath);
        Assert.False(location.IsCustom);
        store.DidNotReceiveWithAnyArgs().Save(default);
    }

    [Fact]
    public void UnavailablePersistedLocation_FallsBackToDefault()
    {
        var fs = NewFileSystem();
        fs.Intercept.Creating(FileSystemTypes.Directory, _ => throw new IOException("Unavailable drive"));
        var (_, _, location) = Create(fs, savedLocation: "/unavailable");

        Assert.Equal("/appdata/CT-MKWII", location.DirectoryPath);
        Assert.False(location.IsCustom);
    }

    [Fact]
    public void UnavailableTarget_FailsBeforeTransferOrPersistence()
    {
        var fs = NewFileSystem();
        var transfer = Substitute.For<IDirectoryTransferService>();
        var (_, store, location) = Create(fs, transfer: transfer);
        fs.Intercept.Creating(FileSystemTypes.Directory, _ => throw new IOException("Unavailable drive"));

        Assert.False(location.TryMove("/unavailable", out var error, out _));

        Assert.Contains("Unable to create", error);
        transfer.DidNotReceiveWithAnyArgs().MoveContents(default!, default!, default, default);
        store.DidNotReceiveWithAnyArgs().Save(default);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/appdata")]
    [InlineData("/appdata/CT-MKWII/child")]
    [InlineData("/appdata/CT-MKWII/..cache")]
    [InlineData("/occupied")]
    public void InvalidDestination_IsRejectedBeforeMoving(string destination)
    {
        var (fs, _, location) = Create();
        fs.Directory.CreateDirectory("/occupied");
        fs.File.WriteAllText("/occupied/keep.dat", "keep");

        Assert.False(location.TryMove(destination, out var error, out _));
        Assert.NotEmpty(error);
        Assert.Equal("/appdata/CT-MKWII", location.DirectoryPath);
        Assert.Equal("keep", fs.File.ReadAllText("/occupied/keep.dat"));
    }

    [Fact]
    public void Reset_MovesDataBackToDefault_AndClearsOverride()
    {
        var (fs, store, location) = Create(savedLocation: "/custom");
        fs.File.WriteAllText("/custom/config.json", "saved settings");

        Assert.True(location.TryReset(out var error));

        Assert.Empty(error);
        Assert.False(location.IsCustom);
        Assert.Equal("saved settings", fs.File.ReadAllText("/appdata/CT-MKWII/config.json"));
        store.Received(1).Save(null);
    }

    [Fact]
    public void SourceDeletionFailure_CanRevertToOriginalCopy()
    {
        var transfer = Substitute.For<IDirectoryTransferService>();
        transfer
            .MoveContents(Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<IProgress<double>?>())
            .Returns(
                new DirectoryMoveContentsResult(
                    DirectoryMoveOutcome.SourceDeletionFailed,
                    "/appdata/CT-MKWII",
                    "/relocated",
                    true,
                    true,
                    true,
                    false
                )
            );
        var (fs, store, location) = Create(transfer: transfer);
        fs.Directory.CreateDirectory(location.DirectoryPath);
        fs.File.WriteAllText(fs.Path.Combine(location.DirectoryPath, "config.json"), "saved settings");

        Assert.True(location.TryMove("/relocated", out var warning, out var result));
        Assert.NotEmpty(warning);
        fs.File.WriteAllText("/relocated/config.json", "saved settings");
        Assert.True(location.TryRevertMove(result.SourcePath, result.DestinationPath, out var error));

        Assert.Empty(error);
        Assert.Equal("/appdata/CT-MKWII", location.DirectoryPath);
        Assert.Equal("saved settings", fs.File.ReadAllText("/appdata/CT-MKWII/config.json"));
        Assert.False(fs.Directory.Exists("/relocated"));
        store.Received(1).Save(null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stale settings")]
    public void Revert_RefusesToDeleteCompleteDestination_WhenPreviousCopyIsMissingOrDifferent(string? previousContents)
    {
        var (fs, store, location) = Create(savedLocation: "/relocated");
        fs.Directory.CreateDirectory("/appdata/CT-MKWII");
        fs.File.WriteAllText("/relocated/config.json", "saved settings");
        if (previousContents is not null)
            fs.File.WriteAllText("/appdata/CT-MKWII/config.json", previousContents);

        Assert.False(location.TryRevertMove("/appdata/CT-MKWII", "/relocated", out var error));

        Assert.Contains("complete copy", error);
        Assert.Equal("/relocated", location.DirectoryPath);
        Assert.Equal("saved settings", fs.File.ReadAllText("/relocated/config.json"));
        store.DidNotReceiveWithAnyArgs().Save(default);
    }

    private static MockFileSystem NewFileSystem() => new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));

    private static (MockFileSystem, IApplicationDataLocationStore, ApplicationDataLocation) Create(
        MockFileSystem? fs = null,
        string? savedLocation = null,
        IDirectoryTransferService? transfer = null
    )
    {
        fs ??= NewFileSystem();
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(true);
        environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Returns("/appdata");
        var store = Substitute.For<IApplicationDataLocationStore>();
        store.Load().Returns(savedLocation);
        var directories = new ApplicationDataDirectories(fs, environment);
        return (fs, store, new ApplicationDataLocation(fs, directories, store, transfer ?? new DirectoryTransferService(fs)));
    }
}

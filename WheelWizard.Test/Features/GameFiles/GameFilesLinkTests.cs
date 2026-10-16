using WheelWizard.GameFiles;
using WheelWizard.Shared.IO;

namespace WheelWizard.Test.Features.GameFiles;

/// <summary>
/// Someone put the game folder on another drive with a symlink or junction. The in-memory disk does not follow
/// links, so these run in a temporary folder on the real one. Where this account can't create links (Windows
/// without Developer Mode) there is nothing to test, and they return early.
/// </summary>
public sealed class GameFilesLinkTests : IDisposable
{
    private readonly GameFilesHarness _harness = GameFilesHarness.OnRealDisk();

    public void Dispose() => _harness.Dispose();

    private bool Linked(out string target)
    {
        target = _harness.Elsewhere("Mario Kart Wii");
        _harness.Fs.Directory.CreateDirectory(target);
        return _harness.TryLink(_harness.Game, target);
    }

    private bool IsLink(string path) => _harness.Fs.DirectoryInfo.New(path).LinkTarget is not null;

    [Fact]
    public async Task ExtractsIntoWhereTheLinkPoints()
    {
        if (!Linked(out var target))
            return;

        await _harness.ExtractAsync(_harness.WriteImage());

        Assert.True(IsLink(_harness.Game));
        Assert.True(_harness.Fs.File.Exists(Path.Combine(target, "DATA", "sys", "main.dol")));
        Assert.True(_harness.Service.GetStatus().IsReady);
        // The work folder sits next to the target, on the same drive, and is gone afterwards.
        Assert.False(_harness.Fs.Directory.Exists(target + ".partial"));
        Assert.False(_harness.Fs.Directory.Exists(_harness.Game + ".partial"));
        _harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task ReplacingKeepsTheLinkAndUnrelatedFiles()
    {
        if (!Linked(out var target))
            return;
        await _harness.ExtractAsync(_harness.WriteImage());
        _harness.Fs.File.WriteAllText(Path.Combine(target, "readme.txt"), "mine");

        await _harness.ExtractAsync(_harness.WriteImage("RMCJ01"), replace: true);

        Assert.True(IsLink(_harness.Game));
        Assert.Equal("RMCJ01", _harness.Manifest()!.GameId);
        Assert.Equal("mine", _harness.Fs.File.ReadAllText(Path.Combine(target, "readme.txt")));
        Assert.False(_harness.Fs.Directory.Exists(target + ".replaced"));
        _harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task RecoversAnInterruptedSwapInsideTheTarget()
    {
        if (!Linked(out var target))
            return;
        await _harness.ExtractAsync(_harness.WriteImage());
        _harness.Stage(_harness.WriteImage("RMCE01"), "RMCE01");
        Directory.CreateDirectory(target + ".replaced");
        File.WriteAllText(Path.Combine(target + ".replaced", GameFilesPaths.WorkMarkerFileName), "");
        File.Move(
            Path.Combine(target, GameFilesPaths.ManifestFileName),
            Path.Combine(target + ".replaced", GameFilesPaths.ManifestFileName)
        );

        _harness.Service.RecoverInterruptedWork();

        Assert.True(IsLink(_harness.Game));
        Assert.Equal("RMCE01", _harness.Manifest()!.GameId);
        Assert.False(Directory.Exists(target + ".replaced"));
        Assert.False(Directory.Exists(target + ".partial"));
    }

    [Fact]
    public void DeletingAnUnknownLink_LeavesWhatItPointsTo()
    {
        if (!Linked(out var target))
            return;
        File.WriteAllText(Path.Combine(target, "precious.bin"), "precious");

        Assert.Equal(GameFilesState.UnknownFolder, _harness.Service.GetStatus().State);
        Assert.True(_harness.Service.DeleteUnknown().IsSuccess);

        Assert.False(Directory.Exists(_harness.Game));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(target, "precious.bin")));
    }

    [Fact]
    public void MovingAnUnknownLinkAside_MovesOnlyTheLink()
    {
        if (!Linked(out var target))
            return;
        File.WriteAllText(Path.Combine(target, "precious.bin"), "precious");

        var moved = _harness.Service.MoveUnknownAside();

        Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error.Message : null);
        Assert.True(IsLink(moved.Value));
        Assert.False(Directory.Exists(_harness.Game));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(target, "precious.bin")));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(moved.Value, "precious.bin")));
    }

    [Fact]
    public void DeletingAnUnknownFolder_NeverFollowsALinkInside()
    {
        var documents = _harness.Elsewhere("Documents");
        Directory.CreateDirectory(documents);
        File.WriteAllText(Path.Combine(documents, "thesis.docx"), "years of work");
        Directory.CreateDirectory(_harness.In("DATA"));
        if (!_harness.TryLink(_harness.In("DATA", "docs"), documents))
            return;

        Assert.True(_harness.Service.DeleteUnknown().IsSuccess);

        Assert.False(Directory.Exists(_harness.Game));
        Assert.Equal("years of work", File.ReadAllText(Path.Combine(documents, "thesis.docx")));
    }

    [Fact]
    public async Task ALinkWhoseDriveIsUnplugged_IsNotExtractedInto()
    {
        var target = _harness.Elsewhere("unplugged drive/GAME");
        if (!_harness.TryLink(_harness.Game, target))
            return;

        Assert.Equal(GameFilesState.UnknownFolder, _harness.Service.GetStatus().State);
        Assert.True((await _harness.Service.ExtractAsync(_harness.WriteImage(), true)).IsFailure);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task MovingTheDataFolder_KeepsALinkedGameWhereItIs()
    {
        if (!Linked(out var target))
            return;
        await _harness.ExtractAsync(_harness.WriteImage());
        File.WriteAllText(_harness.Fs.Path.Combine(_harness.DataDir, "config.json"), "{}");
        var newData = _harness.Elsewhere("New Data Folder");

        var moved = new DirectoryTransferService(_harness.Fs).MoveContents(_harness.DataDir, newData);

        Assert.Equal(DirectoryMoveOutcome.Success, moved.Outcome);
        var movedGame = Path.Combine(newData, GameFilesPaths.FolderName);
        Assert.True(Directory.Exists(movedGame));
        Assert.True(File.Exists(Path.Combine(movedGame, "DATA", "sys", "main.dol")));
        // The extracted game stayed on the other drive; only the link (or, where none can be made, a copy) moved.
        Assert.True(File.Exists(Path.Combine(target, "DATA", "sys", "main.dol")));
        Assert.False(Directory.Exists(_harness.DataDir));
        _harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task RealDisk_FullRoundTrip()
    {
        // The same flow as on the mock, on a real drive: extract, edit, restore the code, switch region.
        await _harness.ExtractAsync(_harness.WriteImage());
        File.WriteAllText(_harness.In("DATA", "sys", "main.dol"), "patched");
        File.WriteAllText(_harness.In("DATA", "files", "Scene", "UI", "Title.szs"), "my edit");
        Assert.Equal(GameFilesState.CodeModified, _harness.Service.GetStatus().State);

        Assert.True((await _harness.Service.RestoreCodeAsync()).IsSuccess);
        Assert.True(_harness.Service.GetStatus().IsReady);
        Assert.Equal("my edit", File.ReadAllText(_harness.In("DATA", "files", "Scene", "UI", "Title.szs")));

        await _harness.ExtractAsync(_harness.WriteImage("RMCE01"), replace: true);
        Assert.Equal("RMCE01", _harness.Manifest()!.GameId);
        Assert.Single(Directory.GetFileSystemEntries(_harness.DataDir));
        _harness.AssertImagesUntouched();
    }
}

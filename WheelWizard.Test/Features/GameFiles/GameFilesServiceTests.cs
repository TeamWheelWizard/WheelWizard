using System.Text.Json;
using Testably.Abstractions.Testing;
using WheelWizard.GameFiles;
using WheelWizard.GameFiles.Domain;
using WheelWizard.Models.Enums;
using WheelWizard.Settings.Types;

namespace WheelWizard.Test.Features.GameFiles;

public class GameFilesServiceTests
{
    #region Status

    [Fact]
    public void Status_NothingPicked_IsLeftToTheOnboarding()
    {
        var harness = new GameFilesHarness();

        Assert.Equal(GameFilesState.NoImage, harness.Service.GetStatus().State);
    }

    [Fact]
    public void Status_AnImageFromAnOlderVersion_NeedsExtraction()
    {
        var harness = new GameFilesHarness();
        harness.GameLocation.Set(harness.WriteImage(), skipSave: true);

        var status = harness.Service.GetStatus();

        Assert.Equal(GameFilesState.NeedsExtraction, status.State);
        Assert.False(harness.Fs.Directory.Exists(harness.Game));
    }

    [Fact]
    public void Status_AnImageThatIsGone_IsMissing()
    {
        var harness = new GameFilesHarness();
        harness.GameLocation.Set("/media/usb/mkw.iso", skipSave: true);

        Assert.Equal(GameFilesState.ImageMissing, harness.Service.GetStatus().State);
    }

    [Fact]
    public void Status_AnImageOnAnUnpluggedDrive_IsMissingNotUnset()
    {
        var harness = new GameFilesHarness();
        ((IWhWzSetting)harness.GameLocation).RememberUnavailable(JsonSerializer.SerializeToElement("/media/usb/mkw.iso"));

        var status = harness.Service.GetStatus();

        Assert.Equal(GameFilesState.ImageMissing, status.State);
        Assert.Equal("/media/usb/mkw.iso", status.ImagePath);
    }

    [Fact]
    public void Status_AFolderWithoutManifest_IsUnknown()
    {
        var harness = new GameFilesHarness();
        harness.Fs.Directory.CreateDirectory(harness.In("DATA", "sys"));
        harness.Fs.File.WriteAllText(harness.In("DATA", "sys", "main.dol"), "someone's own extraction");

        Assert.Equal(GameFilesState.UnknownFolder, harness.Service.GetStatus().State);
    }

    [Fact]
    public void Status_AFileWhereTheFolderGoes_IsUnknown()
    {
        var harness = new GameFilesHarness();
        harness.Fs.File.WriteAllText(harness.Game, "not a folder");

        Assert.Equal(GameFilesState.UnknownFolder, harness.Service.GetStatus().State);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"SchemaVersion":99,"GameId":"RMCP01","MainDolSha256":"a","StaticRelSha256":"b"}""")]
    [InlineData("""{"SchemaVersion":1,"GameId":"","MainDolSha256":"a","StaticRelSha256":"b"}""")]
    public void Status_AManifestThatCantBeTrusted_IsUnknown(string manifest)
    {
        var harness = new GameFilesHarness();
        harness.Fs.Directory.CreateDirectory(harness.Game);
        harness.Fs.File.WriteAllText(harness.Paths.ManifestPath, manifest);

        Assert.Equal(GameFilesState.UnknownFolder, harness.Service.GetStatus().State);
    }

    [Fact]
    public async Task Status_EditingAnyOtherFile_StaysReady()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());

        harness.Fs.File.WriteAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs"), "my custom title screen");
        harness.Fs.File.Delete(harness.In("DATA", "files", "Race", "Course", "beginner_course.szs"));
        harness.Fs.File.WriteAllText(harness.In("DATA", "files", "my-new-asset.brres"), "new");
        harness.Fs.File.WriteAllText(harness.In("DATA", "sys", "bi2.bin"), "edited");

        Assert.Equal(GameFilesState.Ready, harness.Service.GetStatus().State);
    }

    [Theory]
    [InlineData("DATA/sys/main.dol", false)]
    [InlineData("DATA/files/rel/StaticR.rel", false)]
    [InlineData("DATA/sys/main.dol", true)]
    [InlineData("DATA/files/rel/StaticR.rel", true)]
    public async Task Status_EditingOrDeletingTheGameCode_IsModified(string file, bool delete)
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        var path = harness.In(file.Split('/'));

        if (delete)
            harness.Fs.File.Delete(path);
        else
            harness.Fs.File.WriteAllText(path, "patched");

        Assert.Equal(GameFilesState.CodeModified, harness.Service.GetStatus().State);
    }

    [Fact]
    public async Task Status_ADeletedImage_DoesNotMatterOnceExtracted()
    {
        var harness = new GameFilesHarness();
        var image = harness.WriteImage();
        await harness.ExtractAsync(image);

        harness.Fs.File.Delete(image);

        Assert.Equal(GameFilesState.Ready, harness.Service.GetStatus().State);
        Assert.Equal(MarioKartWiiEnums.Regions.Europe, harness.Service.GetRegion());
    }

    #endregion

    #region Extracting

    [Theory]
    [InlineData(SimulationMode.Windows)]
    [InlineData(SimulationMode.Linux)]
    [InlineData(SimulationMode.MacOS)]
    public async Task Extract_MakesTheGameReadyAndRemembersTheImage(SimulationMode mode)
    {
        var harness = new GameFilesHarness(mode);
        var image = harness.WriteImage(new SyntheticWiiDisc(), "Mario Kart Wii (Europe) (En,Fr,De,Es,It).rvz");

        await harness.ExtractAsync(image);

        var status = harness.Service.GetStatus();
        Assert.Equal(GameFilesState.Ready, status.State);
        Assert.Equal(image, harness.GameLocation.Get());
        Assert.Equal("RMCP01", status.Manifest!.GameId);
        Assert.Equal(harness.Fs.Path.GetFullPath(image), status.Manifest.SourcePath);
        Assert.True(harness.Fs.File.Exists(harness.In("DATA", "sys", "main.dol")));
        Assert.True(harness.Fs.File.Exists(harness.In("disc", "header.bin")));
        // Nothing of the work in progress is left next to it.
        Assert.Equal([harness.Game], harness.Siblings().Where(entry => entry.Contains("GAME")).ToArray());
        harness.AssertImagesUntouched();
    }

    [Theory]
    [InlineData("iso")]
    [InlineData("wbfs")]
    [InlineData("gcz")]
    [InlineData("ciso")]
    [InlineData("rvz")]
    public async Task Extract_EveryFormatThePickerOffers(string format)
    {
        var harness = new GameFilesHarness();
        var image = harness.WriteImage(new SyntheticWiiDisc(), $"mkw.{format}", format);

        await harness.ExtractAsync(image);

        Assert.True(harness.Service.GetStatus().IsReady);
        harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task Extract_AnotherGame_ChangesNothing()
    {
        var harness = new GameFilesHarness();
        var image = harness.WriteImage(new SyntheticWiiDisc { GameId = "RSPE01", InternalName = "Wii Sports" }, "sports.iso", "iso");

        var result = await harness.Service.ExtractAsync(image, replaceExisting: true);

        Assert.True(result.IsFailure);
        Assert.False(harness.Fs.Directory.Exists(harness.Game));
        Assert.False(harness.Fs.Directory.Exists(harness.Paths.StagingPath));
        Assert.Equal("", harness.GameLocation.Get());
        harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task Extract_AMissingImage_ChangesNothing()
    {
        var harness = new GameFilesHarness();

        Assert.True((await harness.Service.ExtractAsync("/nowhere/mkw.iso", false)).IsFailure);
        Assert.False(harness.Fs.Directory.Exists(harness.Game));
    }

    [Fact]
    public async Task Extract_AnImageKeptInsideTheGameFolder_IsRefused()
    {
        var harness = new GameFilesHarness();
        var image = harness.WriteImage();
        await harness.ExtractAsync(image);
        var inside = harness.In("DATA", "files", "backup.rvz");
        harness.Fs.File.Copy(image, inside);
        var other = harness.WriteImage("RMCE01");
        harness.Fs.File.Copy(other, harness.In("DATA", "files", "usa.rvz"));

        Assert.True(harness.Service.Plan(inside).IsFailure);
        Assert.True((await harness.Service.ExtractAsync(harness.In("DATA", "files", "usa.rvz"), true)).IsFailure);
        Assert.True(harness.Fs.File.Exists(inside));
        Assert.Equal("RMCP01", harness.Manifest()!.GameId);
    }

    [Fact]
    public async Task Extract_TheSameGameAgain_KeepsEveryChange()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        harness.Fs.File.WriteAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs"), "my edit");
        var moved = harness.WriteImage(new SyntheticWiiDisc(), "moved copy.iso", "iso");

        Assert.Equal(GameChangeKind.SameGame, harness.Service.Plan(moved).Value.Kind);
        await harness.ExtractAsync(moved);

        Assert.Equal("my edit", harness.Fs.File.ReadAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs")));
        Assert.Equal(moved, harness.GameLocation.Get());
        Assert.Equal(harness.Fs.Path.GetFullPath(moved), harness.Manifest()!.SourcePath);
        harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task Extract_ADifferentGame_NeedsConfirmation()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        harness.Fs.File.WriteAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs"), "my edit");
        var usa = harness.WriteImage("RMCE01");

        Assert.Equal(GameChangeKind.Replace, harness.Service.Plan(usa).Value.Kind);
        Assert.True((await harness.Service.ExtractAsync(usa, replaceExisting: false)).IsFailure);

        Assert.Equal("RMCP01", harness.Manifest()!.GameId);
        Assert.Equal("my edit", harness.Fs.File.ReadAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs")));
    }

    [Fact]
    public async Task Extract_ADifferentRevision_IsADifferentGame()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        var revised = harness.WriteImage(new SyntheticWiiDisc { Revision = 1 }, "rev1.rvz");

        Assert.Equal(GameChangeKind.Replace, harness.Service.Plan(revised).Value.Kind);
    }

    [Fact]
    public async Task Extract_ReplacingConfirmed_SwapsTheGameButKeepsUnrelatedFiles()
    {
        var harness = new GameFilesHarness();
        var europe = harness.WriteImage();
        await harness.ExtractAsync(europe);
        harness.Fs.File.WriteAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs"), "my edit");
        harness.Fs.File.WriteAllText(harness.In("notes.txt"), "the user's own file");
        var usa = harness.WriteImage(new SyntheticWiiDisc { GameId = "RMCE01", MainDol = SyntheticWiiDisc.Dol(0x300, 5) }, "usa.rvz");

        await harness.ExtractAsync(usa, replace: true);

        Assert.Equal("RMCE01", harness.Manifest()!.GameId);
        Assert.True(harness.Service.GetStatus().IsReady);
        Assert.NotEqual("my edit", harness.Fs.File.ReadAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs")));
        Assert.Equal("the user's own file", harness.Fs.File.ReadAllText(harness.In("notes.txt")));
        Assert.Equal(usa, harness.GameLocation.Get());
        Assert.Equal([harness.Game], harness.Siblings().Where(entry => entry.Contains("GAME")).ToArray());
        harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task Extract_SomethingUnknownInTheWay_IsNeverTouched()
    {
        var harness = new GameFilesHarness();
        harness.Fs.Directory.CreateDirectory(harness.In("my stuff"));
        harness.Fs.File.WriteAllText(harness.In("my stuff", "keep.txt"), "precious");
        var image = harness.WriteImage();

        Assert.Equal(GameChangeKind.UnknownFolder, harness.Service.Plan(image).Value.Kind);
        Assert.True((await harness.Service.ExtractAsync(image, replaceExisting: true)).IsFailure);

        Assert.Equal("precious", harness.Fs.File.ReadAllText(harness.In("my stuff", "keep.txt")));
        Assert.False(harness.Fs.File.Exists(harness.Paths.ManifestPath));
    }

    [Theory]
    [InlineData("Dolphin")]
    [InlineData("dolphin-emu")]
    [InlineData("WiiCompiled")]
    [InlineData("RetroRewind")]
    public async Task Extract_WhileTheGameRuns_WaitsWithoutTouchingAnything(string process)
    {
        var harness = new GameFilesHarness();
        var europe = harness.WriteImage();
        await harness.ExtractAsync(europe);
        var usa = harness.WriteImage("RMCE01");
        harness.SetRunning(process);

        Assert.True(harness.Service.IsGameRunning());
        Assert.True((await harness.Service.ExtractAsync(usa, replaceExisting: true)).IsFailure);
        Assert.Equal("RMCP01", harness.Manifest()!.GameId);

        harness.SetRunning();
        await harness.ExtractAsync(usa, replace: true);
        Assert.Equal("RMCE01", harness.Manifest()!.GameId);
    }

    [Fact]
    public void CloseRunningGame_ClosesDolphinAndWiiCompiled()
    {
        var harness = new GameFilesHarness();

        harness.Service.CloseRunningGame();

        harness.Processes.Received().KillByName("Dolphin");
        harness.Processes.Received().KillByName("WiiCompiled");
        harness.Processes.Received().KillByName("RetroRewind");
    }

    [Fact]
    public async Task Extract_NotEnoughSpace_SaysSoAndWritesNothing()
    {
        var harness = new GameFilesHarness(configure: fs => fs.WithDrive(drive => drive.SetTotalSize(64L * 1024 * 1024)));
        var image = harness.WriteImage();

        var result = await harness.Service.ExtractAsync(image, false);

        Assert.True(result.IsFailure);
        Assert.Contains("GB", result.Error.Message);
        Assert.False(harness.Fs.Directory.Exists(harness.Game));
        Assert.False(harness.Fs.Directory.Exists(harness.Paths.StagingPath));
        harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task Extract_Cancelled_LeavesTheCurrentGameAsItWas()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        harness.Fs.File.WriteAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs"), "my edit");
        var usa = harness.WriteImage("RMCE01");
        using var cancellation = new CancellationTokenSource();
        var progress = new SyncProgress<GameExtractionProgress>(value =>
        {
            if (value.Fraction > 0)
                cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Service.ExtractAsync(usa, true, progress, cancellation.Token)
        );

        Assert.Equal("RMCP01", harness.Manifest()!.GameId);
        Assert.Equal("my edit", harness.Fs.File.ReadAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs")));
        Assert.False(harness.Fs.Directory.Exists(harness.Paths.StagingPath));
        Assert.True(harness.Service.GetStatus().IsReady);
    }

    [Fact]
    public async Task Extract_ADamagedImage_LeavesTheCurrentGameAsItWas()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        var full = new SyntheticWiiDisc { GameId = "RMCE01" }.BuildIso();
        // The header and file table are there, the file data is not.
        var damaged = harness.Fs.Path.Combine(harness.GamesDir, "damaged.iso");
        harness.Fs.File.WriteAllBytes(damaged, full[..0x7A000]);

        var result = await harness.Service.ExtractAsync(damaged, replaceExisting: true);

        Assert.True(result.IsFailure);
        Assert.Equal("RMCP01", harness.Manifest()!.GameId);
        Assert.True(harness.Service.GetStatus().IsReady);
        Assert.False(harness.Fs.Directory.Exists(harness.Paths.StagingPath));
    }

    [Fact]
    public async Task Extract_ReportsProgressWithAMessage()
    {
        var harness = new GameFilesHarness();
        var reports = new List<GameExtractionProgress>();

        Assert.True(
            (
                await harness.Service.ExtractAsync(harness.WriteImage(), false, new SyncProgress<GameExtractionProgress>(reports.Add))
            ).IsSuccess
        );

        Assert.NotEmpty(reports);
        Assert.All(reports, report => Assert.False(string.IsNullOrWhiteSpace(report.Message)));
        Assert.Equal(1, reports[^1].Fraction);
    }

    #endregion

    #region Folder in the way

    [Fact]
    public async Task EmptyFolder_IsJustAPlaceToExtractInto()
    {
        var harness = new GameFilesHarness();
        harness.Fs.Directory.CreateDirectory(harness.Game);

        Assert.Equal(GameFilesState.NoImage, harness.Service.GetStatus().State);
        await harness.ExtractAsync(harness.WriteImage());
        Assert.True(harness.Service.GetStatus().IsReady);
    }

    [Fact]
    public async Task LinkToTheRootOfADrive_IsNeverExtractedInto()
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        fs.Directory.CreateDirectory("/games");
        fs.File.WriteAllBytes("/games/mkw.rvz", new SyntheticWiiDisc().Build("rvz"));
        var paths = Substitute.For<IGameFilesPaths>();
        paths.RootPath.Returns("/data/GAME");
        paths.ResolvedRootPath.Returns("/");
        paths.ManifestPath.Returns("/data/GAME/" + GameFilesPaths.ManifestFileName);
        paths.StagingPath.Returns("/.partial");
        paths.ReplacedPath.Returns("/.replaced");
        var settings = Substitute.For<WheelWizard.Settings.ISettingsManager>();
        settings.GAME_LOCATION.Returns(new WhWzSetting<string>("GameLocation", ""));
        var service = new GameFilesService(
            paths,
            new DiscExtractor(fs),
            settings,
            fs,
            Substitute.For<WheelWizard.Shared.Processes.IProcessLauncher>(),
            Substitute.For<WheelWizard.Dolphin.Paths.IDolphinPaths>(),
            new WheelWizard.DiscImages.DiscImageService(fs),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GameFilesService>.Instance
        );

        var result = await service.ExtractAsync("/games/mkw.rvz", true);

        Assert.True(result.IsFailure);
        Assert.False(fs.Directory.Exists("/DATA"));
        Assert.False(fs.Directory.Exists("/.partial"));
    }

    #endregion

    #region Unknown folder

    [Fact]
    public void Unknown_MoveAside_KeepsEverythingUnderANewName()
    {
        var harness = new GameFilesHarness();
        harness.Fs.Directory.CreateDirectory(harness.In("DATA"));
        harness.Fs.File.WriteAllText(harness.In("DATA", "keep.bin"), "keep");
        harness.GameLocation.Set(harness.WriteImage(), skipSave: true);

        var first = harness.Service.MoveUnknownAside();
        harness.Fs.Directory.CreateDirectory(harness.Game);
        var second = harness.Service.MoveUnknownAside();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value, second.Value);
        Assert.Equal("keep", harness.Fs.File.ReadAllText(harness.Fs.Path.Combine(first.Value, "DATA", "keep.bin")));
        Assert.Equal(GameFilesState.NeedsExtraction, harness.Service.GetStatus().State);
    }

    [Fact]
    public void Unknown_DeleteRemovesIt()
    {
        var harness = new GameFilesHarness();
        harness.Fs.Directory.CreateDirectory(harness.In("DATA", "sys"));
        harness.Fs.File.WriteAllText(harness.In("DATA", "sys", "main.dol"), "x");
        harness.Fs.FileInfo.New(harness.In("DATA", "sys", "main.dol")).IsReadOnly = true;

        Assert.True(harness.Service.DeleteUnknown().IsSuccess);
        Assert.False(harness.Fs.Directory.Exists(harness.Game));
    }

    [Fact]
    public async Task Unknown_ActionsRefuseAFolderWheelWizardOwns()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());

        Assert.True(harness.Service.DeleteUnknown().IsFailure);
        Assert.True(harness.Service.MoveUnknownAside().IsFailure);
        Assert.True(harness.Service.GetStatus().IsReady);
    }

    #endregion

    #region Interrupted work

    [Fact]
    public void Recover_AnUnfinishedExtraction_IsRemoved()
    {
        var harness = new GameFilesHarness();
        var staging = harness.Paths.StagingPath;
        harness.Fs.Directory.CreateDirectory(harness.Fs.Path.Combine(staging, "DATA", "files"));
        harness.Fs.File.WriteAllText(harness.Fs.Path.Combine(staging, GameFilesPaths.WorkMarkerFileName), "");
        harness.Fs.File.WriteAllText(harness.Fs.Path.Combine(staging, "DATA", "files", "half.szs"), "half");

        harness.Service.RecoverInterruptedWork();

        Assert.False(harness.Fs.Directory.Exists(staging));
    }

    [Fact]
    public async Task Recover_NeverDeletesAFolderWithoutItsMarker()
    {
        var harness = new GameFilesHarness();
        harness.Fs.Directory.CreateDirectory(harness.Paths.StagingPath);
        harness.Fs.File.WriteAllText(harness.Fs.Path.Combine(harness.Paths.StagingPath, "mine.txt"), "mine");
        harness.Fs.Directory.CreateDirectory(harness.Paths.ReplacedPath);
        harness.Fs.File.WriteAllText(harness.Fs.Path.Combine(harness.Paths.ReplacedPath, "mine.txt"), "mine");

        harness.Service.RecoverInterruptedWork();
        var result = await harness.Service.ExtractAsync(harness.WriteImage(), false);

        Assert.True(result.IsFailure);
        Assert.Equal("mine", harness.Fs.File.ReadAllText(harness.Fs.Path.Combine(harness.Paths.StagingPath, "mine.txt")));
        Assert.Equal("mine", harness.Fs.File.ReadAllText(harness.Fs.Path.Combine(harness.Paths.ReplacedPath, "mine.txt")));
    }

    [Fact]
    public async Task Recover_ACrashRightAfterTheOldManifestLeft_FinishesTheSwap()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        var usa = harness.WriteImage("RMCE01");
        harness.Stage(usa, "RMCE01");
        // The swap moved the old manifest out, then the power went.
        harness.Fs.Directory.CreateDirectory(harness.Paths.ReplacedPath);
        harness.Fs.File.WriteAllText(harness.Fs.Path.Combine(harness.Paths.ReplacedPath, GameFilesPaths.WorkMarkerFileName), "");
        harness.Fs.File.Move(
            harness.Paths.ManifestPath,
            harness.Fs.Path.Combine(harness.Paths.ReplacedPath, GameFilesPaths.ManifestFileName)
        );
        Assert.Equal(GameFilesState.UnknownFolder, harness.Service.GetStatus().State);

        harness.Service.RecoverInterruptedWork();

        Assert.Equal("RMCE01", harness.Manifest()!.GameId);
        Assert.True(harness.Service.GetStatus().IsReady);
        Assert.Equal(usa, harness.GameLocation.Get());
        Assert.False(harness.Fs.Directory.Exists(harness.Paths.StagingPath));
        Assert.False(harness.Fs.Directory.Exists(harness.Paths.ReplacedPath));
    }

    [Fact]
    public async Task Recover_ACrashHalfwayThroughTheMoves_FinishesTheSwap()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        harness.Stage(harness.WriteImage("RMCE01"), "RMCE01");
        var replaced = harness.Paths.ReplacedPath;
        harness.Fs.Directory.CreateDirectory(replaced);
        harness.Fs.File.WriteAllText(harness.Fs.Path.Combine(replaced, GameFilesPaths.WorkMarkerFileName), "");
        harness.Fs.File.Move(harness.Paths.ManifestPath, harness.Fs.Path.Combine(replaced, GameFilesPaths.ManifestFileName));
        harness.Fs.Directory.Move(harness.In("DATA"), harness.Fs.Path.Combine(replaced, "DATA"));
        harness.Fs.Directory.Move(harness.Fs.Path.Combine(harness.Paths.StagingPath, "DATA"), harness.In("DATA"));

        harness.Service.RecoverInterruptedWork();

        Assert.Equal("RMCE01", harness.Manifest()!.GameId);
        Assert.True(harness.Service.GetStatus().IsReady);
        Assert.True(harness.Fs.File.Exists(harness.In("disc", "header.bin")));
        Assert.Equal("RMCE01", System.Text.Encoding.ASCII.GetString(harness.Fs.File.ReadAllBytes(harness.In("disc", "header.bin"))[..6]));
        Assert.False(harness.Fs.Directory.Exists(replaced));
    }

    [Fact]
    public async Task Recover_ACrashBeforeCleanup_RemovesOnlyTheLeftovers()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        var replaced = harness.Paths.ReplacedPath;
        harness.Fs.Directory.CreateDirectory(harness.Fs.Path.Combine(replaced, "DATA"));
        harness.Fs.File.WriteAllText(harness.Fs.Path.Combine(replaced, GameFilesPaths.WorkMarkerFileName), "");

        harness.Service.RecoverInterruptedWork();

        Assert.False(harness.Fs.Directory.Exists(replaced));
        Assert.True(harness.Service.GetStatus().IsReady);
    }

    #endregion

    #region Restoring the game code

    [Fact]
    public async Task Restore_PutsBackOnlyTheGameCode()
    {
        var harness = new GameFilesHarness();
        var disc = new SyntheticWiiDisc();
        var image = harness.WriteImage(disc);
        await harness.ExtractAsync(image);
        harness.Fs.File.WriteAllText(harness.In("DATA", "sys", "main.dol"), "patched");
        harness.Fs.File.WriteAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs"), "my edit");
        Assert.Equal(GameFilesState.CodeModified, harness.Service.GetStatus().State);

        var result = await harness.Service.RestoreCodeAsync();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(harness.Service.GetStatus().IsReady);
        Assert.Equal(disc.MainDol, harness.Fs.File.ReadAllBytes(harness.In("DATA", "sys", "main.dol")));
        Assert.Equal("my edit", harness.Fs.File.ReadAllText(harness.In("DATA", "files", "Scene", "UI", "Title.szs")));
        harness.AssertImagesUntouched();
    }

    [Fact]
    public async Task Restore_FromAnotherRegion_IsRefused()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        harness.Fs.File.WriteAllText(harness.In("DATA", "sys", "main.dol"), "patched");

        var result = await harness.Service.RestoreCodeAsync(harness.WriteImage("RMCK01"));

        Assert.True(result.IsFailure);
        Assert.Equal("patched", harness.Fs.File.ReadAllText(harness.In("DATA", "sys", "main.dol")));
    }

    [Fact]
    public async Task Restore_WithTheImageGone_AsksForOne()
    {
        var harness = new GameFilesHarness();
        var image = harness.WriteImage();
        await harness.ExtractAsync(image);
        harness.Fs.File.WriteAllText(harness.In("DATA", "sys", "main.dol"), "patched");
        harness.Fs.File.Delete(image);

        Assert.True((await harness.Service.RestoreCodeAsync()).IsFailure);

        var copy = harness.WriteImage(new SyntheticWiiDisc(), "found it again.iso", "iso");
        Assert.True((await harness.Service.RestoreCodeAsync(copy)).IsSuccess);
        Assert.True(harness.Service.GetStatus().IsReady);
        Assert.Equal(copy, harness.GameLocation.Get());
    }

    [Fact]
    public async Task Restore_WhileTheGameRuns_ChangesNothing()
    {
        var harness = new GameFilesHarness();
        await harness.ExtractAsync(harness.WriteImage());
        harness.Fs.File.WriteAllText(harness.In("DATA", "sys", "main.dol"), "patched");
        harness.SetRunning("Dolphin");

        Assert.True((await harness.Service.RestoreCodeAsync()).IsFailure);
        Assert.Equal("patched", harness.Fs.File.ReadAllText(harness.In("DATA", "sys", "main.dol")));
    }

    #endregion

    [Fact]
    public void Region_ComesFromTheImageBeforeExtracting()
    {
        var harness = new GameFilesHarness();
        harness.GameLocation.Set(harness.WriteImage("RMCJ01"), skipSave: true);

        Assert.Equal(MarioKartWiiEnums.Regions.Japan, harness.Service.GetRegion());
    }

    [Fact]
    public void Region_IsNoneWithoutAGame()
    {
        Assert.Equal(MarioKartWiiEnums.Regions.None, new GameFilesHarness().Service.GetRegion());
    }
}

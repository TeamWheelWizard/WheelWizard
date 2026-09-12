using System.Collections.ObjectModel;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Features.Patches;
using WheelWizard.Models.Mods;
using WheelWizard.Mods;
using WheelWizard.Shared.Processes;

namespace WheelWizard.Test.Features.Mods;

public class ModOperationTests
{
    [Fact]
    public async Task LaunchPreparation_PreservesPriorityAndArchives_AndRemovesStaleFiles()
    {
        var fixture = new Fixture();
        var high = new Mod
        {
            Title = "High",
            Priority = 1,
            IsEnabled = true,
        };
        var low = new Mod
        {
            Title = "Low",
            Priority = 2,
            IsEnabled = true,
        };
        var disabled = new Mod { Title = "Disabled", Priority = 0 };
        fixture.Write("High/shared.bin", "high");
        fixture.Write("Low/shared.bin", "low");
        fixture.Write("Disabled/disabled.bin", "disabled");
        fixture.Write("High/High.ini", "metadata");
        fixture.Write("High/99.custom.tag.szs", "high archive");
        fixture.Write("Low/custom.tag.szs", "low archive");
        var target = fixture.Write("Target/stale.bin", "stale");
        target = Path.GetDirectoryName(target)!;
        var manager = Substitute.For<IModManager>();
        manager.Mods.Returns(new ObservableCollection<Mod> { high, low, disabled });
        var service = new ModsLaunchService(manager, fixture.Fs, fixture.Paths);
        var updates = new List<ModOperationProgress>();

        Assert.True((await service.PrepareModsForLaunch(target, progress: new CaptureProgress(updates.Add))).IsSuccess);

        Assert.Equal("high", fixture.Fs.File.ReadAllText(Path.Combine(target, "shared.bin")));
        Assert.Equal("high archive", fixture.Fs.File.ReadAllText(Path.Combine(target, "1.custom.tag.szs")));
        Assert.Equal("low archive", fixture.Fs.File.ReadAllText(Path.Combine(target, "2.custom.tag.szs")));
        Assert.Equal(3, fixture.Fs.Directory.GetFiles(target).Length);
        Assert.Equal(100, updates[^1].Percent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoEnabledMods_ClearsOnlyWhenRequested(bool clear)
    {
        var fixture = new Fixture();
        var file = fixture.Write("Target/external.bin", "external");
        var manager = Substitute.For<IModManager>();
        manager.Mods.Returns(new ObservableCollection<Mod>());
        var service = new ModsLaunchService(manager, fixture.Fs, fixture.Paths);
        var target = Path.GetDirectoryName(file)!;
        Assert.True(service.ShouldAskToClearTargetFolder(target));

        Assert.True((await service.PrepareModsForLaunch(target, clear)).IsSuccess);

        Assert.Equal(!clear, fixture.Fs.File.Exists(file));
    }

    [Fact]
    public async Task ArchiveInstallation_RoundTripsMetadataAndNestedContentsWithoutUi()
    {
        var fixture = new Fixture();
        var archive = fixture.Archive("nested/file.bin", "payload");
        var service = new ModInstallationService(fixture.Fs, fixture.Paths);
        var updates = new List<ModOperationProgress>();

        var result = await service.InstallModFromFileAsync(archive, "Imported", 7, "Author", 123, new CaptureProgress(updates.Add));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "payload",
            fixture.Fs.File.ReadAllText(Path.Combine(fixture.Paths.GetModDirectoryPath("Imported"), "nested", "file.bin"))
        );
        var loaded = await service.LoadModsAsync();
        Assert.True(loaded.IsSuccess);
        var mod = Assert.Single(loaded.Value);
        Assert.Equal("Imported", mod.Title);
        Assert.Equal("Author", mod.Author);
        Assert.Equal(123, mod.ModID);
        Assert.Equal(7, mod.Priority);
        Assert.True(mod.IsEnabled);
        Assert.Equal(100, updates[^1].Percent);
    }

    [Fact]
    public async Task ArchiveTraversal_ReturnsFailureWithoutWritingOutsideModDirectory()
    {
        var fixture = new Fixture();
        var archive = fixture.Archive("../escaped.bin", "payload");
        var service = new ModInstallationService(fixture.Fs, fixture.Paths);

        var result = await service.InstallModFromFileAsync(archive, "Imported", 1);

        Assert.True(result.IsFailure);
        Assert.False(fixture.Fs.File.Exists(Path.Combine(fixture.Paths.RootFolderPath, "escaped.bin")));
        Assert.False(fixture.Fs.File.Exists(Path.Combine(fixture.Paths.GetModDirectoryPath("Imported"), "Imported.ini")));
    }

    [Fact]
    public async Task ImportLooseFiles_ReportsBothPhases_AndDeletesItsTemporaryArchive()
    {
        var fixture = new Fixture();
        var source = fixture.Write("Source/payload.bin", "payload");
        var service = new ModInstallationService(fixture.Fs, fixture.Paths);
        var manager = new ModManager(
            service,
            Substitute.For<IModPatchConversionService>(),
            fixture.Fs,
            fixture.Paths,
            Substitute.For<IProcessLauncher>(),
            NullLogger<ModManager>.Instance
        );
        var updates = new List<ModOperationProgress>();

        Assert.True((await manager.ImportModFilesAsync([source], "Imported", new CaptureProgress(updates.Add))).IsSuccess);

        Assert.Equal("Imported", Assert.Single(manager.Mods).Title);
        Assert.Contains(updates, update => update.Stage == ModOperationStage.Preparing);
        Assert.Contains(updates, update => update.Stage == ModOperationStage.Extracting);
        Assert.Empty(fixture.Fs.Directory.GetFiles(Path.GetTempPath(), "WheelWizard-*.zip"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conversion_CancellationPreservesOriginalFiles(bool cancelDuringReplacement)
    {
        var fixture = new Fixture();
        var original = fixture.Write("Original/unknown.szs", "unchanged");
        var metadata = fixture.Write("Original/Original.ini", "metadata");
        var service = new ModPatchConversionService(
            Substitute.For<ISzsPatchConverter>(),
            NullLogger<ModPatchConversionService>.Instance,
            fixture.Fs,
            fixture.Paths,
            Substitute.For<IGameBaselineStore>()
        );
        using var cancellation = new CancellationTokenSource();
        if (!cancelDuringReplacement)
            cancellation.Cancel();
        var progress = new CaptureProgress(update =>
        {
            if (update.Stage == ModOperationStage.Applying)
                cancellation.Cancel();
        });

        var result = await service.ConvertToPatchesAsync(new Mod { Title = "Original" }, cancellation.Token, progress);

        Assert.True(result.IsFailure);
        Assert.Equal("unchanged", fixture.Fs.File.ReadAllText(original));
        Assert.Equal("metadata", fixture.Fs.File.ReadAllText(metadata));
        Assert.Empty(fixture.Fs.Directory.GetDirectories(fixture.Paths.RootFolderPath, "*.patch-conversion-backup-*"));
    }

    private sealed class CaptureProgress(Action<ModOperationProgress> report) : IProgress<ModOperationProgress>
    {
        public void Report(ModOperationProgress value) => report(value);
    }

    private sealed class Fixture
    {
        public MockFileSystem Fs { get; } = new();
        public IModPaths Paths { get; }

        public Fixture()
        {
            var location = Substitute.For<IApplicationDataLocation>();
            location.DirectoryPath.Returns(Path.Combine(Path.GetTempPath(), "mod-tests", Guid.NewGuid().ToString("N")));
            Paths = new ModPaths(location, Fs);
            Fs.Directory.CreateDirectory(Paths.RootFolderPath);
            Fs.Directory.CreateDirectory(Path.GetTempPath());
        }

        public string Write(string relative, string contents)
        {
            var file = Path.Combine(Paths.RootFolderPath, relative);
            Fs.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Fs.File.WriteAllText(file, contents);
            return file;
        }

        public string Archive(string entryName, string contents)
        {
            var path = Path.Combine(Path.GetTempPath(), "input.zip");
            using var stream = Fs.File.Create(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
            writer.Write(contents);
            return path;
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Library;

namespace WheelWizard.Test.Features.CustomMusic;

public class CustomMusicLibraryTests
{
    private readonly MockFileSystem _fs = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
    private readonly CustomMusicLibrary _library;

    public CustomMusicLibraryTests()
    {
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns("/appdata");
        _library = new CustomMusicLibrary(_fs, location, NullLogger<CustomMusicLibrary>.Instance);
    }

    [Fact]
    public void SavedEntriesComeBack_AndEditedOrBrokenOnesAreDropped()
    {
        var (good, goodFolder) = _library.CreateStorage();
        _fs.File.WriteAllText(_fs.Path.Combine(goodFolder, "normal.brstm"), "x");
        var (escape, escapeFolder) = _library.CreateStorage();
        _fs.File.WriteAllText(_fs.Path.Combine(escapeFolder, "normal.brstm"), "x");
        var data = new CustomMusicLibraryData
        {
            VolumeOffsetDb = 3,
            Entries =
            [
                Entry("game:a", good),
                Entry("game:b", escape) with { NormalFile = "../../../outside.brstm" },
                Entry("game:c", "../../etc"),
                Entry("game:d", Guid.NewGuid().ToString("N")), // converted files are gone
            ],
        };

        Assert.True(_library.Save(data).IsSuccess);
        var loaded = _library.Load();

        Assert.Equal(["game:a"], loaded.Entries.Select(entry => entry.Key));
        Assert.Equal(3, loaded.VolumeOffsetDb);
    }

    [Fact]
    public void UnusedConversionsAreCleanedUp()
    {
        var (keep, keepFolder) = _library.CreateStorage();
        var (_, dropFolder) = _library.CreateStorage();

        _library.DeleteUnused([Entry("game:a", keep)]);

        Assert.True(_fs.Directory.Exists(keepFolder));
        Assert.False(_fs.Directory.Exists(dropFolder));
    }

    [Fact]
    public void MusicPacksAreExtractedFlat_AndRemovedWhenNoSongUsesThem()
    {
        _fs.Directory.CreateDirectory("/downloads");
        using (var zip = new System.IO.Compression.ZipArchive(_fs.File.Create("/downloads/My Pack.zip"), System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "sound/strm/n_Circuit32_n.brstm", "sound/strm/n_Circuit32_f.brstm", "../evil.mp3", "readme.txt", "covers/art.png" })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("data");
            }
        }

        var extracted = _library.ExtractPack("/downloads/My Pack.zip");

        Assert.True(extracted.IsSuccess);
        Assert.Equal(["evil.mp3", "n_Circuit32_f.brstm", "n_Circuit32_n.brstm"], extracted.Value.Select(path => _fs.Path.GetFileName(path)).Order());
        var folder = _fs.Path.GetDirectoryName(extracted.Value[0])!;
        Assert.StartsWith("/appdata/CustomMusic/Packs/My Pack-", folder);

        var (storage, storageFolder) = _library.CreateStorage();
        _library.DeleteUnused([Entry("game:a", storage) with { SourcePath = extracted.Value[0] }]);
        Assert.True(_fs.Directory.Exists(folder));
        _library.DeleteUnused([]);
        Assert.False(_fs.Directory.Exists(folder));
        Assert.False(_fs.Directory.Exists(storageFolder));
    }

    [Theory]
    [InlineData(-18.0, 0, -18.0, 0, 0.0)] // matched, no volume bytes
    [InlineData(-12.0, 0, -18.0, 0, 6.0)] // louder file
    [InlineData(-18.0, 0x7F, -18.0, 0x40, 5.95)] // same file level, byte raised from 0x40 to full
    public void LoudnessVersusOriginal_CountsFileLevelAndVolumeBytes(double output, int volume, double reference, int referenceVolume, double expected)
    {
        var entry = Entry("game:a", "abc") with
        {
            OutputLufs = output,
            PulsarVolume = (byte)volume,
            ReferenceLufs = reference,
            ReferenceVolume = (byte)referenceVolume,
        };

        Assert.Equal(expected, entry.LoudnessVersusOriginalDb!.Value, 1);
    }

    [Fact]
    public void LoudnessVersusOriginal_IsUnknownForOlderEntries() => Assert.Null(Entry("game:a", "abc").LoudnessVersusOriginalDb);

    private static CustomMusicEntry Entry(string key, string storageId) =>
        new()
        {
            Key = key,
            StorageId = storageId,
            SourcePath = "/music/song.mp3",
            FinalLapStyle = FinalLapStyle.Faster,
        };
}

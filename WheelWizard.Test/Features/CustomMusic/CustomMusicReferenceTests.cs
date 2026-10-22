using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.CustomDistributions;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Audio.Dsp;
using WheelWizard.CustomMusic.Audio.Formats;
using WheelWizard.CustomMusic.Catalog;
using WheelWizard.CustomMusic.Library;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;

namespace WheelWizard.Test.Features.CustomMusic;

public class CustomMusicReferenceTests
{
    private const string GamePath = "/game.iso";

    private readonly MockFileSystem _fs = new(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
    private readonly IGameDiscMusicReader _disc = Substitute.For<IGameDiscMusicReader>();
    private readonly CustomMusicLibrary _library;
    private readonly MusicReferenceService _references;

    public CustomMusicReferenceTests()
    {
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns("/appdata");
        _library = new CustomMusicLibrary(_fs, location, NullLogger<CustomMusicLibrary>.Instance);

        var settings = Substitute.For<ISettingsManager>();
        var gameLocation = new WhWzSetting<string>("GameLocation", GamePath);
        settings.GAME_LOCATION.Returns(gameLocation);
        settings.Get(gameLocation).Returns(_ => (string)gameLocation.Get());
        _fs.File.WriteAllText(GamePath, "disc");

        _references = new MusicReferenceService(_fs, _library, _disc, settings, NullLogger<MusicReferenceService>.Instance);
    }

    [Fact]
    public async Task RetroRewindFile_GivesItsOwnLoudnessAndVolumeByte()
    {
        _fs.Directory.CreateDirectory("/rr");
        _fs.File.WriteAllBytes("/rr/rMC1_n.brstm", Brstm(amplitude: 8000, volume: 0x50));
        var slot = Slot() with { RetroRewindFilePath = "/rr/rMC1_n.brstm" };

        var reference = await _references.GetReferenceAsync(slot);

        Assert.Equal(MusicReferenceSource.RetroRewind, reference.Source);
        Assert.Equal(0x50, reference.PulsarVolume);
        Assert.True(reference.Loops ?? false);
        // The file's own level: the volume byte is copied to replacements instead of being folded into the number.
        var expected = LoudnessMeter.Measure(BrstmReader.Decode(Brstm(8000, 0x50))).IntegratedLufs;
        Assert.Equal(expected, reference.Lufs, 2);
    }

    [Fact]
    public async Task MultiTrackOriginals_AreMeasuredAsTheGamePlaysThem()
    {
        _fs.Directory.CreateDirectory("/rr");
        var channel = Enumerable.Range(0, 32000 * 2).Select(i => (short)(6000 * Math.Sin(2 * Math.PI * 440 * i / 32000))).ToArray();
        _fs.File.WriteAllBytes("/rr/double.brstm", BrstmWriter.Write(new BrstmStream { Channels = [channel, channel, channel, channel], Looping = true }));
        _fs.File.WriteAllBytes("/rr/single.brstm", BrstmWriter.Write(new BrstmStream { Channels = [channel, channel], Looping = true }));

        var single = await _references.GetReferenceAsync(Slot() with { RetroRewindFilePath = "/rr/single.brstm" });
        var race = await _references.GetReferenceAsync(Slot() with { Key = "rr:double", RetroRewindFilePath = "/rr/double.brstm" });
        var menu = await _references.GetReferenceAsync(Slot() with { Key = "menu:title_bg", RetroRewindFilePath = "/rr/double.brstm" });

        Assert.Equal(single.Lufs, race.Lufs, 1); // only the first track is heard in a race
        Assert.InRange(menu.Lufs - single.Lufs, 5.9, 6.1); // menu music plays every track at once
    }

    [Fact]
    public async Task OriginalOnTheDisc_IsMeasuredAndRemembered()
    {
        _disc.ReadStreamFile(GamePath, "n_Circuit32_n.brstm").Returns(Brstm(amplitude: 4000, volume: 0));
        var slot = Slot() with { GameFileName = "n_Circuit32_n.brstm" };

        var first = await _references.GetReferenceAsync(slot);
        var second = await _references.GetReferenceAsync(slot);

        Assert.Equal(MusicReferenceSource.GameDisc, first.Source);
        Assert.Equal(0, first.PulsarVolume);
        Assert.Equal(first, second);
        _disc.Received(1).ReadStreamFile(GamePath, "n_Circuit32_n.brstm"); // the second answer came from the cache
    }

    [Fact]
    public async Task UnreadableDisc_FallsBackToAnEstimateAndSaysWhy()
    {
        _disc.ReadStreamFile(GamePath, "n_Circuit32_n.brstm").Returns(new OperationError { Message = "keys are missing" });
        var slot = Slot() with { GameFileName = "n_Circuit32_n.brstm" };

        var reference = await _references.GetReferenceAsync(slot);

        Assert.Equal(MusicReferenceSource.Estimate, reference.Source);
        Assert.Equal(MusicLoudness.DefaultTargetLufs, reference.Lufs);
        Assert.Equal("keys are missing", reference.Problem);
    }

    [Fact]
    public async Task MusicWithoutAnOriginalFile_IsAnEstimateWithoutAProblem()
    {
        var reference = await _references.GetReferenceAsync(Slot());

        Assert.Equal(MusicReferenceSource.Estimate, reference.Source);
        Assert.Null(reference.Problem);
    }

    [Fact]
    public async Task SeveralTracksWithTheSameName_AreToldApartByFileName()
    {
        var config = new PulsarConfigBuilder()
            .Cup("3DS Cup")
            .Track(0x08, 0x08, "3DS")
            .Track(0x01, 0x01, "3DS")
            .Track(0x02, 0x02, "3DS")
            .Track(0x03, 0x03, "Unique Track")
            .File(0, "dsA")
            .File(1, "dsB")
            .File(2, "dsC")
            .File(3, "uniq")
            .Build();
        _fs.Directory.CreateDirectory("/wheelwizard/RetroRewind6/Binaries");
        _fs.File.WriteAllBytes("/wheelwizard/RetroRewind6/Binaries/ConfigRT.pul", config);
        var paths = Substitute.For<ICustomDistributionPaths>();
        paths.RootFolderPath.Returns("/wheelwizard");
        paths.RetroRewindFolderPath.Returns("/wheelwizard/RetroRewind6");
        paths.XmlFilePath.Returns("/wheelwizard/riivolution/RetroRewind6.xml");

        var catalog = await new MusicCatalogService(_fs, paths, NullLogger<MusicCatalogService>.Instance).LoadAsync();

        var names = catalog.Slots.Where(slot => slot.Category == MusicCategory.RetroRewindTrack).Select(slot => slot.Name).ToList();
        Assert.Equal(["3DS (dsA)", "3DS (dsB)", "3DS (dsC)", "Unique Track"], names.Take(4).ToList());
        Assert.All(catalog.Slots.Where(slot => slot.Category == MusicCategory.RetroRewindTrack).Take(4), slot => Assert.Contains(" · ", slot.Detail));
    }

    private static MusicSlot Slot() =>
        new()
        {
            Key = "test",
            Name = "Test",
            Category = MusicCategory.Course,
            Group = "Test",
            Order = 1,
            FileNames = ["test_n.brstm"],
        };

    private static byte[] Brstm(short amplitude, byte volume)
    {
        var channel = Enumerable.Range(0, 32000 * 2).Select(i => (short)(amplitude * Math.Sin(2 * Math.PI * 440 * i / 32000))).ToArray();
        return BrstmWriter.Write(
            new BrstmStream
            {
                Channels = [channel, channel],
                Looping = true,
                LoopStart = 0,
                PulsarVolume = volume == 0 ? null : volume,
            }
        );
    }
}

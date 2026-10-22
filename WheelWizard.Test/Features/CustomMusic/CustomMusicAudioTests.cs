using System.Buffers.Binary;
using System.Text;
using Testably.Abstractions.Testing;
using WheelWizard.CustomMusic;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Audio.Codecs;
using WheelWizard.CustomMusic.Audio.Dsp;
using WheelWizard.CustomMusic.Audio.Formats;
using WheelWizard.CustomMusic.Audio.Tags;
using WheelWizard.CustomMusic.Catalog;

namespace WheelWizard.Test.Features.CustomMusic;

public class CustomMusicAudioTests
{
    private const int Rate = 32000;

    [Fact]
    public void DspAdpcm_RoundTrip_KeepsTheSignal()
    {
        var pcm = ToPcm16(Tone(Rate * 2, Rate, [(440, 0.3), (1320, 0.1)]));

        var encoded = DspAdpcm.Encode(pcm);

        Assert.Equal(DspAdpcm.SampleCountToByteCount(pcm.Length), encoded.Adpcm.Length);
        Assert.Equal(encoded.Decoded, DspAdpcm.Decode(encoded.Adpcm, encoded.Coefficients, pcm.Length));
        Assert.True(Snr(pcm, encoded.Decoded) > 25, "4-bit ADPCM should keep tones well above 25 dB SNR");
    }

    [Fact]
    public void DspAdpcm_SilenceEncodes()
    {
        var encoded = DspAdpcm.Encode(new short[10_000]);

        Assert.All(encoded.Decoded, sample => Assert.Equal(0, sample));
    }

    [Theory]
    [InlineData(14, 8)]
    [InlineData(15, 10)]
    [InlineData(28, 16)]
    [InlineData(14336, 8192)]
    public void DspAdpcm_SampleCountToByteCount(int samples, int bytes) => Assert.Equal(bytes, DspAdpcm.SampleCountToByteCount(samples));

    [Fact]
    public void Brstm_StoresLoopAndStreamInfo_AndDecodesToTheEncodedAudio()
    {
        var length = Brstm.SamplesPerBlock * 3 + 1234; // several blocks plus a short, padded last block
        var left = ToPcm16(Tone(length, Rate, [(330, 0.25)]));
        var right = ToPcm16(Tone(length, Rate, [(495, 0.25)]));
        var loopStart = Brstm.SamplesPerBlock * 2;

        var file = BrstmWriter.Write(new BrstmStream { Channels = [left, right], SampleRate = Rate, Looping = true, LoopStart = loopStart });
        var info = BrstmReader.ReadInfo(file);
        var decoded = BrstmReader.Decode(file);

        Assert.Equal("RSTM", Encoding.ASCII.GetString(file, 0, 4));
        Assert.Equal(file.Length, BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(8)));
        Assert.True(info.Looping);
        Assert.Equal(loopStart, info.LoopStart);
        Assert.Equal(length, info.SampleCount);
        Assert.Equal(2, info.ChannelCount);
        Assert.Equal(1, info.TrackCount);
        Assert.Equal(Rate, info.SampleRate);
        Assert.Equal(new LoopRegion(loopStart, length), decoded.Loop);
        var expectedRight = DspAdpcm.Encode(right).Decoded;
        for (var i = 0; i < length; i += 97)
            Assert.Equal(expectedRight[i], (short)Math.Round(decoded.Channels[1][i] * 32768));
    }

    [Fact]
    public void Brstm_PulsarVolumeByteIsWrittenAndRead()
    {
        var file = BrstmWriter.Write(new BrstmStream { Channels = [new short[5000]], PulsarVolume = 0x40 });

        Assert.Equal(0x40, file[Brstm.PulsarVolumeOffset]);
        Assert.InRange(BrstmReader.ReadInfo(file).PulsarGain, 0.50, 0.51);
    }

    [Fact]
    public void Brstm_TracksAreReadFromTheHeader()
    {
        var tone = ToPcm16(Tone(32000, 32000, [(440, 0.2)]));
        var file = BrstmWriter.Write(new BrstmStream { Channels = [tone, tone, tone, tone, tone] });

        var tracks = BrstmReader.ReadTracks(file);

        Assert.Equal(3, tracks.Count);
        Assert.Equal([0, 1], tracks[0].Channels);
        Assert.Equal([2, 3], tracks[1].Channels);
        Assert.Equal([4], tracks[2].Channels);
        Assert.All(tracks, track => Assert.Equal(0x7F, track.Volume));
    }

    [Fact]
    public void Brstm_IsHeardTheWayTheGamePlaysItsTracks()
    {
        // Two stereo tracks with the same music, as custom track BRSTMs often have.
        var song = ToPcm16(Tone(32000 * 3, 32000, [(440, 0.2), (660, 0.1)]));
        var file = BrstmWriter.Write(new BrstmStream { Channels = [song, song, song, song] });

        var race = BrstmReader.DecodeAsPlayed(file, allTracks: false);
        var menu = BrstmReader.DecodeAsPlayed(file, allTracks: true);
        var everyChannel = LoudnessMeter.Measure(BrstmReader.Decode(file)).IntegratedLufs;
        var firstTrack = LoudnessMeter.Measure(race).IntegratedLufs;
        var allTracks = LoudnessMeter.Measure(menu).IntegratedLufs;

        Assert.Equal(2, race.ChannelCount);
        Assert.Equal(2, menu.ChannelCount);
        // Counting all four channels as separate speakers overstates a race file by 3 dB...
        Assert.InRange(everyChannel - firstTrack, 2.9, 3.1);
        // ...and understates menu music, whose identical tracks add up to twice the amplitude (+6 dB).
        Assert.InRange(allTracks - firstTrack, 5.9, 6.1);
    }

    [Fact]
    public void LoudnessMeter_MeasuresAMinus20DbfsStereoSineAsMinus20Lufs()
    {
        // BS.1770: a 1 kHz sine at -20 dBFS (peak) in both channels reads -20 LUFS.
        var tone = Tone(48000 * 5, 48000, [(1000, 0.1)]);

        var result = LoudnessMeter.Measure([tone, tone], 48000);

        Assert.InRange(result.IntegratedLufs, -20.1, -19.9);
        Assert.InRange(result.SamplePeak, 0.099, 0.101);
    }

    [Fact]
    public void LoudnessMeter_ReportsSilence() => Assert.True(LoudnessMeter.Measure([new float[48000 * 2]], 48000).IsSilent);

    [Fact]
    public void Resampler_KeepsTonesAndRemovesWhatCannotBeRepresented()
    {
        var kept = Resampler.Resample(Tone(44100 * 2, 44100, [(5000, 0.5)]), 44100, Rate);
        var aliased = Resampler.Resample(Tone(44100 * 2, 44100, [(19000, 0.5)]), 44100, Rate);

        Assert.InRange(kept.Length, Rate * 2 - 2, Rate * 2);
        Assert.InRange(Rms(kept, 2000), 0.35, 0.36); // 0.5 / sqrt(2)
        Assert.True(Rms(aliased, 2000) < 0.001, "content above 16 kHz must not fold back");
    }

    [Fact]
    public void Converter_MatchesTheTargetLoudness_AndLoopsSeamlessly()
    {
        var song = Song(44100, seconds: 20);

        var result = MusicConverter.Convert(song, new ConversionOptions { TargetLufs = -18, CreateFinalLap = true });
        var info = BrstmReader.ReadInfo(result.Normal);
        var audio = BrstmReader.Decode(result.Normal);

        Assert.InRange(LoudnessMeter.Measure(audio).IntegratedLufs, -18.6, -17.4);
        Assert.True(info.Looping);
        Assert.Equal(0, info.LoopStart % Brstm.SamplesPerBlock);
        Assert.Equal(Rate, info.SampleRate);
        Assert.Equal(2, info.ChannelCount);
        // Jumping from the last sample back to the loop start must be as smooth as the music itself.
        foreach (var channel in audio.Channels)
            Assert.True(Math.Abs(channel[info.LoopStart] - channel[info.SampleCount - 1]) < 0.05);
    }

    [Theory]
    [InlineData(FinalLapStyle.Faster, MusicLoudness.FinalLapPitchSpeed)]
    [InlineData(FinalLapStyle.FasterKeepPitch, MusicLoudness.FinalLapTempoSpeed)]
    [InlineData(FinalLapStyle.Unchanged, 1.0)]
    public void Converter_FinalLapVersionPlaysFaster(FinalLapStyle style, double speed)
    {
        var result = MusicConverter.Convert(Song(32000, seconds: 12), new ConversionOptions { CreateFinalLap = true, FinalLapStyle = style });
        var normal = BrstmReader.ReadInfo(result.Normal);
        var final = BrstmReader.ReadInfo(result.FinalLap!);

        var ratio = (normal.SampleCount - normal.LoopStart) / (double)(final.SampleCount - final.LoopStart);
        Assert.InRange(ratio, speed - 0.01, speed + 0.01);
        Assert.True(final.Looping);
    }

    [Fact]
    public void Converter_JinglesPlayOnce()
    {
        var result = MusicConverter.Convert(Song(48000, seconds: 4), new ConversionOptions { Loop = false });

        Assert.False(BrstmReader.ReadInfo(result.Normal).Looping);
        Assert.Null(result.FinalLap);
    }

    [Fact]
    public void Converter_KeepsLoopPointsFromTheFile()
    {
        var song = Song(32000, seconds: 10);
        song.Loop = new LoopRegion(32000 * 3, 32000 * 9);

        var result = MusicConverter.Convert(song, new ConversionOptions());
        var info = BrstmReader.ReadInfo(result.Normal);

        Assert.True(result.Report.Loop == LoopKind.FromFile);
        // The loop start moves forward onto a block boundary and the end moves with it: same loop length.
        Assert.Equal(32000 * 6, info.SampleCount - info.LoopStart);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x40)]
    [InlineData(0xFF)]
    public void Converter_WritesTheRequestedVolumeByteToBothFiles(int volume)
    {
        var result = MusicConverter.Convert(Song(32000, seconds: 8), new ConversionOptions { CreateFinalLap = true, PulsarVolume = (byte)volume });

        var expected = (byte)Math.Min(volume, 0x7F); // Retro Rewind ignores anything above 0x7F
        Assert.Equal(expected, BrstmReader.ReadInfo(result.Normal).PulsarVolume);
        Assert.Equal(expected, BrstmReader.ReadInfo(result.FinalLap!).PulsarVolume);
    }

    [Fact]
    public void Converter_VolumeOffsetMakesTheSongLouder()
    {
        var song = Song(32000, seconds: 10);
        var plain = MusicConverter.Convert(song, new ConversionOptions { TargetLufs = -20 });
        var louder = MusicConverter.Convert(song, new ConversionOptions { TargetLufs = -20, VolumeOffsetDb = 4 });

        var difference = LoudnessMeter.Measure(BrstmReader.Decode(louder.Normal)).IntegratedLufs - LoudnessMeter.Measure(BrstmReader.Decode(plain.Normal)).IntegratedLufs;
        Assert.InRange(difference, 3.6, 4.4);
    }

    [Fact]
    public void Converter_RaisesTheVolumeByteBeforeLimiting()
    {
        // Asking for more level than the peaks allow: with a volume byte to work with, the byte goes up instead
        // of the limiter squashing the song.
        var withByte = MusicConverter.Convert(Song(32000, seconds: 10), new ConversionOptions { TargetLufs = -8, PulsarVolume = 0x40 });
        var withoutByte = MusicConverter.Convert(Song(32000, seconds: 10), new ConversionOptions { TargetLufs = -8 });

        Assert.True(withByte.Report.PulsarVolume > 0x40);
        Assert.Equal(withByte.Report.PulsarVolume, BrstmReader.ReadInfo(withByte.Normal).PulsarVolume);
        Assert.True(withByte.Report.LimiterReductionDb < withoutByte.Report.LimiterReductionDb);
        Assert.Equal(0, withoutByte.Report.PulsarVolume);
        // In game: file loudness plus the byte's gain lands on the target (-8 LUFS at byte 0x40).
        var inGame = withByte.Report.OutputLufs + MusicLoudness.VolumeByteDb(withByte.Report.PulsarVolume) - MusicLoudness.VolumeByteDb(0x40);
        Assert.InRange(inGame, -8.8, -7.2);
    }

    [Fact]
    public void Converter_ReportsTheLoudnessItReached()
    {
        var result = MusicConverter.Convert(Song(32000, seconds: 10), new ConversionOptions { TargetLufs = -18 });

        Assert.InRange(result.Report.OutputLufs, -18.6, -17.4);
        var decoded = LoudnessMeter.Measure(BrstmReader.Decode(result.Normal)).IntegratedLufs;
        Assert.True(Math.Abs(result.Report.OutputLufs - decoded) < 0.2, $"report {result.Report.OutputLufs:0.00} vs file {decoded:0.00}");
    }

    [Fact]
    public void Converter_IgnoresLoopPointsShorterThanASecond()
    {
        var song = Song(32000, seconds: 10);
        song.Loop = new LoopRegion(32000 * 10 - 500, 32000 * 10); // would buzz on a 15 ms fragment

        var result = MusicConverter.Convert(song, new ConversionOptions());

        Assert.False(result.Report.Loop == LoopKind.FromFile);
        var info = BrstmReader.ReadInfo(result.Normal);
        Assert.True(info.SampleCount - info.LoopStart > 32000 * 5);
    }

    [Fact]
    public void Converter_SurvivesDamagedSamples()
    {
        var song = Song(32000, seconds: 5);
        song.Channels[0][1000] = float.NaN;
        song.Channels[1][2000] = float.PositiveInfinity;

        var result = MusicConverter.Convert(song, new ConversionOptions { TargetLufs = -18 });

        Assert.InRange(result.Report.OutputLufs, -18.6, -17.4);
    }

    [Fact]
    public void Converter_RejectsSongsTooShortToLoop() =>
        Assert.Throws<InvalidDataException>(() => MusicConverter.Convert(Song(32000, seconds: 0.5), new ConversionOptions()));

    [Fact]
    public void Converter_ConvertsShortJingles()
    {
        var result = MusicConverter.Convert(Song(32000, seconds: 0.3), new ConversionOptions { Loop = false, TargetLufs = -18 });

        Assert.False(BrstmReader.ReadInfo(result.Normal).Looping);
        Assert.InRange(result.Report.OutputLufs, -19, -17);
    }

    [Fact]
    public void LoudnessMeter_MeasuresClipsShorterThanOneBlock()
    {
        // 200 ms is shorter than BS.1770's 400 ms block; the ungated mean is used instead of "silent".
        var tone = Tone(48000 / 5, 48000, [(1000, 0.1)]);

        var result = LoudnessMeter.Measure([tone, tone], 48000);

        Assert.InRange(result.IntegratedLufs, -20.3, -19.7);
    }

    [Fact]
    public void PeakLimiter_HoldsPeaksUnderTheCeilingAndLeavesQuietPassagesAlone()
    {
        var length = 32000 * 3;
        var left = new float[length];
        for (var i = 0; i < length; i++)
            left[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * i / 32000));
        for (var i = 40000; i < 40100; i++)
            left[i] = 1.5f; // a burst well over full scale
        var buffer = new AudioBuffer([left, (float[])left.Clone()], 32000);

        var reduction = PeakLimiter.Apply(buffer, ceilingDb: -1);

        var ceiling = (float)Math.Pow(10, -1 / 20.0);
        Assert.All(buffer.Channels, channel => Assert.True(channel.Max(sample => Math.Abs(sample)) <= ceiling + 1e-4f));
        Assert.InRange(reduction, 4.0, 5.0); // 1.5 down to 0.891
        // Far from the burst the signal is untouched.
        Assert.Equal(0.2 * Math.Sin(2 * Math.PI * 440 * 10 / 32000), buffer.Channels[0][10], 5);
        Assert.Equal(0.2 * Math.Sin(2 * Math.PI * 440 * 90000 / 32000), buffer.Channels[0][90000], 4);
    }

    [Fact]
    public void FfmpegMetadata_IsParsedWithEscapesAndStreamFallback()
    {
        var metadata = new AudioMetadata { Artist = "Already known" };

        FfmpegReader.ApplyFfmetadata(";FFMETADATA1\ntitle=A \\= B\\;C\nARTIST=Ignored\n[STREAM]\ntitle=Stream title\nalbum=Stream Album\n", metadata);

        Assert.Equal("A = B;C", metadata.Title);
        Assert.Equal("Already known", metadata.Artist);
        Assert.Equal("Stream Album", metadata.Album);
    }

    [Fact]
    public void FolderArt_IsOnlyBorrowedByAlbumTracks()
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        fs.Directory.CreateDirectory("/music");
        fs.File.WriteAllBytes("/music/cover.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6]);
        fs.File.WriteAllBytes("/music/My Song.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]);

        Assert.Null(FolderArtFinder.Find(fs, "/music/Download.webm", allowAlbumArt: false));
        Assert.Equal("image/jpeg", FolderArtFinder.Find(fs, "/music/Track 01.mp3", allowAlbumArt: true)?.MimeType);
        Assert.Equal("image/png", FolderArtFinder.Find(fs, "/music/My Song.webm", allowAlbumArt: false)?.MimeType);
    }

    [Theory]
    [InlineData(@"C:\Music\Song.MP3", "Song.MP3", "Song", ".mp3")]
    [InlineData("/home/me/my.song.flac", "my.song.flac", "my.song", ".flac")]
    [InlineData("noextension", "noextension", "noextension", "")]
    [InlineData("/music/.hidden", ".hidden", ".hidden", ".hidden")]
    public void PathText_SplitsFileNamesOnEveryPlatform(string path, string name, string baseName, string extension)
    {
        Assert.Equal(name, PathText.FileName(path));
        Assert.Equal(baseName, PathText.FileNameWithoutExtension(path));
        Assert.Equal(extension, PathText.Extension(path));
    }

    [Theory]
    [InlineData("rMC1", true)]
    [InlineData("3DS Toad Circuit", true)]
    [InlineData("a/b", false)]
    [InlineData("what?", false)]
    [InlineData("trailing.", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    public void PathText_KnowsValidFileNames(string name, bool valid) => Assert.Equal(valid, PathText.IsValidFileName(name));

    [Fact]
    public void LoopFinder_FindsTheRepeatAndSplicesItWithoutAJump()
    {
        // Intro, then verse and chorus twice, then an outro: the loop should jump from the second verse-chorus
        // back to the first, exactly one cycle (32 s) long.
        var rate = 32000;
        float[] song = [.. Section(1, 6), .. Section(2, 16), .. Section(3, 16), .. Section(2, 16), .. Section(3, 16), .. Section(4, 8)];
        var audio = new AudioBuffer([song, (float[])song.Clone()], rate);

        var loop = LoopFinder.Find(audio);

        Assert.NotNull(loop);
        Assert.InRange((loop.Value.End - loop.Value.Start) / (double)rate, 31.99, 32.01);
        Assert.True(loop.Value.Similarity > LoopFinder.MinSimilarity);
        var looped = LoopFinder.Apply(audio, loop.Value);
        var channel = looped.Channels[0];
        Assert.Equal(new LoopRegion(loop.Value.Start, loop.Value.End), looped.Loop);
        Assert.True(Math.Abs(channel[^1] - channel[loop.Value.Start - 1]) < 0.01, "the last sample leads into the loop start");

        float[] Section(int id, double seconds)
        {
            var random = new Random(id);
            var notes = Enumerable.Range(0, 32).Select(_ => 110 * Math.Pow(2, random.Next(24) / 12.0)).ToArray();
            return Enumerable
                .Range(0, (int)(seconds * rate))
                .Select(i =>
                {
                    var t = i / (double)rate;
                    var note = notes[(int)(t * 2) % notes.Length];
                    return (float)(0.25 * Math.Exp(-(t % 0.5) * 4) * Math.Sin(2 * Math.PI * note * t));
                })
                .ToArray();
        }
    }

    [Fact]
    public void LoopFinder_GivesUpOnMusicThatNeverRepeats()
    {
        var random = new Random(3);
        var noise = Enumerable.Range(0, 32000 * 40).Select(i => (float)((random.NextDouble() - 0.5) * (0.2 + 0.15 * Math.Sin(i / 9000.0)) * Math.Sin(i / (200.0 + i / 20000.0)))).ToArray();

        Assert.Null(LoopFinder.Find(new AudioBuffer([noise], 32000)));
    }

    [Theory]
    [InlineData("SNES Rainbow Road", "rainbow road")]
    [InlineData("Rainbow Road", "rainbow road")]
    [InlineData("Wii U Rainbow Road v2", "rainbow road")]
    [InlineData("3DS Rainbow Road (r3RR)", "rainbow road")]
    [InlineData("Luigi Circuit / Mario Circuit", "luigi circuit")]
    [InlineData("N64 Luigi Raceway (Night)", "luigi raceway")]
    [InlineData("DS", "ds")]
    public void TrackNames_GroupTheSameCourseAcrossConsoles(string name, string family) => Assert.Equal(family, TrackNames.FamilyKey(name));

    [Fact]
    public void Converter_RejectsSilence() =>
        Assert.Throws<InvalidDataException>(() => MusicConverter.Convert(new AudioBuffer([new float[96000], new float[96000]], 48000), new ConversionOptions()));

    [Fact]
    public void WavReader_ReadsSamplesAndSmplLoop()
    {
        var samples = new short[] { 0, 1000, -1000, short.MaxValue, short.MinValue, 42 };
        var wav = BuildWav(samples, loopStart: 1, loopEndInclusive: 4);

        Assert.True(WavReader.IsWav(wav));
        var audio = WavReader.Read(wav);

        Assert.Equal(44100, audio.SampleRate);
        Assert.Equal(samples.Length, audio.SampleCount);
        Assert.Equal(1000 / 32768f, audio.Channels[0][1], 5);
        Assert.Equal(new LoopRegion(1, 5), audio.Loop);
    }

    [Fact]
    public void Id3v2Reader_ReadsTitleArtistAndFrontCover()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6 };
        var tag = BuildId3([TextFrame("TIT2", "Rainbow Road"), TextFrame("TPE1", "Wheel Wizard"), PictureFrame(jpeg)]);

        var metadata = Id3v2Reader.Read(tag);

        Assert.Equal(tag.Length, Id3v2Reader.GetTagSize(tag));
        Assert.Equal("Rainbow Road", metadata.Title);
        Assert.Equal("Wheel Wizard", metadata.Artist);
        Assert.Equal("image/jpeg", metadata.Cover?.MimeType);
        Assert.Equal(jpeg, metadata.Cover?.Data);
    }

    [Fact]
    public void VorbisComments_ReadTagsAndGameLoopPoints()
    {
        var metadata = VorbisComments.FromComments(["TITLE=Coconut Mall", "artist=Someone", "LOOPSTART=44100", "LOOPLENGTH=88200"]);

        Assert.Equal("Coconut Mall", metadata.Title);
        Assert.Equal("Someone", metadata.Artist);
        Assert.Equal(new LoopRegion(44100, 132300), metadata.ResolveLoopTags(500_000));
    }

    [Fact]
    public void CoverArt_IsRecognisedByItsSignature()
    {
        Assert.Equal("image/png", CoverArt.FromBytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0])?.MimeType);
        Assert.Null(CoverArt.FromBytes([1, 2, 3, 4, 5, 6, 7, 8, 9], "application/octet-stream"));
    }

    #region Helpers

    private static float[] Tone(int length, int rate, (double Frequency, double Amplitude)[] partials)
    {
        var output = new float[length];
        for (var i = 0; i < length; i++)
        {
            double value = 0;
            foreach (var (frequency, amplitude) in partials)
                value += amplitude * Math.Sin(2 * Math.PI * frequency * i / rate);
            output[i] = (float)value;
        }

        return output;
    }

    /// <summary>A few seconds of chords, a bass pulse and noise bursts: enough structure to look like music.</summary>
    private static AudioBuffer Song(int rate, double seconds)
    {
        var random = new Random(1234);
        var length = (int)(rate * seconds);
        var left = new float[length];
        var right = new float[length];
        double[] roots = [220, 174.61, 261.63, 196];
        for (var i = 0; i < length; i++)
        {
            var t = i / (double)rate;
            var root = roots[(int)(t / 2) % roots.Length];
            var envelope = Math.Exp(-(t % 2) * 0.8);
            var chord = 0.12 * envelope * (Math.Sin(2 * Math.PI * root * t) + Math.Sin(2 * Math.PI * root * 1.26 * t) + Math.Sin(2 * Math.PI * root * 1.5 * t));
            var bass = t % 0.5 < 0.2 ? 0.25 * Math.Sin(2 * Math.PI * 55 * t) : 0;
            var noise = t % 0.25 < 0.03 ? (random.NextDouble() - 0.5) * 0.1 : 0;
            left[i] = (float)(chord + bass + noise);
            right[i] = (float)(chord * 0.9 + bass + noise);
        }

        return new AudioBuffer([left, right], rate);
    }

    private static short[] ToPcm16(float[] samples) => samples.Select(s => (short)Math.Clamp(Math.Round(s * 32767), short.MinValue, short.MaxValue)).ToArray();

    private static double Snr(short[] reference, short[] test)
    {
        double signal = 0,
            noise = 0;
        for (var i = 0; i < reference.Length; i++)
        {
            signal += (double)reference[i] * reference[i];
            noise += Math.Pow(reference[i] - test[i], 2);
        }

        return 10 * Math.Log10(signal / Math.Max(noise, 1));
    }

    private static double Rms(float[] samples, int skip) => Math.Sqrt(samples.Skip(skip).Take(samples.Length - 2 * skip).Average(s => (double)s * s));

    private static byte[] BuildWav(short[] samples, int loopStart, int loopEndInclusive)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(44100 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
            writer.Write(sample);
        writer.Write("smpl"u8);
        writer.Write(36 + 24);
        writer.Write(new byte[28]);
        writer.Write(1); // loop count
        writer.Write(0); // sampler data
        writer.Write(0); // cue id
        writer.Write(0); // loop type
        writer.Write(loopStart);
        writer.Write(loopEndInclusive);
        writer.Write(0);
        writer.Write(0);
        writer.Flush();
        var bytes = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        return bytes;
    }

    private static byte[] TextFrame(string id, string text) => Frame(id, [3, .. Encoding.UTF8.GetBytes(text)]);

    private static byte[] PictureFrame(byte[] image) => Frame("APIC", [0, .. "image/jpeg"u8, 0, 3, 0, .. image]);

    private static byte[] Frame(string id, byte[] body)
    {
        var frame = new byte[10 + body.Length];
        Encoding.ASCII.GetBytes(id, frame.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4), body.Length); // ID3v2.3 sizes are plain big endian
        body.CopyTo(frame, 10);
        return frame;
    }

    private static byte[] BuildId3(byte[][] frames)
    {
        var body = frames.SelectMany(frame => frame).ToArray();
        var size = body.Length;
        byte[] header = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, (byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)];
        return [.. header, .. body];
    }

    #endregion
}

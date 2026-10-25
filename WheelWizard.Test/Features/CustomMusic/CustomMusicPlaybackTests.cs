using System.Buffers.Binary;
using System.Text;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Audio.Formats;
using WheelWizard.CustomMusic.Playback;

namespace WheelWizard.Test.Features.CustomMusic;

public class CustomMusicPlaybackTests
{
    [Fact]
    public void PreviewClip_BuildsAStereoWavFromAnyPosition()
    {
        var clip = new PreviewClip([.. Enumerable.Range(0, 3200).Select(i => (short)i)], 1600); // 1600 frames = 1 second

        var wav = clip.ToWav(TimeSpan.FromSeconds(0.5));

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
        Assert.Equal(1600, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(800 * 4, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        Assert.Equal(44 + 800 * 4, wav.Length);
        Assert.Equal(1600, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44))); // first sample of frame 800
        Assert.Equal(1.0, clip.Duration.TotalSeconds, 3);
    }

    [Fact]
    public void PreviewClip_PlaysABrstmWithTheVolumeByteApplied()
    {
        var tone = Enumerable.Range(0, 32000).Select(i => (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / 32000))).ToArray();
        var quiet = BrstmWriter.Write(new BrstmStream { Channels = [tone, tone], PulsarVolume = 0x40 });
        var full = BrstmWriter.Write(new BrstmStream { Channels = [tone, tone] });

        var quietClip = PreviewClip.FromBrstm(quiet);
        var fullClip = PreviewClip.FromBrstm(full);

        Assert.Equal(32000, quietClip.SampleRate);
        Assert.InRange(quietClip.Duration.TotalSeconds, 0.99, 1.01);
        // 0x40 / 0x7F is about -6 dB.
        Assert.InRange(Peak(quietClip) / (double)Peak(fullClip), 0.49, 0.52);
    }

    [Fact]
    public void Player_PlaysPausesSeeksAndStops()
    {
        var output = new RecordingOutput();
        using var player = new MusicPreviewPlayer(output);
        var clip = new PreviewClip(new short[32000 * 2 * 10], 32000); // 10 seconds

        player.Play(clip);
        Assert.Equal(PreviewState.Playing, player.State);
        Assert.Equal(10, player.Duration.TotalSeconds, 3);
        Assert.Single(output.Started);

        player.Seek(TimeSpan.FromSeconds(5));
        Assert.Equal(2, output.Started.Count);
        Assert.InRange(output.Started[1].Length, 44 + 32000 * 4 * 5 - 4, 44 + 32000 * 4 * 5 + 4);
        Assert.InRange(player.Position.TotalSeconds, 5.0, 5.5);

        player.Pause();
        var paused = player.Position;
        Assert.Equal(PreviewState.Paused, player.State);
        Assert.Equal(1, output.Stops);
        Thread.Sleep(150);
        Assert.Equal(paused, player.Position);

        player.Resume();
        Assert.Equal(PreviewState.Playing, player.State);
        Assert.Equal(3, output.Started.Count);

        player.Stop();
        Assert.Equal(PreviewState.Stopped, player.State);
        Assert.Equal(TimeSpan.Zero, player.Position);
    }

    [Fact]
    public void Player_StopsByItselfWhenTheSongEnds()
    {
        var output = new RecordingOutput();
        using var player = new MusicPreviewPlayer(output);
        var changes = 0;
        player.Changed += (_, _) => Interlocked.Increment(ref changes);

        player.Play(new PreviewClip(new short[32000 * 2 / 5], 32000)); // 0.2 seconds

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (player.State != PreviewState.Stopped && DateTime.UtcNow < deadline)
            Thread.Sleep(20);
        Assert.Equal(PreviewState.Stopped, player.State);
        Assert.True(changes >= 2, "start and end are both reported");
    }

    [Fact]
    public void Player_ReportsAnOutputThatCannotPlay()
    {
        var output = new RecordingOutput { Fail = true };
        using var player = new MusicPreviewPlayer(output);

        Assert.Throws<IOException>(() => player.Play(new PreviewClip(new short[100], 32000)));

        Assert.Equal(PreviewState.Stopped, player.State);
    }

    private static int Peak(PreviewClip clip) => clip.Samples.Max(sample => Math.Abs((int)sample));

    private sealed class RecordingOutput : IAudioOutput
    {
        public List<byte[]> Started { get; } = [];
        public int Stops { get; private set; }
        public bool Fail { get; init; }
        public bool IsAvailable => true;

        public void Start(byte[] wav)
        {
            if (Fail)
                throw new IOException("no sound card");
            Started.Add(wav);
        }

        public void Stop() => Stops++;

        public void Dispose() { }
    }
}

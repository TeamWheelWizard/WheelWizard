using System.Buffers.Binary;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Audio.Dsp;
using WheelWizard.CustomMusic.Audio.Formats;

namespace WheelWizard.CustomMusic.Playback;

/// <summary>Music ready to be played: 16-bit stereo, interleaved.</summary>
public sealed class PreviewClip
{
    private const int HeaderSize = 44;

    public PreviewClip(short[] interleavedSamples, int sampleRate)
    {
        if (interleavedSamples.Length % 2 != 0)
            throw new ArgumentException("Samples must be interleaved stereo.", nameof(interleavedSamples));
        Samples = interleavedSamples;
        SampleRate = sampleRate;
    }

    public short[] Samples { get; }
    public int SampleRate { get; }
    public int FrameCount => Samples.Length / 2;
    public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / (double)SampleRate);

    /// <summary>
    /// Decodes a BRSTM the way the game plays it, including the volume byte Retro Rewind applies, so previews of the
    /// original and of a replacement are as loud relative to each other as they are in game.
    /// </summary>
    /// <param name="brstm">The BRSTM file.</param>
    /// <param name="allTracks">Play every track at once (Retro Rewind's menu music) instead of only the first.</param>
    public static PreviewClip FromBrstm(byte[] brstm, bool allTracks = false)
    {
        var info = BrstmReader.ReadInfo(brstm);
        return FromAudio(BrstmReader.DecodeAsPlayed(brstm, allTracks), info.PulsarGain);
    }

    public static PreviewClip FromAudio(AudioBuffer audio, double gain = 1.0)
    {
        var stereo = AudioOps.ToStereo(audio);
        var samples = new short[stereo.SampleCount * 2];
        var left = stereo.Channels[0];
        var right = stereo.Channels[1];
        var scale = (float)(gain * short.MaxValue);
        for (var i = 0; i < stereo.SampleCount; i++)
        {
            samples[i * 2] = ToShort(left[i] * scale);
            samples[i * 2 + 1] = ToShort(right[i] * scale);
        }

        return new PreviewClip(samples, stereo.SampleRate);

        static short ToShort(float value) => (short)Math.Clamp(MathF.Round(value), short.MinValue, short.MaxValue);
    }

    /// <summary>A WAV file with the audio from <paramref name="start"/> onwards.</summary>
    public byte[] ToWav(TimeSpan start)
    {
        var firstFrame = (int)Math.Clamp(start.TotalSeconds * SampleRate, 0, Math.Max(0, FrameCount - 1));
        var frames = FrameCount - firstFrame;
        var dataBytes = frames * 4;
        var wav = new byte[HeaderSize + dataBytes];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 4);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 4);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        var target = span[HeaderSize..];
        for (var i = 0; i < frames * 2; i++)
            BinaryPrimitives.WriteInt16LittleEndian(target[(i * 2)..], Samples[firstFrame * 2 + i]);
        return wav;
    }
}

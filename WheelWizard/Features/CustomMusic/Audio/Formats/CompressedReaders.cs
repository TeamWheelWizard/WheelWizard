using NLayer;
using NVorbis;
using WheelWizard.CustomMusic.Audio.Tags;

namespace WheelWizard.CustomMusic.Audio.Formats;

/// <summary>MP3 through NLayer (fully managed), tags through our own ID3v2 reader.</summary>
public static class Mp3Reader
{
    public static bool LooksLikeMp3(ReadOnlySpan<byte> data)
    {
        if (Id3v2Reader.GetTagSize(data) > 0)
            return true;
        // MPEG audio frame sync: 11 set bits, layer bits not 00.
        return data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0 && (data[1] & 0x06) != 0;
    }

    public static AudioBuffer Read(byte[] file, CancellationToken cancellationToken = default)
    {
        var metadata = Id3v2Reader.Read(file);
        using var stream = new MemoryStream(file, writable: false);
        using var mpeg = new MpegFile(stream);

        var channels = Math.Max(1, mpeg.Channels);
        var sampleRate = mpeg.SampleRate;
        var planar = new List<float>[channels];
        for (var c = 0; c < channels; c++)
            planar[c] = new List<float>(sampleRate * 60);

        var buffer = new float[4096 * channels];
        int read;
        while ((read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i + channels <= read; i += channels)
            {
                for (var c = 0; c < channels; c++)
                    planar[c].Add(buffer[i + c]);
            }
        }

        if (sampleRate <= 0 || planar[0].Count == 0)
            throw new InvalidDataException("The MP3 file contains no decodable audio.");

        // Gapless playback: drop the encoder delay/padding the LAME header describes (plus the 529 sample
        // decoder delay), so loop points from tags line up and there is no extra silence.
        var total = planar[0].Count;
        var (skipStart, skipEnd) = ReadGaplessInfo(file) is { } gapless ? (gapless.Delay + DecoderDelay, Math.Max(0, gapless.Padding - DecoderDelay)) : (0, 0);
        if (skipStart + skipEnd >= total)
            (skipStart, skipEnd) = (0, 0);

        var channelsOut = planar.Select(list => list.GetRange(skipStart, total - skipStart - skipEnd).ToArray()).ToArray();
        return new AudioBuffer(channelsOut, sampleRate) { Metadata = metadata };
    }

    private const int DecoderDelay = 529;

    /// <summary>Reads encoder delay and padding from the LAME/Info header in the first MPEG frame.</summary>
    internal static (int Delay, int Padding)? ReadGaplessInfo(ReadOnlySpan<byte> file)
    {
        var position = Id3v2Reader.GetTagSize(file);
        // Find the first frame sync after the tag.
        while (position + 4 < file.Length && !(file[position] == 0xFF && (file[position + 1] & 0xE0) == 0xE0))
        {
            position++;
            if (position > 64 * 1024 + Id3v2Reader.GetTagSize(file))
                return null;
        }

        if (position + 4 >= file.Length)
            return null;

        var versionBits = (file[position + 1] >> 3) & 0x3; // 3 = MPEG1, 2 = MPEG2, 0 = MPEG2.5
        var channelMode = (file[position + 3] >> 6) & 0x3; // 3 = mono
        var isMpeg1 = versionBits == 3;
        var sideInfo = isMpeg1 ? (channelMode == 3 ? 17 : 32) : (channelMode == 3 ? 9 : 17);
        var tag = position + 4 + sideInfo;
        if (tag + 120 > file.Length)
            return null;

        var magic = System.Text.Encoding.ASCII.GetString(file.Slice(tag, 4));
        if (magic is not ("Xing" or "Info"))
            return null;

        var flags = (file[tag + 4] << 24) | (file[tag + 5] << 16) | (file[tag + 6] << 8) | file[tag + 7];
        var lame = tag + 8;
        if ((flags & 1) != 0)
            lame += 4;
        if ((flags & 2) != 0)
            lame += 4;
        if ((flags & 4) != 0)
            lame += 100;
        if ((flags & 8) != 0)
            lame += 4;

        if (lame + 24 > file.Length)
            return null;
        var encoder = System.Text.Encoding.ASCII.GetString(file.Slice(lame, 4));
        if (encoder is not ("LAME" or "Lavf" or "Lavc" or "GOGO"))
            return null;

        var delay = (file[lame + 21] << 4) | (file[lame + 22] >> 4);
        var padding = ((file[lame + 22] & 0x0F) << 8) | file[lame + 23];
        return delay is >= 0 and < 4096 && padding is >= 0 and < 4096 ? (delay, padding) : null;
    }
}

/// <summary>Ogg Vorbis through NVorbis (fully managed).</summary>
public static class OggVorbisReader
{
    public static bool IsOgg(ReadOnlySpan<byte> data) => data.Length >= 4 && data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S';

    /// <summary>True when the first Ogg logical stream carries Vorbis (as opposed to Opus, FLAC or Speex).</summary>
    public static bool IsVorbis(ReadOnlySpan<byte> data)
    {
        if (!IsOgg(data) || data.Length < 28)
            return false;
        var segments = data[26];
        var packetStart = 27 + segments;
        return data.Length >= packetStart + 7
            && data[packetStart] == 0x01
            && data[packetStart + 1] == 'v'
            && data[packetStart + 2] == 'o'
            && data[packetStart + 3] == 'r'
            && data[packetStart + 4] == 'b'
            && data[packetStart + 5] == 'i'
            && data[packetStart + 6] == 's';
    }

    public static AudioBuffer Read(byte[] file, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(file, writable: false);
        using var vorbis = new VorbisReader(stream, closeOnDispose: false);

        var channels = vorbis.Channels;
        var sampleRate = vorbis.SampleRate;
        var expected = vorbis.TotalSamples > 0 ? (int)Math.Min(vorbis.TotalSamples, int.MaxValue / 2) : sampleRate * 60;
        var planar = new List<float>[channels];
        for (var c = 0; c < channels; c++)
            planar[c] = new List<float>(expected);

        var buffer = new float[4096 * channels];
        int read;
        while ((read = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i + channels <= read; i += channels)
            {
                for (var c = 0; c < channels; c++)
                    planar[c].Add(buffer[i + c]);
            }
        }

        if (planar[0].Count == 0)
            throw new InvalidDataException("The Ogg file contains no decodable audio.");

        var comments = vorbis.Tags.All.SelectMany(pair => pair.Value.Select(value => $"{pair.Key}={value}"));
        var metadata = VorbisComments.FromComments(comments);
        return new AudioBuffer(planar.Select(list => list.ToArray()).ToArray(), sampleRate) { Metadata = metadata };
    }
}

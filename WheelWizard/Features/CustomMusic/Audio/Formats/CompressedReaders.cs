using NLayer;
using NVorbis;
using WheelWizard.CustomMusic.Audio.Tags;

namespace WheelWizard.CustomMusic.Audio.Formats;

/// <summary>MP3 through NLayer (fully managed), tags through our own ID3v2 reader.</summary>
public static class Mp3Reader
{
    /// <summary>
    /// An MPEG audio frame (after any ID3v2 tag and padding). ID3v2 tags alone prove nothing: AAC (ADTS) files carry
    /// them too, and their frame sync has layer bits 00, so they go to FFmpeg instead.
    /// </summary>
    public static bool LooksLikeMp3(ReadOnlySpan<byte> data)
    {
        // Untagged files must start with a frame. After a tag, the first frame sync decides (tags can be followed
        // by padding or a little junk).
        var start = Id3v2Reader.GetTagSize(data);
        var end = Math.Min(data.Length - 1, start == 0 ? 1 : start + 8 * 1024);
        for (var i = start; i < end; i++)
        {
            // MPEG audio frame sync: 11 set bits. Layer bits 00 mean AAC (ADTS), which FFmpeg decodes.
            if (data[i] == 0xFF && (data[i + 1] & 0xE0) == 0xE0)
                return (data[i + 1] & 0x06) != 0;
        }

        return false;
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

        // NLayer already trims the encoder delay and padding from LAME/Info and iTunSMPB tags (gapless playback).
        var channelsOut = planar.Select(list => list.ToArray()).ToArray();
        return new AudioBuffer(channelsOut, sampleRate) { Metadata = metadata };
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

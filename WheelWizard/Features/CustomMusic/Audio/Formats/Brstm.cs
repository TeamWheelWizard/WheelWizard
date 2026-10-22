using System.Buffers.Binary;
using System.Text;
using WheelWizard.CustomMusic.Audio.Codecs;

namespace WheelWizard.CustomMusic.Audio.Formats;

/// <summary>Constants shared by the BRSTM reader and writer (NintendoWare for Revolution stream files).</summary>
public static class Brstm
{
    /// <summary>Bytes per channel in one interleave block; every retail Mario Kart Wii BRSTM uses 0x2000.</summary>
    public const int InterleaveBytes = 0x2000;

    /// <summary>Samples per interleave block for DSP-ADPCM (0x2000 bytes / 8 bytes * 14 samples).</summary>
    public const int SamplesPerBlock = InterleaveBytes / DspAdpcm.BytesPerFrame * DspAdpcm.SamplesPerFrame;

    /// <summary>Offset of the volume byte Pulsar (and CTGP) read from a BRSTM header; 0 means "unchanged".</summary>
    public const int PulsarVolumeOffset = 0x3F;

    public const byte CodecPcm8 = 0;
    public const byte CodecPcm16 = 1;
    public const byte CodecDspAdpcm = 2;

    internal const int OffsetMarker = 0x01000000;
    internal const int OffsetMarkerTrackV2 = 0x01010000;
}

/// <summary>A stereo-or-mono stream ready to be written as a BRSTM.</summary>
public sealed class BrstmStream
{
    public required short[][] Channels { get; init; }
    public int SampleRate { get; init; } = 32000;
    public bool Looping { get; init; }
    public int LoopStart { get; init; }

    /// <summary>Optional Pulsar volume byte (1..127). Null leaves the header byte at 0.</summary>
    public byte? PulsarVolume { get; init; }
}

public static class BrstmWriter
{
    public static byte[] Write(BrstmStream stream)
    {
        var channelCount = stream.Channels.Length;
        if (channelCount is < 1 or > 16)
            throw new ArgumentException("A BRSTM needs between 1 and 16 channels.", nameof(stream));

        var sampleCount = stream.Channels[0].Length;
        if (sampleCount == 0 || stream.Channels.Any(c => c.Length != sampleCount))
            throw new ArgumentException("All channels need the same, non-zero length.", nameof(stream));
        if (stream.Looping && (stream.LoopStart < 0 || stream.LoopStart >= sampleCount))
            throw new ArgumentOutOfRangeException(nameof(stream), "The loop start must fall inside the audio.");
        if (stream.SampleRate is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(stream), "BRSTM stores the sample rate as 16 bits.");

        var encoded = new DspAdpcm.EncodedChannel[channelCount];
        Parallel.For(0, channelCount, i => encoded[i] = DspAdpcm.Encode(stream.Channels[i]));

        return Write(stream, encoded);
    }

    internal static byte[] Write(BrstmStream stream, DspAdpcm.EncodedChannel[] encoded)
    {
        var channelCount = encoded.Length;
        var sampleCount = stream.Channels[0].Length;
        // Mono gets one track; everything else is paired into stereo tracks like the retail files.
        var trackCount = (channelCount + 1) / 2;

        var samplesPerBlock = Brstm.SamplesPerBlock;
        var blockCount = (sampleCount + samplesPerBlock - 1) / samplesPerBlock;
        var lastBlockSamples = sampleCount - (blockCount - 1) * samplesPerBlock;
        var lastBlockBytes = DspAdpcm.SampleCountToByteCount(lastBlockSamples);
        var lastBlockPadded = Align(lastBlockBytes, 0x20);
        var channelDataSize = Align(DspAdpcm.SampleCountToByteCount(sampleCount), 0x20);

        const int headTableSize = 8 * 3;
        const int head1Size = 0x34;
        const int trackInfoSize = 0x0C;
        const int channelInfoSize = 0x38;
        var head2Size = 4 + 8 * trackCount + trackInfoSize * trackCount;
        var head3Size = 4 + 8 * channelCount + channelInfoSize * channelCount;
        var headSize = Align(8 + headTableSize + head1Size + head2Size + head3Size, 0x20);

        var seekEntryCount = (sampleCount + samplesPerBlock - 1) / samplesPerBlock;
        var adpcSize = Align(8 + seekEntryCount * channelCount * 4, 0x20);
        const int headOffset = 0x40;
        var adpcOffset = headOffset + headSize;
        var dataOffset = adpcOffset + adpcSize;
        var dataSize = 0x20 + channelDataSize * channelCount;
        var fileSize = dataOffset + dataSize;

        var file = new byte[fileSize];
        var span = file.AsSpan();

        // RSTM header
        WriteAscii(span, 0x00, "RSTM");
        WriteU16(span, 0x04, 0xFEFF);
        WriteU16(span, 0x06, 0x0100);
        WriteS32(span, 0x08, fileSize);
        WriteU16(span, 0x0C, 0x40);
        WriteU16(span, 0x0E, 2);
        WriteS32(span, 0x10, headOffset);
        WriteS32(span, 0x14, headSize);
        WriteS32(span, 0x18, adpcOffset);
        WriteS32(span, 0x1C, adpcSize);
        WriteS32(span, 0x20, dataOffset);
        WriteS32(span, 0x24, dataSize);
        if (stream.PulsarVolume is { } volume && volume > 0)
            file[Brstm.PulsarVolumeOffset] = Math.Min(volume, (byte)0x7F);

        // HEAD chunk
        var head = span[headOffset..];
        WriteAscii(head, 0x00, "HEAD");
        WriteS32(head, 0x04, headSize);
        WriteS32(head, 0x08, Brstm.OffsetMarker);
        WriteS32(head, 0x0C, headTableSize);
        WriteS32(head, 0x10, Brstm.OffsetMarker);
        WriteS32(head, 0x14, headTableSize + head1Size);
        WriteS32(head, 0x18, Brstm.OffsetMarker);
        WriteS32(head, 0x1C, headTableSize + head1Size + head2Size);

        // Offsets inside HEAD are relative to HEAD + 8.
        var body = head[8..];

        // Part 1: stream info
        var p1 = body[headTableSize..];
        p1[0x00] = Brstm.CodecDspAdpcm;
        p1[0x01] = (byte)(stream.Looping ? 1 : 0);
        p1[0x02] = (byte)channelCount;
        WriteU16(p1, 0x04, (ushort)stream.SampleRate);
        WriteS32(p1, 0x08, stream.Looping ? stream.LoopStart : 0);
        WriteS32(p1, 0x0C, sampleCount);
        WriteS32(p1, 0x10, dataOffset + 0x20);
        WriteS32(p1, 0x14, blockCount);
        WriteS32(p1, 0x18, Brstm.InterleaveBytes);
        WriteS32(p1, 0x1C, samplesPerBlock);
        WriteS32(p1, 0x20, lastBlockBytes);
        WriteS32(p1, 0x24, lastBlockSamples);
        WriteS32(p1, 0x28, lastBlockPadded);
        WriteS32(p1, 0x2C, samplesPerBlock);
        WriteS32(p1, 0x30, 4);

        // Part 2: tracks (the "standard" layout with volume/pan that retail files use)
        var p2Offset = headTableSize + head1Size;
        var p2 = body[p2Offset..];
        p2[0x00] = (byte)trackCount;
        p2[0x01] = 1;
        var trackInfoBase = p2Offset + 4 + 8 * trackCount;
        for (var t = 0; t < trackCount; t++)
        {
            WriteS32(p2, 4 + t * 8, Brstm.OffsetMarkerTrackV2);
            WriteS32(p2, 8 + t * 8, trackInfoBase + t * trackInfoSize);

            var info = body[(trackInfoBase + t * trackInfoSize)..];
            var left = t * 2;
            var channelsInTrack = left + 1 < channelCount ? 2 : 1;
            info[0x00] = 0x7F; // volume
            info[0x01] = 0x40; // centred pan
            info[0x08] = (byte)channelsInTrack;
            info[0x09] = (byte)left;
            info[0x0A] = (byte)(channelsInTrack == 2 ? left + 1 : 0);
        }

        // Part 3: channels with their ADPCM coefficients and start/loop contexts
        var p3Offset = p2Offset + head2Size;
        var p3 = body[p3Offset..];
        p3[0x00] = (byte)channelCount;
        var channelInfoBase = p3Offset + 4 + 8 * channelCount;
        for (var c = 0; c < channelCount; c++)
        {
            var infoOffset = channelInfoBase + c * channelInfoSize;
            WriteS32(p3, 4 + c * 8, Brstm.OffsetMarker);
            WriteS32(p3, 8 + c * 8, infoOffset);

            var channel = encoded[c];
            var info = body[infoOffset..];
            WriteS32(info, 0x00, Brstm.OffsetMarker);
            WriteS32(info, 0x04, infoOffset + 8);
            for (var i = 0; i < 16; i++)
                WriteS16(info, 0x08 + i * 2, channel.Coefficients[i]);

            WriteU16(info, 0x28, 0); // gain
            WriteU16(info, 0x2A, channel.InitialPredictorScale);
            WriteS16(info, 0x2C, 0);
            WriteS16(info, 0x2E, 0);

            if (stream.Looping)
            {
                WriteU16(info, 0x30, DspAdpcm.GetPredictorScale(channel.Adpcm, stream.LoopStart));
                WriteS16(info, 0x32, stream.LoopStart >= 1 ? channel.Decoded[stream.LoopStart - 1] : (short)0);
                WriteS16(info, 0x34, stream.LoopStart >= 2 ? channel.Decoded[stream.LoopStart - 2] : (short)0);
            }
            else
            {
                WriteU16(info, 0x30, channel.InitialPredictorScale);
            }
        }

        // ADPC chunk: decoder history at the start of every block, so the game can seek/loop mid-stream.
        var adpc = span[adpcOffset..];
        WriteAscii(adpc, 0x00, "ADPC");
        WriteS32(adpc, 0x04, adpcSize);
        for (var entry = 0; entry < seekEntryCount; entry++)
        {
            var sampleIndex = entry * samplesPerBlock;
            for (var c = 0; c < channelCount; c++)
            {
                var decoded = encoded[c].Decoded;
                var hist1 = entry == 0 ? (short)0 : decoded[sampleIndex - 1];
                var hist2 = entry == 0 ? (short)0 : decoded[sampleIndex - 2];
                var at = 8 + (entry * channelCount + c) * 4;
                WriteS16(adpc, at, hist1);
                WriteS16(adpc, at + 2, hist2);
            }
        }

        // DATA chunk: blocks interleaved per channel; the last block is padded to 0x20 per channel.
        var data = span[dataOffset..];
        WriteAscii(data, 0x00, "DATA");
        WriteS32(data, 0x04, dataSize);
        WriteS32(data, 0x08, 0x18);
        var audio = data[0x20..];
        for (var block = 0; block < blockCount; block++)
        {
            var isLast = block == blockCount - 1;
            var blockStride = isLast ? lastBlockPadded : Brstm.InterleaveBytes;
            var bytesInBlock = isLast ? lastBlockBytes : Brstm.InterleaveBytes;
            var blockBase = block * Brstm.InterleaveBytes * channelCount;
            for (var c = 0; c < channelCount; c++)
            {
                var source = encoded[c].Adpcm.AsSpan(block * Brstm.InterleaveBytes, bytesInBlock);
                source.CopyTo(audio[(blockBase + c * blockStride)..]);
            }
        }

        return file;
    }

    private static int Align(int value, int multiple) => (value + multiple - 1) / multiple * multiple;

    private static void WriteAscii(Span<byte> span, int offset, string text) => Encoding.ASCII.GetBytes(text, span[offset..]);

    private static void WriteU16(Span<byte> span, int offset, int value) =>
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], (ushort)value);

    private static void WriteS16(Span<byte> span, int offset, short value) => BinaryPrimitives.WriteInt16BigEndian(span[offset..], value);

    private static void WriteS32(Span<byte> span, int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(span[offset..], value);
}

/// <summary>What a BRSTM header says about the stream.</summary>
public sealed record BrstmInfo(
    int Codec,
    bool Looping,
    int ChannelCount,
    int SampleRate,
    int LoopStart,
    int SampleCount,
    int TrackCount,
    byte PulsarVolume
)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(SampleCount / (double)Math.Max(1, SampleRate));

    /// <summary>The gain Pulsar applies through header byte 0x3F (1.0 when the byte is 0).</summary>
    public double PulsarGain => PulsarVolume == 0 ? 1.0 : Math.Min((int)PulsarVolume, 0x7F) / 127.0;
}

public static class BrstmReader
{
    public static bool IsBrstm(ReadOnlySpan<byte> header) =>
        header.Length >= 4 && header[0] == 'R' && header[1] == 'S' && header[2] == 'T' && header[3] == 'M';

    public static BrstmInfo ReadInfo(ReadOnlySpan<byte> file)
    {
        var layout = ReadLayout(file);
        return layout.Info;
    }

    /// <summary>Decodes a BRSTM into planar 16-bit PCM. Supports DSP-ADPCM, PCM16 and PCM8 streams.</summary>
    public static AudioBuffer Decode(ReadOnlySpan<byte> file)
    {
        var layout = ReadLayout(file);
        var info = layout.Info;
        var channelCount = info.ChannelCount;
        var sampleCount = info.SampleCount;
        var channels = new float[channelCount][];

        var bytesPerChannel = info.Codec switch
        {
            Brstm.CodecDspAdpcm => DspAdpcm.SampleCountToByteCount(sampleCount),
            Brstm.CodecPcm16 => sampleCount * 2,
            _ => sampleCount,
        };

        for (var c = 0; c < channelCount; c++)
        {
            var raw = Deinterleave(file, layout, c, bytesPerChannel);
            var output = new float[sampleCount];
            switch (info.Codec)
            {
                case Brstm.CodecDspAdpcm:
                {
                    var pcm = DspAdpcm.Decode(raw, layout.Coefficients[c], sampleCount, layout.History1[c], layout.History2[c]);
                    for (var i = 0; i < sampleCount; i++)
                        output[i] = pcm[i] / 32768f;
                    break;
                }
                case Brstm.CodecPcm16:
                    for (var i = 0; i < sampleCount && i * 2 + 1 < raw.Length; i++)
                        output[i] = BinaryPrimitives.ReadInt16BigEndian(raw.AsSpan(i * 2)) / 32768f;
                    break;
                default:
                    for (var i = 0; i < sampleCount && i < raw.Length; i++)
                        output[i] = (sbyte)raw[i] / 128f;
                    break;
            }

            channels[c] = output;
        }

        var buffer = new AudioBuffer(channels, info.SampleRate);
        if (info.Looping && info.LoopStart < sampleCount)
            buffer.Loop = new LoopRegion(info.LoopStart, sampleCount);
        return buffer;
    }

    /// <summary>One track of a BRSTM: its channels (one or two) and its volume byte (127 = full).</summary>
    public sealed record Track(int[] Channels, byte Volume);

    /// <summary>
    /// The tracks a BRSTM declares. Custom track music often carries the song in more than one track (copies,
    /// or alternative versions such as an underwater mix), and the game decides which tracks are heard.
    /// Files without a usable track table are treated as consecutive stereo pairs.
    /// </summary>
    public static IReadOnlyList<Track> ReadTracks(ReadOnlySpan<byte> file)
    {
        var info = ReadInfo(file);
        var tracks = new List<Track>();
        try
        {
            var headOffset = BinaryPrimitives.ReadInt32BigEndian(file[0x10..]);
            var body = headOffset + 8;
            var part2 = body + BinaryPrimitives.ReadInt32BigEndian(file[(headOffset + 0x14)..]);
            int count = file[part2];
            for (var t = 0; t < count; t++)
            {
                var marker = BinaryPrimitives.ReadUInt32BigEndian(file[(part2 + 4 + t * 8)..]);
                var offset = body + BinaryPrimitives.ReadInt32BigEndian(file[(part2 + 8 + t * 8)..]);
                // Marker 0x0101xxxx: volume, pan, padding, then the channel list at 0x08. 0x0100xxxx: channel list at 0x00.
                var extended = ((marker >> 16) & 0xFF) == 1;
                var volume = extended ? file[offset] : (byte)0x7F;
                var listOffset = extended ? offset + 8 : offset;
                int channels = file[listOffset];
                if (channels is < 1 or > 2)
                    throw new InvalidDataException("Unexpected channel count in a BRSTM track.");
                var indices = new int[channels];
                for (var c = 0; c < channels; c++)
                {
                    indices[c] = file[listOffset + 1 + c];
                    if (indices[c] >= info.ChannelCount)
                        throw new InvalidDataException("A BRSTM track refers to a missing channel.");
                }

                tracks.Add(new Track(indices, volume));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            tracks.Clear();
        }

        if (tracks.Count == 0)
        {
            for (var c = 0; c < info.ChannelCount; c += 2)
                tracks.Add(new Track(c + 1 < info.ChannelCount ? [c, c + 1] : [c], 0x7F));
        }

        return tracks;
    }

    /// <summary>
    /// Decodes a BRSTM the way it is heard: in races the game plays the first track (further tracks are
    /// alternatives it switches to, such as Koopa Cape's underwater mix); Retro Rewind's own menu music is played
    /// with every track at full volume, so the tracks add up (<paramref name="allTracks"/>).
    /// Each track's volume byte is applied.
    /// </summary>
    public static AudioBuffer DecodeAsPlayed(ReadOnlySpan<byte> file, bool allTracks)
    {
        var decoded = Decode(file);
        var tracks = ReadTracks(file);
        var heard = allTracks ? tracks : tracks.Take(1).ToList();
        if (heard.Count == 1 && heard[0].Volume >= 0x7F && heard[0].Channels.Length == decoded.ChannelCount)
            return decoded; // the common single-track file: nothing to mix

        var length = decoded.SampleCount;
        var stereo = heard.Any(track => track.Channels.Length == 2);
        var left = new float[length];
        var right = stereo ? new float[length] : left;
        foreach (var track in heard)
        {
            var gain = Math.Min((int)track.Volume, 0x7F) / 127f;
            var trackLeft = decoded.Channels[track.Channels[0]];
            var trackRight = decoded.Channels[track.Channels[^1]];
            for (var i = 0; i < length; i++)
            {
                left[i] += trackLeft[i] * gain;
                if (stereo)
                    right[i] += trackRight[i] * gain;
            }
        }

        return new AudioBuffer(stereo ? [left, right] : [left], decoded.SampleRate) { Loop = decoded.Loop };
    }

    private static byte[] Deinterleave(ReadOnlySpan<byte> file, Layout layout, int channel, int bytesPerChannel)
    {
        var output = new byte[bytesPerChannel];
        var channelCount = layout.Info.ChannelCount;
        var written = 0;
        for (var block = 0; block < layout.BlockCount && written < bytesPerChannel; block++)
        {
            var isLast = block == layout.BlockCount - 1;
            var stride = isLast ? layout.LastBlockPaddedSize : layout.BlockSize;
            var size = isLast ? layout.LastBlockSize : layout.BlockSize;
            var start = layout.AudioOffset + block * layout.BlockSize * channelCount + channel * stride;
            var take = Math.Min(Math.Min(size, bytesPerChannel - written), Math.Max(0, file.Length - start));
            if (take <= 0)
                break;
            file.Slice(start, take).CopyTo(output.AsSpan(written));
            written += take;
        }

        return output;
    }

    private sealed record Layout(
        BrstmInfo Info,
        int AudioOffset,
        int BlockCount,
        int BlockSize,
        int LastBlockSize,
        int LastBlockPaddedSize,
        short[][] Coefficients,
        short[] History1,
        short[] History2
    );

    private static Layout ReadLayout(ReadOnlySpan<byte> file)
    {
        if (file.Length < 0x40 || !IsBrstm(file))
            throw new InvalidDataException("This is not a BRSTM file.");
        if (BinaryPrimitives.ReadUInt16BigEndian(file[4..]) != 0xFEFF)
            throw new InvalidDataException("Only big-endian (Wii) BRSTM files are supported.");

        var headOffset = BinaryPrimitives.ReadInt32BigEndian(file[0x10..]);
        if (headOffset < 0 || headOffset + 0x20 > file.Length || Encoding.ASCII.GetString(file.Slice(headOffset, 4)) != "HEAD")
            throw new InvalidDataException("The BRSTM HEAD chunk is missing.");

        var body = headOffset + 8;
        var part1 = body + BinaryPrimitives.ReadInt32BigEndian(file[(headOffset + 0x0C)..]);
        var part2 = body + BinaryPrimitives.ReadInt32BigEndian(file[(headOffset + 0x14)..]);
        var part3 = body + BinaryPrimitives.ReadInt32BigEndian(file[(headOffset + 0x1C)..]);
        if (part1 + 0x34 > file.Length || part3 + 4 > file.Length)
            throw new InvalidDataException("The BRSTM header is truncated.");

        int codec = file[part1];
        var looping = file[part1 + 1] != 0;
        int channelCount = file[part1 + 2];
        int sampleRate = BinaryPrimitives.ReadUInt16BigEndian(file[(part1 + 4)..]);
        var loopStart = BinaryPrimitives.ReadInt32BigEndian(file[(part1 + 0x08)..]);
        var sampleCount = BinaryPrimitives.ReadInt32BigEndian(file[(part1 + 0x0C)..]);
        var audioOffset = BinaryPrimitives.ReadInt32BigEndian(file[(part1 + 0x10)..]);
        var blockCount = BinaryPrimitives.ReadInt32BigEndian(file[(part1 + 0x14)..]);
        var blockSize = BinaryPrimitives.ReadInt32BigEndian(file[(part1 + 0x18)..]);
        var lastBlockSize = BinaryPrimitives.ReadInt32BigEndian(file[(part1 + 0x20)..]);
        var lastBlockPadded = BinaryPrimitives.ReadInt32BigEndian(file[(part1 + 0x28)..]);
        int trackCount = part2 < file.Length ? file[part2] : 0;

        if (codec is not (Brstm.CodecDspAdpcm or Brstm.CodecPcm16 or Brstm.CodecPcm8))
            throw new InvalidDataException($"Unsupported BRSTM codec {codec}.");
        if (channelCount < 1 || sampleCount < 0 || blockCount < 1 || blockSize <= 0 || sampleRate <= 0)
            throw new InvalidDataException("The BRSTM header has invalid stream values.");

        var coefficients = new short[channelCount][];
        var history1 = new short[channelCount];
        var history2 = new short[channelCount];
        if (codec == Brstm.CodecDspAdpcm)
        {
            for (var c = 0; c < channelCount; c++)
            {
                var entry = part3 + 4 + c * 8;
                var infoRef = body + BinaryPrimitives.ReadInt32BigEndian(file[(entry + 4)..]);
                var adpcmRef = body + BinaryPrimitives.ReadInt32BigEndian(file[(infoRef + 4)..]);
                if (adpcmRef + 0x30 > file.Length)
                    throw new InvalidDataException("The BRSTM channel table is truncated.");

                var coefs = new short[16];
                for (var i = 0; i < 16; i++)
                    coefs[i] = BinaryPrimitives.ReadInt16BigEndian(file[(adpcmRef + i * 2)..]);
                coefficients[c] = coefs;
                history1[c] = BinaryPrimitives.ReadInt16BigEndian(file[(adpcmRef + 0x24)..]);
                history2[c] = BinaryPrimitives.ReadInt16BigEndian(file[(adpcmRef + 0x26)..]);
            }
        }

        var info = new BrstmInfo(
            codec,
            looping,
            channelCount,
            sampleRate,
            loopStart,
            sampleCount,
            trackCount,
            file[Brstm.PulsarVolumeOffset]
        );
        return new(info, audioOffset, blockCount, blockSize, lastBlockSize, lastBlockPadded, coefficients, history1, history2);
    }
}

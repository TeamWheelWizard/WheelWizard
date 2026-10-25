using System.Buffers.Binary;
using System.Text;
using WheelWizard.CustomMusic.Audio.Tags;

namespace WheelWizard.CustomMusic.Audio.Formats;

/// <summary>Reads uncompressed WAV (PCM / IEEE float / WAVE_FORMAT_EXTENSIBLE, RIFF and RF64).</summary>
public static class WavReader
{
    private const int FormatPcm = 1;
    private const int FormatFloat = 3;
    private const int FormatExtensible = 0xFFFE;

    public static bool IsWav(ReadOnlySpan<byte> header) =>
        header.Length >= 12
        && (Ascii(header, 0) == "RIFF" || Ascii(header, 0) == "RF64")
        && Ascii(header, 8) == "WAVE";

    public static AudioBuffer Read(ReadOnlySpan<byte> file)
    {
        if (!IsWav(file))
            throw new InvalidDataException("This is not a WAV file.");

        int formatTag = 0,
            channels = 0,
            sampleRate = 0,
            bitsPerSample = 0,
            blockAlign = 0;
        var dataOffset = -1;
        long dataLength = 0;
        long rf64DataSize = -1;
        LoopRegion? loop = null;
        var metadata = new AudioMetadata();

        var position = 12;
        while (position + 8 <= file.Length)
        {
            var id = Ascii(file, position);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 4)..]);
            var body = position + 8;

            if (id == "ds64" && size >= 16)
                rf64DataSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(file[(body + 8)..]);

            if (id == "data")
            {
                if (size == 0xFFFFFFFF && rf64DataSize >= 0)
                    size = rf64DataSize;
                dataOffset = body;
                dataLength = Math.Min(size, file.Length - body);
            }
            else if (body + size > file.Length)
            {
                break;
            }
            else
            {
                var chunk = file.Slice(body, (int)size);
                switch (id)
                {
                    case "fmt ":
                        formatTag = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                        channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                        sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                        blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]);
                        bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
                        if (formatTag == FormatExtensible && chunk.Length >= 40)
                            formatTag = BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]); // first two bytes of the sub-format GUID
                        break;
                    case "smpl" when chunk.Length >= 36 + 24:
                    {
                        var loopCount = BinaryPrimitives.ReadUInt32LittleEndian(chunk[28..]);
                        if (loopCount > 0)
                        {
                            var start = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[(36 + 8)..]);
                            var end = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[(36 + 12)..]);
                            // smpl end points are inclusive.
                            loop = new LoopRegion(start, end + 1);
                        }

                        break;
                    }
                    case "LIST" when chunk.Length >= 4 && Ascii(chunk, 0) == "INFO":
                        ReadInfoList(chunk[4..], metadata);
                        break;
                    case "id3 ":
                    case "ID3 ":
                        metadata.MergeMissing(Id3v2Reader.Read(chunk));
                        break;
                }
            }

            position = body + (int)Math.Min(size, int.MaxValue - body);
            if ((size & 1) == 1)
                position++; // chunks are word aligned
            if (id == "data" && position > file.Length)
                break;
        }

        if (dataOffset < 0 || channels <= 0 || sampleRate <= 0)
            throw new InvalidDataException("The WAV file has no audio data.");

        var buffer = new AudioBuffer(DecodeSamples(file.Slice(dataOffset, (int)dataLength), formatTag, channels, bitsPerSample, blockAlign, false), sampleRate)
        {
            Metadata = metadata,
        };
        if (loop is { } region && region.IsValid(buffer.SampleCount))
            buffer.Loop = region;
        return buffer;
    }

    internal static float[][] DecodeSamples(
        ReadOnlySpan<byte> data,
        int formatTag,
        int channels,
        int bitsPerSample,
        int blockAlign,
        bool bigEndian
    )
    {
        if (formatTag is not (FormatPcm or FormatFloat))
            throw new NotSupportedException($"WAV format 0x{formatTag:X4} is compressed; it needs FFmpeg to decode.");

        var bytesPerSample = (bitsPerSample + 7) / 8;
        if (blockAlign <= 0)
            blockAlign = bytesPerSample * channels;
        if (bytesPerSample is < 1 or > 8 || blockAlign < bytesPerSample * channels)
            throw new InvalidDataException("The WAV file has an invalid sample layout.");

        var frames = data.Length / blockAlign;
        var output = new float[channels][];
        for (var c = 0; c < channels; c++)
            output[c] = new float[frames];

        for (var f = 0; f < frames; f++)
        {
            var frame = data.Slice(f * blockAlign, blockAlign);
            for (var c = 0; c < channels; c++)
            {
                var sample = frame.Slice(c * bytesPerSample, bytesPerSample);
                output[c][f] = formatTag == FormatFloat ? ReadFloat(sample, bigEndian) : ReadInteger(sample, bigEndian);
            }
        }

        return output;
    }

    private static float ReadFloat(ReadOnlySpan<byte> sample, bool bigEndian) =>
        sample.Length switch
        {
            4 => bigEndian ? BinaryPrimitives.ReadSingleBigEndian(sample) : BinaryPrimitives.ReadSingleLittleEndian(sample),
            8 => (float)(bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(sample) : BinaryPrimitives.ReadDoubleLittleEndian(sample)),
            _ => 0f,
        };

    private static float ReadInteger(ReadOnlySpan<byte> sample, bool bigEndian)
    {
        // 8-bit WAV is unsigned; everything else is signed two's complement.
        if (sample.Length == 1)
            return bigEndian ? (sbyte)sample[0] / 128f : (sample[0] - 128) / 128f;

        long value = 0;
        if (bigEndian)
        {
            for (var i = 0; i < sample.Length; i++)
                value = (value << 8) | sample[i];
        }
        else
        {
            for (var i = sample.Length - 1; i >= 0; i--)
                value = (value << 8) | sample[i];
        }

        var bits = sample.Length * 8;
        value = (value << (64 - bits)) >> (64 - bits); // sign extend
        return (float)(value / Math.Pow(2, bits - 1));
    }

    private static void ReadInfoList(ReadOnlySpan<byte> list, AudioMetadata metadata)
    {
        var position = 0;
        while (position + 8 <= list.Length)
        {
            var id = Ascii(list, position);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(list[(position + 4)..]);
            if (size < 0 || position + 8 + size > list.Length)
                break;
            var text = Encoding.UTF8.GetString(list.Slice(position + 8, size)).Trim('\0', ' ');
            if (text.Length > 0)
            {
                switch (id)
                {
                    case "INAM":
                        metadata.Title ??= text;
                        break;
                    case "IART":
                        metadata.Artist ??= text;
                        break;
                    case "IPRD":
                        metadata.Album ??= text;
                        break;
                }
            }

            position += 8 + size + (size & 1);
        }
    }

    internal static string Ascii(ReadOnlySpan<byte> data, int offset) =>
        offset + 4 <= data.Length ? Encoding.ASCII.GetString(data.Slice(offset, 4)) : string.Empty;
}

/// <summary>Reads uncompressed AIFF and AIFF-C (NONE, sowt, fl32, fl64).</summary>
public static class AiffReader
{
    public static bool IsAiff(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && WavReader.Ascii(header, 0) == "FORM" && WavReader.Ascii(header, 8) is "AIFF" or "AIFC";

    public static AudioBuffer Read(ReadOnlySpan<byte> file)
    {
        if (!IsAiff(file))
            throw new InvalidDataException("This is not an AIFF file.");

        int channels = 0,
            bits = 0,
            sampleRate = 0;
        long frames = 0;
        var compression = "NONE";
        var soundOffset = -1;
        var soundLength = 0;
        var metadata = new AudioMetadata();

        var position = 12;
        while (position + 8 <= file.Length)
        {
            var id = WavReader.Ascii(file, position);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(file[(position + 4)..]), int.MaxValue);
            var body = position + 8;
            var available = Math.Min(size, file.Length - body);
            var chunk = file.Slice(body, Math.Max(0, available));

            switch (id)
            {
                case "COMM" when chunk.Length >= 18:
                    channels = BinaryPrimitives.ReadInt16BigEndian(chunk);
                    frames = BinaryPrimitives.ReadUInt32BigEndian(chunk[2..]);
                    bits = BinaryPrimitives.ReadInt16BigEndian(chunk[6..]);
                    sampleRate = (int)Math.Round(ReadExtended(chunk.Slice(8, 10)));
                    if (chunk.Length >= 22)
                        compression = WavReader.Ascii(chunk, 18);
                    break;
                case "SSND" when chunk.Length >= 8:
                    var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk);
                    soundOffset = body + 8 + offset;
                    soundLength = Math.Max(0, available - 8 - offset);
                    break;
                case "NAME":
                    metadata.Title ??= Encoding.Latin1.GetString(chunk).Trim('\0', ' ');
                    break;
                case "AUTH":
                    metadata.Artist ??= Encoding.Latin1.GetString(chunk).Trim('\0', ' ');
                    break;
                case "ID3 ":
                case "id3 ":
                    metadata.MergeMissing(Id3v2Reader.Read(chunk));
                    break;
            }

            position = body + size + (size & 1);
        }

        if (soundOffset < 0 || channels <= 0 || sampleRate <= 0)
            throw new InvalidDataException("The AIFF file has no audio data.");

        var (formatTag, bigEndian) = compression switch
        {
            "NONE" or "twos" => (1, true),
            "sowt" => (1, false),
            "fl32" or "FL32" => (3, true),
            "fl64" or "FL64" => (3, true),
            _ => throw new NotSupportedException($"AIFF-C compression '{compression}' needs FFmpeg to decode."),
        };
        if (compression is "fl64" or "FL64")
            bits = 64;
        else if (compression is "fl32" or "FL32")
            bits = 32;

        var bytesPerSample = (bits + 7) / 8;
        var blockAlign = bytesPerSample * channels;
        var length = (int)Math.Min(soundLength, frames * blockAlign);
        var samples = WavReader.DecodeSamples(file.Slice(soundOffset, length), formatTag, channels, bits, blockAlign, bigEndian);
        return new AudioBuffer(samples, sampleRate) { Metadata = metadata };
    }

    /// <summary>Decodes the 80-bit IEEE 754 extended precision number AIFF uses for its sample rate.</summary>
    private static double ReadExtended(ReadOnlySpan<byte> bytes)
    {
        var exponent = ((bytes[0] & 0x7F) << 8) | bytes[1];
        var mantissa = BinaryPrimitives.ReadUInt64BigEndian(bytes[2..]);
        if (exponent == 0 && mantissa == 0)
            return 0;
        var value = mantissa * Math.Pow(2, exponent - 16383 - 63);
        return (bytes[0] & 0x80) != 0 ? -value : value;
    }
}

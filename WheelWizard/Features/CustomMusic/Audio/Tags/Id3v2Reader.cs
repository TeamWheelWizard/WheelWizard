using System.Text;

namespace WheelWizard.CustomMusic.Audio.Tags;

/// <summary>Reads title, artist, album, cover art and loop hints from ID3v2.2/2.3/2.4 tags.</summary>
public static class Id3v2Reader
{
    /// <summary>Size of the ID3v2 tag at the start of <paramref name="data"/> (header + footer), or 0 if there is none.</summary>
    public static int GetTagSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10 || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
            return 0;

        var size = SyncSafe(data.Slice(6, 4));
        var hasFooter = (data[5] & 0x10) != 0;
        return 10 + size + (hasFooter ? 10 : 0);
    }

    public static AudioMetadata Read(ReadOnlySpan<byte> data)
    {
        var metadata = new AudioMetadata();
        var tagSize = GetTagSize(data);
        if (tagSize == 0 || tagSize > data.Length)
            return metadata;

        int version = data[3];
        var flags = data[5];
        var body = data.Slice(10, tagSize - 10 - ((flags & 0x10) != 0 ? 10 : 0)).ToArray();

        // Tag-level unsynchronisation (mostly v2.2/2.3): undo the 0xFF 0x00 escaping first.
        if ((flags & 0x80) != 0 && version < 4)
            body = RemoveUnsynchronisation(body);

        var position = 0;
        if ((flags & 0x40) != 0 && version >= 3 && body.Length >= 4)
        {
            // Skip the extended header.
            var extendedSize = version == 4 ? SyncSafe(body.AsSpan(0, 4)) : ReadBigEndian(body, 0, 4) + 4;
            position = Math.Clamp(extendedSize, 0, body.Length);
        }

        var idLength = version == 2 ? 3 : 4;
        var headerLength = version == 2 ? 6 : 10;
        var bestPictureScore = -1;

        while (position + headerLength <= body.Length)
        {
            if (body[position] == 0)
                break; // padding

            var id = Encoding.ASCII.GetString(body, position, idLength);
            int frameSize;
            var frameFlags = 0;
            if (version == 2)
                frameSize = ReadBigEndian(body, position + 3, 3);
            else if (version == 4)
            {
                frameSize = SyncSafe(body.AsSpan(position + 4, 4));
                frameFlags = (body[position + 8] << 8) | body[position + 9];
            }
            else
            {
                frameSize = ReadBigEndian(body, position + 4, 4);
                frameFlags = (body[position + 8] << 8) | body[position + 9];
            }

            position += headerLength;
            if (frameSize <= 0 || position + frameSize > body.Length)
                break;

            var frame = body.AsSpan(position, frameSize).ToArray();
            position += frameSize;

            // v2.4 frame flags: unsynchronisation (0x0002) and data length indicator (0x0001).
            if (version == 4)
            {
                var offset = 0;
                if ((frameFlags & 0x0001) != 0 && frame.Length >= 4)
                    offset = 4;
                if ((frameFlags & 0x0002) != 0)
                    frame = RemoveUnsynchronisation(frame[offset..]);
                else if (offset > 0)
                    frame = frame[offset..];
            }

            // Compressed or encrypted frames are not worth supporting here.
            if (version == 3 && (frameFlags & 0x00C0) != 0)
                continue;
            if (version == 4 && (frameFlags & 0x000C) != 0)
                continue;

            switch (id)
            {
                case "TIT2":
                case "TT2":
                    metadata.Title ??= ReadText(frame);
                    break;
                case "TPE1":
                case "TP1":
                    metadata.Artist ??= ReadText(frame);
                    break;
                case "TPE2":
                case "TP2":
                    // Album artist is a decent fallback when there is no track artist.
                    metadata.Artist ??= ReadText(frame);
                    break;
                case "TALB":
                case "TAL":
                    metadata.Album ??= ReadText(frame);
                    break;
                case "TXXX":
                case "TXX":
                    ReadUserText(frame, metadata);
                    break;
                case "APIC":
                case "PIC":
                {
                    var picture = ReadPicture(frame, id == "PIC", out var pictureType);
                    if (picture == null)
                        break;
                    // Prefer the front cover (type 3), then "other" (0), then anything.
                    var score = pictureType == 3 ? 2 : pictureType == 0 ? 1 : 0;
                    if (score > bestPictureScore)
                    {
                        bestPictureScore = score;
                        metadata.Cover = picture;
                    }

                    break;
                }
            }
        }

        return metadata;
    }

    private static void ReadUserText(byte[] frame, AudioMetadata metadata)
    {
        if (frame.Length < 2)
            return;

        var encoding = frame[0];
        var (description, next) = ReadTerminated(frame, 1, encoding);
        var value = Decode(frame.AsSpan(next), encoding).Trim('\0', ' ');
        if (!long.TryParse(value, out var number))
            return;

        switch (description.Trim().ToUpperInvariant())
        {
            case "LOOPSTART":
            case "LOOP_START":
                metadata.LoopStartTag = number;
                break;
            case "LOOPLENGTH":
            case "LOOP_LENGTH":
                metadata.LoopLengthTag = number;
                break;
            case "LOOPEND":
            case "LOOP_END":
                metadata.LoopEndTag = number;
                break;
        }
    }

    private static CoverArt? ReadPicture(byte[] frame, bool isV22, out int pictureType)
    {
        pictureType = -1;
        if (frame.Length < 4)
            return null;

        var encoding = frame[0];
        string mime;
        int position;
        if (isV22)
        {
            // v2.2 uses a 3-character image format instead of a MIME type.
            var format = Encoding.ASCII.GetString(frame, 1, 3).ToUpperInvariant();
            mime = format == "PNG" ? "image/png" : "image/jpeg";
            position = 4;
        }
        else
        {
            var mimeEnd = Array.IndexOf(frame, (byte)0, 1);
            if (mimeEnd < 0)
                return null;
            mime = Encoding.Latin1.GetString(frame, 1, mimeEnd - 1);
            position = mimeEnd + 1;
        }

        if (position >= frame.Length)
            return null;

        pictureType = frame[position];
        var (_, dataStart) = ReadTerminated(frame, position + 1, encoding);
        if (dataStart >= frame.Length)
            return null;

        return CoverArt.FromBytes(frame[dataStart..], mime);
    }

    private static string? ReadText(byte[] frame)
    {
        if (frame.Length < 2)
            return null;

        var text = Decode(frame.AsSpan(1), frame[0]);
        // Multiple values are NUL separated; the first one is what players show.
        var first = text.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    private static (string Text, int Next) ReadTerminated(byte[] data, int start, byte encoding)
    {
        var wide = encoding is 1 or 2;
        var position = start;
        if (wide)
        {
            while (position + 1 < data.Length && (data[position] != 0 || data[position + 1] != 0))
                position += 2;
            var text = Decode(data.AsSpan(start, Math.Max(0, position - start)), encoding);
            return (text, Math.Min(data.Length, position + 2));
        }

        while (position < data.Length && data[position] != 0)
            position++;
        var narrow = Decode(data.AsSpan(start, position - start), encoding);
        return (narrow, Math.Min(data.Length, position + 1));
    }

    private static string Decode(ReadOnlySpan<byte> data, byte encoding)
    {
        return encoding switch
        {
            1 => DecodeUtf16WithBom(data),
            2 => Encoding.BigEndianUnicode.GetString(data),
            3 => Encoding.UTF8.GetString(data),
            _ => Encoding.Latin1.GetString(data),
        };
    }

    private static string DecodeUtf16WithBom(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data[2..]);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data[2..]);
        return Encoding.Unicode.GetString(data);
    }

    private static byte[] RemoveUnsynchronisation(byte[] data)
    {
        var output = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            output.Add(data[i]);
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0x00)
                i++;
        }

        return output.ToArray();
    }

    private static int SyncSafe(ReadOnlySpan<byte> bytes) => (bytes[0] & 0x7F) << 21 | (bytes[1] & 0x7F) << 14 | (bytes[2] & 0x7F) << 7 | (bytes[3] & 0x7F);

    private static int ReadBigEndian(byte[] data, int offset, int length)
    {
        var value = 0;
        for (var i = 0; i < length; i++)
            value = (value << 8) | data[offset + i];
        return value;
    }
}

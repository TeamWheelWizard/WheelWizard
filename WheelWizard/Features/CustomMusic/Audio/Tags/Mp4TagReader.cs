using System.Buffers.Binary;
using System.Text;

namespace WheelWizard.CustomMusic.Audio.Tags;

/// <summary>Reads iTunes-style metadata (moov/udta/meta/ilst) from MP4/M4A files: title, artist, album and cover art.</summary>
public static class Mp4TagReader
{
    public static bool IsMp4(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p';

    public static AudioMetadata Read(Stream stream)
    {
        var metadata = new AudioMetadata();
        try
        {
            var moov = FindTopLevelAtom(stream, "moov");
            if (moov == null)
                return metadata;

            var ilst = FindPath(moov, ["udta", "meta", "ilst"]);
            if (ilst == null)
                return metadata;

            foreach (var (type, payload) in Children(ilst, 0, ilst.Length))
            {
                var data = Children(payload, 0, payload.Length).FirstOrDefault(child => child.Type == "data").Payload;
                if (data == null || data.Length < 8)
                    continue;

                var value = data.AsSpan(8);
                switch (type)
                {
                    case "©nam":
                        metadata.Title ??= Encoding.UTF8.GetString(value).Trim('\0', ' ');
                        break;
                    case "©ART":
                    case "aART":
                        metadata.Artist ??= Encoding.UTF8.GetString(value).Trim('\0', ' ');
                        break;
                    case "©alb":
                        metadata.Album ??= Encoding.UTF8.GetString(value).Trim('\0', ' ');
                        break;
                    case "covr":
                        metadata.Cover ??= CoverArt.FromBytes(value.ToArray());
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or EndOfStreamException)
        {
            // Broken metadata should never stop the audio from importing.
        }

        return metadata;
    }

    private static byte[]? FindTopLevelAtom(Stream stream, string wanted)
    {
        stream.Position = 0;
        Span<byte> header = stackalloc byte[16];
        while (stream.Position + 8 <= stream.Length)
        {
            var start = stream.Position;
            if (stream.Read(header[..8]) < 8)
                return null;

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.Latin1.GetString(header.Slice(4, 4));
            var headerSize = 8;
            if (size == 1)
            {
                if (stream.Read(header.Slice(8, 8)) < 8)
                    return null;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = stream.Length - start;
            }

            if (size < headerSize || start + size > stream.Length)
                return null;

            if (type == wanted)
            {
                // moov is small (a few MB at most, mostly the cover art), so reading it whole is fine.
                if (size > 64 * 1024 * 1024)
                    return null;
                var payload = new byte[size - headerSize];
                stream.ReadExactly(payload);
                return payload;
            }

            stream.Position = start + size;
        }

        return null;
    }

    private static byte[]? FindPath(byte[] parent, string[] path)
    {
        var current = parent;
        foreach (var name in path)
        {
            var child = Children(current, 0, current.Length).FirstOrDefault(atom => atom.Type == name).Payload;
            if (child == null)
                return null;

            // "meta" is a full box: 4 bytes of version/flags precede its children.
            current = name == "meta" && child.Length >= 4 ? child[4..] : child;
        }

        return current;
    }

    private static IEnumerable<(string Type, byte[] Payload)> Children(byte[] data, int start, int end)
    {
        var position = start;
        while (position + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            var type = Encoding.Latin1.GetString(data, position + 4, 4);
            var headerSize = 8;
            if (size == 1 && position + 16 <= end)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(position + 8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = end - position;
            }

            if (size < headerSize || position + size > end)
                yield break;

            yield return (type, data.AsSpan(position + headerSize, (int)size - headerSize).ToArray());
            position += (int)size;
        }
    }
}

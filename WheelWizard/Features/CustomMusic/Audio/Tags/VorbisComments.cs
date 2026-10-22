using System.Buffers.Binary;
using System.Text;

namespace WheelWizard.CustomMusic.Audio.Tags;

/// <summary>Vorbis comments (used by FLAC, Ogg Vorbis and Opus) and the FLAC PICTURE structure they embed.</summary>
public static class VorbisComments
{
    /// <summary>Parses a Vorbis comment block (vendor string + list of KEY=value) in little endian.</summary>
    public static AudioMetadata ParseBlock(ReadOnlySpan<byte> block)
    {
        var comments = new List<string>();
        try
        {
            var position = 0;
            var vendorLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[position..]);
            position += 4 + vendorLength;
            var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[position..]);
            position += 4;
            for (var i = 0; i < count && position + 4 <= block.Length; i++)
            {
                var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[position..]);
                position += 4;
                if (length < 0 || position + length > block.Length)
                    break;
                comments.Add(Encoding.UTF8.GetString(block.Slice(position, length)));
                position += length;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Truncated block: keep whatever was read.
        }

        return FromComments(comments);
    }

    public static AudioMetadata FromComments(IEnumerable<string> comments)
    {
        var metadata = new AudioMetadata();
        foreach (var comment in comments)
        {
            var separator = comment.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = comment[..separator].Trim().ToUpperInvariant();
            var value = comment[(separator + 1)..].Trim();
            if (value.Length == 0)
                continue;

            switch (key)
            {
                case "TITLE":
                    metadata.Title ??= value;
                    break;
                case "ARTIST":
                    metadata.Artist ??= value;
                    break;
                case "ALBUMARTIST":
                case "ALBUM ARTIST":
                    metadata.Artist ??= value;
                    break;
                case "ALBUM":
                    metadata.Album ??= value;
                    break;
                case "LOOPSTART":
                case "LOOP_START":
                    if (long.TryParse(value, out var start))
                        metadata.LoopStartTag ??= start;
                    break;
                case "LOOPLENGTH":
                case "LOOP_LENGTH":
                    if (long.TryParse(value, out var length))
                        metadata.LoopLengthTag ??= length;
                    break;
                case "LOOPEND":
                case "LOOP_END":
                    if (long.TryParse(value, out var end))
                        metadata.LoopEndTag ??= end;
                    break;
                case "METADATA_BLOCK_PICTURE":
                    if (metadata.Cover == null && TryFromBase64(value, out var pictureBlock))
                        metadata.Cover = ParsePicture(pictureBlock, out _);
                    break;
                case "COVERART":
                    // Legacy, pre-standard way of embedding art: plain base64 image bytes.
                    if (metadata.Cover == null && TryFromBase64(value, out var image))
                        metadata.Cover = CoverArt.FromBytes(image);
                    break;
            }
        }

        return metadata;
    }

    /// <summary>Parses a FLAC PICTURE block (big endian), as stored in FLAC metadata or base64 in Vorbis comments.</summary>
    public static CoverArt? ParsePicture(ReadOnlySpan<byte> block, out int pictureType)
    {
        pictureType = -1;
        try
        {
            var position = 0;
            pictureType = (int)BinaryPrimitives.ReadUInt32BigEndian(block[position..]);
            position += 4;
            var mimeLength = (int)BinaryPrimitives.ReadUInt32BigEndian(block[position..]);
            position += 4;
            var mime = Encoding.ASCII.GetString(block.Slice(position, mimeLength));
            position += mimeLength;
            var descriptionLength = (int)BinaryPrimitives.ReadUInt32BigEndian(block[position..]);
            position += 4 + descriptionLength;
            position += 16; // width, height, depth, colour count
            var dataLength = (int)BinaryPrimitives.ReadUInt32BigEndian(block[position..]);
            position += 4;
            if (dataLength <= 0 || position + dataLength > block.Length)
                return null;
            return CoverArt.FromBytes(block.Slice(position, dataLength).ToArray(), mime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}

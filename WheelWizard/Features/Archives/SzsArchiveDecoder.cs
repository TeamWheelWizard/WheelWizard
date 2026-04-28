using WheelWizard.Helpers;

namespace WheelWizard.Features.Archives;

public sealed class SzsArchiveDecoder : ISzsArchiveDecoder
{
    private const uint U8Magic = 0x55aa382d;

    public OperationResult<DecodedArchive> TryDecodeU8Archive(byte[] bytes)
    {
        var decompressResult = DecompressYaz0IfNeeded(bytes);
        if (decompressResult.IsFailure)
            return new OperationError { Message = decompressResult.Error.Message };

        var raw = decompressResult.Value;

        if (BigEndianBinaryHelper.BufferToUint32(raw, 0) != U8Magic)
            return new OperationError { Message = "The provided file is not a valid Yaz0/U8 archive." };
        if (raw.Length < 8)
            return new OperationError { Message = "The provided file is too small to contain a valid U8 archive header." };
        return ParseU8Archive(raw);
    }

    public OperationResult<byte[]> DecompressYaz0IfNeeded(byte[] bytes)
    {
        if (bytes.Length < 4)
            return bytes;

        if (BinaryStringHelper.ReadAscii(bytes, 0, 4) != "Yaz0")
            return bytes;

        if (bytes.Length < 8)
            return new OperationError { Message = "Yaz0 header is truncated." };

        var outputSize = checked((int)BigEndianBinaryHelper.BufferToUint32(bytes, 4));
        var output = new byte[outputSize];
        var src = 0x10;
        var dst = 0;
        var groupHeader = 0;
        var bitsRemaining = 0;

        while (dst < output.Length)
        {
            if (bitsRemaining == 0)
            {
                if (src >= bytes.Length)
                    return new OperationError { Message = "Yaz0 group header is truncated." };
                groupHeader = bytes[src++];
                bitsRemaining = 8;
            }

            if ((groupHeader & 0x80) != 0)
            {
                if (src >= bytes.Length)
                    return new OperationError { Message = "Yaz0 literal chunk is truncated." };
                output[dst++] = bytes[src++];
            }
            else
            {
                if (src + 1 >= bytes.Length)
                    return new OperationError { Message = "Yaz0 backreference is truncated." };

                var b1 = bytes[src++];
                var b2 = bytes[src++];
                var backOffset = (((b1 & 0x0f) << 8) | b2) + 1;
                var length = b1 >> 4;
                if (length == 0)
                {
                    if (src >= bytes.Length)
                        return new OperationError { Message = "Yaz0 extended length byte is truncated." };
                    length = bytes[src++] + 0x12;
                }
                else
                {
                    length += 2;
                }

                if (backOffset > dst)
                    return new OperationError { Message = "Yaz0 backreference offset is out of bounds." };

                var copySrc = dst - backOffset;
                for (var index = 0; index < length && dst < output.Length; index++)
                    output[dst++] = output[copySrc++];
            }

            groupHeader <<= 1;
            bitsRemaining--;
        }

        return output;
    }

    private static DecodedArchive ParseU8Archive(byte[] bytes)
    {
        var rootOffset = (int)BigEndianBinaryHelper.BufferToUint32(bytes, 4);
        var rootNode = ReadU8Node(bytes, rootOffset);
        var nodeCount = rootNode.Size;
        var stringTableOffset = rootOffset + nodeCount * 12;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (rootNode.Type != 1)
            throw new InvalidDataException("U8 root node is not a directory.");
        if (nodeCount == 0 || stringTableOffset > bytes.Length)
            throw new InvalidDataException("U8 node table is invalid or truncated.");

        void Walk(int dirIndex, string prefix, int parentEndIndex)
        {
            var nodeIndex = dirIndex + 1;
            var directoryNode = ReadU8Node(bytes, rootOffset + dirIndex * 12);
            var endIndex = Math.Min(directoryNode.Size, parentEndIndex);

            if (endIndex <= dirIndex)
                return;

            while (nodeIndex < endIndex)
            {
                var nodeOffset = rootOffset + nodeIndex * 12;
                var node = ReadU8Node(bytes, nodeOffset);
                var nameOffset = stringTableOffset + node.NameOffset;

                if (nameOffset < stringTableOffset || nameOffset >= bytes.Length)
                {
                    nodeIndex++;
                    continue;
                }

                var name = BinaryStringHelper.ReadNullTerminatedAscii(bytes, nameOffset);
                var logicalPath = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";

                if (node.Type == 1)
                {
                    Walk(nodeIndex, logicalPath, endIndex);
                    nodeIndex = Math.Min(Math.Max(node.Size, nodeIndex + 1), endIndex);
                }
                else
                {
                    var endOffset = node.DataOffset + node.Size;
                    if (endOffset <= bytes.Length && endOffset >= node.DataOffset)
                        files[logicalPath] = bytes[node.DataOffset..endOffset];
                    nodeIndex++;
                }
            }
        }

        Walk(0, string.Empty, nodeCount);
        return new(files);
    }

    private static U8Node ReadU8Node(byte[] bytes, int offset)
    {
        if (offset + 12 > bytes.Length)
            throw new InvalidDataException("U8 node table is truncated.");

        return new(
            bytes[offset] == 0 ? 0 : 1,
            (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3],
            (int)BigEndianBinaryHelper.BufferToUint32(bytes, offset + 4),
            (int)BigEndianBinaryHelper.BufferToUint32(bytes, offset + 8)
        );
    }
}

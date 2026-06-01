using System.Text;
using WheelWizard.Helpers;

namespace WheelWizard.Features.Archives;

public static class U8ArchiveBuilder
{
    private const uint U8Magic = 0x55aa382d;
    private static readonly Encoding Utf8 = Encoding.UTF8;

    public static byte[] Build(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        var root = new BuildDirectory(string.Empty);

        foreach (var (path, bytes) in entries)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                throw new InvalidDataException("Cannot add an empty path to a U8 archive.");

            var directory = root;
            foreach (var segment in segments[..^1])
            {
                if (!directory.Children.TryGetValue(segment, out var child))
                {
                    child = new BuildDirectory(segment);
                    directory.Children[segment] = child;
                }

                if (child is not BuildDirectory childDirectory)
                    throw new InvalidDataException($"{path} conflicts with an archive file path.");

                directory = childDirectory;
            }

            directory.Children[segments[^1]] = new BuildFile(segments[^1], bytes);
        }

        var nodes = new List<BuildNode>();
        var strings = new List<byte> { 0 };

        int EncodeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            var offset = strings.Count;
            strings.AddRange(Utf8.GetBytes(name));
            strings.Add(0);
            return offset;
        }

        int EmitNode(BuildEntry entry, int parentIndex)
        {
            var index = nodes.Count;
            nodes.Add(
                new BuildNode
                {
                    IsDirectory = entry is BuildDirectory,
                    NameOffset = EncodeName(entry.Name),
                    DataOffset = entry is BuildDirectory ? parentIndex : 0,
                    Size = entry is BuildFile file ? file.Bytes.Length : 0,
                    Bytes = entry is BuildFile fileEntry ? fileEntry.Bytes : null,
                }
            );

            if (entry is BuildDirectory directory)
            {
                foreach (var child in directory.Children.Values.OrderBy(child => child.Name, StringComparer.Ordinal))
                    EmitNode(child, index);

                nodes[index].Size = nodes.Count;
            }

            return index;
        }

        EmitNode(root, 0);

        var nodeTableSize = nodes.Count * 12;
        var combinedNodeSize = nodeTableSize + strings.Count;
        const int rootOffset = 0x20;
        var writeOffset = Align32(rootOffset + combinedNodeSize);

        foreach (var node in nodes.Where(node => !node.IsDirectory))
        {
            node.DataOffset = writeOffset;
            writeOffset += Align32(node.Size);
        }

        var output = new byte[writeOffset];
        BigEndianBinaryHelper.WriteUInt32BigEndian(output, 0x00, U8Magic);
        BigEndianBinaryHelper.WriteUInt32BigEndian(output, 0x04, rootOffset);
        BigEndianBinaryHelper.WriteUInt32BigEndian(output, 0x08, (uint)combinedNodeSize);
        BigEndianBinaryHelper.WriteUInt32BigEndian(output, 0x0c, (uint)Align32(rootOffset + combinedNodeSize));

        var nodeOffset = rootOffset;
        foreach (var node in nodes)
        {
            output[nodeOffset] = node.IsDirectory ? (byte)1 : (byte)0;
            output[nodeOffset + 1] = (byte)((node.NameOffset >> 16) & 0xff);
            output[nodeOffset + 2] = (byte)((node.NameOffset >> 8) & 0xff);
            output[nodeOffset + 3] = (byte)(node.NameOffset & 0xff);
            BigEndianBinaryHelper.WriteUInt32BigEndian(output, nodeOffset + 4, (uint)node.DataOffset);
            BigEndianBinaryHelper.WriteUInt32BigEndian(output, nodeOffset + 8, (uint)node.Size);
            nodeOffset += 12;
        }

        strings.CopyTo(output, rootOffset + nodeTableSize);
        foreach (var node in nodes.Where(node => !node.IsDirectory && node.Bytes != null))
            Buffer.BlockCopy(node.Bytes!, 0, output, node.DataOffset, node.Bytes!.Length);

        return output;
    }

    public static byte[] BuildYaz0(IEnumerable<KeyValuePair<string, byte[]>> entries) => EncodeYaz0(Build(entries));

    private static int Align32(int value) => (value + 0x1f) & ~0x1f;

    private static byte[] EncodeYaz0(byte[] input)
    {
        var output = new List<byte>(input.Length);
        output.AddRange(
            [
                0x59,
                0x61,
                0x7a,
                0x30,
                (byte)((input.Length >> 24) & 0xff),
                (byte)((input.Length >> 16) & 0xff),
                (byte)((input.Length >> 8) & 0xff),
                (byte)(input.Length & 0xff),
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
            ]
        );

        var head = new int[1 << 16];
        var previous = new int[input.Length];
        Array.Fill(head, -1);
        Array.Fill(previous, -1);

        int HashAt(int offset) =>
            offset + 2 < input.Length ? ((input[offset] << 8) ^ (input[offset + 1] << 4) ^ input[offset + 2]) & 0xffff : -1;

        void Insert(int offset)
        {
            var hash = HashAt(offset);
            if (hash < 0)
                return;

            previous[offset] = head[hash];
            head[hash] = offset;
        }

        (int Length, int Distance) FindMatch(int offset)
        {
            var hash = HashAt(offset);
            if (hash < 0)
                return (0, 0);

            var bestLength = 0;
            var bestDistance = 0;
            var candidate = head[hash];
            var searched = 0;
            var maxLength = Math.Min(0x111, input.Length - offset);

            while (candidate >= 0 && searched < 256)
            {
                var distance = offset - candidate;
                if (distance > 0x1000)
                    break;

                var length = 0;
                while (length < maxLength && input[candidate + length] == input[offset + length])
                    length++;

                if (length > bestLength && length >= 3)
                {
                    bestLength = length;
                    bestDistance = distance;
                    if (length == maxLength)
                        break;
                }

                candidate = previous[candidate];
                searched++;
            }

            return (bestLength, bestDistance);
        }

        var src = 0;
        while (src < input.Length)
        {
            var codeOffset = output.Count;
            output.Add(0);
            byte code = 0;

            for (var bit = 0; bit < 8 && src < input.Length; bit++)
            {
                var match = FindMatch(src);
                if (match.Length >= 3)
                {
                    var distance = match.Distance - 1;
                    if (match.Length >= 0x12)
                    {
                        output.Add((byte)((distance >> 8) & 0x0f));
                        output.Add((byte)(distance & 0xff));
                        output.Add((byte)(match.Length - 0x12));
                    }
                    else
                    {
                        output.Add((byte)(((match.Length - 2) << 4) | ((distance >> 8) & 0x0f)));
                        output.Add((byte)(distance & 0xff));
                    }

                    for (var index = 0; index < match.Length; index++)
                        Insert(src + index);
                    src += match.Length;
                }
                else
                {
                    code |= (byte)(0x80 >> bit);
                    output.Add(input[src]);
                    Insert(src);
                    src++;
                }
            }

            output[codeOffset] = code;
        }

        return output.ToArray();
    }

    private abstract record BuildEntry(string Name);

    private sealed record BuildDirectory(string Name) : BuildEntry(Name)
    {
        public SortedDictionary<string, BuildEntry> Children { get; } = new(StringComparer.Ordinal);
    }

    private sealed record BuildFile(string Name, byte[] Bytes) : BuildEntry(Name);

    private sealed class BuildNode
    {
        public bool IsDirectory { get; init; }
        public int NameOffset { get; init; }
        public int DataOffset { get; set; }
        public int Size { get; set; }
        public byte[]? Bytes { get; init; }
    }
}

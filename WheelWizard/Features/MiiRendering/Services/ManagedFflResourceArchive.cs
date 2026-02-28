using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace WheelWizard.MiiRendering.Services;

internal sealed class ManagedFflResourceArchive
{
    private const uint ExpectedMagic = 0x46465241; // FFRA
    private const uint ExpectedVersion = 0x00070000;

    private const int HeaderSizeDefault = 0x4A00;
    private const int TextureHeaderOffset = 0x14;

    private static readonly int[] TexturePartCountsDefault =
    [
        3, // beard
        132, // cap
        62, // eye
        24, // eyebrow
        12, // faceline
        12, // face makeup
        9, // glass
        2, // mole
        37, // mouth
        6, // mustache
        18, // noseline
    ];

    private static readonly int[] TexturePartCountsAfl =
    [
        3, // beard
        132, // cap
        80, // eye
        28, // eyebrow
        12, // faceline
        12, // face makeup
        9, // glass
        2, // mole
        52, // mouth
        6, // mustache
        18, // noseline
    ];

    private static readonly int[] TexturePartCountsAfl23 =
    [
        3, // beard
        132, // cap
        80, // eye
        28, // eyebrow
        12, // faceline
        12, // face makeup
        20, // glass
        2, // mole
        52, // mouth
        6, // mustache
        18, // noseline
    ];

    private const uint ExpandedSizeAfl = 0x0239D5E0;
    private const uint ExpandedSizeAfl23 = 0x02502DE0;

    private static readonly int[] ShapePartCounts =
    [
        4, // beard
        132, // hat normal
        132, // hat cap
        12, // faceline
        1, // glass
        12, // mask
        18, // noseline
        18, // nose
        132, // hair normal
        132, // hair cap
        132, // forehead normal
        132, // forehead cap
    ];

    private readonly byte[] _bytes;
    private readonly bool _bigEndian;
    private readonly ResourcePartInfo[][] _textureParts;
    private readonly ResourcePartInfo[][] _shapeParts;

    private readonly ConcurrentDictionary<(bool IsShape, int PartType, int Index), byte[]> _decodedPartCache = new();

    private ManagedFflResourceArchive(
        byte[] bytes,
        bool bigEndian,
        ResourcePartInfo[][] textureParts,
        ResourcePartInfo[][] shapeParts,
        bool textureFormatIsLinear,
        bool ignoreMipMaps,
        bool isHalfFloatLayout
    )
    {
        _bytes = bytes;
        _bigEndian = bigEndian;
        _textureParts = textureParts;
        _shapeParts = shapeParts;
        TextureFormatIsLinear = textureFormatIsLinear;
        IgnoreMipMaps = ignoreMipMaps;
        IsHalfFloatLayout = isHalfFloatLayout;
    }

    public bool TextureFormatIsLinear { get; }
    public bool IgnoreMipMaps { get; }
    public bool IsHalfFloatLayout { get; }

    public static OperationResult<ManagedFflResourceArchive> Load(string resourcePath)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(resourcePath);
        }
        catch (Exception exception)
        {
            return Fail($"Failed to read FFL resource file '{resourcePath}': {exception.Message}");
        }

        if (bytes.Length < HeaderSizeDefault)
            return Fail($"FFL resource file '{resourcePath}' is too small ({bytes.Length} bytes).");

        var beMagic = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4));
        var leMagic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));

        bool bigEndian;
        if (beMagic == ExpectedMagic)
            bigEndian = true;
        else if (leMagic == ExpectedMagic)
            bigEndian = false;
        else
            return Fail($"FFL resource has invalid magic 0x{beMagic:X8}/0x{leMagic:X8}.");

        var version = ReadU32(bytes, 4, bigEndian);
        if (version != ExpectedVersion)
            return Fail($"FFL resource has unsupported version 0x{version:X8}. Expected 0x{ExpectedVersion:X8}.");

        var expandedBufferSize = ReadU32(bytes, 12, bigEndian);
        var isExpand = ReadU32(bytes, 16, bigEndian);
        var isHalfFloatLayout = isExpand == 0x841F10A7;

        var resourceHint = expandedBufferSize >> 29;
        var expandedSizeNoHint = expandedBufferSize & 0x1FFFFFFF;

        var isAfl23 = resourceHint == 3 || expandedSizeNoHint == ExpandedSizeAfl23;
        var isAfl = isAfl23 || resourceHint == 2 || expandedSizeNoHint == ExpandedSizeAfl;

        var textureCounts =
            isAfl23 ? TexturePartCountsAfl23
            : isAfl ? TexturePartCountsAfl
            : TexturePartCountsDefault;
        var texturePartsTableOffset = TextureHeaderOffset + textureCounts.Length * 4;
        var textureParts = ParsePartsTable(bytes, bigEndian, texturePartsTableOffset, textureCounts);

        var textureInfoBytes = textureCounts.Sum(static c => c * 16);
        var textureHeaderSize = textureCounts.Length * 4 + textureInfoBytes;
        var shapeHeaderOffset = TextureHeaderOffset + textureHeaderSize;
        var shapePartsTableOffset = shapeHeaderOffset + ShapePartCounts.Length * 4;
        var shapeParts = ParsePartsTable(bytes, bigEndian, shapePartsTableOffset, ShapePartCounts);

        var textureFormatIsLinear = isAfl;
        var ignoreMipMaps = isAfl;

        return new ManagedFflResourceArchive(
            bytes,
            bigEndian,
            textureParts,
            shapeParts,
            textureFormatIsLinear,
            ignoreMipMaps,
            isHalfFloatLayout
        );
    }

    public OperationResult<byte[]> LoadShapePart(int partType, int index) => LoadPart(isShape: true, partType, index);

    public OperationResult<byte[]> LoadTexturePart(int partType, int index) => LoadPart(isShape: false, partType, index);

    private OperationResult<byte[]> LoadPart(bool isShape, int partType, int index)
    {
        if (partType < 0)
            return Fail("Invalid part type.");

        var table = isShape ? _shapeParts : _textureParts;
        if (partType >= table.Length)
            return Fail($"Part type {partType} is out of range.");

        var parts = table[partType];
        if ((uint)index >= (uint)parts.Length)
            return Fail($"Part index {index} is out of range for part type {partType}.");

        var key = (isShape, partType, index);
        if (_decodedPartCache.TryGetValue(key, out var cached))
            return cached;

        var info = parts[index];
        if (info.DataSize <= 0)
        {
            var empty = Array.Empty<byte>();
            _decodedPartCache.TryAdd(key, empty);
            return empty;
        }

        byte[] loaded;
        try
        {
            loaded = ReadAndDecodePart(info);
        }
        catch (Exception exception)
        {
            return Fail($"Failed to load part {partType}:{index}: {exception.Message}");
        }

        _decodedPartCache.TryAdd(key, loaded);
        return loaded;
    }

    private byte[] ReadAndDecodePart(ResourcePartInfo info)
    {
        if (info.DataPos < 0 || info.DataPos >= _bytes.Length)
            throw new InvalidDataException($"dataPos out of range: {info.DataPos}");

        if (info.Strategy == 5)
        {
            if (info.DataPos + info.DataSize > _bytes.Length)
                throw new InvalidDataException("Uncompressed part range is out of file bounds.");

            var raw = new byte[info.DataSize];
            Buffer.BlockCopy(_bytes, info.DataPos, raw, 0, info.DataSize);
            return raw;
        }

        if (info.CompressedSize <= 0)
            throw new InvalidDataException("Compressed part has empty compressedSize.");

        if (info.DataPos + info.CompressedSize > _bytes.Length)
            throw new InvalidDataException("Compressed part range is out of file bounds.");

        var compressed = new byte[info.CompressedSize];
        Buffer.BlockCopy(_bytes, info.DataPos, compressed, 0, info.CompressedSize);

        var decoded = Decompress(compressed, info.WindowBits);
        if (decoded.Length == info.DataSize)
            return decoded;

        if (decoded.Length > info.DataSize)
            return decoded.AsSpan(0, info.DataSize).ToArray();

        var padded = new byte[info.DataSize];
        Buffer.BlockCopy(decoded, 0, padded, 0, decoded.Length);
        return padded;
    }

    private static byte[] Decompress(byte[] compressed, byte windowBits)
    {
        if (windowBits <= 7)
            return DecompressWithZlib(compressed);

        if (windowBits is >= 8 and <= 15)
            return DecompressWithGzip(compressed);

        if (windowBits == 16)
        {
            try
            {
                return DecompressWithZlib(compressed);
            }
            catch
            {
                return DecompressWithGzip(compressed);
            }
        }

        return DecompressWithZlib(compressed);
    }

    private static byte[] DecompressWithZlib(byte[] compressed)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] DecompressWithGzip(byte[] compressed)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static ResourcePartInfo[][] ParsePartsTable(byte[] bytes, bool bigEndian, int baseOffset, int[] counts)
    {
        var result = new ResourcePartInfo[counts.Length][];
        var offset = baseOffset;

        for (var part = 0; part < counts.Length; part++)
        {
            var count = counts[part];
            var entries = new ResourcePartInfo[count];

            for (var i = 0; i < count; i++)
            {
                if (offset + 16 > bytes.Length)
                    throw new InvalidDataException("Resource header is truncated while parsing parts info.");

                entries[i] = new ResourcePartInfo(
                    DataPos: (int)ReadU32(bytes, offset + 0, bigEndian),
                    DataSize: (int)ReadU32(bytes, offset + 4, bigEndian),
                    CompressedSize: (int)ReadU32(bytes, offset + 8, bigEndian),
                    CompressLevel: bytes[offset + 12],
                    WindowBits: bytes[offset + 13],
                    MemoryLevel: bytes[offset + 14],
                    Strategy: bytes[offset + 15]
                );

                offset += 16;
            }

            result[part] = entries;
        }

        return result;
    }

    internal static uint ReadU32(byte[] bytes, int offset, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    internal static ushort ReadU16(byte[] bytes, int offset, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2))
            : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

    internal static float ReadF32(byte[] bytes, int offset, bool bigEndian)
    {
        var u = ReadU32(bytes, offset, bigEndian);
        return BitConverter.Int32BitsToSingle(unchecked((int)u));
    }

    internal bool NeedsEndianSwap() => _bigEndian;

    internal readonly record struct ResourcePartInfo(
        int DataPos,
        int DataSize,
        int CompressedSize,
        byte CompressLevel,
        byte WindowBits,
        byte MemoryLevel,
        byte Strategy
    );
}

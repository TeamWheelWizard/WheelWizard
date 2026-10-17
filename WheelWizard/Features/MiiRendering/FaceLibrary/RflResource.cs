using System.Buffers.Binary;
using System.Text;

namespace WheelWizard.MiiRendering.FaceLibrary;

/// <summary>The archives in the Wii face library's resource (RFL_Res.dat), in file order.</summary>
public enum RflArchive
{
    Beard,
    Eye,
    Eyebrow,
    Faceline,
    FaceTex,
    Forehead,
    Glass,
    GlassTex,
    Hair,
    Mask,
    Mole,
    Mouth,
    Mustache,
    Nose,
    Noseline,
    NoselineTex,
    Cap,
    CapTex,
}

/// <summary>
/// The Wii's Mii face library resource (RFL_Res.dat): every Mii part shape and texture, as games ship it in
/// <c>RFLRes01.arc</c>. Layout from the RVLFaceLib decompilation (RFL_NANDLoader.c).
/// </summary>
public sealed class RflResource
{
    /// <summary>Where Mario Kart Wii keeps the face library on its disc.</summary>
    public const string DiscPath = "contents/RFLRes01.arc";

    private const string ResourceFileName = "RFL_Res.dat";
    private const ushort MinimumVersion = 0x39D;

    private readonly byte[] _bytes;
    private readonly (int Count, int Offset)[] _archives;

    private RflResource(byte[] bytes, (int Count, int Offset)[] archives)
    {
        _bytes = bytes;
        _archives = archives;
    }

    public int Count(RflArchive archive) => _archives[(int)archive].Count;

    /// <summary>One part file, or an empty span when <paramref name="index"/> is out of range.</summary>
    public ReadOnlySpan<byte> Get(RflArchive archive, int index)
    {
        var (count, offset) = _archives[(int)archive];
        if ((uint)index >= (uint)count)
            return [];

        var start = ReadU32(_bytes, offset + 4 + index * 4);
        var end = ReadU32(_bytes, offset + 8 + index * 4);
        var data = offset + 8 + count * 4;
        return end > start && data + end <= _bytes.Length ? _bytes.AsSpan(data + start, end - start) : [];
    }

    /// <summary>Reads the resource out of the U8 archive (<c>RFLRes01.arc</c>) it ships in.</summary>
    public static OperationResult<RflResource> FromArchive(byte[] archive)
    {
        try
        {
            var resource = FindU8File(archive, ResourceFileName);
            return resource == null ? Fail($"The face library archive has no {ResourceFileName}.") : Parse(resource);
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException)
        {
            return Fail("The face library archive is damaged.");
        }
    }

    public static OperationResult<RflResource> Parse(byte[] bytes)
    {
        var archiveCount = Enum.GetValues<RflArchive>().Length;
        if (bytes.Length < 4 + archiveCount * 4)
            return Fail("The face library is too small.");
        if (BinaryPrimitives.ReadUInt16BigEndian(bytes) != archiveCount)
            return Fail("The face library has an unexpected layout.");
        if (BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2)) < MinimumVersion)
            return Fail("The face library is too old.");

        var archives = new (int Count, int Offset)[archiveCount];
        for (var i = 0; i < archiveCount; i++)
        {
            var offset = ReadU32(bytes, 4 + i * 4);
            if (offset < 0 || offset + 4 > bytes.Length)
                return Fail("The face library is damaged.");
            var count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
            if (offset + 8 + count * 4 > bytes.Length)
                return Fail("The face library is damaged.");
            archives[i] = (count, offset);
        }

        return new RflResource(bytes, archives);
    }

    /// <summary>A file by name from a U8 archive, or null.</summary>
    private static byte[]? FindU8File(byte[] archive, string name)
    {
        if (archive.Length < 0x20 || BinaryPrimitives.ReadUInt32BigEndian(archive) != 0x55AA382D)
            return null;

        var nodes = ReadU32(archive, 4);
        var nodeCount = ReadU32(archive, nodes + 8);
        var names = nodes + nodeCount * 12;
        for (var i = 1; i < nodeCount; i++)
        {
            var node = nodes + i * 12;
            if (archive[node] != 0)
                continue;

            var nameStart = names + (ReadU32(archive, node) & 0xFFFFFF);
            var nameEnd = Array.IndexOf(archive, (byte)0, nameStart);
            if (
                nameEnd < 0
                || !Encoding.ASCII.GetString(archive, nameStart, nameEnd - nameStart).Equals(name, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            return archive.AsSpan(ReadU32(archive, node + 4), ReadU32(archive, node + 8)).ToArray();
        }

        return null;
    }

    private static int ReadU32(byte[] bytes, int offset) => (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
}

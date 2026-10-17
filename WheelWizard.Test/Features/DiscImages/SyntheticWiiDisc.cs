using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RVZSharp;
using RVZSharp.Blobs;
using RVZSharp.Models;
using RVZSharp.Wii;

namespace WheelWizard.Test.Features.DiscImages;

/// <summary>
/// Builds a tiny but real Wii disc image: a partition table, an unencrypted partition header (ticket, TMD,
/// certificates, H3 table) and a game partition encrypted with the ticket's title key, holding a boot header,
/// apploader, main.dol, FST and files. Small enough to build per test, real enough that RVZSharp reads it exactly
/// like a retail disc, so extraction runs end to end without any copyrighted data.
/// </summary>
internal sealed class SyntheticWiiDisc
{
    private const int Cluster = 0x8000;
    private const int Payload = 0x7C00;
    private const long PartitionOffset = 0x50000;
    private const long PartitionDataOffset = 0x20000;

    public string GameId { get; init; } = "RMCP01";
    public byte Revision { get; init; }
    public string InternalName { get; init; } = "MARIO KART WII";

    /// <summary>Files by their path on the disc; directories come from the paths.</summary>
    public Dictionary<string, byte[]> Files { get; init; } =
        new()
        {
            ["rel/StaticR.rel"] = Bytes(5000, 1),
            ["Race/Course/beginner_course.szs"] = Encoding.ASCII.GetBytes("course data"),
            ["Scene/UI/Title.szs"] = Bytes(1234, 7),
            ["empty.bin"] = [],
        };

    public byte[] MainDol { get; init; } = Dol(0x200, 3);

    public static byte[] Bytes(int length, int seed) => Enumerable.Range(0, length).Select(index => (byte)(index * seed + seed)).ToArray();

    public static byte[] Dol(int textSize, int seed)
    {
        var dol = new byte[0x100 + textSize];
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(0x0), 0x100);
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(0x48), 0x80004000);
        BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(0x90), (uint)textSize);
        for (var index = 0x100; index < dol.Length; index++)
            dol[index] = (byte)(index * seed);
        return dol;
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public byte[] Ticket { get; private set; } = [];
    public byte[] Tmd { get; } = Bytes(0x208, 3);
    public byte[] Certificates { get; } = Bytes(0xA00, 9);
    public byte[] H3 { get; } = Bytes(0x18000, 5);
    public byte[] Bi2 { get; } = Bytes(0x2000, 11);
    public byte[] Apploader { get; private set; } = [];
    public byte[] Fst { get; private set; } = [];
    public byte[] Boot { get; private set; } = [];

    /// <summary>The image as a plain ISO.</summary>
    public byte[] BuildIso()
    {
        var (plain, fst) = BuildPartitionData();
        Fst = fst;
        var clusters = plain.Length / Payload;
        var disc = new byte[PartitionOffset + PartitionDataOffset + (long)clusters * Cluster];

        WriteHeader(disc, 0);
        // Partition table: one group of one partition, the game partition (type 0).
        WriteUInt32(disc, 0x40000, 1);
        WriteUInt32(disc, 0x40004, 0x40020 >> 2);
        WriteUInt32(disc, 0x40020, (uint)(PartitionOffset >> 2));
        WriteUInt32(disc, 0x40024, 0);
        WriteUInt32(disc, 0x4E000, 2);

        var ticket = new byte[0x2A4];
        WriteUInt32(ticket, 0, 0x00010001);
        for (var index = 0; index < 16; index++)
            ticket[0x1BF + index] = (byte)(index * 13 + 1);
        WriteUInt32(ticket, 0x1DC, 0x00010000);
        Encoding.ASCII.GetBytes(GameId[..4]).CopyTo(ticket, 0x1E0);
        Ticket = ticket;
        ticket.CopyTo(disc, (int)PartitionOffset);

        var header = (int)PartitionOffset;
        WriteUInt32(disc, header + 0x2A4, (uint)Tmd.Length);
        WriteUInt32(disc, header + 0x2A8, 0x2C0 >> 2);
        WriteUInt32(disc, header + 0x2AC, (uint)Certificates.Length);
        WriteUInt32(disc, header + 0x2B0, 0x500 >> 2);
        WriteUInt32(disc, header + 0x2B4, 0x8000 >> 2);
        WriteUInt32(disc, header + 0x2B8, (uint)(PartitionDataOffset >> 2));
        WriteUInt32(disc, header + 0x2BC, (uint)((long)clusters * Cluster >> 2));
        Tmd.CopyTo(disc, header + 0x2C0);
        Certificates.CopyTo(disc, header + 0x500);
        H3.CopyTo(disc, header + 0x8000);

        // Clusters: an encrypted (here empty) hash block, then the data encrypted with an IV taken from it.
        using var aes = Aes.Create();
        aes.Key = WiiVolume.GetTitleKey(ticket);
        for (var cluster = 0; cluster < clusters; cluster++)
        {
            var hashes = aes.EncryptCbc(new byte[0x400], new byte[16], PaddingMode.None);
            var data = aes.EncryptCbc(plain.AsSpan(cluster * Payload, Payload), hashes.AsSpan(0x3D0, 16), PaddingMode.None);
            var offset = (int)(PartitionOffset + PartitionDataOffset + (long)cluster * Cluster);
            hashes.CopyTo(disc, offset);
            data.CopyTo(disc, offset + 0x400);
        }

        return disc;
    }

    /// <summary>The same disc in another container, written by RVZSharp's own encoders.</summary>
    public byte[] Build(string format)
    {
        var iso = BuildIso();
        if (format == "iso")
            return iso;

        using var reader = Blob.Open(new MemoryStream(iso), "disc.iso", leaveOpen: false);
        using var output = new MemoryStream();
        switch (format)
        {
            case "wbfs":
                WbfsWriter.Write(reader, output, WbfsWriteOptions.Default, null!, CancellationToken.None);
                break;
            case "gcz":
                GczWriter.Write(reader, output, new GczWriteOptions(), null!, CancellationToken.None);
                break;
            case "ciso":
                CisoWriter.Write(reader, output, new CisoWriteOptions(), null!, CancellationToken.None);
                break;
            case "rvz":
                RvzWriter.Write(reader, output, RvzWriteOptions.Default, null!, CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }

        return output.ToArray();
    }

    private (byte[] Plain, byte[] Fst) BuildPartitionData()
    {
        const long dolOffset = 0x10000;
        const long fstOffset = 0x11000;
        var (fst, placed, end) = BuildFst(firstDataOffset: 0x20000);

        var size = (end + Payload - 1) / Payload * Payload;
        var plain = new byte[size];
        var boot = new byte[0x440];
        WriteHeader(boot, 0);
        WriteUInt32(boot, 0x420, (uint)(dolOffset >> 2));
        WriteUInt32(boot, 0x424, (uint)(fstOffset >> 2));
        WriteUInt32(boot, 0x428, (uint)((fst.Length + 3) >> 2));
        WriteUInt32(boot, 0x42C, (uint)((fst.Length + 3) >> 2));
        Boot = boot;
        boot.CopyTo(plain, 0);
        Bi2.CopyTo(plain, 0x440);

        var apploader = new byte[0x20 + 0x40 + 0x10];
        Encoding.ASCII.GetBytes("2008/04/01").CopyTo(apploader, 0);
        WriteUInt32(apploader, 0x14, 0x40);
        WriteUInt32(apploader, 0x18, 0x10);
        for (var index = 0x20; index < apploader.Length; index++)
            apploader[index] = (byte)index;
        Apploader = apploader;
        apploader.CopyTo(plain, 0x2440);

        MainDol.CopyTo(plain, (int)dolOffset);
        fst.CopyTo(plain, (int)fstOffset);
        foreach (var (offset, data) in placed)
            data.CopyTo(plain, (int)offset);

        // The FST is stored rounded up to four bytes, and that is what a dump contains.
        var storedFst = new byte[(fst.Length + 3) & ~3];
        fst.CopyTo(storedFst, 0);
        return (plain, storedFst);
    }

    private sealed class Node
    {
        public SortedDictionary<string, Node> Directories { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    }

    private (byte[] Fst, List<(long Offset, byte[] Data)> Placed, long End) BuildFst(long firstDataOffset)
    {
        var root = new Node();
        foreach (var (path, data) in Files)
        {
            var parts = path.Split('/');
            var node = root;
            foreach (var directory in parts[..^1])
            {
                if (!node.Directories.TryGetValue(directory, out var child))
                    node.Directories[directory] = child = new Node();
                node = child;
            }

            node.Files[parts[^1]] = data;
        }

        var names = new MemoryStream();
        var entries = new List<(bool Directory, uint Name, uint A, uint B)> { (true, 0, 0, 0) };
        var placed = new List<(long, byte[])>();
        var cursor = firstDataOffset;

        uint Name(string name)
        {
            var offset = (uint)names.Length;
            names.Write(Encoding.ASCII.GetBytes(name));
            names.WriteByte(0);
            return offset;
        }

        void Add(Node node, int parent)
        {
            foreach (var (name, data) in node.Files)
            {
                placed.Add((cursor, data));
                entries.Add((false, Name(name), (uint)(cursor >> 2), (uint)data.Length));
                cursor = (cursor + data.Length + 0x3F) & ~0x3FL;
            }

            foreach (var (name, child) in node.Directories)
            {
                var index = entries.Count;
                entries.Add((true, Name(name), (uint)parent, 0));
                Add(child, index);
                entries[index] = (true, entries[index].Name, (uint)parent, (uint)entries.Count);
            }
        }

        Add(root, 0);
        entries[0] = (true, 0, 0, (uint)entries.Count);

        var fst = new byte[entries.Count * 12 + names.Length];
        for (var index = 0; index < entries.Count; index++)
        {
            var (isDirectory, name, a, b) = entries[index];
            WriteUInt32(fst, index * 12, (isDirectory ? 1u << 24 : 0) | name);
            WriteUInt32(fst, index * 12 + 4, a);
            WriteUInt32(fst, index * 12 + 8, b);
        }

        names.ToArray().CopyTo(fst, entries.Count * 12);
        return (fst, placed, cursor);
    }

    private void WriteHeader(byte[] target, int offset)
    {
        Encoding.ASCII.GetBytes(GameId).CopyTo(target, offset);
        target[offset + 7] = Revision;
        WriteUInt32(target, offset + 0x18, 0x5D1C9EA3);
        Encoding.ASCII.GetBytes(InternalName).CopyTo(target, offset + 0x20);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset), value);
}

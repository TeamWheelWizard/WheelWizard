using System.Buffers.Binary;
using System.Text;

namespace WheelWizard.Test.Features.CustomMusic;

/// <summary>Writes a minimal Pulsar config (PULS header, CUPS, BMG names and FILE block) like Pulsar's creator tool.</summary>
internal sealed class PulsarConfigBuilder
{
    private readonly List<string> _cups = [];
    private readonly List<(byte Slot, byte Music, string Name, string? Version, (byte Slot, byte Music, string Name)[] Variants)> _tracks = [];
    private readonly Dictionary<int, string> _files = [];

    public PulsarConfigBuilder Cup(string name)
    {
        _cups.Add(name);
        return this;
    }

    public PulsarConfigBuilder Track(byte slot, byte music, string name, string? version = null, (byte, byte, string)[]? variants = null)
    {
        _tracks.Add((slot, music, name, version, variants ?? []));
        return this;
    }

    public PulsarConfigBuilder File(int id, string fileName)
    {
        _files[id] = fileName;
        return this;
    }

    public byte[] Build()
    {
        var storedCups = _cups.Count + _cups.Count % 2;
        var trackTable = new List<byte>();
        for (var i = 0; i < storedCups * 4; i++)
        {
            var track = _tracks[i % _tracks.Count];
            trackTable.AddRange([track.Slot, track.Music, .. U16(i < _tracks.Count ? track.Variants.Length : 0), .. U32(0x1234)]);
        }

        var variants = _tracks.SelectMany(track => track.Variants).SelectMany(v => new[] { v.Slot, v.Music }).ToArray();
        byte[] cupsBody = [.. U16(_cups.Count), 2, 0, .. new byte[8], .. S32(variants.Length / 2), .. trackTable, .. variants, .. new byte[storedCups * 8]];
        byte[] cups = [.. "CUPS"u8, .. U32(3), .. U32(12 + cupsBody.Length), .. cupsBody];

        var messages = new List<(uint Id, byte[] Text)>();
        for (var i = 0; i < _cups.Count; i++)
            messages.Add((0x10000u + (uint)i, Utf16(_cups[i])));
        for (var i = 0; i < _tracks.Count; i++)
        {
            var track = _tracks[i];
            messages.Add((0x20000u + (uint)i, track.Version == null ? Utf16(track.Name) : [.. Utf16(track.Name), .. ColourEscape, .. Utf16(" " + track.Version), .. ColourEscape]));
            if (track.Variants.Length == 0)
                continue;
            messages.Add((0x20000u + (8u << 12) + (uint)i, Utf16(track.Name + " (all versions)")));
            for (var v = 0; v < track.Variants.Length; v++)
                messages.Add((0x20000u + ((uint)(v + 1) << 12) + (uint)i, Utf16(track.Variants[v].Name)));
        }

        var fileText = "FILE\n" + string.Concat(_files.Select(file => $"{file.Key:X}={file.Value}|e1|e2|e3|e4\n")) + "0?cupicon\n";

        const int headerLength = 0x24;
        byte[] info = [.. "INFO"u8, .. U32(1), .. U32(12 + 0x30), .. new byte[0x30]];
        var cupsOffset = headerLength + info.Length;
        var bmgOffset = cupsOffset + cups.Length;
        var header = new byte[headerLength];
        "PULS"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 3);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), headerLength);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0x0C), cupsOffset);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0x10), bmgOffset);
        return [.. header, .. info, .. cups, .. Bmg(messages), .. Encoding.UTF8.GetBytes(fileText)];
    }

    private static readonly byte[] ColourEscape = [0, 0x1A, 8, 0, 0, 3, 0, 0];

    private static byte[] Bmg(List<(uint Id, byte[] Text)> messages)
    {
        var text = new List<byte> { 0, 0 };
        var offsets = new List<int>();
        foreach (var message in messages)
        {
            offsets.Add(text.Count);
            text.AddRange([.. message.Text, 0, 0]);
        }

        var inf = Block("INF1", [.. U16(messages.Count), .. U16(8), .. U32(0), .. offsets.SelectMany(offset => U32(offset).Concat(U32(0)))]);
        var dat = Block("DAT1", [.. text]);
        var mid = Block("MID1", [.. U16(messages.Count), 0x10, 0, .. U32(0), .. messages.SelectMany(message => U32((int)message.Id))]);
        byte[] body = [.. inf, .. dat, .. mid];
        byte[] header = [.. "MESGbmg1"u8, .. U32(0x20 + body.Length), .. U32(3), 2, .. new byte[15]];
        return [.. header, .. body];
    }

    private static byte[] Block(string magic, byte[] body)
    {
        var length = (8 + body.Length + 0x1F) & ~0x1F;
        var block = new byte[length];
        Encoding.ASCII.GetBytes(magic, block.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32BigEndian(block.AsSpan(4), length);
        body.CopyTo(block, 8);
        return block;
    }

    /// <summary>UTF-16 text; a \u0001 in the text becomes a colour escape code.</summary>
    private static byte[] Utf16(string text) => text.Split('\u0001').Select(part => Encoding.BigEndianUnicode.GetBytes(part)).Aggregate((a, b) => [.. a, .. ColourEscape, .. b]);

    private static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];

    private static byte[] U32(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] S32(int value) => U32(value);
}

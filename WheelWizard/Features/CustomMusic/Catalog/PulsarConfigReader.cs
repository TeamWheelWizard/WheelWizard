using System.Buffers.Binary;
using System.Text;

namespace WheelWizard.CustomMusic.Catalog;

/// <summary>A track defined in a Pulsar config file.</summary>
public sealed record PulsarTrack(
    int LocalIndex,
    int CupIndex,
    byte Slot,
    byte MusicSlot,
    string? Name,
    string? FileName,
    IReadOnlyList<PulsarVariant> Variants
);

public sealed record PulsarVariant(int VariantIndex, byte Slot, byte MusicSlot, string? Name, string? FileName);

public sealed record PulsarConfig(IReadOnlyList<string> CupNames, IReadOnlyList<PulsarTrack> Tracks)
{
    public int CupCount => CupNames.Count;
}

/// <summary>
/// Reads Pulsar's Binaries/Config*.pul: the CUPS section (tracks, music slots, variants), the embedded BMG
/// (track and cup names) and the FILE text block after it (the creator file names Retro Rewind uses to find
/// a track's music: strm/&lt;file name&gt;_n.brstm).
/// </summary>
public static class PulsarConfigReader
{
    private const uint PulsMagic = 0x50554C53; // "PULS"
    private const uint CupsMagic = 0x43555053; // "CUPS"
    private const int BmgCups = 0x10000;
    private const int BmgTracks = 0x20000;

    public static PulsarConfig Read(byte[] file)
    {
        if (file.Length < 0x20 || BinaryPrimitives.ReadUInt32BigEndian(file) != PulsMagic)
            throw new InvalidDataException("This is not a Pulsar config file.");

        var cupsOffset = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(0x0C));
        var bmgOffset = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(0x10));
        if (cupsOffset <= 0 || cupsOffset + 0x1C > file.Length)
            throw new InvalidDataException("The Pulsar config has no CUPS section.");
        if (BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(cupsOffset)) != CupsMagic)
            throw new InvalidDataException("The Pulsar CUPS section is invalid.");

        var cupCount = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(cupsOffset + 0x0C));
        var totalVariants = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(cupsOffset + 0x18));
        var storedCups = cupCount % 2 == 1 ? cupCount + 1 : cupCount;
        var trackCount = cupCount * 4;
        var tracksStart = cupsOffset + 0x1C;
        var variantsStart = tracksStart + storedCups * 4 * 8;
        if (variantsStart + Math.Max(0, totalVariants) * 2 > file.Length)
            throw new InvalidDataException("The Pulsar track table is truncated.");

        var messages = new Dictionary<uint, string>();
        var fileNames = new Dictionary<int, string>();
        if (bmgOffset > 0 && bmgOffset + 0x20 <= file.Length && BmgReader.IsBmg(file.AsSpan(bmgOffset)))
        {
            var bmgLength = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(bmgOffset + 8)), (uint)(file.Length - bmgOffset));
            messages = BmgReader.Read(file.AsSpan(bmgOffset, bmgLength));
            fileNames = ReadFileSection(file.AsSpan(bmgOffset + bmgLength));
        }

        var cupNames = new List<string>(cupCount);
        for (var cup = 0; cup < cupCount; cup++)
            cupNames.Add(messages.TryGetValue((uint)(BmgCups + cup), out var cupName) ? CleanName(cupName) : $"Cup {cup + 1}");

        var tracks = new List<PulsarTrack>(trackCount);
        var variantCursor = 0;
        for (var i = 0; i < trackCount; i++)
        {
            var entry = tracksStart + i * 8;
            var slot = file[entry];
            var musicSlot = file[entry + 1];
            var variantCount = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(entry + 2));

            var variants = new List<PulsarVariant>(variantCount);
            for (var v = 1; v <= variantCount && variantCursor < totalVariants; v++, variantCursor++)
            {
                var variantEntry = variantsStart + variantCursor * 2;
                variants.Add(
                    new PulsarVariant(
                        v,
                        file[variantEntry],
                        file[variantEntry + 1],
                        messages.TryGetValue((uint)(BmgTracks + (v << 12) + i), out var variantName) ? CleanName(variantName) : null,
                        fileNames.GetValueOrDefault((v << 12) + i)
                    )
                );
            }

            // Tracks with variants have a shared "common name" at variant index 8.
            string? name = null;
            if (variantCount > 0 && messages.TryGetValue((uint)(BmgTracks + (8 << 12) + i), out var common))
                name = CleanName(common);
            if (string.IsNullOrWhiteSpace(name) && messages.TryGetValue((uint)(BmgTracks + i), out var trackName))
                name = CleanName(trackName);

            tracks.Add(new PulsarTrack(i, i / 4, slot, musicSlot, string.IsNullOrWhiteSpace(name) ? null : name, fileNames.GetValueOrDefault(i), variants));
        }

        return new PulsarConfig(cupNames, tracks);
    }

    /// <summary>"FILE" followed by lines like "1A=uMKS|expert1|expert2..."; keys are (variant &lt;&lt; 12) | track.</summary>
    public static Dictionary<int, string> ReadFileSection(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<int, string>();
        if (data.Length < 4)
            return result;

        var text = Encoding.UTF8.GetString(data).TrimStart('﻿', '\0');
        if (!text.StartsWith("FILE", StringComparison.Ordinal))
            return result;

        foreach (var rawLine in text.Split('\n').Skip(1))
        {
            var line = rawLine.Trim('\r', ' ', '\t', '\0');
            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..];
            var pipe = value.IndexOf('|');
            if (pipe < 0)
                continue;
            value = value[..pipe].Trim();
            if (value.Length == 0)
                continue;

            if (int.TryParse(key, System.Globalization.NumberStyles.HexNumber, null, out var id))
                result[id] = value;
        }

        return result;
    }

    private static string CleanName(string name) => name.Trim();
}

/// <summary>Minimal reader for Nintendo BMG message files (UTF-16 with INF1/DAT1/MID1 blocks).</summary>
public static class BmgReader
{
    public static bool IsBmg(ReadOnlySpan<byte> data) => data.Length >= 8 && Encoding.ASCII.GetString(data[..8]) == "MESGbmg1";

    /// <summary>Returns message id -> text, with escape sequences (colours, icons) removed.</summary>
    public static Dictionary<uint, string> Read(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<uint, string>();
        if (!IsBmg(data))
            return result;

        var encoding = data[0x10];
        var blockCount = BinaryPrimitives.ReadUInt32BigEndian(data[0x0C..]);
        int inf = -1,
            dat = -1,
            mid = -1;
        var position = 0x20;
        for (var block = 0; block < blockCount && position + 8 <= data.Length; block++)
        {
            var magic = Encoding.ASCII.GetString(data.Slice(position, 4));
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
            if (size < 8)
                break;
            switch (magic)
            {
                case "INF1":
                    inf = position;
                    break;
                case "DAT1":
                    dat = position;
                    break;
                case "MID1":
                    mid = position;
                    break;
            }

            position += size;
        }

        if (inf < 0 || dat < 0)
            return result;

        var count = BinaryPrimitives.ReadUInt16BigEndian(data[(inf + 8)..]);
        var entrySize = BinaryPrimitives.ReadUInt16BigEndian(data[(inf + 10)..]);
        var midCount = mid >= 0 ? BinaryPrimitives.ReadUInt16BigEndian(data[(mid + 8)..]) : 0;
        var textStart = dat + 8;

        for (var i = 0; i < count; i++)
        {
            var entry = inf + 0x10 + i * entrySize;
            if (entry + 4 > data.Length)
                break;
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data[entry..]);
            var id = mid >= 0 && i < midCount ? BinaryPrimitives.ReadUInt32BigEndian(data[(mid + 0x10 + i * 4)..]) : (uint)i;
            var text = ReadString(data, textStart + offset, encoding);
            if (text != null)
                result[id] = text;
        }

        return result;
    }

    private static string? ReadString(ReadOnlySpan<byte> data, int start, byte encoding)
    {
        if (start < 0 || start >= data.Length)
            return null;

        var builder = new StringBuilder();
        if (encoding is 2 or 0)
        {
            var position = start;
            while (position + 1 < data.Length)
            {
                var c = (char)BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
                if (c == '\0')
                    break;
                if (c == '\u001A')
                {
                    // Escape: 0x1A, then the total escape length in bytes (including the 0x1A and length byte).
                    if (position + 2 >= data.Length)
                        break;
                    var length = data[position + 2];
                    // Colour and icon escapes are dropped but the text around them is kept: names like
                    // "<red>3DS<off> Toad Circuit" or "Track<red> v2<off>" must not be cut at the first escape.
                    position += Math.Max(4, length + (length % 2));
                    continue;
                }

                builder.Append(c);
                position += 2;
            }
        }
        else
        {
            var end = data[start..].IndexOf((byte)0);
            var slice = end < 0 ? data[start..] : data.Slice(start, end);
            builder.Append(encoding == 4 ? Encoding.UTF8.GetString(slice) : Encoding.Latin1.GetString(slice));
        }

        return System.Text.RegularExpressions.Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }
}

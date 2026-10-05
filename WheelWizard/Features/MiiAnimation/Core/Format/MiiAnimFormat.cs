using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using MiiAnim.Core.Animation;

namespace MiiAnim.Core.Format;

/// <summary>
/// Reads and writes .miianim files. All numbers are little-endian; "varuint" is LEB128.
/// <code>
/// "MIAN"            magic
/// u8                version (1)
/// u8                flags   bit0 = has author Mii, bit1 = has events
/// u8                fps
/// varuint           length in frames
/// varuint + utf8    name
/// [74 bytes]        author Mii (Wii format), if flag bit0
/// varuint           track count
/// track*            see below
/// [events]          if flag bit1: varuint count, then per event (sorted by frame):
///                   varuint frameDelta (first is absolute), varuint + utf8 name
/// u32               CRC-32 of everything above
///
/// track:
///   u8      target  (bones 0x00-0x3F, limbs 0x40|limb, global 0x80)
///   u8      channel
///   u8      value encoding (0 = f32, 1 = f16, 2 = i16 in hundredths, 3 = u8 integer)
///           | 0x80 when every key uses the same interpolation
///   [u8]    that shared interpolation, if 0x80 is set
///   varuint key count
///   key*:
///     varuint frameDelta, or (frameDelta &lt;&lt; 4) | interpolation without a shared interpolation
///                                                    (first key's delta is its absolute frame)
///     value                                          (track encoding)
///     if interpolation == Custom: 4 handle values    (track encoding: outX, outY, inX, inY)
/// </code>
/// The writer picks the smallest encoding that reproduces every value exactly, so files are lossless.
/// </summary>
public static class MiiAnimFormat
{
    public const string FileExtension = ".miianim";
    private static ReadOnlySpan<byte> Magic => "MIAN"u8;
    public const byte Version = 1;
    private const byte SharedInterpolationFlag = 0x80;

    private enum ValueEncoding : byte
    {
        Float32 = 0,
        Float16 = 1,
        Hundredths16 = 2,
        Byte = 3,
    }

    public static byte[] Write(MiiAnimation animation)
    {
        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.WriteByte(Version);
        var hasMii = animation.AuthorMii is { Length: MiiAnimation.MiiDataLength };
        var hasEvents = animation.Events.Count > 0;
        ms.WriteByte((byte)((hasMii ? 1 : 0) | (hasEvents ? 2 : 0)));
        ms.WriteByte((byte)Math.Clamp(animation.Fps, 1, 255));
        WriteVarUInt(ms, (uint)Math.Max(1, animation.Length));
        var name = Encoding.UTF8.GetBytes(animation.Name ?? "");
        WriteVarUInt(ms, (uint)name.Length);
        ms.Write(name);
        if (hasMii)
            ms.Write(animation.AuthorMii!);

        var tracks = animation
            .Tracks.Where(t => t.Value.Count > 0)
            .OrderBy(t => t.Key.Target.Pack())
            .ThenBy(t => (byte)t.Key.Channel)
            .ToList();
        WriteVarUInt(ms, (uint)tracks.Count);
        foreach (var (id, curve) in tracks)
        {
            ms.WriteByte(id.Target.Pack());
            ms.WriteByte((byte)id.Channel);
            var encoding = ChooseEncoding(curve);
            var shared = curve.Keys.All(k => k.Interpolation == curve.Keys[0].Interpolation);
            ms.WriteByte((byte)((byte)encoding | (shared ? SharedInterpolationFlag : 0)));
            if (shared)
                ms.WriteByte((byte)curve.Keys[0].Interpolation);
            WriteVarUInt(ms, (uint)curve.Count);

            var previousFrame = 0;
            for (var i = 0; i < curve.Count; i++)
            {
                var key = curve.Keys[i];
                var delta = i == 0 ? key.Frame : key.Frame - previousFrame;
                if (delta < 0)
                    throw new InvalidOperationException("Keys must be sorted and on non-negative frames.");
                previousFrame = key.Frame;
                WriteVarUInt(ms, shared ? (uint)delta : ((uint)delta << 4) | ((uint)key.Interpolation & 0xF));
                WriteValue(ms, encoding, key.Value);
                if (key.Interpolation == Interpolation.Custom)
                {
                    WriteValue(ms, encoding, key.HandleOut.X);
                    WriteValue(ms, encoding, key.HandleOut.Y);
                    WriteValue(ms, encoding, key.HandleIn.X);
                    WriteValue(ms, encoding, key.HandleIn.Y);
                }
            }
        }

        if (hasEvents)
        {
            var events = animation.Events.OrderBy(e => e.Frame).ToList();
            WriteVarUInt(ms, (uint)events.Count);
            var previousFrame = 0;
            foreach (var animEvent in events)
            {
                WriteVarUInt(ms, (uint)Math.Max(0, animEvent.Frame - previousFrame));
                previousFrame = Math.Max(previousFrame, animEvent.Frame);
                var eventName = Encoding.UTF8.GetBytes(animEvent.Name ?? "");
                WriteVarUInt(ms, (uint)eventName.Length);
                ms.Write(eventName);
            }
        }

        var body = ms.ToArray();
        var result = new byte[body.Length + 4];
        body.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(body.Length), Crc32.Compute(body));
        return result;
    }

    public static MiiAnimation Read(byte[] data)
    {
        if (data.Length < 12 || !data.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("Not a .miianim file.");
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4));
        if (storedCrc != Crc32.Compute(data.AsSpan(0, data.Length - 4)))
            throw new InvalidDataException("The .miianim file is corrupted (checksum mismatch).");

        var reader = new SpanReader(data.AsSpan(0, data.Length - 4).ToArray()) { Position = 4 };
        var version = reader.ReadByte();
        if (version > Version)
            throw new InvalidDataException($"This .miianim file was made with a newer version (v{version}).");
        var flags = reader.ReadByte();
        var animation = new MiiAnimation { Fps = reader.ReadByte(), Length = (int)reader.ReadVarUInt() };
        var nameLength = (int)reader.ReadVarUInt();
        animation.Name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
        if ((flags & 1) != 0)
            animation.AuthorMii = reader.ReadBytes(MiiAnimation.MiiDataLength).ToArray();

        var trackCount = (int)reader.ReadVarUInt();
        for (var t = 0; t < trackCount; t++)
        {
            var target = TrackTarget.Unpack(reader.ReadByte());
            var channel = (Channel)reader.ReadByte();
            var encodingByte = reader.ReadByte();
            var encoding = (ValueEncoding)(encodingByte & 0x7F);
            Interpolation? shared = (encodingByte & SharedInterpolationFlag) != 0 ? (Interpolation)reader.ReadByte() : null;
            var keyCount = (int)reader.ReadVarUInt();
            var curve = new AnimCurve();
            var frame = 0;
            for (var k = 0; k < keyCount; k++)
            {
                var packed = reader.ReadVarUInt();
                frame += (int)(shared.HasValue ? packed : packed >> 4);
                var interpolation = shared ?? (Interpolation)(packed & 0xF);
                var key = new Keyframe(frame, ReadValue(ref reader, encoding), interpolation);
                if (interpolation == Interpolation.Custom)
                {
                    key.HandleOut = new Vector2(ReadValue(ref reader, encoding), ReadValue(ref reader, encoding));
                    key.HandleIn = new Vector2(ReadValue(ref reader, encoding), ReadValue(ref reader, encoding));
                }

                curve.SetKey(key);
            }

            animation.Tracks[new TrackId(target, channel)] = curve;
        }

        if ((flags & 2) != 0)
        {
            var eventCount = (int)reader.ReadVarUInt();
            var frame = 0;
            for (var e = 0; e < eventCount; e++)
            {
                frame += (int)reader.ReadVarUInt();
                var eventNameLength = (int)reader.ReadVarUInt();
                animation.Events.Add(new AnimEvent(frame, Encoding.UTF8.GetString(reader.ReadBytes(eventNameLength))));
            }
        }

        return animation;
    }

    private static ValueEncoding ChooseEncoding(AnimCurve curve)
    {
        var values = new List<float>();
        foreach (var key in curve.Keys)
        {
            values.Add(key.Value);
            if (key.Interpolation == Interpolation.Custom)
            {
                values.Add(key.HandleOut.X);
                values.Add(key.HandleOut.Y);
                values.Add(key.HandleIn.X);
                values.Add(key.HandleIn.Y);
            }
        }

        if (values.All(v => v is >= 0 and <= 255 && v == MathF.Round(v)))
            return ValueEncoding.Byte;
        if (values.All(v => (float)(Half)v == v))
            return ValueEncoding.Float16;
        if (values.All(v => MathF.Abs(v) <= 327.67f && MathF.Round(v * 100f) / 100f == v))
            return ValueEncoding.Hundredths16;
        return ValueEncoding.Float32;
    }

    private static void WriteValue(Stream stream, ValueEncoding encoding, float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        switch (encoding)
        {
            case ValueEncoding.Byte:
                stream.WriteByte((byte)value);
                return;
            case ValueEncoding.Float16:
                BinaryPrimitives.WriteHalfLittleEndian(buffer, (Half)value);
                stream.Write(buffer[..2]);
                return;
            case ValueEncoding.Hundredths16:
                BinaryPrimitives.WriteInt16LittleEndian(buffer, (short)MathF.Round(value * 100f));
                stream.Write(buffer[..2]);
                return;
            default:
                BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
                stream.Write(buffer);
                return;
        }
    }

    private static float ReadValue(ref SpanReader reader, ValueEncoding encoding) =>
        encoding switch
        {
            ValueEncoding.Byte => reader.ReadByte(),
            ValueEncoding.Float16 => (float)BinaryPrimitives.ReadHalfLittleEndian(reader.ReadBytes(2)),
            ValueEncoding.Hundredths16 => BinaryPrimitives.ReadInt16LittleEndian(reader.ReadBytes(2)) / 100f,
            ValueEncoding.Float32 => BinaryPrimitives.ReadSingleLittleEndian(reader.ReadBytes(4)),
            _ => throw new InvalidDataException($"Unknown value encoding {(byte)encoding}."),
        };

    private static void WriteVarUInt(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private ref struct SpanReader(byte[] data)
    {
        private readonly byte[] _data = data;
        public int Position;

        public byte ReadByte()
        {
            if (Position >= _data.Length)
                throw new InvalidDataException("Unexpected end of .miianim file.");
            return _data[Position++];
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || Position + count > _data.Length)
                throw new InvalidDataException("Unexpected end of .miianim file.");
            var span = _data.AsSpan(Position, count);
            Position += count;
            return span;
        }

        public uint ReadVarUInt()
        {
            uint result = 0;
            var shift = 0;
            while (true)
            {
                var b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
                shift += 7;
                if (shift > 28)
                    throw new InvalidDataException("Invalid varint in .miianim file.");
            }
        }
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}

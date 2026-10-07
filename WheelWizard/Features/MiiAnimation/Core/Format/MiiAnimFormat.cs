using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using MiiAnim.Core.Animation;

namespace MiiAnim.Core.Format;

/// <summary>
/// Reads and writes .miianim files. All numbers are little-endian; "varuint" is LEB128, "string" is varuint + UTF-8.
/// <code>
/// "MIAN"            magic
/// u8                version (2)
/// chunk*            4 ASCII tag, varuint byte length, data
/// u32               CRC-32 of everything above
///
/// chunks (readers skip tags they don't know, so new features never break older players):
///   HEAD   u8 fps, varuint length in frames, string name                      (required)
///   AMII   74-byte Wii Mii the animation was authored with
///   TRKS   main Mii's tracks: varuint count, track* (see below)
///   EVNT   varuint count, per event (sorted by frame): varuint frameDelta (first is absolute), string name
///   ACTR   record list of extra Miis: string role name, u8 flags (bit0 = preview Mii), [74-byte Mii], tracks
///   SHAP   record list of particle images: string name, string SVG document
///   PART   record list of particle effects (fields in <see cref="WriteParticle"/> order)
///
/// record list: varuint count, then per record varuint byte length + record. New fields are only ever appended
/// to records: readers skip bytes they don't know and use defaults for fields an older file doesn't have.
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

    /// <summary>Only goes up for changes older readers can't skip over; new data goes in new chunks or fields.</summary>
    public const byte Version = 2;

    private const byte SharedInterpolationFlag = 0x80;

    private const string HeaderTag = "HEAD";
    private const string AuthorMiiTag = "AMII";
    private const string TracksTag = "TRKS";
    private const string EventsTag = "EVNT";
    private const string ActorsTag = "ACTR";
    private const string ShapesTag = "SHAP";
    private const string ParticlesTag = "PART";

    private enum ValueEncoding : byte
    {
        Float32 = 0,
        Float16 = 1,
        Hundredths16 = 2,
        Byte = 3,
    }

    public static byte[] Write(MiiAnimation animation)
    {
        animation = animation.Root;
        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.WriteByte(Version);

        WriteChunk(
            ms,
            HeaderTag,
            chunk =>
            {
                chunk.WriteByte((byte)Math.Clamp(animation.Fps, 1, 255));
                WriteVarUInt(chunk, (uint)Math.Max(1, animation.Length));
                WriteString(chunk, animation.Name);
            }
        );

        if (animation.AuthorMii is { Length: MiiAnimation.MiiDataLength } mii)
            WriteChunk(ms, AuthorMiiTag, chunk => chunk.Write(mii));

        WriteChunk(ms, TracksTag, chunk => WriteTracks(chunk, animation.Tracks));

        if (animation.Events.Count > 0)
            WriteChunk(
                ms,
                EventsTag,
                chunk =>
                {
                    var events = animation.Events.OrderBy(e => e.Frame).ToList();
                    WriteVarUInt(chunk, (uint)events.Count);
                    var previousFrame = 0;
                    foreach (var animEvent in events)
                    {
                        WriteVarUInt(chunk, (uint)Math.Max(0, animEvent.Frame - previousFrame));
                        previousFrame = Math.Max(previousFrame, animEvent.Frame);
                        WriteString(chunk, animEvent.Name);
                    }
                }
            );

        if (animation.Actors.Count > 0)
            WriteChunk(
                ms,
                ActorsTag,
                chunk =>
                    WriteRecords(
                        chunk,
                        animation.Actors,
                        (record, actor) =>
                        {
                            WriteString(record, actor.Name);
                            var hasPreview = actor.PreviewMii is { Length: MiiAnimation.MiiDataLength };
                            record.WriteByte((byte)(hasPreview ? 1 : 0));
                            if (hasPreview)
                                record.Write(actor.PreviewMii!);
                            WriteTracks(record, actor.Motion.Tracks);
                        }
                    )
            );

        // Each distinct image once, in order of first use; effects refer to it by position.
        var shapes = animation.Particles.Select(p => p.Shape).OfType<ParticleShape>().Distinct().ToList();
        if (shapes.Count > 0)
            WriteChunk(
                ms,
                ShapesTag,
                chunk =>
                    WriteRecords(
                        chunk,
                        shapes,
                        (record, shape) =>
                        {
                            WriteString(record, shape.Name);
                            WriteString(record, shape.Svg);
                        }
                    )
            );

        if (animation.Particles.Count > 0)
            WriteChunk(
                ms,
                ParticlesTag,
                chunk => WriteRecords(chunk, animation.Particles, (record, effect) => WriteParticle(record, effect, shapes))
            );

        var body = ms.ToArray();
        var result = new byte[body.Length + 4];
        body.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(body.Length), Crc32.Compute(body));
        return result;
    }

    private static void WriteChunk(Stream stream, string tag, Action<MemoryStream> write)
    {
        using var chunk = new MemoryStream();
        write(chunk);
        stream.Write(Encoding.ASCII.GetBytes(tag));
        WriteVarUInt(stream, (uint)chunk.Length);
        chunk.Position = 0;
        chunk.CopyTo(stream);
    }

    private static void WriteRecords<T>(Stream stream, IReadOnlyCollection<T> items, Action<MemoryStream, T> write)
    {
        WriteVarUInt(stream, (uint)items.Count);
        foreach (var item in items)
        {
            using var record = new MemoryStream();
            write(record, item);
            WriteVarUInt(stream, (uint)record.Length);
            record.Position = 0;
            record.CopyTo(stream);
        }
    }

    private static void WriteString(Stream stream, string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        WriteVarUInt(stream, (uint)bytes.Length);
        stream.Write(bytes);
    }

    /// <summary>
    /// Particle record. New fields are only ever appended: readers skip bytes they don't know and use defaults for
    /// fields an older file doesn't have. The shape is its position in the SHAP chunk + 1 (0 = no shape).
    /// </summary>
    private static void WriteParticle(Stream ms, ParticleEffect effect, List<ParticleShape> shapes)
    {
        var buffer = new byte[4];
        void F32(float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
            ms.Write(buffer);
        }
        void Vec3(Vector3 value)
        {
            F32(value.X);
            F32(value.Y);
            F32(value.Z);
        }
        void Rgba(Vector4 value)
        {
            ms.WriteByte(ToByte(value.X));
            ms.WriteByte(ToByte(value.Y));
            ms.WriteByte(ToByte(value.Z));
            ms.WriteByte(ToByte(value.W));
        }

        WriteString(ms, effect.Name);
        WriteVarUInt(ms, (uint)Math.Max(0, effect.StartFrame));
        WriteVarUInt(ms, (uint)Math.Max(0, effect.Duration));
        WriteVarUInt(ms, (uint)Math.Clamp(effect.Count, 0, ParticleEffect.MaxCount));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, effect.Seed);
        ms.Write(buffer);
        ms.WriteByte((byte)Math.Clamp(effect.Actor, 0, 255));
        ms.WriteByte(effect.AttachBone);
        Vec3(effect.Offset);
        Vec3(effect.SpawnRadius);
        F32(effect.SpawnShell);
        Vec3(effect.Direction);
        F32(effect.Spread);
        F32(effect.SpeedMin);
        F32(effect.SpeedMax);
        F32(effect.Gravity);
        F32(effect.Drag);
        F32(effect.SpinMin);
        F32(effect.SpinMax);
        WriteVarUInt(ms, effect.Shape is { } shape ? (uint)shapes.IndexOf(shape) + 1 : 0);
        ms.WriteByte((byte)effect.Orientation);
        F32(effect.Aspect);
        F32(effect.Stretch);
        F32(effect.LifeMin);
        F32(effect.LifeMax);
        F32(effect.SizeStart);
        F32(effect.SizeEnd);
        Rgba(effect.ColorStart);
        Rgba(effect.ColorEnd);
        ms.WriteByte((byte)effect.Tint);
        ms.WriteByte((byte)((effect.Additive ? 1 : 0) | (effect.StopAtFloor ? 2 : 0)));
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp(MathF.Round(channel * 255f), 0f, 255f);

    private static ParticleEffect ReadParticle(ReadOnlySpan<byte> record, out int shapeIndex)
    {
        // Fields missing from a shorter (older) record keep their defaults; unknown trailing bytes are ignored.
        var r = new RecordReader(record.ToArray());
        var e = new ParticleEffect();
        e.Name = r.String(e.Name);
        e.StartFrame = (int)r.VarUInt((uint)e.StartFrame);
        e.Duration = (int)r.VarUInt((uint)e.Duration);
        e.Count = Math.Min((int)r.VarUInt((uint)e.Count), ParticleEffect.MaxCount);
        e.Seed = r.U32(e.Seed);
        e.Actor = r.U8((byte)e.Actor);
        e.AttachBone = r.U8(e.AttachBone);
        e.Offset = r.Vec3(e.Offset);
        e.SpawnRadius = r.Vec3(e.SpawnRadius);
        e.SpawnShell = r.F32(e.SpawnShell);
        e.Direction = r.Vec3(e.Direction);
        e.Spread = r.F32(e.Spread);
        e.SpeedMin = r.F32(e.SpeedMin);
        e.SpeedMax = r.F32(e.SpeedMax);
        e.Gravity = r.F32(e.Gravity);
        e.Drag = r.F32(e.Drag);
        e.SpinMin = r.F32(e.SpinMin);
        e.SpinMax = r.F32(e.SpinMax);
        shapeIndex = (int)r.VarUInt(0) - 1;
        e.Orientation = (ParticleOrientation)r.U8((byte)e.Orientation);
        e.Aspect = r.F32(e.Aspect);
        e.Stretch = r.F32(e.Stretch);
        e.LifeMin = r.F32(e.LifeMin);
        e.LifeMax = r.F32(e.LifeMax);
        e.SizeStart = r.F32(e.SizeStart);
        e.SizeEnd = r.F32(e.SizeEnd);
        e.ColorStart = r.Rgba(e.ColorStart);
        e.ColorEnd = r.Rgba(e.ColorEnd);
        e.Tint = (ParticleTint)r.U8((byte)e.Tint);
        var flags = r.U8((byte)((e.Additive ? 1 : 0) | (e.StopAtFloor ? 2 : 0)));
        e.Additive = (flags & 1) != 0;
        e.StopAtFloor = (flags & 2) != 0;
        return e;
    }

    private sealed class RecordReader(byte[] data)
    {
        private int _position;

        private bool Has(int bytes) => _position + bytes <= data.Length;

        public byte U8(byte fallback) => Has(1) ? data[_position++] : fallback;

        public uint U32(uint fallback)
        {
            if (!Has(4))
                return fallback;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_position));
            _position += 4;
            return value;
        }

        public float F32(float fallback)
        {
            if (!Has(4))
                return fallback;
            var value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(_position));
            _position += 4;
            return float.IsFinite(value) ? value : fallback;
        }

        public Vector3 Vec3(Vector3 fallback) => Has(12) ? new Vector3(F32(0), F32(0), F32(0)) : fallback;

        public Vector4 Rgba(Vector4 fallback) => Has(4) ? new Vector4(U8(0) / 255f, U8(0) / 255f, U8(0) / 255f, U8(0) / 255f) : fallback;

        public uint VarUInt(uint fallback)
        {
            if (!Has(1))
                return fallback;
            uint result = 0;
            for (var shift = 0; shift <= 28 && Has(1); shift += 7)
            {
                var b = data[_position++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
            }

            throw new InvalidDataException("Invalid varint in .miianim particle.");
        }

        public string String(string fallback)
        {
            if (!Has(1))
                return fallback;
            var length = (int)VarUInt(0);
            if (!Has(length))
                throw new InvalidDataException("Unexpected end of .miianim particle.");
            var value = Encoding.UTF8.GetString(data, _position, length);
            _position += length;
            return value;
        }
    }

    private static void WriteTracks(Stream ms, Dictionary<TrackId, AnimCurve> allTracks)
    {
        var tracks = allTracks.Where(t => t.Value.Count > 0).OrderBy(t => t.Key.Target.Pack()).ThenBy(t => (byte)t.Key.Channel).ToList();
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
    }

    public static MiiAnimation Read(byte[] data)
    {
        if (data.Length < 9 || !data.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("Not a .miianim file.");
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4));
        if (storedCrc != Crc32.Compute(data.AsSpan(0, data.Length - 4)))
            throw new InvalidDataException("The .miianim file is corrupted (checksum mismatch).");

        var reader = new SpanReader(data.AsSpan(0, data.Length - 4)) { Position = 4 };
        var version = reader.ReadByte();
        if (version > Version)
            throw new InvalidDataException($"This .miianim file was made with a newer version (v{version}).");
        if (version < Version)
            throw new InvalidDataException($"This .miianim file uses an old format (v{version}) that is no longer supported.");

        var animation = new MiiAnimation();
        var hasHeader = false;
        var shapes = new List<ParticleShape>();
        var particleShapes = new List<(ParticleEffect Effect, int Shape)>();
        while (!reader.AtEnd)
        {
            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunk = new SpanReader(reader.ReadBytes((int)reader.ReadVarUInt()));
            switch (tag)
            {
                case HeaderTag:
                    hasHeader = true;
                    animation.Fps = chunk.ReadByte();
                    animation.Length = (int)chunk.ReadVarUInt();
                    animation.Name = chunk.ReadString();
                    break;
                case AuthorMiiTag:
                    animation.AuthorMii = chunk.ReadBytes(MiiAnimation.MiiDataLength).ToArray();
                    break;
                case TracksTag:
                    ReadTracks(ref chunk, animation.Tracks);
                    break;
                case EventsTag:
                    var eventCount = (int)chunk.ReadVarUInt();
                    var frame = 0;
                    for (var e = 0; e < eventCount; e++)
                    {
                        frame += (int)chunk.ReadVarUInt();
                        animation.Events.Add(new AnimEvent(frame, chunk.ReadString()));
                    }

                    break;
                case ActorsTag:
                    var actorCount = (int)chunk.ReadVarUInt();
                    for (var a = 0; a < actorCount; a++)
                    {
                        var record = new SpanReader(chunk.ReadBytes((int)chunk.ReadVarUInt()));
                        var actor = animation.AddActor(record.ReadString());
                        if ((record.ReadByte() & 1) != 0)
                            actor.PreviewMii = record.ReadBytes(MiiAnimation.MiiDataLength).ToArray();
                        ReadTracks(ref record, actor.Motion.Tracks);
                    }

                    break;
                case ShapesTag:
                    var shapeCount = (int)chunk.ReadVarUInt();
                    for (var s = 0; s < shapeCount; s++)
                    {
                        var record = new SpanReader(chunk.ReadBytes((int)chunk.ReadVarUInt()));
                        var name = record.ReadString();
                        var svg = record.ReadString();
                        if (!ParticleShape.IsSvg(svg))
                            throw new InvalidDataException($"Particle shape '{name}' is not an SVG document.");
                        shapes.Add(new ParticleShape(name, svg));
                    }

                    break;
                case ParticlesTag:
                    var effectCount = (int)chunk.ReadVarUInt();
                    for (var p = 0; p < effectCount; p++)
                    {
                        var effect = ReadParticle(chunk.ReadBytes((int)chunk.ReadVarUInt()), out var shapeIndex);
                        animation.Particles.Add(effect);
                        particleShapes.Add((effect, shapeIndex));
                    }

                    break;
            }
        }

        if (!hasHeader)
            throw new InvalidDataException("The .miianim file has no header.");
        foreach (var (effect, index) in particleShapes)
        {
            if (index >= shapes.Count)
                throw new InvalidDataException($"Particle effect '{effect.Name}' uses a shape the file doesn't have.");
            effect.Shape = index >= 0 ? shapes[index] : null;
        }

        return animation;
    }

    private static void ReadTracks(ref SpanReader reader, Dictionary<TrackId, AnimCurve> tracks)
    {
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

            tracks[new TrackId(target, channel)] = curve;
        }
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

    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        public int Position;

        public SpanReader(ReadOnlySpan<byte> data) => _data = data;

        public bool AtEnd => Position >= _data.Length;

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
            var span = _data.Slice(Position, count);
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

        public string ReadString() => Encoding.UTF8.GetString(ReadBytes((int)ReadVarUInt()));
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

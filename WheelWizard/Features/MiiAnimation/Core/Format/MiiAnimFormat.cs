using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;
using System.Text;
using MiiAnim.Core.Animation;

namespace MiiAnim.Core.Format;

/// <summary>
/// Reads .miianim files (the Mii Animator writes them). All numbers are little-endian; "varuint" is LEB128 and
/// "string" is varuint + UTF-8, which is exactly what <see cref="BinaryReader"/> reads.
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
///   PART   record list of particle effects (fields in <see cref="ReadParticle"/> order)
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

    public static MiiAnimation Read(byte[] data)
    {
        if (data.Length < 9 || !data.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("Not a .miianim file.");
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4));
        if (storedCrc != Crc32.HashToUInt32(data.AsSpan(0, data.Length - 4)))
            throw new InvalidDataException("The .miianim file is corrupted (checksum mismatch).");

        using var reader = new Reader(data[4..^4]);
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
            var tag = Encoding.ASCII.GetString(reader.Exactly(4));
            using var chunk = reader.Record();
            switch (tag)
            {
                case HeaderTag:
                    hasHeader = true;
                    animation.Fps = chunk.ReadByte();
                    animation.Length = chunk.Read7BitEncodedInt();
                    animation.Name = chunk.ReadString();
                    break;
                case AuthorMiiTag:
                    animation.AuthorMii = chunk.Exactly(MiiAnimation.MiiDataLength);
                    break;
                case TracksTag:
                    ReadTracks(chunk, animation.Tracks);
                    break;
                case EventsTag:
                    var eventCount = chunk.Read7BitEncodedInt();
                    var frame = 0;
                    for (var e = 0; e < eventCount; e++)
                    {
                        frame += chunk.Read7BitEncodedInt();
                        animation.Events.Add(new AnimEvent(frame, chunk.ReadString()));
                    }

                    break;
                case ActorsTag:
                    var actorCount = chunk.Read7BitEncodedInt();
                    for (var a = 0; a < actorCount; a++)
                    {
                        using var record = chunk.Record();
                        var actor = animation.AddActor(record.ReadString());
                        if ((record.ReadByte() & 1) != 0)
                            actor.PreviewMii = record.Exactly(MiiAnimation.MiiDataLength);
                        ReadTracks(record, actor.Motion.Tracks);
                    }

                    break;
                case ShapesTag:
                    var shapeCount = chunk.Read7BitEncodedInt();
                    for (var s = 0; s < shapeCount; s++)
                    {
                        using var record = chunk.Record();
                        var name = record.ReadString();
                        var svg = record.ReadString();
                        if (!ParticleShape.IsSvg(svg))
                            throw new InvalidDataException($"Particle shape '{name}' is not an SVG document.");
                        shapes.Add(new ParticleShape(name, svg));
                    }

                    break;
                case ParticlesTag:
                    var effectCount = chunk.Read7BitEncodedInt();
                    for (var p = 0; p < effectCount; p++)
                    {
                        using var record = chunk.Record();
                        var effect = ReadParticle(record, out var shapeIndex);
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

    /// <summary>The shape is its position in the SHAP chunk + 1 (0 = no shape).</summary>
    private static ParticleEffect ReadParticle(Reader r, out int shapeIndex)
    {
        // Fields missing from a shorter (older) record keep their defaults; unknown trailing bytes are ignored.
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

    private static void ReadTracks(Reader reader, Dictionary<TrackId, AnimCurve> tracks)
    {
        var trackCount = reader.Read7BitEncodedInt();
        for (var t = 0; t < trackCount; t++)
        {
            var target = TrackTarget.Unpack(reader.ReadByte());
            var channel = (Channel)reader.ReadByte();
            var encodingByte = reader.ReadByte();
            var encoding = (ValueEncoding)(encodingByte & 0x7F);
            Interpolation? shared = (encodingByte & SharedInterpolationFlag) != 0 ? (Interpolation)reader.ReadByte() : null;
            var keyCount = reader.Read7BitEncodedInt();
            var curve = new AnimCurve();
            var frame = 0;
            for (var k = 0; k < keyCount; k++)
            {
                var packed = (uint)reader.Read7BitEncodedInt();
                frame += (int)(shared.HasValue ? packed : packed >> 4);
                var interpolation = shared ?? (Interpolation)(packed & 0xF);
                var key = new Keyframe(frame, ReadValue(reader, encoding), interpolation);
                if (interpolation == Interpolation.Custom)
                {
                    key.HandleOut = new Vector2(ReadValue(reader, encoding), ReadValue(reader, encoding));
                    key.HandleIn = new Vector2(ReadValue(reader, encoding), ReadValue(reader, encoding));
                }

                curve.SetKey(key);
            }

            tracks[new TrackId(target, channel)] = curve;
        }
    }

    private static float ReadValue(Reader reader, ValueEncoding encoding) =>
        encoding switch
        {
            ValueEncoding.Byte => reader.ReadByte(),
            ValueEncoding.Float16 => (float)reader.ReadHalf(),
            ValueEncoding.Hundredths16 => reader.ReadInt16() / 100f,
            ValueEncoding.Float32 => reader.ReadSingle(),
            _ => throw new InvalidDataException($"Unknown value encoding {(byte)encoding}."),
        };

    private sealed class Reader(byte[] data) : BinaryReader(new MemoryStream(data))
    {
        public bool AtEnd => BaseStream.Position >= BaseStream.Length;

        private bool Has(int bytes) => BaseStream.Position + bytes <= BaseStream.Length;

        /// <summary>Like <see cref="BinaryReader.ReadBytes"/>, but a short read is an error instead of a shorter array.</summary>
        public byte[] Exactly(int count) =>
            count >= 0 && Has(count) ? ReadBytes(count) : throw new InvalidDataException("Unexpected end of .miianim file.");

        /// <summary>A length-prefixed chunk or record, read on its own so unknown trailing bytes are skipped.</summary>
        public Reader Record() => new(Exactly(Read7BitEncodedInt()));

        // Appendable record fields: the fallback when an older file's record ends before this field.
        public byte U8(byte fallback) => Has(1) ? ReadByte() : fallback;

        public uint U32(uint fallback) => Has(4) ? ReadUInt32() : fallback;

        public float F32(float fallback) => Has(4) && ReadSingle() is var value && float.IsFinite(value) ? value : fallback;

        public Vector3 Vec3(Vector3 fallback) => Has(12) ? new Vector3(F32(0), F32(0), F32(0)) : fallback;

        public Vector4 Rgba(Vector4 fallback) => Has(4) ? new Vector4(U8(0) / 255f, U8(0) / 255f, U8(0) / 255f, U8(0) / 255f) : fallback;

        public uint VarUInt(uint fallback) => Has(1) ? (uint)Read7BitEncodedInt() : fallback;

        public string String(string fallback) => Has(1) ? ReadString() : fallback;
    }
}

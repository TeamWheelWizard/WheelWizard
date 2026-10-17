using System.Buffers.Binary;
using System.Numerics;

namespace WheelWizard.MiiRendering.FaceLibrary;

/// <summary>
/// One face library shape as a plain triangle mesh: the part's GX vertex arrays and display list (RFLiInitShapeRes)
/// unpacked into one vertex per used position/normal/texcoord combination. Triangles are counter-clockwise.
/// </summary>
public sealed record RflShape(Vector3[] Positions, Vector3[] Normals, Vector2[] Texcoords, int[] Indices)
{
    public bool IsEmpty => Indices.Length == 0;

    public static readonly RflShape Empty = new([], [], [], []);

    // Fixed point of the face library's vertex formats (RFLLoadVertexSetting).
    private const float PositionScale = 1f / (1 << 8);
    private const float NormalScale = 1f / (1 << 14);
    private const float TexcoordScale = 1f / (1 << 13);

    private const byte GxQuads = 0x80;
    private const byte GxTriangles = 0x90;
    private const byte GxTriangleStrip = 0x98;
    private const byte GxTriangleFan = 0xA0;

    /// <summary>Where the faceline puts the nose, beard and hair (only faceline shapes have these).</summary>
    public readonly record struct FacelineAnchors(Vector3 Nose, Vector3 Beard, Vector3 Hair);

    public static FacelineAnchors ReadAnchors(ReadOnlySpan<byte> faceline)
    {
        if (faceline.Length < 4 + 36 || BinaryPrimitives.ReadUInt32BigEndian(faceline) != Magic(RflArchive.Faceline))
            return default;

        static Vector3 Read(ReadOnlySpan<byte> data) =>
            new(
                BinaryPrimitives.ReadSingleBigEndian(data),
                BinaryPrimitives.ReadSingleBigEndian(data[4..]),
                BinaryPrimitives.ReadSingleBigEndian(data[8..])
            );

        return new FacelineAnchors(Read(faceline[4..]), Read(faceline[16..]), Read(faceline[28..]));
    }

    /// <summary>
    /// Unpacks a shape, scaled by <paramref name="scale"/>, mirrored on X when <paramref name="flipX"/> (hair parted
    /// the other way), then moved by <paramref name="translate"/>.
    /// </summary>
    public static OperationResult<RflShape> Decode(
        RflArchive archive,
        ReadOnlySpan<byte> data,
        float scale = 1f,
        Vector3 translate = default,
        bool flipX = false
    )
    {
        try
        {
            return DecodeCore(archive, data, scale, translate, flipX);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return Fail($"Face library shape {archive} is damaged.");
        }
    }

    private static OperationResult<RflShape> DecodeCore(
        RflArchive archive,
        ReadOnlySpan<byte> data,
        float scale,
        Vector3 translate,
        bool flipX
    )
    {
        if (data.Length < 6)
            return Empty;
        if (BinaryPrimitives.ReadUInt32BigEndian(data) != Magic(archive))
            return Fail($"Face library shape {archive} has the wrong header.");

        var p = archive == RflArchive.Faceline ? 4 + 36 : 4;
        var positionCount = BinaryPrimitives.ReadUInt16BigEndian(data[p..]);
        if (positionCount == 0)
            return Empty;
        p += 2;

        var flip = flipX ? -1f : 1f;
        var positions = new Vector3[positionCount];
        for (var i = 0; i < positions.Length; i++, p += 6)
        {
            var position = ReadS16x3(data[p..]) * PositionScale;
            positions[i] = new Vector3(position.X * flip, position.Y, position.Z) * scale + translate;
        }

        var normals = new Vector3[BinaryPrimitives.ReadUInt16BigEndian(data[p..])];
        p += 2;
        for (var i = 0; i < normals.Length; i++, p += 6)
        {
            var normal = ReadS16x3(data[p..]) * NormalScale;
            normals[i] = new Vector3(normal.X * flip, normal.Y, normal.Z);
        }

        var hasTexcoords = archive is not (RflArchive.Nose or RflArchive.Forehead or RflArchive.Hair or RflArchive.Beard);
        var texcoords = Array.Empty<Vector2>();
        if (hasTexcoords)
        {
            texcoords = new Vector2[BinaryPrimitives.ReadUInt16BigEndian(data[p..])];
            p += 2;
            for (var i = 0; i < texcoords.Length; i++, p += 4)
            {
                texcoords[i] =
                    new Vector2(BinaryPrimitives.ReadInt16BigEndian(data[p..]), BinaryPrimitives.ReadInt16BigEndian(data[(p + 2)..]))
                    * TexcoordScale;
            }
        }

        // The display list indexes positions, normals and texcoords separately; a GPU vertex needs all three.
        var vertices = new Dictionary<(int Position, int Normal, int Texcoord), int>();
        var outPositions = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outTexcoords = new List<Vector2>();
        var indices = new List<int>();
        var primitiveCount = data[p++];
        var corners = new List<int>();
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            var count = data[p++];
            var type = data[p++];
            corners.Clear();
            for (var i = 0; i < count; i++)
            {
                var key = (data[p++], data[p++], hasTexcoords ? data[p++] : 0);
                if (!vertices.TryGetValue(key, out var vertex))
                {
                    vertex = outPositions.Count;
                    vertices[key] = vertex;
                    outPositions.Add(positions[key.Item1]);
                    outNormals.Add(key.Item2 < normals.Length ? normals[key.Item2] : Vector3.UnitZ);
                    outTexcoords.Add(key.Item3 < texcoords.Length ? texcoords[key.Item3] : Vector2.Zero);
                }

                corners.Add(vertex);
            }

            AddTriangles(type, corners, indices, flipX);
        }

        return new RflShape(outPositions.ToArray(), outNormals.ToArray(), outTexcoords.ToArray(), indices.ToArray());
    }

    /// <summary>
    /// GX faces are clockwise; they're emitted counter-clockwise. Mirroring already reverses them, so flipped
    /// shapes keep their order.
    /// </summary>
    private static void AddTriangles(byte type, List<int> corners, List<int> indices, bool flipped)
    {
        void Add(int a, int b, int c)
        {
            indices.Add(corners[a]);
            indices.Add(flipped ? corners[b] : corners[c]);
            indices.Add(flipped ? corners[c] : corners[b]);
        }

        switch (type)
        {
            case GxTriangles:
                for (var i = 0; i + 2 < corners.Count; i += 3)
                    Add(i, i + 1, i + 2);
                break;
            case GxTriangleStrip:
                for (var i = 0; i + 2 < corners.Count; i++)
                {
                    if ((i & 1) == 0)
                        Add(i, i + 1, i + 2);
                    else
                        Add(i + 1, i, i + 2);
                }
                break;
            case GxTriangleFan:
                for (var i = 1; i + 1 < corners.Count; i++)
                    Add(0, i, i + 1);
                break;
            case GxQuads:
                for (var i = 0; i + 3 < corners.Count; i += 4)
                {
                    Add(i, i + 1, i + 2);
                    Add(i, i + 2, i + 3);
                }
                break;
        }
    }

    private static Vector3 ReadS16x3(ReadOnlySpan<byte> data) =>
        new(
            BinaryPrimitives.ReadInt16BigEndian(data),
            BinaryPrimitives.ReadInt16BigEndian(data[2..]),
            BinaryPrimitives.ReadInt16BigEndian(data[4..])
        );

    private static uint Magic(RflArchive archive) =>
        archive switch
        {
            RflArchive.Nose => 0x6E6F7365, // nose
            RflArchive.Forehead => 0x66726864, // frhd
            RflArchive.Faceline => 0x66616365, // face
            RflArchive.Hair => 0x68616972, // hair
            RflArchive.Cap => 0x6361705F, // cap_
            RflArchive.Beard => 0x62657264, // berd
            RflArchive.Noseline => 0x6E736C6E, // nsln
            RflArchive.Mask => 0x6D61736B, // mask
            RflArchive.Glass => 0x676C6173, // glas
            _ => 0,
        };
}

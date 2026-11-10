using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace MiiAnim.Core.Rig;

/// <summary>
/// Minimal glTF 2.0 binary (.glb) reader. Only supports what the Mii body assets use:
/// nodes with TRS, one skin, and triangle meshes with float/byte/short accessors.
/// </summary>
internal sealed class GlbFile
{
    private readonly JsonElement _root;
    private readonly byte[] _bin;

    public GlbNode[] Nodes { get; }

    private GlbFile(JsonElement root, byte[] bin)
    {
        _root = root;
        _bin = bin;
        Nodes = ReadNodes();
    }

    public static GlbFile Parse(byte[] bytes)
    {
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46546C67)
            throw new InvalidDataException("Not a GLB file.");

        var offset = 12;
        JsonElement? json = null;
        byte[] bin = [];
        while (offset + 8 <= bytes.Length)
        {
            var chunkLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
            var data = bytes.AsSpan(offset + 8, chunkLength);
            if (chunkType == 0x4E4F534A)
                json = JsonDocument.Parse(data.ToArray()).RootElement.Clone();
            else if (chunkType == 0x004E4942)
                bin = data.ToArray();
            offset += 8 + chunkLength;
        }

        if (json is null)
            throw new InvalidDataException("GLB has no JSON chunk.");
        return new GlbFile(json.Value, bin);
    }

    public JsonElement Json => _root;

    private GlbNode[] ReadNodes()
    {
        var nodes = new List<GlbNode>();
        foreach (var n in _root.GetProperty("nodes").EnumerateArray())
        {
            var t = n.TryGetProperty("translation", out var tp) ? ReadVector3(tp) : Vector3.Zero;
            var r = n.TryGetProperty("rotation", out var rp) ? ReadQuaternion(rp) : Quaternion.Identity;
            var s = n.TryGetProperty("scale", out var sp) ? ReadVector3(sp) : Vector3.One;
            var children = n.TryGetProperty("children", out var cp) ? cp.EnumerateArray().Select(c => c.GetInt32()).ToArray() : [];
            var name = n.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
            int? mesh = n.TryGetProperty("mesh", out var mp) ? mp.GetInt32() : null;
            nodes.Add(new GlbNode(name, t, Quaternion.Normalize(r), s, children, mesh));
        }

        var result = nodes.ToArray();
        for (var i = 0; i < result.Length; i++)
        {
            foreach (var child in result[i].Children)
                result[child].Parent = i;
        }

        return result;
    }

    /// <summary>World matrix of a node in its rest (TRS) pose, using System.Numerics row-vector convention.</summary>
    public Matrix4x4 RestWorld(int node)
    {
        var m = Nodes[node].LocalMatrix;
        var parent = Nodes[node].Parent;
        while (parent >= 0)
        {
            m *= Nodes[parent].LocalMatrix;
            parent = Nodes[parent].Parent;
        }

        return m;
    }

    public float[] ReadFloats(int accessorIndex, out int components)
    {
        var accessor = _root.GetProperty("accessors")[accessorIndex];
        components = ComponentCount(accessor.GetProperty("type").GetString()!);
        var count = accessor.GetProperty("count").GetInt32();
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var normalized = accessor.TryGetProperty("normalized", out var np) && np.GetBoolean();
        var (start, stride) = Locate(accessor, components, ComponentSize(componentType));

        var values = new float[count * components];
        for (var i = 0; i < count; i++)
        {
            var o = start + i * stride;
            for (var c = 0; c < components; c++)
                values[i * components + c] = ReadComponent(o, c, componentType, normalized);
        }

        return values;
    }

    public int[] ReadInts(int accessorIndex, out int components)
    {
        var accessor = _root.GetProperty("accessors")[accessorIndex];
        components = ComponentCount(accessor.GetProperty("type").GetString()!);
        var count = accessor.GetProperty("count").GetInt32();
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var size = ComponentSize(componentType);
        var (start, stride) = Locate(accessor, components, size);

        var values = new int[count * components];
        for (var i = 0; i < count; i++)
        {
            var o = start + i * stride;
            for (var c = 0; c < components; c++)
            {
                var p = o + c * size;
                values[i * components + c] = componentType switch
                {
                    5121 => _bin[p],
                    5123 => BinaryPrimitives.ReadUInt16LittleEndian(_bin.AsSpan(p)),
                    5125 => (int)BinaryPrimitives.ReadUInt32LittleEndian(_bin.AsSpan(p)),
                    _ => throw new InvalidDataException($"Unsupported index component type {componentType}."),
                };
            }
        }

        return values;
    }

    private (int Start, int Stride) Locate(JsonElement accessor, int components, int componentSize)
    {
        var view = _root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
        var accessorOffset = accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        var stride = view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : components * componentSize;
        return (viewOffset + accessorOffset, stride);
    }

    private float ReadComponent(int offset, int component, int componentType, bool normalized)
    {
        var size = ComponentSize(componentType);
        var p = offset + component * size;
        return componentType switch
        {
            5126 => BinaryPrimitives.ReadSingleLittleEndian(_bin.AsSpan(p)),
            5121 => normalized ? _bin[p] / 255f : _bin[p],
            5123 => normalized
                ? BinaryPrimitives.ReadUInt16LittleEndian(_bin.AsSpan(p)) / 65535f
                : BinaryPrimitives.ReadUInt16LittleEndian(_bin.AsSpan(p)),
            _ => throw new InvalidDataException($"Unsupported component type {componentType}."),
        };
    }

    private static int ComponentCount(string type) =>
        type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT4" => 16,
            _ => throw new InvalidDataException($"Unsupported accessor type {type}."),
        };

    private static int ComponentSize(int componentType) =>
        componentType switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            _ => throw new InvalidDataException($"Unsupported component type {componentType}."),
        };

    private static Vector3 ReadVector3(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

    private static Quaternion ReadQuaternion(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());
}

internal sealed class GlbNode(string name, Vector3 translation, Quaternion rotation, Vector3 scale, int[] children, int? mesh)
{
    public string Name { get; } = name;
    public Vector3 Translation { get; } = translation;
    public Quaternion Rotation { get; } = rotation;
    public Vector3 Scale { get; } = scale;
    public int[] Children { get; } = children;
    public int? Mesh { get; } = mesh;
    public int Parent { get; set; } = -1;

    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);
}

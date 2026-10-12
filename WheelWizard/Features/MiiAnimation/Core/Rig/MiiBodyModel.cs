using System.Collections.Concurrent;
using System.Numerics;

namespace MiiAnim.Core.Rig;

public sealed class MiiBodyMesh
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Vector2[] Texcoords { get; init; }

    /// <summary>Up to 4 bone influences per vertex.</summary>
    public required byte[] Joints { get; init; }

    public required float[] Weights { get; init; }
    public required int[] Indices { get; init; }

    /// <summary>The legs mesh is tinted with the pants color, the other with the favorite color.</summary>
    public required bool IsPants { get; init; }
}

/// <summary>
/// The skinned 3DS Mii body, in "canonical" units (the GLB's units).
/// WheelWizard's renderer units are canonical units * <see cref="CanonicalToRenderUnits"/>.
/// </summary>
public sealed class MiiBodyModel
{
    /// <summary>WheelWizard's static body is exactly this GLB scaled by 0.7.</summary>
    public const float CanonicalToRenderUnits = 0.7f;

    /// <summary>
    /// Where the FFL head sits in the rest pose. WheelWizard uses headYTranslate 10.7766 * modelScale 7 (render units).
    /// </summary>
    public static readonly Vector3 HeadRestPosition = new(0f, 10.7766f * 7f / CanonicalToRenderUnits, 0f);

    private static readonly ConcurrentDictionary<bool, MiiBodyModel> Cache = new();

    public bool IsFemale { get; }
    public Vector3[] RestTranslation { get; } = new Vector3[MiiSkeletonInfo.BoneCount];
    public Quaternion[] RestRotation { get; } = new Quaternion[MiiSkeletonInfo.BoneCount];
    public Matrix4x4[] InverseBind { get; } = new Matrix4x4[MiiSkeletonInfo.BoneCount];
    public IReadOnlyList<MiiBodyMesh> Meshes { get; }

    private MiiBodyModel(bool isFemale, IReadOnlyList<MiiBodyMesh> meshes)
    {
        IsFemale = isFemale;
        Meshes = meshes;
    }

    public static MiiBodyModel Get(bool female) => Cache.GetOrAdd(female, Load);

    private static MiiBodyModel Load(bool female)
    {
        var resource = female ? "MiiAnim.Core.Assets.FemaleBody.glb" : "MiiAnim.Core.Assets.MaleBody.glb";
        using var stream =
            typeof(MiiBodyModel).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded body model '{resource}'.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return FromGlb(ms.ToArray(), female);
    }

    public static MiiBodyModel FromGlb(byte[] glbBytes, bool female)
    {
        var glb = GlbFile.Parse(glbBytes);
        var nodeIndexByBone = new int[MiiSkeletonInfo.BoneCount];
        foreach (var bone in MiiSkeletonInfo.AllBones)
        {
            var name = MiiSkeletonInfo.GlbName(bone);
            nodeIndexByBone[(int)bone] = Array.FindIndex(glb.Nodes, n => n.Name == name);
            if (nodeIndexByBone[(int)bone] < 0)
                throw new InvalidDataException($"Body model is missing bone '{name}'.");
        }

        var skin = glb.Json.GetProperty("skins")[0];
        var skinJoints = skin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
        var ibmValues = glb.ReadFloats(skin.GetProperty("inverseBindMatrices").GetInt32(), out _);

        // Map every skin joint to one of our bones (non-skeleton helper joints fall back to the root).
        var jointToBone = new byte[skinJoints.Length];
        var jointIbm = new Matrix4x4[skinJoints.Length];
        for (var j = 0; j < skinJoints.Length; j++)
        {
            var boneIndex = Array.IndexOf(nodeIndexByBone, skinJoints[j]);
            jointToBone[j] = (byte)(boneIndex < 0 ? 0 : boneIndex);
            var m = ibmValues.AsSpan(j * 16, 16);
            // glTF is column-major column-vector; reading it in order gives the row-vector (System.Numerics) matrix.
            jointIbm[j] = new Matrix4x4(
                m[0],
                m[1],
                m[2],
                m[3],
                m[4],
                m[5],
                m[6],
                m[7],
                m[8],
                m[9],
                m[10],
                m[11],
                m[12],
                m[13],
                m[14],
                m[15]
            );
        }

        var meshes = new List<MiiBodyMesh>();
        var meshNodes = glb.Nodes.Where(n => n.Mesh.HasValue).ToList();
        var jsonMeshes = glb.Json.GetProperty("meshes");
        for (var meshIndex = 0; meshIndex < jsonMeshes.GetArrayLength(); meshIndex++)
        {
            var nodeName = meshNodes.FirstOrDefault(n => n.Mesh == meshIndex)?.Name ?? "";
            var isPants = nodeName.Contains("leg", StringComparison.OrdinalIgnoreCase) || (nodeName.Length == 0 && meshIndex == 1);
            foreach (var primitive in jsonMeshes[meshIndex].GetProperty("primitives").EnumerateArray())
            {
                var attributes = primitive.GetProperty("attributes");
                var positions = ToVector3(glb.ReadFloats(attributes.GetProperty("POSITION").GetInt32(), out _));
                var normals = ToVector3(glb.ReadFloats(attributes.GetProperty("NORMAL").GetInt32(), out _));
                var texcoords = attributes.TryGetProperty("TEXCOORD_0", out var uvp)
                    ? ToVector2(glb.ReadFloats(uvp.GetInt32(), out _))
                    : new Vector2[positions.Length];
                var rawJoints = glb.ReadInts(attributes.GetProperty("JOINTS_0").GetInt32(), out _);
                var weights = glb.ReadFloats(attributes.GetProperty("WEIGHTS_0").GetInt32(), out _);
                var indices = primitive.TryGetProperty("indices", out var ip)
                    ? glb.ReadInts(ip.GetInt32(), out _)
                    : Enumerable.Range(0, positions.Length).ToArray();

                // The body only weights real skeleton joints; helper joints (if any) fall back to the root.
                var joints = new byte[rawJoints.Length];
                for (var k = 0; k < rawJoints.Length; k++)
                    joints[k] = jointToBone[rawJoints[k]];

                meshes.Add(
                    new MiiBodyMesh
                    {
                        Positions = positions,
                        Normals = normals,
                        Texcoords = texcoords,
                        Joints = joints,
                        Weights = weights,
                        Indices = indices,
                        IsPants = isPants,
                    }
                );
            }
        }

        var model = new MiiBodyModel(female, meshes);
        foreach (var bone in MiiSkeletonInfo.AllBones)
        {
            var node = glb.Nodes[nodeIndexByBone[(int)bone]];
            var i = (int)bone;
            if (bone == MiiBone.Root)
            {
                // Fold the GLB's helper parents (body / all_root) into the root's rest transform.
                Matrix4x4.Decompose(glb.RestWorld(nodeIndexByBone[i]), out _, out var rootRotation, out var rootTranslation);
                model.RestTranslation[i] = rootTranslation;
                model.RestRotation[i] = Quaternion.Normalize(rootRotation);
            }
            else
            {
                model.RestTranslation[i] = node.Translation;
                model.RestRotation[i] = node.Rotation;
            }

            var joint = Array.IndexOf(skinJoints, nodeIndexByBone[i]);
            model.InverseBind[i] = joint >= 0 ? jointIbm[joint] : Matrix4x4.Identity;
        }

        return model;
    }

    /// <summary>WheelWizard's per-Mii body scale (from height/build, both 0-127).</summary>
    public static Vector3 CalculateBodyScale(float build, float height)
    {
        build = Math.Clamp(build, 0f, 127f);
        height = Math.Clamp(height, 0f, 127f);
        var x = (build * (height * 0.003671875f + 0.4f)) / 128.0f + height * 0.001796875f + 0.4f;
        var y = (height * 0.006015625f) + 0.5f;
        return new Vector3(x, y, x);
    }

    private static Vector3[] ToVector3(float[] values)
    {
        var result = new Vector3[values.Length / 3];
        for (var i = 0; i < result.Length; i++)
            result[i] = new Vector3(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
        return result;
    }

    private static Vector2[] ToVector2(float[] values)
    {
        var result = new Vector2[values.Length / 2];
        for (var i = 0; i < result.Length; i++)
            result[i] = new Vector2(values[i * 2], values[i * 2 + 1]);
        return result;
    }
}

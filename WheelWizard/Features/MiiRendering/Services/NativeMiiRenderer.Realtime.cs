using System.Collections.Concurrent;
using System.Numerics;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Rig;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Configuration;

namespace WheelWizard.MiiRendering.Services;

/// <summary>A GPU-ready copy of one FFL head draw call (same data WheelWizard's CPU rasterizer consumes).</summary>
public sealed class HeadMeshData
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Vector3[] Tangents { get; init; }
    public required Vector2[] Texcoords { get; init; }
    public required Vector4[] Parameters { get; init; }

    /// <summary>Triangle list (strips are converted).</summary>
    public required int[] Indices { get; init; }

    public required int CullMode { get; init; }
    public required int ModulateMode { get; init; }
    public required Vector4 ColorR { get; init; }
    public required Vector4 ColorG { get; init; }
    public required Vector4 ColorB { get; init; }

    /// <summary>RGBA8 pixels, or null when the draw call has no texture.</summary>
    public byte[]? TexturePixels { get; init; }

    public int TextureWidth { get; init; }
    public int TextureHeight { get; init; }
    public required bool HasTangent { get; init; }
    public required Vector3 Ambient { get; init; }
    public required Vector3 Diffuse { get; init; }
    public required Vector3 Specular { get; init; }
    public required float SpecularPower { get; init; }
    public required int SpecularMode { get; init; }
    public required Vector3 RimColor { get; init; }
}

public sealed partial class NativeMiiRenderer
{
    public static readonly Vector4 PantsColor = new(0.2509804f, 0.2745099f, 0.30588239f, 1.0f);

    public static Vector4 FavoriteColor(int index) => GetFavoriteColor(index);

    /// <summary>Lighting constants shared by the GPU shader so it matches the CPU renderer.</summary>
    public static (Vector3 Ambient, Vector3 Diffuse, Vector3 Specular, Vector3 Direction) LightConstants =>
        (LightAmbient, LightDiffuse, LightSpecular, LightDirection);

    /// <summary>Body/pants material (modulate types 9/10) for the GPU body shader.</summary>
    public static (Vector3 Ambient, Vector3 Diffuse, Vector3 Specular, float Power, Vector3 Rim) BodyMaterial(bool pants)
    {
        var m = ResolveMaterial(pants ? FflNativeInterop.ModulateTypeCustomPants : FflNativeInterop.ModulateTypeCustomBody);
        return (m.Ambient, m.Diffuse, m.Specular, m.SpecularPower, m.RimColor);
    }

    /// <summary>Builds the head meshes for a Mii (studio data hex) and FFL expression id.</summary>
    public OperationResult<List<HeadMeshData>> BuildHeadModel(string studioData, int expressionId)
    {
        var resourcePathResult = resourceLocator.GetFflResourcePath();
        if (resourcePathResult.IsFailure)
            return resourcePathResult.Error!;
        var resourcePath = resourcePathResult.Value;
        var archiveResult = GetManagedArchive(resourcePath);
        if (archiveResult.IsFailure)
            return archiveResult.Error!;

        var charInfoResult = GetOrCreateCharInfo(studioData);
        if (charInfoResult.IsFailure)
            return charInfoResult.Error!;

        var specifications = new MiiImageSpecifications
        {
            Size = MiiImageSpecifications.ImageSize.medium,
            Type = MiiImageSpecifications.BodyType.all_body,
        };
        var request = BuildRequest(studioData, specifications);
        var cachedResult = GetOrCreateCachedHeadDrawParams(
            archiveResult.Value,
            charInfoResult.Value,
            request,
            expressionId,
            studioData,
            resourcePath
        );
        if (cachedResult.IsFailure)
            return cachedResult.Error!;

        var meshes = new List<HeadMeshData>();
        foreach (var mesh in cachedResult.Value.DrawMeshes)
        {
            var modulate = BuildModulateContext(mesh.DrawParam.modulateParam);
            byte[]? pixels = null;
            if (modulate.Texture is { } texture)
                pixels = ToRgba(texture);

            meshes.Add(
                new HeadMeshData
                {
                    Positions = mesh.Positions.ToArray(),
                    Normals = mesh.Normals.ToArray(),
                    Tangents = mesh.Tangents.ToArray(),
                    Texcoords = mesh.Texcoords.ToArray(),
                    Parameters = mesh.VertexParameters.Values.ToArray(),
                    Indices = ToTriangleList(mesh.Indices, mesh.DrawParam.primitiveParam.primitiveType),
                    CullMode = mesh.DrawParam.cullMode,
                    ModulateMode = modulate.Mode,
                    ColorR = modulate.ColorR,
                    ColorG = modulate.ColorG,
                    ColorB = modulate.ColorB,
                    TexturePixels = pixels,
                    TextureWidth = modulate.Texture?.Width ?? 0,
                    TextureHeight = modulate.Texture?.Height ?? 0,
                    HasTangent = mesh.HasTangent,
                    Ambient = mesh.Material.Ambient,
                    Diffuse = mesh.Material.Diffuse,
                    Specular = mesh.Material.Specular,
                    SpecularPower = mesh.Material.SpecularPower,
                    SpecularMode = mesh.Material.SpecularMode,
                    RimColor = mesh.Material.RimColor,
                }
            );
        }

        return meshes;
    }

    private static int[] ToTriangleList(int[] indices, uint primitiveType)
    {
        if (primitiveType != FflNativeInterop.PrimitiveTriangleStrip)
            return indices.ToArray();

        var list = new List<int>(Math.Max(0, (indices.Length - 2) * 3));
        for (var i = 0; i + 2 < indices.Length; i++)
        {
            int a = indices[i],
                b = indices[i + 1],
                c = indices[i + 2];
            if ((i & 1) == 1)
                (b, c) = (c, b);
            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        return list.ToArray();
    }

    private static byte[] ToRgba(TextureData texture)
    {
        var rgba = new byte[texture.Width * texture.Height * 4];
        for (var y = 0; y < texture.Height; y++)
        for (var x = 0; x < texture.Width; x++)
        {
            var c = ReadTextureTexel(texture, x, y);
            var o = (y * texture.Width + x) * 4;
            rgba[o] = (byte)Math.Clamp((int)MathF.Round(c.X * 255f), 0, 255);
            rgba[o + 1] = (byte)Math.Clamp((int)MathF.Round(c.Y * 255f), 0, 255);
            rgba[o + 2] = (byte)Math.Clamp((int)MathF.Round(c.Z * 255f), 0, 255);
            rgba[o + 3] = (byte)Math.Clamp((int)MathF.Round(c.W * 255f), 0, 255);
        }

        return rgba;
    }

    private static readonly ConcurrentDictionary<bool, MiiRig> RealtimeRigs = new();

    /// <summary>
    /// Matrices, camera and colours to draw a posed Mii on the GPU so it looks exactly like <see cref="RenderToBuffer"/>
    /// with the same <paramref name="specifications"/>. The camera is framed on the rest pose, so animation moves inside the frame.
    /// </summary>
    /// <param name="aspect">Width / height of the target; the frame is fitted inside it like Stretch=Uniform.</param>
    public OperationResult<MiiRealtimeFrameSetup> GetRealtimeFrameSetup(
        string studioData,
        MiiImageSpecifications specifications,
        float aspect
    )
    {
        var charInfoResult = GetOrCreateCharInfo(studioData);
        if (charInfoResult.IsFailure)
            return charInfoResult.Error!;
        var charInfo = charInfoResult.Value;
        var request = BuildRequest(studioData, specifications);
        var viewParameters = ResolveViewParameters(request, charInfo);

        var gender = charInfo.gender % 2;
        if (gender < 0)
            gender += 2;
        var female = gender == 1;
        var bodyScale = CalculateBodyScale(Math.Clamp((float)charInfo.build, 0f, 127f), Math.Clamp((float)charInfo.height, 0f, 127f));

        var cameraRotate = ConvertDegreesToRadians(request.CameraXRotate, request.CameraYRotate, request.CameraZRotate);
        var modelRotate = ConvertDegreesToRadians(request.CharacterXRotate, request.CharacterYRotate, request.CharacterZRotate);
        var cameraPosition = CalculateCameraOrbitPosition(viewParameters.OrbitRadius * request.CameraZoom, cameraRotate);
        cameraPosition.Y += viewParameters.BaseCameraY + request.CameraVerticalOffset;
        var cameraTarget = viewParameters.Target + new Vector3(0f, request.CameraVerticalOffset, 0f);
        var baseRotation = CreateRotationMatrix(modelRotate);
        var hasBody = request.BodyType != MiiImageSpecifications.BodyType.face_only;
        if (hasBody && !viewParameters.IsCameraPositionAbsolute)
        {
            var restHead = RealtimeRigs
                .GetOrAdd(female, f => new MiiRig(MiiBodyModel.Get(f)))
                .RestPose.HeadMatrixForRender(bodyScale)
                .Translation;
            cameraPosition += restHead;
            cameraTarget += restHead;
        }

        // Same 15° camera as the CPU renderer; widen the vertical field of view for tall targets so nothing is cropped.
        aspect = Math.Max(0.05f, aspect);
        var halfFov = 7.5f * MathF.PI / 180f;
        var fovY = aspect >= 1f ? halfFov * 2f : 2f * MathF.Atan(MathF.Tan(halfFov) / aspect);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovY, aspect, 10f, 1200f);

        return new MiiRealtimeFrameSetup(
            female,
            hasBody,
            bodyScale,
            baseRotation,
            Matrix4x4.CreateScale(MiiBodyModel.CanonicalToRenderUnits) * baseRotation * Matrix4x4.CreateScale(bodyScale),
            Matrix4x4.CreateLookAt(cameraPosition, cameraTarget, CalculateUpVector(cameraRotate)),
            projection,
            ReadFavoriteColorOrDefault(charInfo.favoriteColor),
            PantsColor
        );
    }
}

/// <summary>See <see cref="NativeMiiRenderer.GetRealtimeFrameSetup"/>.</summary>
/// <param name="BodyMatrix">Applied after skinning: canonical → render units, character rotation, body scale.</param>
public sealed record MiiRealtimeFrameSetup(
    bool Female,
    bool HasBody,
    Vector3 BodyScale,
    Matrix4x4 CharacterRotation,
    Matrix4x4 BodyMatrix,
    Matrix4x4 View,
    Matrix4x4 Projection,
    Vector4 BodyColor,
    Vector4 PantsColor
)
{
    /// <summary>Model matrix for the FFL head in this pose.</summary>
    public Matrix4x4 HeadMatrix(MiiPose pose) => HasBody ? pose.HeadMatrixForRender(BodyScale) * CharacterRotation : CharacterRotation;
}

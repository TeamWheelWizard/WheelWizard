using System.Collections.Concurrent;
using System.Numerics;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Rig;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.FaceLibrary;

namespace WheelWizard.MiiRendering.Services;

/// <summary>One mesh of a Mii head, ready for the GPU (the same data the CPU rasterizer draws).</summary>
public sealed record HeadMeshData
{
    public required HeadShape Shape { get; init; }
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Vector2[] Texcoords { get; init; }

    /// <summary>Counter-clockwise triangle list.</summary>
    public required int[] Indices { get; init; }

    /// <summary>Drawn from both sides (the glasses); everything else culls its back faces.</summary>
    public bool DoubleSided { get; init; }

    /// <summary>The colour of an untextured mesh.</summary>
    public required Vector4 Color { get; init; }

    /// <summary>When set, the mesh is this texture (straight RGBA) instead of <see cref="Color"/>.</summary>
    public MiiTexture? Texture { get; init; }

    public required Vector3 Ambient { get; init; }
    public required Vector3 Diffuse { get; init; }
    public required Vector3 Specular { get; init; }
    public required float SpecularPower { get; init; }
    public required Vector3 RimColor { get; init; }
}

/// <summary>The part of the head a mesh is.</summary>
public enum HeadShape
{
    Faceline = 0,
    Beard = 1,
    Nose = 2,
    Forehead = 3,
    Hair = 4,
    Cap = 5,

    /// <summary>The face decals (eyes, eyebrows, mouth, mustache, mole) painted into one texture.</summary>
    Mask = 6,
    NoseLine = 7,
    Glass = 8,
}

/// <summary>How sharp a head's textures are. Small heads (lists, cards) don't need the full-size face texture.</summary>
public enum MiiHeadDetail
{
    /// <summary>512 px face texture.</summary>
    Full,

    /// <summary>256 px face texture: a quarter of the work to build and upload, for heads drawn up to ~250 px.</summary>
    Small,
}

/// <summary>Parts painted into the face mask texture.</summary>
[Flags]
public enum MiiMaskLayers
{
    None = 0,
    Eyes = 1,
    Eyebrows = 2,
    Mouth = 4,
    Mustache = 8,
    Mole = 16,
    All = Eyes | Eyebrows | Mouth | Mustache | Mole,
}

/// <summary>A face mask texture with only some of its parts (RGBA8, same layout as the head's mask texture).</summary>
public sealed record MiiMaskLayerTexture(byte[] Pixels, int Width, int Height);

public sealed partial class NativeMiiRenderer
{
    public static readonly Vector4 PantsColor = new(0.2509804f, 0.2745099f, 0.30588239f, 1.0f);

    public static Vector4 FavoriteColor(int index) => RflHead.FavoriteColor(index);

    /// <summary>Lighting constants shared by the GPU shader so it matches the CPU renderer.</summary>
    public static (Vector3 Ambient, Vector3 Diffuse, Vector3 Specular, Vector3 Direction) LightConstants =>
        (LightAmbient, LightDiffuse, LightSpecular, LightDirection);

    /// <summary>Body/pants material for the GPU body shader.</summary>
    public static (Vector3 Ambient, Vector3 Diffuse, Vector3 Specular, float Power, Vector3 Rim) BodyMaterial(bool pants)
    {
        var m = pants ? BodyPantsMaterial : BodyShirtMaterial;
        return (m.Ambient, m.Diffuse, m.Specular, m.SpecularPower, m.RimColor);
    }

    /// <summary>Builds the head meshes for a Mii (studio data hex) and expression id (<see cref="MiiExpression"/>).</summary>
    public OperationResult<List<HeadMeshData>> BuildHeadModel(
        string studioData,
        int expressionId,
        MiiHeadDetail detail = MiiHeadDetail.Full
    )
    {
        var charInfo = GetOrCreateCharInfo(studioData);
        if (charInfo.IsFailure)
            return charInfo.Error!;
        return GetOrCreateHead(studioData, charInfo.Value, expressionId, MaskResolution(detail));
    }

    /// <summary>
    /// The face mask of a Mii with only <paramref name="layers"/> painted in (e.g. just the eyes, or everything but
    /// them). Drawn on the head's mask mesh it lines up with the full mask, so parts can be animated on their own.
    /// </summary>
    public OperationResult<MiiMaskLayerTexture> BuildMaskLayer(
        string studioData,
        int expressionId,
        MiiMaskLayers layers,
        MiiHeadDetail detail = MiiHeadDetail.Full
    )
    {
        var resource = faceLibrary.Load();
        if (resource.IsFailure)
            return resource.Error!;
        var charInfo = GetOrCreateCharInfo(studioData);
        if (charInfo.IsFailure)
            return charInfo.Error!;

        var mask = RflHead.BuildMask(resource.Value, charInfo.Value, expressionId, layers, MaskResolution(detail));
        if (mask.IsFailure)
            return mask.Error!;
        return new MiiMaskLayerTexture(mask.Value.Rgba, mask.Value.Width, mask.Value.Height);
    }

    /// <summary>
    /// Where each face mask part of a Mii sits on the mask texture: one or two quads (left/right) of four corners in
    /// texture coordinates (0..1, same as the mask mesh's UVs).
    /// </summary>
    public OperationResult<IReadOnlyDictionary<MiiMaskLayers, Vector2[][]>> GetMaskPartQuads(string studioData)
    {
        var charInfo = GetOrCreateCharInfo(studioData);
        if (charInfo.IsFailure)
            return charInfo.Error!;
        return Ok(RflHead.MaskPartQuads(charInfo.Value));
    }

    /// <summary>The face mask resolution of a head of this detail (the same as a still image of its size).</summary>
    private static int MaskResolution(MiiHeadDetail detail) => detail == MiiHeadDetail.Small ? 256 : 512;

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
        var viewParameters = ResolveViewParameters(request);
        var bodyScale = CalculateBodyScale(charInfo.Build, charInfo.Height);

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
                .GetOrAdd(charInfo.Female, f => new MiiRig(MiiBodyModel.Get(f)))
                .RestPose.HeadMatrixForRender(bodyScale)
                .Translation;
            cameraPosition += restHead;
            cameraTarget += restHead;
        }

        // Same 15° camera as the CPU renderer; widen the vertical field of view for tall targets so nothing is cropped.
        aspect = Math.Max(0.05f, aspect);
        var cameraUp = CalculateUpVector(cameraRotate);
        var halfFov = 7.5f * MathF.PI / 180f;
        var fovY = aspect >= 1f ? halfFov * 2f : 2f * MathF.Atan(MathF.Tan(halfFov) / aspect);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovY, aspect, 10f, 1200f);

        return new MiiRealtimeFrameSetup(
            charInfo.Female,
            hasBody,
            bodyScale,
            baseRotation,
            Matrix4x4.CreateScale(MiiBodyModel.CanonicalToRenderUnits) * baseRotation * Matrix4x4.CreateScale(bodyScale),
            Matrix4x4.CreateLookAt(cameraPosition, cameraTarget, cameraUp),
            projection,
            FavoriteColor(charInfo.FavoriteColor),
            PantsColor,
            cameraPosition,
            cameraTarget,
            cameraUp
        );
    }
}

/// <summary>See <see cref="NativeMiiRenderer.GetRealtimeFrameSetup"/>.</summary>
/// <param name="BodyMatrix">
/// Applied after skinning: canonical → render units, character rotation, body scale. Use <see cref="BodyMatrixFor"/>
/// for animated poses.
/// </param>
public sealed record MiiRealtimeFrameSetup(
    bool Female,
    bool HasBody,
    Vector3 BodyScale,
    Matrix4x4 CharacterRotation,
    Matrix4x4 BodyMatrix,
    Matrix4x4 View,
    Matrix4x4 Projection,
    Vector4 BodyColor,
    Vector4 PantsColor,
    Vector3 CameraPosition,
    Vector3 CameraTarget,
    Vector3 CameraUp
)
{
    /// <summary>
    /// Moves the whole Mii (body, head and particles) in world space after everything else, e.g. to stand Miis of
    /// different heights on the same podium step.
    /// </summary>
    public Vector3 Placement { get; init; }

    /// <summary>Body matrix for this pose: <see cref="BodyMatrix"/> plus the walk-distance correction (see <see cref="MiiStage"/>).</summary>
    public Matrix4x4 BodyMatrixFor(MiiPose pose) => BodyMatrix * Matrix4x4.CreateTranslation(StageOffset(pose));

    /// <summary>Model matrix for the head in this pose.</summary>
    public Matrix4x4 HeadMatrix(MiiPose pose) =>
        HasBody
            ? pose.HeadMatrixForRender(BodyScale) * CharacterRotation * Matrix4x4.CreateTranslation(StageOffset(pose))
            : CharacterRotation * Matrix4x4.CreateTranslation(Placement);

    /// <summary>Stage space (<see cref="MiiStage"/>, used by particles) → world.</summary>
    public Matrix4x4 StageToWorld => CharacterRotation * Matrix4x4.CreateTranslation(Placement);

    /// <summary>Same framing with the camera moved (e.g. part way through a camera transition).</summary>
    public MiiRealtimeFrameSetup WithCamera(Vector3 position, Vector3 target, Vector3 up) =>
        this with
        {
            CameraPosition = position,
            CameraTarget = target,
            CameraUp = up,
            View = Matrix4x4.CreateLookAt(position, target, up),
        };

    private Vector3 StageOffset(MiiPose pose) =>
        (HasBody ? Vector3.TransformNormal(MiiStage.TravelCorrection(pose, BodyScale), CharacterRotation) : Vector3.Zero) + Placement;
}

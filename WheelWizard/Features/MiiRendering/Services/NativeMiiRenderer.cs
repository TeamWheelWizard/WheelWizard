using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Rig;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.FaceLibrary;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiRendering.Services;

/// <summary>
/// Native, fully-offline renderer for Mii images. Heads are built from the Wii face library on the user's Mario Kart
/// Wii disc (see <see cref="RflHead"/>) and drawn by a small software rasterizer.
/// </summary>
public sealed partial class NativeMiiRenderer(IMiiFaceLibrary faceLibrary) : IMiiNativeRenderer
{
    public sealed record NativeMiiRenderRequest(
        string StudioData,
        int Width,
        MiiImageSpecifications.FaceExpression Expression,
        MiiImageSpecifications.BodyType BodyType,
        int InstanceCount,
        int CharacterXRotate,
        int CharacterYRotate,
        int CharacterZRotate,
        int CameraXRotate,
        int CameraYRotate,
        int CameraZRotate,
        float CameraVerticalOffset,
        float CameraZoom,
        string BackgroundColor
    );

    private readonly object _headCacheLock = new();
    private readonly Dictionary<HeadKey, LinkedListNode<(HeadKey Key, List<HeadMeshData> Head)>> _headCache = new();
    private readonly LinkedList<(HeadKey Key, List<HeadMeshData> Head)> _headCacheOrder = new();
    private const int MaxHeadCacheEntries = 96;

    private static readonly object CharInfoCacheLock = new();
    private static readonly Dictionary<string, RflCharInfo> StudioCharInfoCache = new(StringComparer.Ordinal);
    private const int MaxStudioCharInfoCacheEntries = 512;

    private static readonly Vector3 LightAmbient = new(0.73f, 0.73f, 0.73f);
    private static readonly Vector3 LightDiffuse = new(0.60f, 0.60f, 0.60f);
    private static readonly Vector3 LightSpecular = new(0.70f, 0.70f, 0.70f);
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.4531539381f, 0.4226179123f, 0.7848858833f));

    // Indexed by HeadShape.
    private static readonly MaterialInfo[] HeadMaterials =
    [
        new(new(0.85f, 0.75f, 0.75f), new(0.75f, 0.75f, 0.75f), new(0.30f, 0.30f, 0.30f), 1.2f, new(0.3f, 0.3f, 0.3f)), // faceline
        new(new(1.0f, 1.0f, 1.0f), new(0.7f, 0.7f, 0.7f), new(0.0f, 0.0f, 0.0f), 40.0f, new(0.3f, 0.3f, 0.3f)), // beard
        new(new(0.90f, 0.85f, 0.85f), new(0.75f, 0.75f, 0.75f), new(0.22f, 0.22f, 0.22f), 1.5f, new(0.3f, 0.3f, 0.3f)), // nose
        new(new(0.85f, 0.75f, 0.75f), new(0.75f, 0.75f, 0.75f), new(0.30f, 0.30f, 0.30f), 1.2f, new(0.3f, 0.3f, 0.3f)), // forehead
        new(new(1.00f, 1.00f, 1.00f), new(0.70f, 0.70f, 0.70f), new(0.10f, 0.10f, 0.10f), 10.0f, new(0.3f, 0.3f, 0.3f)), // hair
        new(new(0.75f, 0.75f, 0.75f), new(0.72f, 0.72f, 0.72f), new(0.30f, 0.30f, 0.30f), 1.5f, new(0.3f, 0.3f, 0.3f)), // cap
        new(new(1.0f, 1.0f, 1.0f), new(0.7f, 0.7f, 0.7f), new(0.0f, 0.0f, 0.0f), 40.0f, new(0.3f, 0.3f, 0.3f)), // mask
        new(new(1.0f, 1.0f, 1.0f), new(0.7f, 0.7f, 0.7f), new(0.0f, 0.0f, 0.0f), 40.0f, new(0.3f, 0.3f, 0.3f)), // noseline
        new(new(1.0f, 1.0f, 1.0f), new(0.7f, 0.7f, 0.7f), new(0.0f, 0.0f, 0.0f), 40.0f, new(0.3f, 0.3f, 0.3f)), // glass
    ];

    private static readonly MaterialInfo BodyShirtMaterial = new(
        new(0.95622f, 0.95622f, 0.95622f),
        new(0.496733f, 0.496733f, 0.496733f),
        new(0.2409f, 0.2409f, 0.2409f),
        3.0f,
        new(0.4f, 0.4f, 0.4f)
    );

    private static readonly MaterialInfo BodyPantsMaterial = new(
        new(0.95622f, 0.95622f, 0.95622f),
        new(1.084967f, 1.084967f, 1.084967f),
        new(0.2409f, 0.2409f, 0.2409f),
        3.0f,
        new(0.4f, 0.4f, 0.4f)
    );

    private static readonly object BodyModelLock = new();
    private static BodyModelDatabase? _bodyModelDatabase;
    private const string EmbeddedBodyMaleResource = "WheelWizard.Features.MiiRendering.Resources.mii_static_body_3ds_male_LE.rmdl";
    private const string EmbeddedBodyFemaleResource = "WheelWizard.Features.MiiRendering.Resources.mii_static_body_3ds_female_LE.rmdl";

    public async Task<OperationResult<Bitmap>> RenderAsync(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        CancellationToken cancellationToken = default
    )
    {
        var bufferResult = await RenderBufferAsync(mii, studioData, specifications, cancellationToken);
        if (bufferResult.IsFailure)
            return bufferResult.Error!;

        try
        {
            var bitmap = CreateBitmap(bufferResult.Value.BgraPixels, bufferResult.Value.Width, bufferResult.Value.Height);
            return bitmap;
        }
        catch (Exception exception)
        {
            return Fail($"Failed to create bitmap from rendered buffer: {exception.Message}");
        }
    }

    public async Task<OperationResult<NativeMiiPixelBuffer>> RenderBufferAsync(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        CancellationToken cancellationToken = default
    )
    {
        if (cancellationToken.IsCancellationRequested)
            return Fail("Mii render cancelled.");

        try
        {
            return await MiiRenderWorkers.Run(() => RenderToBuffer(mii, studioData, specifications, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Fail("Mii render cancelled.");
        }
    }

    public async Task<OperationResult<NativeMiiPixelBuffer>> RenderPosedBufferAsync(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        MiiPose pose,
        CancellationToken cancellationToken = default
    )
    {
        if (cancellationToken.IsCancellationRequested)
            return Fail("Mii render cancelled.");

        try
        {
            return await MiiRenderWorkers.Run(
                () => RenderToBuffer(mii, studioData, specifications, cancellationToken, pose),
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            return Fail("Mii render cancelled.");
        }
    }

    /// <param name="pose">When set, the skinned body is posed with it and the head follows its head bone and expression.</param>
    public OperationResult<NativeMiiPixelBuffer> RenderToBuffer(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        CancellationToken cancellationToken = default,
        MiiPose? pose = null
    )
    {
        if (mii == null)
            return Fail("Mii cannot be null.");

        var request = BuildRequest(studioData, specifications);
        var lightingProfile = ResolveLightingProfile();
        var charInfoResult = GetOrCreateCharInfo(studioData);
        if (charInfoResult.IsFailure)
            return charInfoResult.Error!;
        var charInfo = charInfoResult.Value;

        if (cancellationToken.IsCancellationRequested)
            return Fail("Mii render cancelled.");

        // A locked Mii has no expressions: its face is a question mark.
        var locked = mii is LockedMii;
        var expression =
            locked ? (int)MiiExpression.Normal
            : pose != null ? (int)pose.Expression
            : MapExpression(request.Expression);
        var resolution = MaskResolution(request.Width);
        var headResult = GetOrCreateHead(studioData, charInfo, expression, resolution);
        if (headResult.IsFailure)
            return headResult.Error!;

        var head = headResult.Value;
        if (locked)
            head = WithLockedFace(head, studioData, charInfo, resolution);

        var viewParameters = ResolveViewParameters(request);
        var bodyRenderData =
            request.BodyType == MiiImageSpecifications.BodyType.face_only ? null
            : pose != null ? CreatePosedBodyRenderData(charInfo, pose)
            : TryCreateBodyRenderData(charInfo);
        var frameWidth = request.Width;
        var frameHeight = RoundUpToEven((int)MathF.Ceiling(frameWidth * viewParameters.AspectHeightFactor));
        if (frameHeight <= 0)
            frameHeight = frameWidth;

        var outputWidth = checked(frameWidth * request.InstanceCount);
        var outputHeight = frameHeight;
        var background = ParseStudioRgba(request.BackgroundColor, defaultColor: new(255, 255, 255, 0));
        var pixelCount = outputWidth * outputHeight;
        // Pooled: a 512 px image needs ~13 MB of samples, depths and lit colours, which would otherwise be allocated
        // and cleared for every image.
        var samples = ArrayPool<byte>.Shared.Rent(pixelCount * SamplesPerPixel * 4);
        var depth = ArrayPool<float>.Shared.Rent(pixelCount * SamplesPerPixel);
        using var shading = new ShadingCache(pixelCount);
        try
        {
            FillBackground(samples.AsSpan(0, pixelCount * SamplesPerPixel * 4), depth.AsSpan(0, pixelCount * SamplesPerPixel), background);

            for (var i = 0; i < request.InstanceCount; i++)
            {
                var instanceYaw = request.CharacterYRotate + (360f / request.InstanceCount) * i;
                var cameraRotate = ConvertDegreesToRadians(request.CameraXRotate, request.CameraYRotate, request.CameraZRotate);
                var modelRotate = ConvertDegreesToRadians(request.CharacterXRotate, instanceYaw, request.CharacterZRotate);

                var orbitRadius = viewParameters.OrbitRadius * request.CameraZoom;
                var cameraPosition = CalculateCameraOrbitPosition(orbitRadius, cameraRotate);
                cameraPosition.Y += viewParameters.BaseCameraY + request.CameraVerticalOffset;
                var cameraUp = CalculateUpVector(cameraRotate);
                var baseRotationMatrix = CreateRotationMatrix(modelRotate);

                var cameraTarget = viewParameters.Target + new Vector3(0f, request.CameraVerticalOffset, 0f);
                var headModelMatrix = baseRotationMatrix;
                if (bodyRenderData is { } bodyData)
                {
                    // A posed head can be offset sideways and rotated, so it must be placed before the character rotation.
                    headModelMatrix =
                        pose != null ? bodyData.HeadModelMatrix * baseRotationMatrix : baseRotationMatrix * bodyData.HeadModelMatrix;
                    if (!viewParameters.IsCameraPositionAbsolute)
                    {
                        cameraPosition += bodyData.HeadTranslation;
                        cameraTarget += bodyData.HeadTranslation;
                    }
                }

                var viewMatrix = Matrix4x4.CreateLookAt(cameraPosition, cameraTarget, cameraUp);
                var frame = new RasterFrame(samples, depth, shading, outputWidth, outputHeight, i * frameWidth, frameWidth, frameHeight);

                if (bodyRenderData is { } bodyMeshData)
                    RasterizeBody(frame, bodyMeshData, baseRotationMatrix, viewMatrix, viewParameters.Projection, lightingProfile);

                foreach (var mesh in head)
                {
                    var prepared = PrepareHeadMesh(frame, mesh, headModelMatrix, viewMatrix, viewParameters.Projection);
                    if (prepared != null)
                        RasterizeMesh(frame, prepared, lightingProfile);
                }
            }

            var pixels = Resolve(samples, pixelCount);
            if (locked)
                TurnGrey(pixels);
            return new NativeMiiPixelBuffer(outputWidth, outputHeight, pixels);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(samples);
            ArrayPool<float>.Shared.Return(depth);
        }
    }

    public NativeMiiRenderRequest BuildRequest(string studioData, MiiImageSpecifications specifications)
    {
        var renderScale = Math.Clamp(specifications.RenderScale, 0.05f, 1f);
        var scaledWidth = MathF.Round((int)specifications.Size * renderScale);
        var width = Math.Clamp((int)scaledWidth, 16, 4096);
        var instanceCount = Math.Clamp(specifications.InstanceCount, 1, 32);

        return new NativeMiiRenderRequest(
            studioData,
            width,
            specifications.Expression,
            specifications.Type,
            instanceCount,
            (int)MathF.Round(specifications.CharacterRotate.X),
            (int)MathF.Round(specifications.CharacterRotate.Y),
            (int)MathF.Round(specifications.CharacterRotate.Z),
            (int)MathF.Round(specifications.CameraRotate.X),
            (int)MathF.Round(specifications.CameraRotate.Y),
            (int)MathF.Round(specifications.CameraRotate.Z),
            specifications.CameraVerticalOffset,
            Math.Clamp(specifications.CameraZoom, 0.35f, 3f),
            string.IsNullOrWhiteSpace(specifications.BackgroundColor) ? "FFFFFF00" : specifications.BackgroundColor
        );
    }

    public MiiLightingProfile ResolveLightingProfile() => MiiLightingProfiles.Default;

    /// <summary>Face mask resolution: images drawn up to ~384 px don't need more than 256.</summary>
    private static int MaskResolution(int width) => width <= 384 ? 256 : 512;

    private static int MapExpression(MiiImageSpecifications.FaceExpression expression) =>
        (int)(
            expression switch
            {
                MiiImageSpecifications.FaceExpression.normal_open_mouth => MiiExpression.OpenMouth,
                MiiImageSpecifications.FaceExpression.smile => MiiExpression.Smile,
                MiiImageSpecifications.FaceExpression.smile_open_mouth => MiiExpression.SmileOpenMouth,
                MiiImageSpecifications.FaceExpression.frustrated => MiiExpression.Frustrated,
                MiiImageSpecifications.FaceExpression.anger => MiiExpression.Anger,
                MiiImageSpecifications.FaceExpression.anger_open_mouth => MiiExpression.AngerOpenMouth,
                MiiImageSpecifications.FaceExpression.blink => MiiExpression.Blink,
                MiiImageSpecifications.FaceExpression.blink_open_mouth => MiiExpression.BlinkOpenMouth,
                MiiImageSpecifications.FaceExpression.sorrow => MiiExpression.Sorrow,
                MiiImageSpecifications.FaceExpression.sorrow_open_mouth => MiiExpression.SorrowOpenMouth,
                MiiImageSpecifications.FaceExpression.surprise => MiiExpression.Surprise,
                MiiImageSpecifications.FaceExpression.surprise_open_mouth => MiiExpression.SurpriseOpenMouth,
                MiiImageSpecifications.FaceExpression.wink_right => MiiExpression.WinkRight,
                MiiImageSpecifications.FaceExpression.wink_left => MiiExpression.WinkLeft,
                MiiImageSpecifications.FaceExpression.like_wink_left => MiiExpression.LikeWinkLeft,
                MiiImageSpecifications.FaceExpression.like_wink_right => MiiExpression.LikeWinkRight,
                MiiImageSpecifications.FaceExpression.wink_left_open_mouth => MiiExpression.WinkLeftOpenMouth,
                MiiImageSpecifications.FaceExpression.wink_right_open_mouth => MiiExpression.WinkRightOpenMouth,
                _ => MiiExpression.Normal,
            }
        );

    #region Heads

    private readonly record struct HeadKey(RflResource Resource, string StudioData, int Expression, int Resolution);

    /// <summary>The head of a Mii with an expression, built from the face library the first time it's asked for.</summary>
    private OperationResult<List<HeadMeshData>> GetOrCreateHead(string studioData, RflCharInfo charInfo, int expression, int resolution)
    {
        var resource = faceLibrary.Load();
        if (resource.IsFailure)
            return resource.Error!;

        var key = new HeadKey(resource.Value, studioData, expression, resolution);
        lock (_headCacheLock)
        {
            if (_headCache.TryGetValue(key, out var cached))
            {
                _headCacheOrder.Remove(cached);
                _headCacheOrder.AddFirst(cached);
                return cached.Value.Head;
            }
        }

        var parts = RflHead.Build(resource.Value, charInfo, expression, resolution);
        if (parts.IsFailure)
            return parts.Error!;

        var head = parts
            .Value.Select(part =>
            {
                var material = HeadMaterials[(int)part.Shape];
                return new HeadMeshData
                {
                    Shape = part.Shape,
                    Positions = part.Mesh.Positions,
                    Normals = part.Mesh.Normals,
                    Texcoords = part.Mesh.Texcoords,
                    Indices = part.Mesh.Indices,
                    DoubleSided = part.DoubleSided,
                    Color = part.Color,
                    Texture = part.Texture,
                    Ambient = material.Ambient,
                    Diffuse = material.Diffuse,
                    Specular = material.Specular,
                    SpecularPower = material.SpecularPower,
                    RimColor = material.RimColor,
                };
            })
            .ToList();

        lock (_headCacheLock)
        {
            if (_headCache.TryGetValue(key, out var existing))
                return existing.Value.Head;

            _headCache[key] = _headCacheOrder.AddFirst((key, head));
            while (_headCache.Count > MaxHeadCacheEntries)
            {
                _headCache.Remove(_headCacheOrder.Last!.Value.Key);
                _headCacheOrder.RemoveLast();
            }
        }

        return head;
    }

    private static OperationResult<RflCharInfo> GetOrCreateCharInfo(string studioData)
    {
        lock (CharInfoCacheLock)
        {
            if (StudioCharInfoCache.TryGetValue(studioData, out var cached))
                return cached;
        }

        var decoded = RflCharInfo.FromStudioData(studioData);
        if (decoded.IsFailure)
            return decoded.Error!;

        lock (CharInfoCacheLock)
        {
            if (StudioCharInfoCache.Count >= MaxStudioCharInfoCacheEntries)
                StudioCharInfoCache.Clear();
            StudioCharInfoCache[studioData] = decoded.Value;
        }

        return decoded.Value;
    }

    #endregion

    #region Body

    private static BodyRenderData? TryCreateBodyRenderData(RflCharInfo charInfo)
    {
        if (!TryGetBodyModelDatabase(out var database))
            return null;

        var model = charInfo.Female ? database.FemaleModel : database.MaleModel;
        if (model.Meshes.Length == 0)
            return null;

        // Body scale follows the Mii's build and height.
        var bodyScale = CalculateBodyScale(charInfo.Build, charInfo.Height);
        var headTranslation = new Vector3(0f, database.HeadYTranslate, 0f);
        headTranslation = Hadamard(headTranslation, bodyScale);
        headTranslation *= database.ModelScale;

        var headModelMatrix = Matrix4x4.CreateTranslation(headTranslation);
        var bodyScaleMatrix = Matrix4x4.CreateScale(bodyScale);
        return new BodyRenderData(
            model.Meshes,
            bodyScaleMatrix,
            headTranslation,
            headModelMatrix,
            FavoriteColor(charInfo.FavoriteColor),
            PantsColor
        );
    }

    /// <summary>
    /// Skins the animatable 3DS body (same mesh as the static body) with <paramref name="pose"/>.
    /// Vertices end up in the static body's units so the regular body rasterizer and camera work unchanged.
    /// </summary>
    private static BodyRenderData CreatePosedBodyRenderData(RflCharInfo charInfo, MiiPose pose)
    {
        var body = MiiBodyModel.Get(female: charInfo.Female);
        var toRenderUnits = Matrix4x4.CreateScale(MiiBodyModel.CanonicalToRenderUnits);
        var meshes = new BodyMeshData[body.Meshes.Count];
        for (var m = 0; m < meshes.Length; m++)
        {
            var source = body.Meshes[m];
            var vertices = new BodyVertex[source.Positions.Length];
            for (var v = 0; v < vertices.Length; v++)
            {
                var position = Vector3.Zero;
                var normal = Vector3.Zero;
                for (var k = 0; k < 4; k++)
                {
                    var weight = source.Weights[v * 4 + k];
                    if (weight <= 0f)
                        continue;
                    var skin = pose.SkinMatrix[source.Joints[v * 4 + k]] * toRenderUnits;
                    position += Vector3.Transform(source.Positions[v], skin) * weight;
                    normal += Vector3.TransformNormal(source.Normals[v], skin) * weight;
                }

                vertices[v] = new BodyVertex(position, NormalizeOrDefault(normal, Vector3.UnitZ));
            }

            meshes[m] = new BodyMeshData(vertices, source.Indices, source.IsPants, Vector3.One, Vector3.Zero, Vector3.Zero);
        }

        var bodyScale = CalculateBodyScale(charInfo.Build, charInfo.Height);
        var headModelMatrix = pose.HeadMatrixForRender(bodyScale);
        var headTranslation = new Vector3(headModelMatrix.M41, headModelMatrix.M42, headModelMatrix.M43);
        return new BodyRenderData(
            meshes,
            Matrix4x4.CreateScale(bodyScale),
            headTranslation,
            headModelMatrix,
            FavoriteColor(charInfo.FavoriteColor),
            PantsColor
        );
    }

    private static Vector3 CalculateBodyScale(float build, float height)
    {
        var x = (build * (height * 0.003671875f + 0.4f)) / 128.0f + height * 0.001796875f + 0.4f;
        var y = (height * 0.006015625f) + 0.5f;
        return new Vector3(x, y, x);
    }

    private static bool TryGetBodyModelDatabase(out BodyModelDatabase database)
    {
        lock (BodyModelLock)
        {
            if (_bodyModelDatabase != null)
            {
                database = _bodyModelDatabase;
                return true;
            }

            if (!TryLoadBodyModelDatabase(out database))
            {
                database = null!;
                return false;
            }

            _bodyModelDatabase = database;
            return true;
        }
    }

    private static bool TryLoadBodyModelDatabase(out BodyModelDatabase database)
    {
        database = null!;
        if (!TryReadEmbeddedRioModel(EmbeddedBodyMaleResource, out var maleModel))
            return false;
        if (!TryReadEmbeddedRioModel(EmbeddedBodyFemaleResource, out var femaleModel))
            return false;

        // Sourced from reference body_models.csv row:
        // 4,3ds,7.0,10.7766,13,0
        // We use the static non-skeleton body set to keep rendering fully managed/offline.
        database = new BodyModelDatabase(modelScale: 7.0f, headYTranslate: 10.7766f, maleModel, femaleModel);
        return true;
    }

    private static bool TryReadEmbeddedRioModel(string resourceName, out BodyMeshModel model)
    {
        model = new BodyMeshModel([]);
        try
        {
            using var stream = typeof(NativeMiiRenderer).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return false;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return TryReadRioModelBytes(ms.ToArray(), out model);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadRioModelBytes(byte[] bytes, out BodyMeshModel model)
    {
        model = new BodyMeshModel([]);

        if (bytes.Length < 0x20)
            return false;

        if (!bytes.AsSpan(0, 8).SequenceEqual("riomodel"u8))
            return false;

        var declaredSize = ReadUInt32(bytes, 0x0C);
        if (declaredSize > 0 && declaredSize <= int.MaxValue && bytes.Length != declaredSize)
            return false;

        var meshOffsetRelative = ReadInt32(bytes, 0x10);
        var meshCount = ReadUInt32(bytes, 0x14);
        if (meshCount == 0 || meshCount > 1024)
            return false;

        var meshListOffset = 0x10 + meshOffsetRelative;
        if (meshListOffset < 0)
            return false;

        const int meshSize = 0x38;
        var meshes = new List<BodyMeshData>((int)meshCount);
        for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
        {
            var meshOffset = checked(meshListOffset + meshIndex * meshSize);
            if (meshOffset < 0 || meshOffset + meshSize > bytes.Length)
                return false;

            var vertexOffsetRelative = ReadInt32(bytes, meshOffset + 0x00);
            var vertexCount = ReadUInt32(bytes, meshOffset + 0x04);
            var indexOffsetRelative = ReadInt32(bytes, meshOffset + 0x08);
            var indexCount = ReadUInt32(bytes, meshOffset + 0x0C);
            var meshScale = new Vector3(
                ReadSingle(bytes, meshOffset + 0x10),
                ReadSingle(bytes, meshOffset + 0x14),
                ReadSingle(bytes, meshOffset + 0x18)
            );
            var meshRotate = new Vector3(
                ReadSingle(bytes, meshOffset + 0x1C),
                ReadSingle(bytes, meshOffset + 0x20),
                ReadSingle(bytes, meshOffset + 0x24)
            );
            var meshTranslate = new Vector3(
                ReadSingle(bytes, meshOffset + 0x28),
                ReadSingle(bytes, meshOffset + 0x2C),
                ReadSingle(bytes, meshOffset + 0x30)
            );

            if (vertexCount == 0 || indexCount < 3)
                continue;
            if (vertexCount > 200_000 || indexCount > 2_000_000)
                return false;

            var vertexBufferOffset = checked(meshOffset + vertexOffsetRelative);
            var indexBufferOffset = checked(meshOffset + 0x08 + indexOffsetRelative);
            const int vertexStride = 0x20;
            var vertexBytes = checked((int)vertexCount * vertexStride);
            var indexBytes = checked((int)indexCount * sizeof(uint));

            if (
                vertexBufferOffset < 0
                || indexBufferOffset < 0
                || vertexBufferOffset + vertexBytes > bytes.Length
                || indexBufferOffset + indexBytes > bytes.Length
            )
            {
                return false;
            }

            var vertices = new BodyVertex[(int)vertexCount];
            for (var i = 0; i < vertices.Length; i++)
            {
                var o = vertexBufferOffset + i * vertexStride;
                var position = new Vector3(ReadSingle(bytes, o + 0x00), ReadSingle(bytes, o + 0x04), ReadSingle(bytes, o + 0x08));
                var normal = new Vector3(ReadSingle(bytes, o + 0x14), ReadSingle(bytes, o + 0x18), ReadSingle(bytes, o + 0x1C));
                vertices[i] = new BodyVertex(position, normal);
            }

            var indices = new int[(int)indexCount];
            for (var i = 0; i < indices.Length; i++)
            {
                var index = ReadUInt32(bytes, indexBufferOffset + i * sizeof(uint));
                if (index >= vertexCount)
                    return false;
                indices[i] = (int)index;
            }

            meshes.Add(new BodyMeshData(vertices, indices, (meshIndex & 1) == 1, meshScale, meshRotate, meshTranslate));
        }

        model = new BodyMeshModel(meshes.ToArray());
        return model.Meshes.Length > 0;
    }

    private static int ReadInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static uint ReadUInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static float ReadSingle(byte[] bytes, int offset) => BitConverter.Int32BitsToSingle(ReadInt32(bytes, offset));

    #endregion

    #region Rasterizer

    // Software multisampling, like the GPU's: coverage and depth are tested at four points per pixel (the usual rotated
    // grid), but a pixel is lit only once per mesh, so smooth edges cost little more than jagged ones.
    private const int SamplesPerPixel = 4;
    private static readonly Vector2[] SampleOffsets =
    [
        new(-0.125f, -0.375f),
        new(0.375f, -0.125f),
        new(-0.375f, 0.125f),
        new(0.125f, 0.375f),
    ];

    /// <summary>
    /// The image being drawn (<see cref="SamplesPerPixel"/> BGRA samples and depths per pixel), and the part of it one
    /// instance of the Mii goes in.
    /// </summary>
    private readonly record struct RasterFrame(
        byte[] Samples,
        float[] Depth,
        ShadingCache Shading,
        int Width,
        int Height,
        int FrameX,
        int FrameWidth,
        int FrameHeight
    );

    private static void RasterizeBody(
        RasterFrame frame,
        BodyRenderData bodyRenderData,
        Matrix4x4 baseRotationMatrix,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix,
        MiiLightingProfile lightingProfile
    )
    {
        var bodyModelMatrix = baseRotationMatrix * bodyRenderData.BodyScaleMatrix;
        foreach (var bodyMesh in bodyRenderData.Meshes)
        {
            if (bodyMesh.Vertices.Length == 0 || bodyMesh.Indices.Length < 3)
                continue;

            var meshSrt = CreateBodyMeshSrt(bodyMesh.MeshScale, bodyMesh.MeshRotate, bodyMesh.MeshTranslate);
            var modelMatrix = meshSrt * bodyModelMatrix;
            var positions = new Vector3[bodyMesh.Vertices.Length];
            var normals = new Vector3[bodyMesh.Vertices.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = bodyMesh.Vertices[i].Position;
                normals[i] = bodyMesh.Vertices[i].Normal;
            }

            var prepared = Prepare(
                frame,
                positions,
                normals,
                texcoords: null,
                bodyMesh.Indices,
                modelMatrix,
                viewMatrix,
                projectionMatrix,
                bodyMesh.IsPantsMesh ? BodyPantsMaterial : BodyShirtMaterial,
                bodyMesh.IsPantsMesh ? bodyRenderData.PantsColor : bodyRenderData.BodyColor,
                texture: null,
                doubleSided: false
            );
            if (prepared != null)
                RasterizeMesh(frame, prepared, lightingProfile);
        }
    }

    private static PreparedMesh? PrepareHeadMesh(
        RasterFrame frame,
        HeadMeshData mesh,
        Matrix4x4 modelMatrix,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix
    ) =>
        Prepare(
            frame,
            mesh.Positions,
            mesh.Normals,
            mesh.Texcoords,
            mesh.Indices,
            modelMatrix,
            viewMatrix,
            projectionMatrix,
            new MaterialInfo(mesh.Ambient, mesh.Diffuse, mesh.Specular, mesh.SpecularPower, mesh.RimColor),
            mesh.Color,
            mesh.Texture,
            mesh.DoubleSided
        );

    private static PreparedMesh? Prepare(
        RasterFrame frame,
        Vector3[] positions,
        Vector3[] normals,
        Vector2[]? texcoords,
        int[] indices,
        Matrix4x4 modelMatrix,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix,
        MaterialInfo material,
        Vector4 color,
        MiiTexture? texture,
        bool doubleSided
    )
    {
        if (positions.Length == 0 || indices.Length < 3)
            return null;

        var modelView = modelMatrix * viewMatrix;
        var vertices = new RasterVertex[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            var viewPosition = Vector3.Transform(positions[i], modelView);
            var clip = Vector4.Transform(new Vector4(viewPosition, 1f), projectionMatrix);
            if (MathF.Abs(clip.W) <= 1e-6f)
                return null;

            var invW = 1f / clip.W;
            var ndc = new Vector3(clip.X * invW, clip.Y * invW, clip.Z * invW);
            vertices[i] = new RasterVertex(
                new Vector2(frame.FrameX + (ndc.X * 0.5f + 0.5f) * frame.FrameWidth, (0.5f - ndc.Y * 0.5f) * frame.FrameHeight),
                ndc.Z * 0.5f + 0.5f,
                invW,
                texcoords != null && i < texcoords.Length ? texcoords[i] : Vector2.Zero,
                viewPosition,
                Vector3.TransformNormal(i < normals.Length ? normals[i] : Vector3.UnitZ, modelView)
            );
        }

        return new PreparedMesh(vertices, indices, material, color, texture, doubleSided);
    }

    /// <summary>
    /// The colour a mesh last lit each pixel with. Pixels on the edge between two triangles of a mesh are partly
    /// covered by both; the second reuses the first one's colour (the mesh's shading is smooth across the edge), so
    /// multisampling doesn't light those pixels twice.
    /// </summary>
    private sealed class ShadingCache(int pixels) : IDisposable
    {
        private static int _meshes;

        public readonly int[] Mesh = ArrayPool<int>.Shared.Rent(pixels);
        public readonly Vector4[] Color = ArrayPool<Vector4>.Shared.Rent(pixels);
        public int CurrentMesh { get; private set; }

        /// <summary>Starts a mesh. Its number is unique across images, so numbers left in a reused buffer never match.</summary>
        public void NextMesh() => CurrentMesh = Interlocked.Increment(ref _meshes);

        public void Dispose()
        {
            ArrayPool<int>.Shared.Return(Mesh);
            ArrayPool<Vector4>.Shared.Return(Color);
        }
    }

    private static void RasterizeMesh(RasterFrame frame, PreparedMesh mesh, MiiLightingProfile lightingProfile)
    {
        frame.Shading.NextMesh();
        for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
            RasterizeTriangle(frame, mesh, lightingProfile, mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2]);
    }

    private static void RasterizeTriangle(RasterFrame frame, PreparedMesh mesh, MiiLightingProfile lightingProfile, int ia, int ib, int ic)
    {
        if ((uint)ia >= (uint)mesh.Vertices.Length || (uint)ib >= (uint)mesh.Vertices.Length || (uint)ic >= (uint)mesh.Vertices.Length)
            return;

        var a = mesh.Vertices[ia];
        var b = mesh.Vertices[ib];
        var c = mesh.Vertices[ic];

        // Screen y points down, so counter-clockwise (front facing) triangles have a negative area here.
        var area = Cross2D(a.ScreenPosition, b.ScreenPosition, c.ScreenPosition);
        if (MathF.Abs(area) < 1e-6f || (!mesh.DoubleSided && area >= 0f))
            return;

        var ax = a.ScreenPosition.X;
        var ay = a.ScreenPosition.Y;
        var bx = b.ScreenPosition.X;
        var by = b.ScreenPosition.Y;
        var cx = c.ScreenPosition.X;
        var cy = c.ScreenPosition.Y;

        var minX = Math.Clamp((int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))), 0, frame.Width - 1);
        var maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))), 0, frame.Width - 1);
        var minY = Math.Clamp((int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))), 0, frame.Height - 1);
        var maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))), 0, frame.Height - 1);

        var invArea = 1f / area;
        var sampleX = minX + 0.5f;
        var sampleY = minY + 0.5f;

        var e0x = by - cy;
        var e0y = cx - bx;
        var e0c = bx * cy - by * cx;
        var e1x = cy - ay;
        var e1y = ax - cx;
        var e1c = cx * ay - cy * ax;
        var e2x = ay - by;
        var e2y = bx - ax;
        var e2c = ax * by - ay * bx;

        var e0Row = e0x * sampleX + e0y * sampleY + e0c;
        var e1Row = e1x * sampleX + e1y * sampleY + e1c;
        var e2Row = e2x * sampleX + e2y * sampleY + e2c;

        // Depth after the perspective divide is linear on screen: a plane over the triangle.
        var depthX = (e0x * a.Depth + e1x * b.Depth + e2x * c.Depth) * invArea;
        var depthY = (e0y * a.Depth + e1y * b.Depth + e2y * c.Depth) * invArea;

        // How far each sample's edge values and depth are from the pixel centre's.
        Span<float> d0 = stackalloc float[SamplesPerPixel];
        Span<float> d1 = stackalloc float[SamplesPerPixel];
        Span<float> d2 = stackalloc float[SamplesPerPixel];
        Span<float> dDepth = stackalloc float[SamplesPerPixel];
        for (var s = 0; s < SamplesPerPixel; s++)
        {
            var offset = SampleOffsets[s];
            d0[s] = e0x * offset.X + e0y * offset.Y;
            d1[s] = e1x * offset.X + e1y * offset.Y;
            d2[s] = e2x * offset.X + e2y * offset.Y;
            dDepth[s] = depthX * offset.X + depthY * offset.Y;
        }

        // Which side of the edges is inside, and how far the samples reach from the centre across each edge: pixels
        // further inside or outside than that are all in or all out, without testing every sample.
        var inside = area < 0f ? -1f : 1f;
        float reach0 = 0f,
            reach1 = 0f,
            reach2 = 0f;
        for (var s = 0; s < SamplesPerPixel; s++)
        {
            reach0 = MathF.Max(reach0, MathF.Abs(d0[s]));
            reach1 = MathF.Max(reach1, MathF.Abs(d1[s]));
            reach2 = MathF.Max(reach2, MathF.Abs(d2[s]));
        }

        Span<float> sampleDepth = stackalloc float[SamplesPerPixel];
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(frame.Samples.AsSpan());
        for (var y = minY; y <= maxY; y++)
        {
            var e0 = e0Row;
            var e1 = e1Row;
            var e2 = e2Row;
            var pixelIndex = y * frame.Width + minX;

            for (var x = minX; x <= maxX; x++)
            {
                float s0 = e0 * inside,
                    s1 = e1 * inside,
                    s2 = e2 * inside;
                if (s0 < -reach0 || s1 < -reach1 || s2 < -reach2)
                    goto NextPixel;
                var wholePixel = s0 >= reach0 && s1 >= reach1 && s2 >= reach2;

                var firstSample = pixelIndex * SamplesPerPixel;
                var centerDepth = (e0 * a.Depth + e1 * b.Depth + e2 * c.Depth) * invArea;
                var covered = 0;
                for (var s = 0; s < SamplesPerPixel; s++)
                {
                    if (!wholePixel && ((e0 + d0[s]) * inside < 0f || (e1 + d1[s]) * inside < 0f || (e2 + d2[s]) * inside < 0f))
                        continue;
                    var depthValue = centerDepth + dDepth[s];
                    if (depthValue is < 0f or > 1f || depthValue > frame.Depth[firstSample + s])
                        continue;
                    sampleDepth[s] = depthValue;
                    covered |= 1 << s;
                }

                if (covered == 0)
                    goto NextPixel;

                Vector4 color;
                if (frame.Shading.Mesh[pixelIndex] == frame.Shading.CurrentMesh)
                {
                    color = frame.Shading.Color[pixelIndex];
                }
                else
                {
                    // Lit at the pixel centre when the triangle covers all of it, else at a sample it covers (so it
                    // never reads its attributes from outside the triangle).
                    float l0 = e0,
                        l1 = e1,
                        l2 = e2;
                    if (covered != (1 << SamplesPerPixel) - 1)
                    {
                        var s = System.Numerics.BitOperations.TrailingZeroCount(covered);
                        l0 += d0[s];
                        l1 += d1[s];
                        l2 += d2[s];
                    }

                    var w0 = l0 * invArea;
                    var w1 = l1 * invArea;
                    var w2 = l2 * invArea;
                    var perspectiveDenominator = w0 * a.InvW + w1 * b.InvW + w2 * c.InvW;
                    if (MathF.Abs(perspectiveDenominator) < 1e-8f)
                        goto NextPixel;

                    var uv = (w0 * a.Texcoord * a.InvW + w1 * b.Texcoord * b.InvW + w2 * c.Texcoord * c.InvW) / perspectiveDenominator;
                    var viewPosition =
                        (w0 * a.ViewPosition * a.InvW + w1 * b.ViewPosition * b.InvW + w2 * c.ViewPosition * c.InvW)
                        / perspectiveDenominator;
                    var normal = (w0 * a.Normal * a.InvW + w1 * b.Normal * b.InvW + w2 * c.Normal * c.InvW) / perspectiveDenominator;

                    color = Shade(mesh, uv, viewPosition, normal, lightingProfile);
                    frame.Shading.Mesh[pixelIndex] = frame.Shading.CurrentMesh;
                    frame.Shading.Color[pixelIndex] = color;
                }

                if (color.W <= 0f)
                    goto NextPixel;

                var opaque = Clamp01(color.W) >= 0.999f;
                var packed = opaque ? PackOpaque(color) : 0u;
                for (var s = 0; s < SamplesPerPixel; s++)
                {
                    if ((covered & (1 << s)) == 0)
                        continue;
                    if (opaque)
                        samples[firstSample + s] = packed;
                    else
                        BlendPixel(frame.Samples, (firstSample + s) * 4, color);
                    frame.Depth[firstSample + s] = sampleDepth[s];
                }

                NextPixel:
                e0 += e0x;
                e1 += e1x;
                e2 += e2x;
                pixelIndex++;
            }

            e0Row += e0y;
            e1Row += e1y;
            e2Row += e2y;
        }
    }

    /// <summary>An opaque colour as one BGRA sample (little endian, as the byte buffers are laid out).</summary>
    private static uint PackOpaque(Vector4 color) =>
        ToByte01(color.Z) | (uint)ToByte01(color.Y) << 8 | (uint)ToByte01(color.X) << 16 | 0xFF000000u;

    /// <summary>Averages each pixel's samples into one straight-alpha BGRA pixel.</summary>
    private static byte[] Resolve(byte[] samples, int pixelCount)
    {
        var pixels = new byte[pixelCount * 4];
        var packed = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(samples.AsSpan());
        var output = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
        for (var p = 0; p < pixelCount; p++)
        {
            // Most pixels are covered by one surface: all samples the same.
            var first = packed[p * SamplesPerPixel];
            if (
                packed[p * SamplesPerPixel + 1] == first
                && packed[p * SamplesPerPixel + 2] == first
                && packed[p * SamplesPerPixel + 3] == first
            )
            {
                if (first >> 24 != 0)
                    output[p] = first;
                continue;
            }

            int blue = 0,
                green = 0,
                red = 0,
                alpha = 0;
            for (var s = 0; s < SamplesPerPixel; s++)
            {
                var o = (p * SamplesPerPixel + s) * 4;
                var a = samples[o + 3];
                blue += samples[o] * a;
                green += samples[o + 1] * a;
                red += samples[o + 2] * a;
                alpha += a;
            }

            if (alpha == 0)
                continue;
            pixels[p * 4] = (byte)((blue + alpha / 2) / alpha);
            pixels[p * 4 + 1] = (byte)((green + alpha / 2) / alpha);
            pixels[p * 4 + 2] = (byte)((red + alpha / 2) / alpha);
            pixels[p * 4 + 3] = (byte)((alpha + SamplesPerPixel / 2) / SamplesPerPixel);
        }

        return pixels;
    }

    /// <summary>The lit colour of a point (MiiShaders.HeadFragment does the same on the GPU).</summary>
    private static Vector4 Shade(PreparedMesh mesh, Vector2 uv, Vector3 viewPosition, Vector3 normal, MiiLightingProfile lightingProfile)
    {
        var baseColor = mesh.Color;
        if (mesh.Texture is { } texture)
        {
            var (r, g, b, a) = texture.Sample(uv.X, uv.Y);
            if (a <= 0f)
                return Vector4.Zero;
            baseColor = new Vector4(r, g, b, a);
        }

        var n = NormalizeOrDefault(normal, Vector3.UnitZ);
        var eye = NormalizeOrDefault(-viewPosition, Vector3.UnitZ);
        var light = LightDirection;

        var ambient = Hadamard(LightAmbient, mesh.Material.Ambient) * lightingProfile.AmbientScale;
        var directionalDiffuse = MathF.Max(Vector3.Dot(light, n), lightingProfile.DiffuseFloor);
        var diffuseFactor = 1f + (directionalDiffuse - 1f) * lightingProfile.DirectionalLightInfluence;
        var diffuse = Hadamard(LightDiffuse, mesh.Material.Diffuse) * (diffuseFactor * lightingProfile.DiffuseScale);
        var reflection = MathF.Pow(MathF.Max(Vector3.Dot(Vector3.Reflect(-light, n), eye), 0f), mesh.Material.SpecularPower);
        var specular = Hadamard(LightSpecular, mesh.Material.Specular) * reflection * lightingProfile.SpecularScale;
        var rimFactor = MathF.Pow(MathF.Max(0f, 1f - MathF.Abs(n.Z)), lightingProfile.RimPower);
        var rim = mesh.Material.RimColor * (rimFactor * lightingProfile.RimScale);

        var lit = (ambient + diffuse) * new Vector3(baseColor.X, baseColor.Y, baseColor.Z) + specular + rim;
        return new Vector4(Clamp01(lit.X), Clamp01(lit.Y), Clamp01(lit.Z), Clamp01(baseColor.W));
    }

    private static float Cross2D(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>Draws a straight-alpha colour over a BGRA pixel.</summary>
    private static void BlendPixel(byte[] target, int i, Vector4 src)
    {
        var srcA = Clamp01(src.W);
        if (srcA >= 0.999f)
        {
            target[i + 0] = ToByte01(src.Z);
            target[i + 1] = ToByte01(src.Y);
            target[i + 2] = ToByte01(src.X);
            target[i + 3] = 255;
            return;
        }

        var dstA = target[i + 3] / 255f;
        var outA = srcA + dstA * (1f - srcA);
        if (outA <= 0f)
            return;

        target[i + 0] = ToByte01((src.Z * srcA + target[i + 0] / 255f * dstA * (1f - srcA)) / outA);
        target[i + 1] = ToByte01((src.Y * srcA + target[i + 1] / 255f * dstA * (1f - srcA)) / outA);
        target[i + 2] = ToByte01((src.X * srcA + target[i + 2] / 255f * dstA * (1f - srcA)) / outA);
        target[i + 3] = ToByte01(outA);
    }

    #endregion

    #region Camera

    private static ViewParameters ResolveViewParameters(NativeMiiRenderRequest request)
    {
        static Matrix4x4 BuildProjection(float near, float far, float fovyDegrees, float aspect) =>
            Matrix4x4.CreatePerspectiveFieldOfView(fovyDegrees * (MathF.PI / 180f), aspect, near, far);

        var projection = BuildProjection(10f, 1200f, 15f, 1f);
        var position = new Vector3(0f, 34.5f, 600f);
        var target = new Vector3(0f, 34.5f, 0f);
        var isCameraPositionAbsolute = false;

        switch (request.BodyType)
        {
            case MiiImageSpecifications.BodyType.face:
            case MiiImageSpecifications.BodyType.face_only:
            {
                const float scale = 0.14f;
                var y = 4.805f / scale;
                var z = 57.553f / scale;
                position = new(0f, y, z);
                target = new(0f, y, 0f);
                break;
            }
            case MiiImageSpecifications.BodyType.all_body:
            {
                isCameraPositionAbsolute = true;
                // Tuned all-body camera for readability.
                position = new(0f, 90f, 760f);
                target = new(0f, 95f, 0f);
                break;
            }
        }

        return new ViewParameters(position.Y, position.Z, target, projection, AspectHeightFactor: 1f, isCameraPositionAbsolute);
    }

    private static Matrix4x4 CreateRotationMatrix(Vector3 radians)
    {
        var rotationX = Matrix4x4.CreateRotationX(radians.X);
        var rotationY = Matrix4x4.CreateRotationY(radians.Y);
        var rotationZ = Matrix4x4.CreateRotationZ(radians.Z);
        return rotationX * rotationY * rotationZ;
    }

    private static Matrix4x4 CreateBodyMeshSrt(Vector3 scale, Vector3 rotationRadians, Vector3 translation)
    {
        var s = Matrix4x4.CreateScale(scale);
        var r = CreateRotationMatrix(rotationRadians);
        var t = Matrix4x4.CreateTranslation(translation);
        return s * r * t;
    }

    private static Vector3 ConvertDegreesToRadians(float x, float y, float z)
    {
        var toRadians = MathF.PI / 180f;
        return new(
            MathF.IEEERemainder(x, 360f) * toRadians,
            MathF.IEEERemainder(y, 360f) * toRadians,
            MathF.IEEERemainder(z, 360f) * toRadians
        );
    }

    private static Vector3 CalculateCameraOrbitPosition(float radius, Vector3 radians)
    {
        return new(
            radius * -MathF.Sin(radians.Y) * MathF.Cos(radians.X),
            radius * MathF.Sin(radians.X),
            radius * MathF.Cos(radians.Y) * MathF.Cos(radians.X)
        );
    }

    private static Vector3 CalculateUpVector(Vector3 radians) => new(MathF.Sin(radians.Z), MathF.Cos(radians.Z), 0f);

    #endregion

    private static Vector3 NormalizeOrDefault(Vector3 vector, Vector3 fallback) =>
        vector.LengthSquared() < 1e-8f ? fallback : Vector3.Normalize(vector);

    private static Vector3 Hadamard(Vector3 left, Vector3 right) => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);

    private static int RoundUpToEven(int value) => (value & 1) == 0 ? value : value + 1;

    private static void FillBackground(Span<byte> samples, Span<float> depth, RgbaColor background)
    {
        depth.Fill(1f);
        System
            .Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(samples)
            .Fill(background.B | (uint)background.G << 8 | (uint)background.R << 16 | (uint)background.A << 24);
    }

    private static Bitmap CreateBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul
        );

        using var locked = bitmap.Lock();
        var rowBytes = width * 4;
        if (locked.RowBytes == rowBytes)
        {
            Marshal.Copy(pixels, 0, locked.Address, rowBytes * height);
            return bitmap;
        }

        for (var y = 0; y < height; y++)
        {
            var sourceOffset = y * rowBytes;
            var destinationRow = IntPtr.Add(locked.Address, y * locked.RowBytes);
            Marshal.Copy(pixels, sourceOffset, destinationRow, rowBytes);
        }

        return bitmap;
    }

    private static RgbaColor ParseStudioRgba(string value, RgbaColor defaultColor)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 8)
            return defaultColor;

        if (!uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgba))
            return defaultColor;

        return new((byte)((rgba >> 24) & 0xFF), (byte)((rgba >> 16) & 0xFF), (byte)((rgba >> 8) & 0xFF), (byte)(rgba & 0xFF));
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static byte ToByte01(float value) => (byte)Math.Clamp((int)MathF.Round(Clamp01(value) * 255f), 0, 255);

    private readonly record struct ViewParameters(
        float BaseCameraY,
        float OrbitRadius,
        Vector3 Target,
        Matrix4x4 Projection,
        float AspectHeightFactor,
        bool IsCameraPositionAbsolute
    );

    private readonly record struct MaterialInfo(Vector3 Ambient, Vector3 Diffuse, Vector3 Specular, float SpecularPower, Vector3 RimColor);

    private readonly record struct RasterVertex(
        Vector2 ScreenPosition,
        float Depth,
        float InvW,
        Vector2 Texcoord,
        Vector3 ViewPosition,
        Vector3 Normal
    );

    private readonly record struct BodyRenderData(
        BodyMeshData[] Meshes,
        Matrix4x4 BodyScaleMatrix,
        Vector3 HeadTranslation,
        Matrix4x4 HeadModelMatrix,
        Vector4 BodyColor,
        Vector4 PantsColor
    );

    private sealed class BodyModelDatabase(float modelScale, float headYTranslate, BodyMeshModel maleModel, BodyMeshModel femaleModel)
    {
        public float ModelScale { get; } = modelScale;
        public float HeadYTranslate { get; } = headYTranslate;
        public BodyMeshModel MaleModel { get; } = maleModel;
        public BodyMeshModel FemaleModel { get; } = femaleModel;
    }

    private readonly record struct BodyMeshModel(BodyMeshData[] Meshes);

    private readonly record struct BodyMeshData(
        BodyVertex[] Vertices,
        int[] Indices,
        bool IsPantsMesh,
        Vector3 MeshScale,
        Vector3 MeshRotate,
        Vector3 MeshTranslate
    );

    private readonly record struct BodyVertex(Vector3 Position, Vector3 Normal);

    private sealed record PreparedMesh(
        RasterVertex[] Vertices,
        int[] Indices,
        MaterialInfo Material,
        Vector4 Color,
        MiiTexture? Texture,
        bool DoubleSided
    );

    private readonly record struct RgbaColor(byte R, byte G, byte B, byte A);
}

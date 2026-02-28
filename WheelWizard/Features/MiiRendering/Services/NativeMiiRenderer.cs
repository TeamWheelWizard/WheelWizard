using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WheelWizard.MiiImages.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiRendering.Services;

/// <summary>
/// Native, fully-offline renderer entry point for Mii images.
/// Uses FFL-generated geometry and texture data from FFLResHigh.dat.
/// </summary>
public sealed class NativeMiiRenderer(IMiiRenderingResourceLocator resourceLocator) : IMiiNativeRenderer
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
        float CameraZoom,
        string BackgroundColor
    );

    private static readonly TextureStore TextureRegistry = new();
    private static readonly object ManagedArchiveLock = new();
    private static ManagedFflResourceArchive? _managedArchive;
    private static string? _managedArchivePath;

    private static readonly Vector3 LightAmbient = new(0.73f, 0.73f, 0.73f);
    private static readonly Vector3 LightDiffuse = new(0.60f, 0.60f, 0.60f);
    private static readonly Vector3 LightSpecular = new(0.70f, 0.70f, 0.70f);
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.4531539381f, 0.4226179123f, 0.7848858833f));
    private const float RimPower = 2.0f;

    private static readonly MaterialInfo[] MaterialTable =
    [
        new(
            new Vector3(0.85f, 0.75f, 0.75f),
            new Vector3(0.75f, 0.75f, 0.75f),
            new Vector3(0.30f, 0.30f, 0.30f),
            1.2f,
            FflNativeInterop.SpecularModeBlinn,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(0.7f, 0.7f, 0.7f),
            new Vector3(0.0f, 0.0f, 0.0f),
            40.0f,
            FflNativeInterop.SpecularModeBlinn,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(0.90f, 0.85f, 0.85f),
            new Vector3(0.75f, 0.75f, 0.75f),
            new Vector3(0.22f, 0.22f, 0.22f),
            1.5f,
            FflNativeInterop.SpecularModeBlinn,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(0.85f, 0.75f, 0.75f),
            new Vector3(0.75f, 0.75f, 0.75f),
            new Vector3(0.30f, 0.30f, 0.30f),
            1.2f,
            FflNativeInterop.SpecularModeBlinn,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(1.00f, 1.00f, 1.00f),
            new Vector3(0.70f, 0.70f, 0.70f),
            new Vector3(0.35f, 0.35f, 0.35f),
            10.0f,
            FflNativeInterop.SpecularModeAnisotropic,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(0.75f, 0.75f, 0.75f),
            new Vector3(0.72f, 0.72f, 0.72f),
            new Vector3(0.30f, 0.30f, 0.30f),
            1.5f,
            FflNativeInterop.SpecularModeBlinn,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(0.7f, 0.7f, 0.7f),
            new Vector3(0.0f, 0.0f, 0.0f),
            40.0f,
            FflNativeInterop.SpecularModeAnisotropic,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(0.7f, 0.7f, 0.7f),
            new Vector3(0.0f, 0.0f, 0.0f),
            40.0f,
            FflNativeInterop.SpecularModeAnisotropic,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(0.7f, 0.7f, 0.7f),
            new Vector3(0.0f, 0.0f, 0.0f),
            40.0f,
            FflNativeInterop.SpecularModeAnisotropic,
            new Vector3(0.3f, 0.3f, 0.3f)
        ),
        new(
            new Vector3(0.95622f, 0.95622f, 0.95622f),
            new Vector3(0.496733f, 0.496733f, 0.496733f),
            new Vector3(0.2409f, 0.2409f, 0.2409f),
            3.0f,
            FflNativeInterop.SpecularModeBlinn,
            new Vector3(0.4f, 0.4f, 0.4f)
        ),
        new(
            new Vector3(0.95622f, 0.95622f, 0.95622f),
            new Vector3(1.084967f, 1.084967f, 1.084967f),
            new Vector3(0.2409f, 0.2409f, 0.2409f),
            3.0f,
            FflNativeInterop.SpecularModeBlinn,
            new Vector3(0.4f, 0.4f, 0.4f)
        ),
    ];

    private static readonly object BodyModelLock = new();
    private static BodyModelDatabase? _bodyModelDatabase;
    private const float FflBodyResDefaultBuild = 82f;
    private const float FflBodyResDefaultHeight = 83f;
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
            return await Task.Run(() => RenderToBuffer(mii, studioData, specifications, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Fail("Mii render cancelled.");
        }
    }

    public OperationResult<NativeMiiPixelBuffer> RenderToBuffer(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        CancellationToken cancellationToken = default
    )
    {
        if (mii == null)
            return Fail("Mii cannot be null.");

        var resourcePathResult = resourceLocator.GetFflResourcePath();
        if (resourcePathResult.IsFailure)
            return resourcePathResult.Error!;

        var request = BuildRequest(studioData, specifications);
        var archiveResult = GetManagedArchive(resourcePathResult.Value);
        if (archiveResult.IsFailure)
            return archiveResult.Error!;

        if (cancellationToken.IsCancellationRequested)
            return Fail("Mii render cancelled.");

        var decodeResult = DecodeStudioData(studioData);
        if (decodeResult.IsFailure)
            return decodeResult.Error!;

        var charInfo = MapStudioDataToCharInfo(decodeResult.Value);
        var expressionId = MapExpression(request.Expression);
        using var arena = new RenderAllocationTracker();
        var generatedTextureHandles = new List<IntPtr>(capacity: 8);

        try
        {
            var drawParamsResult = BuildManagedDrawParams(
                archiveResult.Value,
                charInfo,
                request,
                expressionId,
                arena,
                generatedTextureHandles
            );
            if (drawParamsResult.IsFailure)
                return drawParamsResult.Error!;

            var drawParams = drawParamsResult.Value;
            if (drawParams.Count == 0)
                return Fail("Managed renderer produced no drawable meshes for this Mii.");

            var viewParameters = ResolveViewParameters(request, charInfo);
            var bodyRenderData = request.BodyType == MiiImageSpecifications.BodyType.face_only ? null : TryCreateBodyRenderData(charInfo);
            var frameWidth = request.Width;
            var frameHeight = RoundUpToEven((int)MathF.Ceiling(frameWidth * viewParameters.AspectHeightFactor));
            if (frameHeight <= 0)
                frameHeight = frameWidth;

            var outputWidth = checked(frameWidth * request.InstanceCount);
            var outputHeight = frameHeight;
            var pixels = new byte[outputWidth * outputHeight * 4];
            var depth = new float[outputWidth * outputHeight];
            var background = ParseStudioRgba(request.BackgroundColor, defaultColor: new(255, 255, 255, 0));

            FillBackground(pixels, depth, outputWidth, outputHeight, background);

            for (var i = 0; i < request.InstanceCount; i++)
            {
                var instanceYaw = request.CharacterYRotate + (360f / request.InstanceCount) * i;
                var cameraRotate = ConvertDegreesToRadians(request.CameraXRotate, request.CameraYRotate, request.CameraZRotate);
                var modelRotate = ConvertDegreesToRadians(request.CharacterXRotate, instanceYaw, request.CharacterZRotate);

                var orbitRadius = viewParameters.OrbitRadius * request.CameraZoom;
                var cameraPosition = CalculateCameraOrbitPosition(orbitRadius, cameraRotate);
                cameraPosition.Y += viewParameters.BaseCameraY;
                var cameraUp = CalculateUpVector(cameraRotate);
                var baseRotationMatrix = CreateRotationMatrix(modelRotate);

                var cameraTarget = viewParameters.Target;
                var headModelMatrix = baseRotationMatrix;
                if (bodyRenderData is { } bodyData)
                {
                    headModelMatrix = baseRotationMatrix * bodyData.HeadModelMatrix;
                    if (!viewParameters.IsCameraPositionAbsolute)
                    {
                        cameraPosition += bodyData.HeadTranslation;
                        cameraTarget += bodyData.HeadTranslation;
                    }
                }

                var viewMatrix = Matrix4x4.CreateLookAt(cameraPosition, cameraTarget, cameraUp);

                if (bodyRenderData is { } bodyMeshData)
                {
                    RasterizeBodyInstance(
                        pixels,
                        depth,
                        outputWidth,
                        outputHeight,
                        i * frameWidth,
                        frameWidth,
                        frameHeight,
                        bodyMeshData,
                        baseRotationMatrix,
                        viewMatrix,
                        viewParameters.Projection
                    );
                }

                RasterizeInstance(
                    pixels,
                    depth,
                    outputWidth,
                    outputHeight,
                    i * frameWidth,
                    frameWidth,
                    frameHeight,
                    drawParams,
                    headModelMatrix,
                    viewMatrix,
                    viewParameters.Projection
                );
            }

            return new NativeMiiPixelBuffer(outputWidth, outputHeight, pixels);
        }
        finally
        {
            foreach (var handle in generatedTextureHandles)
                TextureRegistry.RemoveTexture(handle);
        }
    }

    public NativeMiiRenderRequest BuildRequest(string studioData, MiiImageSpecifications specifications)
    {
        var width = Math.Clamp((int)specifications.Size, 16, 4096);
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
            Math.Clamp(specifications.CameraZoom, 0.35f, 3f),
            string.IsNullOrWhiteSpace(specifications.BackgroundColor) ? "FFFFFF00" : specifications.BackgroundColor
        );
    }

    private static int MapExpression(MiiImageSpecifications.FaceExpression expression) =>
        expression switch
        {
            MiiImageSpecifications.FaceExpression.normal => FflNativeInterop.FflExpressionNormal,
            MiiImageSpecifications.FaceExpression.normal_open_mouth => FflNativeInterop.FflExpressionOpenMouth,
            MiiImageSpecifications.FaceExpression.smile => FflNativeInterop.FflExpressionSmile,
            MiiImageSpecifications.FaceExpression.smile_open_mouth => FflNativeInterop.FflExpressionSmileOpenMouth,
            MiiImageSpecifications.FaceExpression.frustrated => FflNativeInterop.FflExpressionFrustrated,
            MiiImageSpecifications.FaceExpression.anger => FflNativeInterop.FflExpressionAnger,
            MiiImageSpecifications.FaceExpression.anger_open_mouth => FflNativeInterop.FflExpressionAngerOpenMouth,
            MiiImageSpecifications.FaceExpression.blink => FflNativeInterop.FflExpressionBlink,
            MiiImageSpecifications.FaceExpression.blink_open_mouth => FflNativeInterop.FflExpressionBlinkOpenMouth,
            MiiImageSpecifications.FaceExpression.sorrow => FflNativeInterop.FflExpressionSorrow,
            MiiImageSpecifications.FaceExpression.sorrow_open_mouth => FflNativeInterop.FflExpressionSorrowOpenMouth,
            MiiImageSpecifications.FaceExpression.surprise => FflNativeInterop.FflExpressionSurprise,
            MiiImageSpecifications.FaceExpression.surprise_open_mouth => FflNativeInterop.FflExpressionSurpriseOpenMouth,
            MiiImageSpecifications.FaceExpression.wink_right => FflNativeInterop.FflExpressionWinkRight,
            MiiImageSpecifications.FaceExpression.wink_left => FflNativeInterop.FflExpressionWinkLeft,
            MiiImageSpecifications.FaceExpression.like_wink_left => FflNativeInterop.FflExpressionLikeWinkLeft,
            MiiImageSpecifications.FaceExpression.like_wink_right => FflNativeInterop.FflExpressionLikeWinkRight,
            MiiImageSpecifications.FaceExpression.wink_left_open_mouth => FflNativeInterop.FflExpressionWinkLeftOpenMouth,
            MiiImageSpecifications.FaceExpression.wink_right_open_mouth => FflNativeInterop.FflExpressionWinkRightOpenMouth,
            _ => FflNativeInterop.FflExpressionNormal,
        };

    private static OverlayTexture RenderOverlayTexture(
        IReadOnlyList<FflNativeInterop.FFLDrawParam> drawParams,
        int width,
        int height,
        Vector4 clearColor,
        BlendMode blendMode
    )
    {
        var target = new byte[checked(width * height * 4)];
        FillOverlay(target, width, height, clearColor);

        var identity = Matrix4x4.Identity;
        foreach (var drawParam in drawParams)
        {
            var mesh = PrepareMesh(drawParam, frameX: 0, frameWidth: width, frameHeight: height, identity, identity, identity);
            if (mesh == null)
                continue;

            RasterizeMesh(target, depth: null, width, height, mesh, lightEnabled: false, blendMode);
        }

        return new OverlayTexture(width, height, target);
    }

    private static BodyRenderData? TryCreateBodyRenderData(FflNativeInterop.FFLiCharInfo charInfo)
    {
        if (!TryGetBodyModelDatabase(out var database))
            return null;

        var gender = charInfo.gender % 2;
        if (gender < 0)
            gender += 2;

        var model = gender == 1 ? database.FemaleModel : database.MaleModel;
        if (model.Meshes.Length == 0)
            return null;

        // Match reference BodyModel::initialize() behavior:
        // body scale follows per-Mii build/height, not a fixed neutral.
        var build = Math.Clamp((float)charInfo.build, 0f, 127f);
        var height = Math.Clamp((float)charInfo.height, 0f, 127f);
        var bodyScale = CalculateBodyScale(build, height);
        var headTranslation = new Vector3(0f, database.HeadYTranslate, 0f);
        headTranslation = Hadamard(headTranslation, bodyScale);
        headTranslation *= database.ModelScale;

        var headModelMatrix = Matrix4x4.CreateTranslation(headTranslation);
        var bodyScaleMatrix = Matrix4x4.CreateScale(bodyScale);
        var bodyColor = ReadFavoriteColorOrDefault(charInfo.favoriteColor);
        var pantsColor = new Vector4(0.2509804f, 0.2745099f, 0.30588239f, 1.0f);
        return new BodyRenderData(model.Meshes, bodyScaleMatrix, headTranslation, headModelMatrix, bodyColor, pantsColor);
    }

    private static Vector3 CalculateBodyScale(float build, float height)
    {
        var x = (build * (height * 0.003671875f + 0.4f)) / 128.0f + height * 0.001796875f + 0.4f;
        var y = (height * 0.006015625f) + 0.5f;
        return new Vector3(x, y, x);
    }

    private static Vector4 ReadFavoriteColorOrDefault(int favoriteColorIndex)
    {
        return GetFavoriteColor(favoriteColorIndex);
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
                var texcoord = new Vector2(ReadSingle(bytes, o + 0x0C), ReadSingle(bytes, o + 0x10));
                var normal = new Vector3(ReadSingle(bytes, o + 0x14), ReadSingle(bytes, o + 0x18), ReadSingle(bytes, o + 0x1C));
                vertices[i] = new BodyVertex(position, texcoord, normal);
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

    private static OperationResult<byte[]> DecodeStudioData(string studioHex)
    {
        if (string.IsNullOrWhiteSpace(studioHex))
            return Fail("Studio data is empty.");

        if (studioHex.Length % 2 != 0)
            return Fail("Studio data hex length is invalid.");

        var encoded = new byte[studioHex.Length / 2];
        for (var i = 0; i < encoded.Length; i++)
        {
            var hex = studioHex.Substring(i * 2, 2);
            if (!byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out encoded[i]))
                return Fail("Studio data is not valid hex.");
        }

        if (encoded.Length != 47)
            return Fail($"Studio data must decode to 47 bytes but got {encoded.Length}.");

        var decoded = new byte[46];
        var previous = encoded[0];

        for (var i = 1; i < encoded.Length; i++)
        {
            var encodedByte = encoded[i];
            var original = (byte)(((encodedByte - 7 + 256) & 0xFF) ^ previous);
            decoded[i - 1] = original;
            previous = encodedByte;
        }

        return decoded;
    }

    private static FflNativeInterop.FFLiCharInfo MapStudioDataToCharInfo(byte[] studio)
    {
        static int CommonColor(byte value) => unchecked((int)(FflNativeInterop.CommonColorEnableMask | value));

        var info = new FflNativeInterop.FFLiCharInfo
        {
            miiVersion = 0,
            parts = new()
            {
                beardColor = CommonColor(studio[0]),
                mustacheType = studio[1],
                eyeScaleY = studio[3],
                eyeColor = CommonColor(studio[4]),
                eyeRotate = studio[5],
                eyeScale = studio[6],
                eyeType = studio[7],
                eyeSpacingX = studio[8],
                eyePositionY = studio[9],
                eyebrowScaleY = studio[10],
                eyebrowColor = CommonColor(studio[11]),
                eyebrowRotate = studio[12],
                eyebrowScale = studio[13],
                eyebrowType = studio[14],
                eyebrowSpacingX = studio[15],
                eyebrowPositionY = studio[16],
                facelineColor = studio[17],
                faceMakeup = studio[18],
                faceType = studio[19],
                faceLine = studio[20],
                glassColor = CommonColor(studio[23]),
                glassScale = studio[24],
                glassType = studio[25],
                glassPositionY = studio[26],
                hairColor = CommonColor(studio[27]),
                hairDir = studio[28],
                hairType = studio[29],
                moleScale = studio[31],
                moleType = studio[32],
                molePositionX = studio[33],
                molePositionY = studio[34],
                mouthScaleY = studio[35],
                mouthColor = CommonColor(studio[36]),
                mouthScale = studio[37],
                mouthType = studio[38],
                mouthPositionY = studio[39],
                mustacheScale = studio[40],
                beardType = studio[41],
                mustachePositionY = studio[42],
                noseScale = studio[43],
                noseType = studio[44],
                nosePositionY = studio[45],
            },
            height = studio[30],
            build = studio[2],
            name = new ushort[11],
            creatorName = new ushort[11],
            gender = studio[22],
            birthMonth = 0,
            birthDay = 0,
            favoriteColor = studio[21],
            favoriteMii = 0,
            copyable = 0,
            ngWord = 0,
            localOnly = 0,
            regionMove = 0,
            fontRegion = 0,
            pageIndex = 0,
            slotIndex = 0,
            birthPlatform = 3, // FFL_BIRTH_PLATFORM_CTR
            createId = new byte[10],
            padding = 0,
            authorType = 0,
            authorId = new byte[8],
        };

        return info;
    }

    private static OperationResult<ManagedFflResourceArchive> GetManagedArchive(string resourcePath)
    {
        lock (ManagedArchiveLock)
        {
            if (
                _managedArchive != null
                && !string.IsNullOrWhiteSpace(_managedArchivePath)
                && string.Equals(Path.GetFullPath(resourcePath), Path.GetFullPath(_managedArchivePath), StringComparison.OrdinalIgnoreCase)
            )
            {
                return _managedArchive;
            }

            var loadResult = ManagedFflResourceArchive.Load(resourcePath);
            if (loadResult.IsFailure)
                return loadResult.Error!;

            _managedArchive = loadResult.Value;
            _managedArchivePath = resourcePath;
            return _managedArchive;
        }
    }

    private static OperationResult<List<FflNativeInterop.FFLDrawParam>> BuildManagedDrawParams(
        ManagedFflResourceArchive archive,
        FflNativeInterop.FFLiCharInfo charInfo,
        NativeMiiRenderRequest request,
        int expressionId,
        RenderAllocationTracker arena,
        List<IntPtr> generatedTextureHandles
    )
    {
        var drawParams = new List<FflNativeInterop.FFLDrawParam>(capacity: 12);
        var textureCache = new Dictionary<(int PartType, int Index), IntPtr>();
        var bigEndian = archive.NeedsEndianSwap();
        var usesAflScaleBug = archive.TextureFormatIsLinear;
        var resolution = request.Width <= 384 ? 256 : 512;

        var facelineShapeResult = LoadManagedShape(archive, partType: 3, index: charInfo.parts.faceType);
        if (facelineShapeResult.IsFailure)
            return facelineShapeResult.Error!;
        var facelineShape = facelineShapeResult.Value;

        var hairPos = facelineShape.FacelineTransform?.HairTranslate ?? Vector3.Zero;
        var faceCenterPos = facelineShape.FacelineTransform?.NoseTranslate ?? Vector3.Zero;
        var beardPos = facelineShape.FacelineTransform?.BeardTranslate ?? Vector3.Zero;

        var facelineTextureHandleResult = BuildManagedFacelineTexture(
            archive,
            charInfo,
            resolution,
            textureCache,
            generatedTextureHandles,
            arena
        );
        if (facelineTextureHandleResult.IsFailure)
            return facelineTextureHandleResult.Error!;
        var facelineTextureHandle = facelineTextureHandleResult.Value;

        var maskTextureHandleResult = BuildManagedMaskTexture(
            archive,
            charInfo,
            resolution,
            expressionId,
            textureCache,
            generatedTextureHandles,
            arena
        );
        if (maskTextureHandleResult.IsFailure)
            return maskTextureHandleResult.Error!;
        var maskTextureHandle = maskTextureHandleResult.Value;

        {
            var facelineColor = GetFacelineColor(charInfo.parts.facelineColor);
            var facelineModulate =
                facelineTextureHandle == IntPtr.Zero
                    ? CreateModulate(
                        arena,
                        mode: 0,
                        type: FflNativeInterop.ModulateTypeShapeFaceline,
                        colorR: facelineColor,
                        colorG: null,
                        colorB: null,
                        texture: IntPtr.Zero
                    )
                    : CreateModulate(
                        arena,
                        mode: 1,
                        type: FflNativeInterop.ModulateTypeShapeFaceline,
                        colorR: null,
                        colorG: null,
                        colorB: null,
                        texture: facelineTextureHandle
                    );

            var faceParam = BuildManagedShapeDrawParam(
                facelineShape,
                scaleX: 1f,
                scaleY: 1f,
                translate: null,
                flipX: false,
                cullMode: FflNativeInterop.FflCullBack,
                facelineModulate,
                arena
            );
            if (faceParam.IsFailure)
                return faceParam.Error!;
            drawParams.Add(faceParam.Value);
        }

        var hairColor = GetHairColor(charInfo.parts.hairColor);
        var facelineSkinColor = GetFacelineColor(charInfo.parts.facelineColor);
        var hairFlip = charInfo.parts.hairDir > 0;
        var hairCull = hairFlip ? FflNativeInterop.FflCullFront : FflNativeInterop.FflCullBack;

        var hairShapeResult = LoadManagedShape(archive, partType: 8, index: charInfo.parts.hairType);
        if (hairShapeResult.IsSuccess)
        {
            var hairShapeParam = BuildManagedShapeDrawParam(
                hairShapeResult.Value,
                scaleX: 1f,
                scaleY: 1f,
                translate: hairPos,
                flipX: hairFlip,
                cullMode: hairCull,
                CreateModulate(
                    arena,
                    mode: 0,
                    type: FflNativeInterop.ModulateTypeShapeHair,
                    colorR: hairColor,
                    colorG: null,
                    colorB: null,
                    texture: IntPtr.Zero
                ),
                arena
            );
            if (hairShapeParam.IsFailure)
                return hairShapeParam.Error!;
            drawParams.Add(hairShapeParam.Value);
        }

        var foreheadShapeResult = LoadManagedShape(archive, partType: 10, index: charInfo.parts.hairType);
        if (foreheadShapeResult.IsSuccess)
        {
            var foreheadParam = BuildManagedShapeDrawParam(
                foreheadShapeResult.Value,
                scaleX: 1f,
                scaleY: 1f,
                translate: hairPos,
                flipX: hairFlip,
                cullMode: hairCull,
                CreateModulate(
                    arena,
                    mode: 0,
                    type: FflNativeInterop.ModulateTypeShapeForehead,
                    colorR: facelineSkinColor,
                    colorG: null,
                    colorB: null,
                    texture: IntPtr.Zero
                ),
                arena
            );
            if (foreheadParam.IsFailure)
                return foreheadParam.Error!;
            drawParams.Add(foreheadParam.Value);
        }

        var capTextureHandle = LoadTextureHandle(
            archive,
            textureCache,
            generatedTextureHandles,
            partType: 1,
            index: charInfo.parts.hairType
        );
        if (capTextureHandle.IsFailure)
            return capTextureHandle.Error!;
        if (capTextureHandle.Value != IntPtr.Zero)
        {
            var hatShapeResult = LoadManagedShape(archive, partType: 1, index: charInfo.parts.hairType);
            if (hatShapeResult.IsSuccess)
            {
                var capParam = BuildManagedShapeDrawParam(
                    hatShapeResult.Value,
                    scaleX: 1f,
                    scaleY: 1f,
                    translate: hairPos,
                    flipX: hairFlip,
                    cullMode: hairCull,
                    CreateModulate(
                        arena,
                        mode: 5,
                        type: FflNativeInterop.ModulateTypeShapeCap,
                        colorR: GetFavoriteColor(charInfo.favoriteColor),
                        colorG: null,
                        colorB: null,
                        texture: capTextureHandle.Value
                    ),
                    arena
                );
                if (capParam.IsFailure)
                    return capParam.Error!;
                drawParams.Add(capParam.Value);
            }
        }

        if (charInfo.parts.beardType is >= 0 and < 4)
        {
            var beardShapeResult = LoadManagedShape(archive, partType: 0, index: charInfo.parts.beardType);
            if (beardShapeResult.IsSuccess)
            {
                var beardParam = BuildManagedShapeDrawParam(
                    beardShapeResult.Value,
                    scaleX: 1f,
                    scaleY: 1f,
                    translate: beardPos,
                    flipX: false,
                    cullMode: FflNativeInterop.FflCullBack,
                    CreateModulate(
                        arena,
                        mode: 0,
                        type: FflNativeInterop.ModulateTypeShapeBeard,
                        colorR: hairColor,
                        colorG: null,
                        colorB: null,
                        texture: IntPtr.Zero
                    ),
                    arena
                );
                if (beardParam.IsFailure)
                    return beardParam.Error!;
                drawParams.Add(beardParam.Value);
            }
        }

        var skipNoseAndMask = expressionId is 49 or 50 or 51 or 52 or 61 or 62;
        if (!skipNoseAndMask)
        {
            var noseScale = charInfo.parts.noseScale * 0.175f + 0.4f;
            var nosePos = new Vector3(faceCenterPos.X, faceCenterPos.Y + (charInfo.parts.nosePositionY - 8) * -1.5f, faceCenterPos.Z);

            var noseShapeResult = LoadManagedShape(archive, partType: 7, index: charInfo.parts.noseType);
            if (noseShapeResult.IsSuccess)
            {
                var noseParam = BuildManagedShapeDrawParam(
                    noseShapeResult.Value,
                    scaleX: noseScale,
                    scaleY: noseScale,
                    translate: nosePos,
                    flipX: false,
                    cullMode: FflNativeInterop.FflCullBack,
                    CreateModulate(
                        arena,
                        mode: 0,
                        type: FflNativeInterop.ModulateTypeShapeNose,
                        colorR: facelineSkinColor,
                        colorG: null,
                        colorB: null,
                        texture: IntPtr.Zero
                    ),
                    arena
                );
                if (noseParam.IsFailure)
                    return noseParam.Error!;
                drawParams.Add(noseParam.Value);
            }

            var noseLineTexture = LoadTextureHandle(
                archive,
                textureCache,
                generatedTextureHandles,
                partType: 10,
                index: charInfo.parts.noseType
            );
            if (noseLineTexture.IsFailure)
                return noseLineTexture.Error!;
            if (noseLineTexture.Value != IntPtr.Zero)
            {
                var noseLineShapeResult = LoadManagedShape(archive, partType: 6, index: charInfo.parts.noseType);
                if (noseLineShapeResult.IsSuccess)
                {
                    var noseLineParam = BuildManagedShapeDrawParam(
                        noseLineShapeResult.Value,
                        scaleX: noseScale,
                        scaleY: noseScale,
                        translate: nosePos,
                        flipX: false,
                        cullMode: FflNativeInterop.FflCullBack,
                        CreateModulate(
                            arena,
                            mode: 3,
                            type: FflNativeInterop.ModulateTypeShapeNoseLine,
                            colorR: new Vector4(0f, 0f, 0f, 1f),
                            colorG: null,
                            colorB: null,
                            texture: noseLineTexture.Value
                        ),
                        arena
                    );
                    if (noseLineParam.IsFailure)
                        return noseLineParam.Error!;
                    drawParams.Add(noseLineParam.Value);
                }
            }

            if (maskTextureHandle != IntPtr.Zero)
            {
                var maskShapeResult = LoadManagedShape(archive, partType: 5, index: charInfo.parts.faceType);
                if (maskShapeResult.IsSuccess)
                {
                    var maskCull = archive.TextureFormatIsLinear ? FflNativeInterop.FflCullNone : FflNativeInterop.FflCullBack;
                    var maskParam = BuildManagedShapeDrawParam(
                        maskShapeResult.Value,
                        scaleX: 1f,
                        scaleY: 1f,
                        translate: null,
                        flipX: false,
                        cullMode: maskCull,
                        CreateModulate(
                            arena,
                            mode: 1,
                            type: FflNativeInterop.ModulateTypeShapeMask,
                            colorR: null,
                            colorG: null,
                            colorB: null,
                            texture: maskTextureHandle
                        ),
                        arena
                    );
                    if (maskParam.IsFailure)
                        return maskParam.Error!;
                    drawParams.Add(maskParam.Value);
                }
            }
        }

        if (charInfo.parts.glassType > 0)
        {
            var glassTexture = LoadTextureHandle(
                archive,
                textureCache,
                generatedTextureHandles,
                partType: 6,
                index: charInfo.parts.glassType
            );
            if (glassTexture.IsFailure)
                return glassTexture.Error!;
            if (glassTexture.Value != IntPtr.Zero)
            {
                var glassScale = charInfo.parts.glassScale * (usesAflScaleBug ? 0.175f : 0.15f) + 0.4f;
                var glassPos = new Vector3(
                    faceCenterPos.X,
                    faceCenterPos.Y + (charInfo.parts.glassPositionY - 11) * -1.5f + 5.0f,
                    faceCenterPos.Z + 2.0f
                );

                var glassShapeResult = LoadManagedShape(archive, partType: 4, index: 0);
                if (glassShapeResult.IsSuccess)
                {
                    var glassParam = BuildManagedShapeDrawParam(
                        glassShapeResult.Value,
                        scaleX: glassScale,
                        scaleY: glassScale,
                        translate: glassPos,
                        flipX: false,
                        cullMode: FflNativeInterop.FflCullNone,
                        CreateModulate(
                            arena,
                            mode: 4,
                            type: FflNativeInterop.ModulateTypeShapeGlass,
                            colorR: GetGlassColor(charInfo.parts.glassColor),
                            colorG: null,
                            colorB: null,
                            texture: glassTexture.Value
                        ),
                        arena
                    );
                    if (glassParam.IsFailure)
                        return glassParam.Error!;
                    drawParams.Add(glassParam.Value);
                }
            }
        }

        return drawParams;
    }

    private static OperationResult<IntPtr> BuildManagedFacelineTexture(
        ManagedFflResourceArchive archive,
        FflNativeInterop.FFLiCharInfo charInfo,
        int resolution,
        Dictionary<(int PartType, int Index), IntPtr> textureCache,
        List<IntPtr> generatedTextureHandles,
        RenderAllocationTracker arena
    )
    {
        var needsFacelineTexture = charInfo.parts.faceLine != 0 || charInfo.parts.faceMakeup != 0 || charInfo.parts.beardType >= 4;
        if (!needsFacelineTexture)
            return IntPtr.Zero;

        var overlays = new List<FflNativeInterop.FFLDrawParam>(capacity: 3);
        if (charInfo.parts.faceMakeup > 0)
        {
            var faceMake = LoadTextureHandle(archive, textureCache, generatedTextureHandles, partType: 5, index: charInfo.parts.faceMakeup);
            if (faceMake.IsFailure)
                return faceMake.Error!;
            if (faceMake.Value != IntPtr.Zero)
            {
                overlays.Add(
                    CreateFullScreenOverlayDrawParam(
                        arena,
                        CreateModulate(
                            arena,
                            mode: 1,
                            type: FflNativeInterop.ModulateTypeShapeFaceline,
                            colorR: null,
                            colorG: null,
                            colorB: null,
                            texture: faceMake.Value
                        )
                    )
                );
            }
        }

        if (charInfo.parts.faceLine > 0)
        {
            var faceLine = LoadTextureHandle(archive, textureCache, generatedTextureHandles, partType: 4, index: charInfo.parts.faceLine);
            if (faceLine.IsFailure)
                return faceLine.Error!;
            if (faceLine.Value != IntPtr.Zero)
            {
                overlays.Add(
                    CreateFullScreenOverlayDrawParam(
                        arena,
                        CreateModulate(
                            arena,
                            mode: 3,
                            type: FflNativeInterop.ModulateTypeShapeFaceline,
                            colorR: new Vector4(0f, 0f, 0f, 1f),
                            colorG: null,
                            colorB: null,
                            texture: faceLine.Value
                        )
                    )
                );
            }
        }

        if (charInfo.parts.beardType >= 4)
        {
            var beardTex = LoadTextureHandle(
                archive,
                textureCache,
                generatedTextureHandles,
                partType: 0,
                index: charInfo.parts.beardType - 3
            );
            if (beardTex.IsFailure)
                return beardTex.Error!;
            if (beardTex.Value != IntPtr.Zero)
            {
                overlays.Add(
                    CreateFullScreenOverlayDrawParam(
                        arena,
                        CreateModulate(
                            arena,
                            mode: 3,
                            type: FflNativeInterop.ModulateTypeShapeFaceline,
                            colorR: GetHairColor(charInfo.parts.beardColor),
                            colorG: null,
                            colorB: null,
                            texture: beardTex.Value
                        )
                    )
                );
            }
        }

        if (overlays.Count == 0)
            return IntPtr.Zero;

        var width = Math.Max(1, resolution / 2);
        var height = Math.Max(1, resolution);
        var clear = GetFacelineColor(charInfo.parts.facelineColor);
        var facelineTexture = RenderOverlayTexture(overlays, width, height, clear, BlendMode.Faceline);
        var handle = TextureRegistry.RegisterTexture(
            new TextureData(
                facelineTexture.Width,
                facelineTexture.Height,
                FflNativeInterop.TextureFormatRgba8,
                4,
                ConvertBgraToRgba(facelineTexture.BgraPixels)
            )
        );
        if (handle != IntPtr.Zero)
            generatedTextureHandles.Add(handle);
        return handle;
    }

    private static OperationResult<IntPtr> BuildManagedMaskTexture(
        ManagedFflResourceArchive archive,
        FflNativeInterop.FFLiCharInfo charInfo,
        int resolution,
        int expressionId,
        Dictionary<(int PartType, int Index), IntPtr> textureCache,
        List<IntPtr> generatedTextureHandles,
        RenderAllocationTracker arena
    )
    {
        var expression = Math.Clamp(expressionId, 0, 18);
        var element = MaskExpressionElements[expression];
        var eyeIndexR = EyeTextureIndex(charInfo, element.EyeRightType);
        var eyeIndexL = EyeTextureIndex(charInfo, element.EyeLeftType);
        var mouthIndex = MouthTextureIndex(charInfo, element.MouthType);
        var eyebrowIndex = EyebrowTextureIndex(charInfo, element.EyebrowType);

        var parts = BuildRawMaskParts(charInfo);
        var overlays = new List<FflNativeInterop.FFLDrawParam>(capacity: 8);

        if (charInfo.parts.mustacheType != 0)
        {
            var mustacheTexture = LoadTextureHandle(
                archive,
                textureCache,
                generatedTextureHandles,
                partType: 9,
                index: charInfo.parts.mustacheType
            );
            if (mustacheTexture.IsFailure)
                return mustacheTexture.Error!;
            if (mustacheTexture.Value != IntPtr.Zero)
            {
                var modulate = CreateModulate(
                    arena,
                    mode: 3,
                    type: FflNativeInterop.ModulateTypeShapeMask,
                    colorR: GetHairColor(charInfo.parts.beardColor),
                    colorG: null,
                    colorB: null,
                    texture: mustacheTexture.Value
                );
                overlays.Add(CreateRawMaskOverlayDrawParam(arena, parts.MustacheR, modulate));
                overlays.Add(CreateRawMaskOverlayDrawParam(arena, parts.MustacheL, modulate));
            }
        }

        {
            var mouthTexture = LoadTextureHandle(archive, textureCache, generatedTextureHandles, partType: 8, index: mouthIndex);
            if (mouthTexture.IsFailure)
                return mouthTexture.Error!;
            if (mouthTexture.Value != IntPtr.Zero)
            {
                var mouthMode = mouthIndex > 36 ? 1 : 2;
                var mouthModulate =
                    mouthMode == 1
                        ? CreateModulate(
                            arena,
                            mode: 1,
                            type: FflNativeInterop.ModulateTypeShapeMask,
                            colorR: null,
                            colorG: null,
                            colorB: null,
                            texture: mouthTexture.Value
                        )
                        : CreateModulate(
                            arena,
                            mode: 2,
                            type: FflNativeInterop.ModulateTypeShapeMask,
                            colorR: GetMouthColorR(charInfo.parts.mouthColor),
                            colorG: GetMouthColorG(charInfo.parts.mouthColor),
                            colorB: new Vector4(1f, 1f, 1f, 1f),
                            texture: mouthTexture.Value
                        );
                overlays.Add(CreateRawMaskOverlayDrawParam(arena, parts.Mouth, mouthModulate));
            }
        }

        if (eyebrowIndex != 23)
        {
            var eyebrowTexture = LoadTextureHandle(archive, textureCache, generatedTextureHandles, partType: 3, index: eyebrowIndex);
            if (eyebrowTexture.IsFailure)
                return eyebrowTexture.Error!;
            if (eyebrowTexture.Value != IntPtr.Zero)
            {
                var browModulate = CreateModulate(
                    arena,
                    mode: 3,
                    type: FflNativeInterop.ModulateTypeShapeMask,
                    colorR: GetHairColor(charInfo.parts.eyebrowColor),
                    colorG: null,
                    colorB: null,
                    texture: eyebrowTexture.Value
                );
                overlays.Add(CreateRawMaskOverlayDrawParam(arena, parts.EyebrowR, browModulate));
                overlays.Add(CreateRawMaskOverlayDrawParam(arena, parts.EyebrowL, browModulate));
            }
        }

        {
            var eyeTextureR = LoadTextureHandle(archive, textureCache, generatedTextureHandles, partType: 2, index: eyeIndexR);
            if (eyeTextureR.IsFailure)
                return eyeTextureR.Error!;
            var eyeTextureL = LoadTextureHandle(archive, textureCache, generatedTextureHandles, partType: 2, index: eyeIndexL);
            if (eyeTextureL.IsFailure)
                return eyeTextureL.Error!;

            if (eyeTextureR.Value != IntPtr.Zero)
            {
                var mode = ShouldUseEyeTextureDirect(eyeIndexR) ? 1 : 2;
                var eyeModulateR =
                    mode == 1
                        ? CreateModulate(
                            arena,
                            mode: 1,
                            type: FflNativeInterop.ModulateTypeShapeMask,
                            colorR: null,
                            colorG: null,
                            colorB: null,
                            texture: eyeTextureR.Value
                        )
                        : CreateModulate(
                            arena,
                            mode: 2,
                            type: FflNativeInterop.ModulateTypeShapeMask,
                            colorR: new Vector4(0f, 1f, 1f, 1f),
                            colorG: new Vector4(1f, 1f, 1f, 1f),
                            colorB: GetEyeColorB(charInfo.parts.eyeColor),
                            texture: eyeTextureR.Value
                        );
                overlays.Add(CreateRawMaskOverlayDrawParam(arena, parts.EyeR, eyeModulateR));
            }

            if (eyeTextureL.Value != IntPtr.Zero)
            {
                var mode = ShouldUseEyeTextureDirect(eyeIndexL) ? 1 : 2;
                var eyeModulateL =
                    mode == 1
                        ? CreateModulate(
                            arena,
                            mode: 1,
                            type: FflNativeInterop.ModulateTypeShapeMask,
                            colorR: null,
                            colorG: null,
                            colorB: null,
                            texture: eyeTextureL.Value
                        )
                        : CreateModulate(
                            arena,
                            mode: 2,
                            type: FflNativeInterop.ModulateTypeShapeMask,
                            colorR: new Vector4(0f, 1f, 1f, 1f),
                            colorG: new Vector4(1f, 1f, 1f, 1f),
                            colorB: GetEyeColorB(charInfo.parts.eyeColor),
                            texture: eyeTextureL.Value
                        );
                overlays.Add(CreateRawMaskOverlayDrawParam(arena, parts.EyeL, eyeModulateL));
            }
        }

        if (charInfo.parts.moleType != 0)
        {
            var moleTexture = LoadTextureHandle(
                archive,
                textureCache,
                generatedTextureHandles,
                partType: 7,
                index: charInfo.parts.moleType
            );
            if (moleTexture.IsFailure)
                return moleTexture.Error!;
            if (moleTexture.Value != IntPtr.Zero)
            {
                overlays.Add(
                    CreateRawMaskOverlayDrawParam(
                        arena,
                        parts.Mole,
                        CreateModulate(
                            arena,
                            mode: 3,
                            type: FflNativeInterop.ModulateTypeShapeMask,
                            colorR: new Vector4(0.071f, 0.059f, 0.059f, 1f),
                            colorG: null,
                            colorB: null,
                            texture: moleTexture.Value
                        )
                    )
                );
            }
        }

        if (overlays.Count == 0)
            return IntPtr.Zero;

        var size = Math.Max(1, resolution);
        var maskTexture = RenderOverlayTexture(overlays, size, size, new Vector4(0f, 0f, 0f, 0f), BlendMode.MaskNoRenderTexture);
        var handle = TextureRegistry.RegisterTexture(
            new TextureData(
                maskTexture.Width,
                maskTexture.Height,
                FflNativeInterop.TextureFormatRgba8,
                4,
                ConvertBgraToRgba(maskTexture.BgraPixels)
            )
        );
        if (handle != IntPtr.Zero)
            generatedTextureHandles.Add(handle);
        return handle;
    }

    private static OperationResult<DecodedShape> LoadManagedShape(ManagedFflResourceArchive archive, int partType, int index)
    {
        return DecodeManagedShapeAtIndex(archive, partType, index);
    }

    private static OperationResult<DecodedShape> DecodeManagedShapeAtIndex(ManagedFflResourceArchive archive, int partType, int index)
    {
        var bytesResult = archive.LoadShapePart(partType, index);
        if (bytesResult.IsFailure)
            return bytesResult.Error!;

        var bytes = bytesResult.Value;
        if (bytes.Length == 0)
            return Fail($"Shape part {partType}:{index} is empty.");

        var decodeResult = DecodeShapeData(bytes, partType, archive.NeedsEndianSwap(), archive.IsHalfFloatLayout);
        if (decodeResult.IsFailure)
            return Fail($"Failed to decode shape part {partType}:{index} (size={bytes.Length}): {decodeResult.Error!.Message}");
        return decodeResult.Value;
    }

    private static OperationResult<IntPtr> LoadTextureHandle(
        ManagedFflResourceArchive archive,
        Dictionary<(int PartType, int Index), IntPtr> textureCache,
        List<IntPtr> generatedTextureHandles,
        int partType,
        int index
    )
    {
        if (index < 0)
            return IntPtr.Zero;

        if (textureCache.TryGetValue((partType, index), out var cached))
            return cached;

        var bytesResult = archive.LoadTexturePart(partType, index);
        if (bytesResult.IsFailure)
            return bytesResult.Error!;

        if (bytesResult.Value.Length <= 12)
            return IntPtr.Zero;

        var decodeResult = DecodeTexturePart(bytesResult.Value, archive.NeedsEndianSwap(), archive.IgnoreMipMaps);
        if (decodeResult.IsFailure)
            return decodeResult.Error!;

        var texture = decodeResult.Value;
        var handle = TextureRegistry.RegisterTexture(texture);
        if (handle != IntPtr.Zero)
            generatedTextureHandles.Add(handle);
        textureCache[(partType, index)] = handle;
        return handle;
    }

    private static OperationResult<TextureData> DecodeTexturePart(byte[] bytes, bool bigEndian, bool ignoreMipMaps)
    {
        if (bytes.Length < 12)
            return Fail("Texture part is truncated.");

        var footerOffset = bytes.Length - 12;
        var width = ManagedFflResourceArchive.ReadU16(bytes, footerOffset + 4, bigEndian);
        var height = ManagedFflResourceArchive.ReadU16(bytes, footerOffset + 6, bigEndian);
        var numMips = bytes[footerOffset + 8];
        var format = bytes[footerOffset + 9];

        if (width == 0 || height == 0)
            return Fail("Texture part has invalid dimensions.");

        var pixelStride = format switch
        {
            FflNativeInterop.TextureFormatR8 => 1,
            FflNativeInterop.TextureFormatRg8 => 2,
            FflNativeInterop.TextureFormatRgba8 => 4,
            _ => 0,
        };
        if (pixelStride == 0)
            return Fail($"Unsupported texture format {format}.");

        var imageSizeLong = (long)width * height * pixelStride;
        if (imageSizeLong <= 0 || imageSizeLong > int.MaxValue)
            return Fail("Texture size is invalid.");

        var imageSize = (int)imageSizeLong;
        if (footerOffset < imageSize)
            return Fail("Texture image payload is truncated.");

        var pixels = new byte[imageSize];
        Buffer.BlockCopy(bytes, 0, pixels, 0, imageSize);

        if (!ignoreMipMaps && numMips > 1)
        {
            // Mipmaps are not currently sampled in the software rasterizer.
            // Base level is enough for parity tests at target sizes.
        }

        return new TextureData(width, height, format, pixelStride, pixels);
    }

    private static OperationResult<FflNativeInterop.FFLDrawParam> BuildManagedShapeDrawParam(
        DecodedShape shape,
        float scaleX,
        float scaleY,
        Vector3? translate,
        bool flipX,
        int cullMode,
        FflNativeInterop.FFLModulateParam modulate,
        RenderAllocationTracker arena
    )
    {
        if (shape.Positions.Length == 0 || shape.Indices.Length < 3)
            return Fail("Shape has no drawable geometry.");

        var scaleZ = (scaleX + scaleY) * 0.5f;
        var t = translate ?? Vector3.Zero;

        var transformedPositions = new Vector3[shape.Positions.Length];
        var transformedNormals = new Vector3[shape.Positions.Length];
        var transformedTangents = new Vector3[shape.Positions.Length];
        var texcoords = new Vector2[shape.Positions.Length];
        var parameters = new Vector4[shape.Positions.Length];

        for (var i = 0; i < shape.Positions.Length; i++)
        {
            var p = shape.Positions[i];
            if (flipX)
                p.X = -p.X;
            p.X = p.X * scaleX + t.X;
            p.Y = p.Y * scaleY + t.Y;
            p.Z = p.Z * scaleZ + t.Z;
            transformedPositions[i] = p;

            var n = i < shape.Normals.Length ? shape.Normals[i] : new Vector3(0f, 0f, 1f);
            var tg = i < shape.Tangents.Length ? shape.Tangents[i] : Vector3.Zero;
            if (flipX)
            {
                n.X = -n.X;
                tg.X = -tg.X;
            }
            transformedNormals[i] = n;
            transformedTangents[i] = tg;
            texcoords[i] = i < shape.Texcoords.Length ? shape.Texcoords[i] : Vector2.Zero;
            parameters[i] = i < shape.Parameters.Length ? shape.Parameters[i] : new Vector4(1f, 1f, 0f, 1f);
        }

        var positionBytes = EncodePositions(transformedPositions);
        var texcoordBytes = EncodeTexcoords(texcoords);
        var normalBytes = EncodeNormalsInt10(transformedNormals);
        var tangentBytes = EncodeTangentsSnorm8(transformedTangents);
        var parameterBytes = EncodeParameters(parameters);

        var indexBuffer = new short[shape.Indices.Length];
        for (var i = 0; i < shape.Indices.Length; i++)
            indexBuffer[i] = unchecked((short)shape.Indices[i]);

        var drawParam = new FflNativeInterop.FFLDrawParam
        {
            cullMode = cullMode,
            modulateParam = modulate,
            attributeBufferParam = new FflNativeInterop.FFLAttributeBufferParam
            {
                position = new FflNativeInterop.FFLAttributeBuffer
                {
                    size = (uint)positionBytes.Length,
                    stride = 12,
                    ptr = arena.Pin(positionBytes),
                },
                texcoord = new FflNativeInterop.FFLAttributeBuffer
                {
                    size = (uint)texcoordBytes.Length,
                    stride = 8,
                    ptr = arena.Pin(texcoordBytes),
                },
                normal = new FflNativeInterop.FFLAttributeBuffer
                {
                    size = (uint)normalBytes.Length,
                    stride = 4,
                    ptr = arena.Pin(normalBytes),
                },
                tangent = new FflNativeInterop.FFLAttributeBuffer
                {
                    size = (uint)tangentBytes.Length,
                    stride = 4,
                    ptr = arena.Pin(tangentBytes),
                },
                color = new FflNativeInterop.FFLAttributeBuffer
                {
                    size = (uint)parameterBytes.Length,
                    stride = 4,
                    ptr = arena.Pin(parameterBytes),
                },
            },
            primitiveParam = new FflNativeInterop.FFLPrimitiveParam
            {
                primitiveType = FflNativeInterop.PrimitiveTriangles,
                indexCount = (uint)indexBuffer.Length,
                pIndexBuffer = arena.Pin(indexBuffer),
            },
        };

        return drawParam;
    }

    private static OperationResult<DecodedShape> DecodeShapeData(byte[] bytes, int partType, bool bigEndian, bool halfFloatLayout)
    {
        const int shapeHeaderSize = 0x90;
        if (bytes.Length < shapeHeaderSize)
            return Fail("Shape payload is truncated.");

        const int elementCount = 6; // position, normal, texcoord, tangent, color, index
        Span<uint> elementPos = stackalloc uint[elementCount];
        Span<uint> elementSize = stackalloc uint[elementCount];
        for (var i = 0; i < elementCount; i++)
        {
            elementPos[i] = ManagedFflResourceArchive.ReadU32(bytes, i * 4, bigEndian);
            elementSize[i] = ManagedFflResourceArchive.ReadU32(bytes, elementCount * 4 + i * 4, bigEndian);
            if (elementSize[i] == 0)
                continue;

            // In FFL shape headers, index "size" is an element count (u16 indices),
            // while other element sizes are byte sizes.
            var elementByteSize = i == 5 ? elementSize[i] * 2u : elementSize[i];
            if (elementPos[i] >= bytes.Length || (long)elementPos[i] + elementByteSize > bytes.Length)
                return Fail("Shape element exceeds payload bounds.");
        }

        var posSize = (int)elementSize[0];
        if (posSize <= 0)
            return Fail("Shape position buffer is empty.");

        var srcPosStride = halfFloatLayout ? 6 : 16;
        if (!halfFloatLayout && posSize % srcPosStride != 0 && posSize % 12 == 0)
            srcPosStride = 12;
        var vertexCount = posSize / srcPosStride;
        if (vertexCount <= 0)
            return Fail("Shape has no vertices.");

        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var tangents = new Vector3[vertexCount];
        var texcoords = new Vector2[vertexCount];
        var parameters = new Vector4[vertexCount];
        Array.Fill(normals, new Vector3(0f, 0f, 1f));
        Array.Fill(parameters, new Vector4(1f, 1f, 0f, 1f));

        var posOffset = (int)elementPos[0];
        for (var i = 0; i < vertexCount; i++)
        {
            var o = posOffset + i * srcPosStride;
            if (halfFloatLayout)
            {
                positions[i] = new Vector3(
                    (float)BitConverter.UInt16BitsToHalf(ManagedFflResourceArchive.ReadU16(bytes, o + 0, bigEndian)),
                    (float)BitConverter.UInt16BitsToHalf(ManagedFflResourceArchive.ReadU16(bytes, o + 2, bigEndian)),
                    (float)BitConverter.UInt16BitsToHalf(ManagedFflResourceArchive.ReadU16(bytes, o + 4, bigEndian))
                );
            }
            else
            {
                positions[i] = new Vector3(
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0, bigEndian),
                    ManagedFflResourceArchive.ReadF32(bytes, o + 4, bigEndian),
                    ManagedFflResourceArchive.ReadF32(bytes, o + 8, bigEndian)
                );
            }
        }

        if (elementSize[2] > 0)
        {
            var texOffset = (int)elementPos[2];
            var srcTexStride = halfFloatLayout ? 4 : 8;
            var texCount = Math.Min(vertexCount, (int)elementSize[2] / srcTexStride);
            for (var i = 0; i < texCount; i++)
            {
                var o = texOffset + i * srcTexStride;
                texcoords[i] = halfFloatLayout
                    ? new Vector2(
                        (float)BitConverter.UInt16BitsToHalf(ManagedFflResourceArchive.ReadU16(bytes, o + 0, bigEndian)),
                        (float)BitConverter.UInt16BitsToHalf(ManagedFflResourceArchive.ReadU16(bytes, o + 2, bigEndian))
                    )
                    : new Vector2(
                        ManagedFflResourceArchive.ReadF32(bytes, o + 0, bigEndian),
                        ManagedFflResourceArchive.ReadF32(bytes, o + 4, bigEndian)
                    );
            }
        }

        if (elementSize[1] > 0)
        {
            var normalOffset = (int)elementPos[1];
            var normalCount = Math.Min(vertexCount, (int)elementSize[1] / 4);
            for (var i = 0; i < normalCount; i++)
            {
                var o = normalOffset + i * 4;
                if (halfFloatLayout)
                {
                    var nx = ((sbyte)bytes[o + 0]) / 127f;
                    var ny = ((sbyte)bytes[o + 1]) / 127f;
                    var nz = ((sbyte)bytes[o + 2]) / 127f;
                    var n = new Vector3(nx, ny, nz);
                    normals[i] = n.LengthSquared() < 1e-8f ? new Vector3(0f, 0f, 1f) : Vector3.Normalize(n);
                }
                else
                {
                    var packed = unchecked((int)ManagedFflResourceArchive.ReadU32(bytes, o, bigEndian));
                    normals[i] = DecodeInt2101010(packed);
                }
            }
        }

        if (elementSize[3] > 0)
        {
            var tangentOffset = (int)elementPos[3];
            var tangentCount = Math.Min(vertexCount, (int)elementSize[3] / 4);
            for (var i = 0; i < tangentCount; i++)
            {
                var o = tangentOffset + i * 4;
                var tx = ((sbyte)bytes[o + 0]) / 127f;
                var ty = ((sbyte)bytes[o + 1]) / 127f;
                var tz = ((sbyte)bytes[o + 2]) / 127f;
                var t = new Vector3(tx, ty, tz);
                tangents[i] = t.LengthSquared() < 1e-8f ? Vector3.Zero : Vector3.Normalize(t);
            }
        }

        if (elementSize[4] > 0)
        {
            var colorOffset = (int)elementPos[4];
            var colorCount = Math.Min(vertexCount, (int)elementSize[4] / 4);
            for (var i = 0; i < colorCount; i++)
            {
                var o = colorOffset + i * 4;
                parameters[i] = new Vector4(bytes[o + 0] / 255f, bytes[o + 1] / 255f, bytes[o + 2] / 255f, bytes[o + 3] / 255f);
            }
        }

        if (elementSize[5] <= 0)
            return Fail("Shape index buffer is empty.");

        var indexOffset = (int)elementPos[5];
        var indexCount = checked((int)elementSize[5]);
        var indices = new int[indexCount];
        for (var i = 0; i < indexCount; i++)
        {
            var index = ManagedFflResourceArchive.ReadU16(bytes, indexOffset + i * 2, bigEndian);
            if (index >= vertexCount)
                return Fail("Shape index references a vertex outside the decoded position buffer.");
            indices[i] = index;
        }

        FacelineTransform? facelineTransform = null;
        if (partType == 3 && bytes.Length >= 0x48 + 0x24)
        {
            const int o = 0x48;
            facelineTransform = new FacelineTransform(
                new Vector3(
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x00, bigEndian),
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x04, bigEndian),
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x08, bigEndian)
                ),
                new Vector3(
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x0C, bigEndian),
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x10, bigEndian),
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x14, bigEndian)
                ),
                new Vector3(
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x18, bigEndian),
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x1C, bigEndian),
                    ManagedFflResourceArchive.ReadF32(bytes, o + 0x20, bigEndian)
                )
            );
        }

        return new DecodedShape(positions, texcoords, normals, tangents, parameters, indices, facelineTransform);
    }

    private static FflNativeInterop.FFLModulateParam CreateModulate(
        RenderAllocationTracker arena,
        int mode,
        int type,
        Vector4? colorR,
        Vector4? colorG,
        Vector4? colorB,
        IntPtr texture
    )
    {
        return new FflNativeInterop.FFLModulateParam
        {
            mode = mode,
            type = type,
            pColorR = colorR.HasValue ? arena.AllocColor(colorR.Value) : IntPtr.Zero,
            pColorG = colorG.HasValue ? arena.AllocColor(colorG.Value) : IntPtr.Zero,
            pColorB = colorB.HasValue ? arena.AllocColor(colorB.Value) : IntPtr.Zero,
            pTexture2D = texture,
        };
    }

    private static FflNativeInterop.FFLDrawParam CreateFullScreenOverlayDrawParam(
        RenderAllocationTracker arena,
        FflNativeInterop.FFLModulateParam modulate
    )
    {
        var positions = new[] { new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f) };
        var texcoords = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        var normals = Enumerable.Repeat(new Vector3(0f, 0f, 1f), 4).ToArray();
        var tangents = new Vector3[4];
        var parameters = Enumerable.Repeat(new Vector4(1f, 1f, 0f, 1f), 4).ToArray();
        var indices = new[] { 0, 1, 2, 2, 1, 3 };
        var shape = new DecodedShape(positions, texcoords, normals, tangents, parameters, indices, null);
        return BuildManagedShapeDrawParam(shape, 1f, 1f, null, false, FflNativeInterop.FflCullNone, modulate, arena).Value;
    }

    private static FflNativeInterop.FFLDrawParam CreateRawMaskOverlayDrawParam(
        RenderAllocationTracker arena,
        RawMaskPartDescriptor desc,
        FflNativeInterop.FFLModulateParam modulate
    )
    {
        var posXAdd = desc.Origin switch
        {
            RawMaskOrigin.Center => -0.5f,
            RawMaskOrigin.Left => -1f,
            _ => 0f,
        };
        var tex01 = desc.Origin == RawMaskOrigin.Right ? 0f : 1f;
        var tex23 = desc.Origin == RawMaskOrigin.Right ? 1f : 0f;

        Span<float> baseX = stackalloc float[4] { 1f, 1f, 0f, 0f };
        Span<float> baseY = stackalloc float[4] { -0.5f, 0.5f, 0.5f, -0.5f };
        Span<float> uvY = stackalloc float[4] { 0f, 1f, 1f, 0f };

        var rad = desc.RotationDegrees * (MathF.PI / 180f);
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);
        const float texScaleX = 0.88961464f;
        const float texScaleY = 0.9276675f;

        var positions = new Vector3[4];
        var texcoords = new Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            var lx = baseX[i] + posXAdd;
            var ly = baseY[i];
            var xr = lx * desc.Scale.X * cos - ly * desc.Scale.Y * sin;
            var yr = lx * desc.Scale.X * sin + ly * desc.Scale.Y * cos;
            var xw = texScaleX * xr + desc.Position.X;
            var yw = texScaleY * yr + desc.Position.Y;

            var ndcX = xw * (2f / 64f) - 1f;
            var ndcY = 1f - yw * (2f / 64f);
            positions[i] = new Vector3(ndcX, ndcY, 0f);

            var tx = i < 2 ? tex01 : tex23;
            texcoords[i] = new Vector2(tx, uvY[i]);
        }

        var normals = Enumerable.Repeat(new Vector3(0f, 0f, 1f), 4).ToArray();
        var tangents = new Vector3[4];
        var parameters = Enumerable.Repeat(new Vector4(1f, 1f, 0f, 1f), 4).ToArray();
        var indices = new[] { 2, 1, 3, 1, 3, 0 };
        var shape = new DecodedShape(positions, texcoords, normals, tangents, parameters, indices, null);
        return BuildManagedShapeDrawParam(shape, 1f, 1f, null, false, FflNativeInterop.FflCullNone, modulate, arena).Value;
    }

    private static RawMaskParts BuildRawMaskParts(FflNativeInterop.FFLiCharInfo charInfo)
    {
        const float posXAdd = 3.5323312f;
        const float posYAdd = 4.629278f;
        const float spacingMul = 0.88961464f;
        const float posXMul = 1.7792293f;
        const float posYMul = 1.0760943f;
        var posYAddEye = posYAdd + 13.822246f;
        var posYAddEyebrow = posYAdd + 11.920528f;
        var posYAddMouth = posYAdd + 24.629572f;
        var posYAddMustache = posYAdd + 27.134275f;
        var posXAddMole = posXAdd + 14.233834f;
        var posYAddMole = posYAdd + 11.178394f + 2f * posYMul;

        var eyeSpacingX = charInfo.parts.eyeSpacingX * spacingMul;
        var eyeBaseScale = 0.4f * charInfo.parts.eyeScale + 1f;
        var eyeBaseScaleY = 0.12f * charInfo.parts.eyeScaleY + 0.64f;
        var eyeScaleX = 5.34375f * eyeBaseScale;
        var eyeScaleY = 4.5f * eyeBaseScale * eyeBaseScaleY;
        var eyePosY = charInfo.parts.eyePositionY * posYMul + posYAddEye;
        var eyeBaseRotate = charInfo.parts.eyeRotate + EyeRotateOffset(charInfo.parts.eyeType);
        var eyeRotate = (eyeBaseRotate % 32) * (360f / 32f);

        var browSpacingX = charInfo.parts.eyebrowSpacingX * spacingMul;
        var browBaseScale = 0.4f * charInfo.parts.eyebrowScale + 1f;
        var browBaseScaleY = 0.12f * charInfo.parts.eyebrowScaleY + 0.64f;
        var browScaleX = 5.0625f * browBaseScale;
        var browScaleY = 4.5f * browBaseScale * browBaseScaleY;
        var browPosY = charInfo.parts.eyebrowPositionY * posYMul + posYAddEyebrow;
        var browBaseRotate = charInfo.parts.eyebrowRotate + EyebrowRotateOffset(charInfo.parts.eyebrowType);
        var browRotate = (browBaseRotate % 32) * (360f / 32f);

        var mouthBaseScale = 0.4f * charInfo.parts.mouthScale + 1f;
        var mouthBaseScaleY = 0.12f * charInfo.parts.mouthScaleY + 0.64f;
        var mouthScaleX = 6.1875f * mouthBaseScale;
        var mouthScaleY = 4.5f * mouthBaseScale * mouthBaseScaleY;
        var mouthPosY = charInfo.parts.mouthPositionY * posYMul + posYAddMouth;

        var mustacheBaseScale = 0.4f * charInfo.parts.mustacheScale + 1f;
        var mustacheScaleX = 4.5f * mustacheBaseScale;
        var mustacheScaleY = 9.0f * mustacheBaseScale;
        var mustachePosY = charInfo.parts.mustachePositionY * posYMul + posYAddMustache;

        var moleScale = 0.4f * charInfo.parts.moleScale + 1f;
        var molePosX = charInfo.parts.molePositionX * posXMul + posXAddMole;
        var molePosY = charInfo.parts.molePositionY * posYMul + posYAddMole;

        return new RawMaskParts(
            EyeR: new RawMaskPartDescriptor(
                new Vector2(32 - eyeSpacingX, eyePosY),
                new Vector2(eyeScaleX, eyeScaleY),
                eyeRotate,
                RawMaskOrigin.Left
            ),
            EyeL: new RawMaskPartDescriptor(
                new Vector2(eyeSpacingX + 32, eyePosY),
                new Vector2(eyeScaleX, eyeScaleY),
                360f - eyeRotate,
                RawMaskOrigin.Right
            ),
            EyebrowR: new RawMaskPartDescriptor(
                new Vector2(32 - browSpacingX, browPosY),
                new Vector2(browScaleX, browScaleY),
                browRotate,
                RawMaskOrigin.Left
            ),
            EyebrowL: new RawMaskPartDescriptor(
                new Vector2(browSpacingX + 32, browPosY),
                new Vector2(browScaleX, browScaleY),
                360f - browRotate,
                RawMaskOrigin.Right
            ),
            Mouth: new RawMaskPartDescriptor(new Vector2(32, mouthPosY), new Vector2(mouthScaleX, mouthScaleY), 0f, RawMaskOrigin.Center),
            MustacheR: new RawMaskPartDescriptor(
                new Vector2(32, mustachePosY),
                new Vector2(mustacheScaleX, mustacheScaleY),
                0f,
                RawMaskOrigin.Left
            ),
            MustacheL: new RawMaskPartDescriptor(
                new Vector2(32, mustachePosY),
                new Vector2(mustacheScaleX, mustacheScaleY),
                0f,
                RawMaskOrigin.Right
            ),
            Mole: new RawMaskPartDescriptor(new Vector2(molePosX, molePosY), new Vector2(moleScale, moleScale), 0f, RawMaskOrigin.Center)
        );
    }

    private static int EyeTextureIndex(FflNativeInterop.FFLiCharInfo charInfo, int type) =>
        type switch
        {
            0 or 2 => charInfo.parts.eyeType,
            1 => 60,
            3 => 61,
            4 => 26,
            5 => 47,
            _ => charInfo.parts.eyeType,
        };

    private static int MouthTextureIndex(FflNativeInterop.FFLiCharInfo charInfo, int type) =>
        type switch
        {
            0 => charInfo.parts.mouthType,
            1 => 10,
            2 => 12,
            3 => 36,
            5 => 19,
            _ => charInfo.parts.mouthType,
        };

    private static int EyebrowTextureIndex(FflNativeInterop.FFLiCharInfo charInfo, int type) =>
        type == 0 ? charInfo.parts.eyebrowType : type;

    private static bool ShouldUseEyeTextureDirect(int eyeIndex) =>
        eyeIndex is 60 or 62 or 65 or 69 or 70 or 71 or 72 or 73 or 74 or 75 or 78 or 79;

    private static int EyeRotateOffset(int type)
    {
        ReadOnlySpan<byte> rotate =
        [
            3,
            4,
            4,
            4,
            3,
            4,
            4,
            4,
            3,
            4,
            4,
            4,
            4,
            3,
            3,
            4,
            4,
            4,
            3,
            3,
            4,
            3,
            4,
            3,
            3,
            4,
            3,
            4,
            4,
            3,
            4,
            4,
            4,
            3,
            3,
            3,
            4,
            4,
            3,
            3,
            3,
            4,
            4,
            3,
            3,
            3,
            3,
            3,
            3,
            3,
            4,
            4,
            4,
            4,
            3,
            4,
            4,
            3,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
            4,
        ];
        var clamped = Math.Clamp(type, 0, rotate.Length - 1);
        return 32 - rotate[clamped];
    }

    private static int EyebrowRotateOffset(int type)
    {
        ReadOnlySpan<byte> rotate = [6, 6, 5, 7, 6, 7, 6, 7, 4, 7, 6, 8, 5, 5, 6, 6, 7, 7, 6, 6, 5, 6, 7, 5, 6, 6, 6, 6];
        var clamped = Math.Clamp(type, 0, rotate.Length - 1);
        return 32 - rotate[clamped];
    }

    private static byte[] EncodePositions(Vector3[] positions)
    {
        var bytes = new byte[positions.Length * 12];
        for (var i = 0; i < positions.Length; i++)
        {
            var o = i * 12;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 0, 4), BitConverter.SingleToInt32Bits(positions[i].X));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 4, 4), BitConverter.SingleToInt32Bits(positions[i].Y));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 8, 4), BitConverter.SingleToInt32Bits(positions[i].Z));
        }

        return bytes;
    }

    private static byte[] EncodeTexcoords(Vector2[] texcoords)
    {
        var bytes = new byte[texcoords.Length * 8];
        for (var i = 0; i < texcoords.Length; i++)
        {
            var o = i * 8;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 0, 4), BitConverter.SingleToInt32Bits(texcoords[i].X));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o + 4, 4), BitConverter.SingleToInt32Bits(texcoords[i].Y));
        }

        return bytes;
    }

    private static byte[] EncodeNormalsInt10(Vector3[] normals)
    {
        var bytes = new byte[normals.Length * 4];
        for (var i = 0; i < normals.Length; i++)
        {
            static int Pack10(float value)
            {
                var clamped = Math.Clamp(value, -1f, 1f);
                var signed = (int)MathF.Round(clamped * 511f);
                if (signed < -512)
                    signed = -512;
                if (signed > 511)
                    signed = 511;
                return signed & 0x3FF;
            }

            var x = Pack10(normals[i].X);
            var y = Pack10(normals[i].Y);
            var z = Pack10(normals[i].Z);
            var packed = x | (y << 10) | (z << 20);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), packed);
        }

        return bytes;
    }

    private static byte[] EncodeTangentsSnorm8(Vector3[] tangents)
    {
        var bytes = new byte[tangents.Length * 4];
        for (var i = 0; i < tangents.Length; i++)
        {
            static sbyte Pack(float v)
            {
                var clamped = Math.Clamp(v, -1f, 1f);
                return (sbyte)Math.Clamp((int)MathF.Round(clamped * 127f), -127, 127);
            }

            var o = i * 4;
            bytes[o + 0] = unchecked((byte)Pack(tangents[i].X));
            bytes[o + 1] = unchecked((byte)Pack(tangents[i].Y));
            bytes[o + 2] = unchecked((byte)Pack(tangents[i].Z));
            bytes[o + 3] = 0;
        }

        return bytes;
    }

    private static byte[] EncodeParameters(Vector4[] parameters)
    {
        var bytes = new byte[parameters.Length * 4];
        for (var i = 0; i < parameters.Length; i++)
        {
            var o = i * 4;
            bytes[o + 0] = (byte)Math.Clamp((int)MathF.Round(Clamp01(parameters[i].X) * 255f), 0, 255);
            bytes[o + 1] = (byte)Math.Clamp((int)MathF.Round(Clamp01(parameters[i].Y) * 255f), 0, 255);
            bytes[o + 2] = (byte)Math.Clamp((int)MathF.Round(Clamp01(parameters[i].Z) * 255f), 0, 255);
            bytes[o + 3] = (byte)Math.Clamp((int)MathF.Round(Clamp01(parameters[i].W) * 255f), 0, 255);
        }

        return bytes;
    }

    private static Vector4 GetFacelineColor(int index)
    {
        ReadOnlySpan<Vector4> colors =
        [
            new(1.000f, 0.827f, 0.678f, 1.000f),
            new(1.000f, 0.714f, 0.420f, 1.000f),
            new(0.870f, 0.475f, 0.259f, 1.000f),
            new(1.000f, 0.667f, 0.549f, 1.000f),
            new(0.678f, 0.318f, 0.161f, 1.000f),
            new(0.388f, 0.173f, 0.094f, 1.000f),
        ];
        return colors[Math.Clamp(index, 0, colors.Length - 1)];
    }

    private static readonly Vector4[] CommonColorSrgb =
    [
        new(0.1764706f, 0.1568628f, 0.1568628f, 1f), // 0
        new(0.2509804f, 0.1254902f, 0.0627451f, 1f), // 1
        new(0.3607844f, 0.0941177f, 0.0392157f, 1f), // 2
        new(0.4862746f, 0.2274510f, 0.0784314f, 1f), // 3
        new(0.4705883f, 0.4705883f, 0.5019608f, 1f), // 4
        new(0.3058824f, 0.2431373f, 0.0627451f, 1f), // 5
        new(0.5333334f, 0.3450981f, 0.0941177f, 1f), // 6
        new(0.8156863f, 0.6274510f, 0.2901961f, 1f), // 7
        new(0.0000000f, 0.0000000f, 0.0000000f, 1f), // 8
        new(0.4235295f, 0.4392157f, 0.4392157f, 1f), // 9
        new(0.4000000f, 0.2352942f, 0.1725491f, 1f), // 10
        new(0.3764706f, 0.3686275f, 0.1882353f, 1f), // 11
        new(0.2745099f, 0.3294118f, 0.6588236f, 1f), // 12
        new(0.2196079f, 0.4392157f, 0.3450981f, 1f), // 13
        new(0.3764706f, 0.2196079f, 0.0627451f, 1f), // 14
        new(0.6588236f, 0.0627451f, 0.0313726f, 1f), // 15
        new(0.1254902f, 0.1882353f, 0.4078432f, 1f), // 16
        new(0.6588236f, 0.3764706f, 0.0000000f, 1f), // 17
        new(0.4705883f, 0.4392157f, 0.4078432f, 1f), // 18
        new(0.8470589f, 0.3215687f, 0.0313726f, 1f), // 19
        new(0.9411765f, 0.0470589f, 0.0313726f, 1f), // 20
        new(0.9607844f, 0.2823530f, 0.2823530f, 1f), // 21
        new(0.9411765f, 0.6039216f, 0.4549020f, 1f), // 22
        new(0.5490197f, 0.3137255f, 0.2509804f, 1f), // 23
    ];

    private static readonly Vector4[] UpperLipCommonColorSrgb =
    [
        new(0.0901961f, 0.0784314f, 0.0784314f, 1f), // 0
        new(0.1254902f, 0.0627451f, 0.0313726f, 1f), // 1
        new(0.1803922f, 0.0470589f, 0.0196079f, 1f), // 2
        new(0.2901961f, 0.1372550f, 0.0470589f, 1f), // 3
        new(0.3294118f, 0.3294118f, 0.3529412f, 1f), // 4
        new(0.1529412f, 0.1215687f, 0.0313726f, 1f), // 5
        new(0.3215687f, 0.2078432f, 0.0549020f, 1f), // 6
        new(0.6941177f, 0.5019608f, 0.1568628f, 1f), // 7
        new(0.0000000f, 0.0000000f, 0.0000000f, 1f), // 8
        new(0.2980393f, 0.3058824f, 0.3058824f, 1f), // 9
        new(0.2000000f, 0.1176471f, 0.0862746f, 1f), // 10
        new(0.2274510f, 0.2196079f, 0.1137255f, 1f), // 11
        new(0.1647059f, 0.1960785f, 0.3960785f, 1f), // 12
        new(0.1529412f, 0.3058824f, 0.2431373f, 1f), // 13
        new(0.1882353f, 0.1098040f, 0.0313726f, 1f), // 14
        new(0.3960785f, 0.0392157f, 0.0196079f, 1f), // 15
        new(0.0627451f, 0.0941177f, 0.2039216f, 1f), // 16
        new(0.4627451f, 0.2627451f, 0.0000000f, 1f), // 17
        new(0.3294118f, 0.3058824f, 0.2862746f, 1f), // 18
        new(0.5098040f, 0.1882353f, 0.0941177f, 1f), // 19
        new(0.4705883f, 0.0470589f, 0.0470589f, 1f), // 20
        new(0.5333334f, 0.1254902f, 0.1568628f, 1f), // 21
        new(0.8627451f, 0.4705883f, 0.3137255f, 1f), // 22
        new(0.2745099f, 0.1176471f, 0.0392157f, 1f), // 23
    ];

    private static bool TryGetCommonColorSrgb(int encodedIndex, out Vector4 color)
    {
        const int commonMask = unchecked((int)FflNativeInterop.CommonColorEnableMask);
        if ((encodedIndex & commonMask) == 0)
        {
            color = Vector4.One;
            return false;
        }

        var index = encodedIndex & 0xFF;
        if ((uint)index >= (uint)CommonColorSrgb.Length)
        {
            color = CommonColorSrgb[0];
            return true;
        }

        color = CommonColorSrgb[index];
        return true;
    }

    private static bool TryGetUpperLipCommonColorSrgb(int encodedIndex, out Vector4 color)
    {
        const int commonMask = unchecked((int)FflNativeInterop.CommonColorEnableMask);
        if ((encodedIndex & commonMask) == 0)
        {
            color = Vector4.One;
            return false;
        }

        var index = encodedIndex & 0xFF;
        if ((uint)index >= (uint)UpperLipCommonColorSrgb.Length)
        {
            color = UpperLipCommonColorSrgb[0];
            return true;
        }

        color = UpperLipCommonColorSrgb[index];
        return true;
    }

    private static Vector4 GetHairColor(int encodedIndex)
    {
        if (TryGetCommonColorSrgb(encodedIndex, out var commonColor))
            return commonColor;

        ReadOnlySpan<Vector4> colors =
        [
            new(0.118f, 0.102f, 0.094f, 1.000f),
            new(0.251f, 0.125f, 0.063f, 1.000f),
            new(0.361f, 0.094f, 0.039f, 1.000f),
            new(0.486f, 0.227f, 0.078f, 1.000f),
            new(0.471f, 0.471f, 0.502f, 1.000f),
            new(0.306f, 0.243f, 0.063f, 1.000f),
            new(0.533f, 0.345f, 0.094f, 1.000f),
            new(0.816f, 0.627f, 0.290f, 1.000f),
        ];
        return ResolvePaletteColor(encodedIndex, colors);
    }

    private static Vector4 GetGlassColor(int encodedIndex)
    {
        if (TryGetCommonColorSrgb(encodedIndex, out var commonColor))
            return commonColor;

        ReadOnlySpan<Vector4> colors =
        [
            new(0.094f, 0.094f, 0.094f, 1.000f),
            new(0.376f, 0.219f, 0.062f, 1.000f),
            new(0.658f, 0.062f, 0.031f, 1.000f),
            new(0.125f, 0.188f, 0.407f, 1.000f),
            new(0.658f, 0.376f, 0.000f, 1.000f),
            new(0.470f, 0.439f, 0.407f, 1.000f),
        ];
        return ResolvePaletteColor(encodedIndex, colors);
    }

    private static Vector4 GetEyeColorB(int encodedIndex)
    {
        if (TryGetCommonColorSrgb(encodedIndex, out var commonColor))
            return commonColor;

        ReadOnlySpan<Vector4> colors =
        [
            new(0.000f, 0.000f, 0.000f, 1.000f),
            new(0.424f, 0.439f, 0.439f, 1.000f),
            new(0.400f, 0.235f, 0.173f, 1.000f),
            new(0.376f, 0.369f, 0.188f, 1.000f),
            new(0.275f, 0.329f, 0.659f, 1.000f),
            new(0.220f, 0.439f, 0.345f, 1.000f),
        ];
        return ResolvePaletteColor(encodedIndex, colors);
    }

    private static Vector4 GetMouthColorR(int encodedIndex)
    {
        if (TryGetCommonColorSrgb(encodedIndex, out var commonColor))
            return commonColor;

        ReadOnlySpan<Vector4> colors =
        [
            new(0.847f, 0.322f, 0.031f, 1.000f),
            new(0.941f, 0.047f, 0.031f, 1.000f),
            new(0.961f, 0.282f, 0.282f, 1.000f),
            new(0.941f, 0.604f, 0.455f, 1.000f),
            new(0.549f, 0.314f, 0.251f, 1.000f),
        ];
        return ResolvePaletteColor(encodedIndex, colors);
    }

    private static Vector4 GetMouthColorG(int encodedIndex)
    {
        if (TryGetUpperLipCommonColorSrgb(encodedIndex, out var commonColor))
            return commonColor;

        ReadOnlySpan<Vector4> colors =
        [
            new(0.510f, 0.188f, 0.094f, 1.000f),
            new(0.471f, 0.047f, 0.047f, 1.000f),
            new(0.533f, 0.125f, 0.157f, 1.000f),
            new(0.863f, 0.471f, 0.314f, 1.000f),
            new(0.275f, 0.118f, 0.039f, 1.000f),
        ];
        return ResolvePaletteColor(encodedIndex, colors);
    }

    private static Vector4 GetFavoriteColor(int index)
    {
        ReadOnlySpan<Vector4> colors =
        [
            new(0.824f, 0.118f, 0.078f, 1.000f),
            new(1.000f, 0.431f, 0.098f, 1.000f),
            new(1.000f, 0.847f, 0.125f, 1.000f),
            new(0.471f, 0.824f, 0.125f, 1.000f),
            new(0.000f, 0.471f, 0.188f, 1.000f),
            new(0.039f, 0.282f, 0.706f, 1.000f),
            new(0.235f, 0.667f, 0.871f, 1.000f),
            new(0.961f, 0.353f, 0.490f, 1.000f),
            new(0.451f, 0.157f, 0.678f, 1.000f),
            new(0.282f, 0.220f, 0.094f, 1.000f),
            new(0.878f, 0.878f, 0.878f, 1.000f),
            new(0.094f, 0.094f, 0.078f, 1.000f),
        ];
        return colors[Math.Clamp(index, 0, colors.Length - 1)];
    }

    private static Vector4 ResolvePaletteColor(int encodedIndex, ReadOnlySpan<Vector4> palette)
    {
        const int commonMask = unchecked((int)0x80000000);
        int index;
        if ((encodedIndex & commonMask) != 0)
            index = encodedIndex & 0xFF;
        else
            index = encodedIndex;

        if (palette.Length == 0)
            return Vector4.One;

        return palette[Math.Clamp(index, 0, palette.Length - 1)];
    }

    private static readonly EyeMouthTypeElement[] MaskExpressionElements =
    [
        new(0, 0, 0, 0),
        new(1, 1, 0, 0),
        new(0, 0, 1, 0),
        new(2, 2, 2, 0),
        new(3, 3, 0, 0),
        new(4, 4, 0, 0),
        new(0, 0, 3, 0),
        new(1, 1, 3, 0),
        new(0, 0, 3, 0),
        new(2, 2, 3, 0),
        new(3, 3, 3, 0),
        new(4, 4, 3, 0),
        new(5, 0, 0, 0),
        new(0, 5, 0, 0),
        new(5, 0, 3, 0),
        new(0, 5, 3, 0),
        new(5, 0, 5, 0),
        new(0, 5, 5, 0),
        new(5, 5, 2, 0),
    ];

    private static void RasterizeBodyInstance(
        byte[] target,
        float[] depth,
        int outputWidth,
        int outputHeight,
        int frameX,
        int frameWidth,
        int frameHeight,
        BodyRenderData bodyRenderData,
        Matrix4x4 baseRotationMatrix,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix
    )
    {
        var bodyModelMatrix = baseRotationMatrix * bodyRenderData.BodyScaleMatrix;
        foreach (var bodyMesh in bodyRenderData.Meshes)
        {
            var color = bodyMesh.IsPantsMesh ? bodyRenderData.PantsColor : bodyRenderData.BodyColor;
            var modulateType = bodyMesh.IsPantsMesh ? FflNativeInterop.ModulateTypeCustomPants : FflNativeInterop.ModulateTypeCustomBody;

            var mesh = PrepareBodyMesh(
                bodyMesh,
                frameX,
                frameWidth,
                frameHeight,
                bodyModelMatrix,
                viewMatrix,
                projectionMatrix,
                color,
                modulateType
            );
            if (mesh == null)
                continue;

            RasterizeMesh(target, depth, outputWidth, outputHeight, mesh, lightEnabled: true, BlendMode.Over);
        }
    }

    private static PreparedMesh? PrepareBodyMesh(
        BodyMeshData bodyMesh,
        int frameX,
        int frameWidth,
        int frameHeight,
        Matrix4x4 modelMatrix,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix,
        Vector4 color,
        int modulateType
    )
    {
        if (bodyMesh.Vertices.Length == 0 || bodyMesh.Indices.Length < 3)
            return null;

        var meshSrt = CreateBodyMeshSrt(bodyMesh.MeshScale, bodyMesh.MeshRotate, bodyMesh.MeshTranslate);
        var modelView = modelMatrix * viewMatrix;
        var vertices = new RasterVertex[bodyMesh.Vertices.Length];

        for (var i = 0; i < bodyMesh.Vertices.Length; i++)
        {
            var vertex = bodyMesh.Vertices[i];
            var meshPosition = Vector3.Transform(vertex.Position, meshSrt);
            var worldPosition = Vector3.Transform(meshPosition, modelMatrix);
            var viewPosition = Vector3.Transform(worldPosition, viewMatrix);
            var clip = Vector4.Transform(new Vector4(viewPosition, 1f), projectionMatrix);
            if (MathF.Abs(clip.W) <= 1e-6f)
                return null;

            var invW = 1f / clip.W;
            var ndc = new Vector3(clip.X * invW, clip.Y * invW, clip.Z * invW);
            var depthValue = ndc.Z * 0.5f + 0.5f;
            var screenX = frameX + (ndc.X * 0.5f + 0.5f) * frameWidth;
            var screenY = (1f - (ndc.Y * 0.5f + 0.5f)) * frameHeight;

            var meshNormal = Vector3.TransformNormal(vertex.Normal, meshSrt);
            var normal = Vector3.TransformNormal(meshNormal, modelView);
            vertices[i] = new RasterVertex(
                new Vector2(screenX, screenY),
                depthValue,
                invW,
                vertex.Texcoord,
                viewPosition,
                normal,
                Vector3.Zero,
                new Vector4(1f, 1f, 0f, 1f)
            );
        }

        var drawParam = new FflNativeInterop.FFLDrawParam
        {
            cullMode = FflNativeInterop.FflCullBack,
            modulateParam = new FflNativeInterop.FFLModulateParam { mode = 0, type = modulateType },
            primitiveParam = new FflNativeInterop.FFLPrimitiveParam
            {
                primitiveType = FflNativeInterop.PrimitiveTriangles,
                indexCount = (uint)bodyMesh.Indices.Length,
            },
        };
        var material = ResolveMaterial(modulateType);
        return new PreparedMesh(
            drawParam,
            vertices,
            bodyMesh.Indices,
            material,
            FflNativeInterop.ParameterModeDefault1,
            constantColor: color,
            hasTangent: false
        );
    }

    private static void RasterizeInstance(
        byte[] target,
        float[] depth,
        int outputWidth,
        int outputHeight,
        int frameX,
        int frameWidth,
        int frameHeight,
        IReadOnlyList<FflNativeInterop.FFLDrawParam> drawParams,
        Matrix4x4 modelMatrix,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix
    )
    {
        foreach (var drawParam in drawParams)
        {
            var mesh = PrepareMesh(drawParam, frameX, frameWidth, frameHeight, modelMatrix, viewMatrix, projectionMatrix);
            if (mesh == null)
                continue;

            RasterizeMesh(target, depth, outputWidth, outputHeight, mesh, lightEnabled: true, BlendMode.Over);
        }
    }

    private static PreparedMesh? PrepareMesh(
        FflNativeInterop.FFLDrawParam drawParam,
        int frameX,
        int frameWidth,
        int frameHeight,
        Matrix4x4 modelMatrix,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix
    )
    {
        var positions = ReadPositions(drawParam.attributeBufferParam.position);
        if (positions.Length == 0)
            return null;

        var texcoords = ReadTexcoords(drawParam.attributeBufferParam.texcoord, positions.Length);
        var normals = ReadNormals(drawParam.attributeBufferParam.normal, positions.Length);
        var tangents = ReadTangents(drawParam.attributeBufferParam.tangent, positions.Length);
        var parameterData = ReadVertexParameters(drawParam.attributeBufferParam.color, positions.Length);
        var indices = ReadIndices(drawParam.primitiveParam.pIndexBuffer, (int)drawParam.primitiveParam.indexCount);
        if (indices.Length < 3)
            return null;

        var modelView = modelMatrix * viewMatrix;
        var material = ResolveMaterial(drawParam.modulateParam.type);
        var vertices = new RasterVertex[positions.Length];

        for (var i = 0; i < positions.Length; i++)
        {
            var worldPosition = Vector3.Transform(positions[i], modelMatrix);
            var viewPosition = Vector3.Transform(worldPosition, viewMatrix);
            var clip = Vector4.Transform(new Vector4(viewPosition, 1f), projectionMatrix);
            if (MathF.Abs(clip.W) <= 1e-6f)
                return null;

            var invW = 1f / clip.W;
            var ndc = new Vector3(clip.X * invW, clip.Y * invW, clip.Z * invW);
            var depthValue = ndc.Z * 0.5f + 0.5f;
            var screenX = frameX + (ndc.X * 0.5f + 0.5f) * frameWidth;
            var screenY = (1f - (ndc.Y * 0.5f + 0.5f)) * frameHeight;

            var normal = Vector3.TransformNormal(normals[i], modelView);
            var tangent = Vector3.TransformNormal(tangents[i], modelView);

            vertices[i] = new RasterVertex(
                new Vector2(screenX, screenY),
                depthValue,
                invW,
                texcoords[i],
                viewPosition,
                normal,
                tangent,
                parameterData.Values[i]
            );
        }

        var hasTangent = drawParam.attributeBufferParam.tangent.ptr != IntPtr.Zero && drawParam.attributeBufferParam.tangent.stride > 0;
        return new PreparedMesh(drawParam, vertices, indices, material, parameterData.Mode, constantColor: null, hasTangent: hasTangent);
    }

    private static void RasterizeMesh(
        byte[] target,
        float[]? depth,
        int outputWidth,
        int outputHeight,
        PreparedMesh mesh,
        bool lightEnabled,
        BlendMode blendMode
    )
    {
        if (mesh.DrawParam.primitiveParam.primitiveType == FflNativeInterop.PrimitiveTriangles)
        {
            for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                RasterizeTriangle(
                    target,
                    depth,
                    outputWidth,
                    outputHeight,
                    mesh,
                    lightEnabled,
                    blendMode,
                    mesh.Indices[i],
                    mesh.Indices[i + 1],
                    mesh.Indices[i + 2]
                );
            return;
        }

        if (mesh.DrawParam.primitiveParam.primitiveType == FflNativeInterop.PrimitiveTriangleStrip)
        {
            for (var i = 0; i + 2 < mesh.Indices.Length; i++)
            {
                var a = mesh.Indices[i];
                var b = mesh.Indices[i + 1];
                var c = mesh.Indices[i + 2];
                if ((i & 1) == 1)
                    (b, c) = (c, b);
                RasterizeTriangle(target, depth, outputWidth, outputHeight, mesh, lightEnabled, blendMode, a, b, c);
            }
        }
    }

    private static void RasterizeTriangle(
        byte[] target,
        float[]? depth,
        int outputWidth,
        int outputHeight,
        PreparedMesh mesh,
        bool lightEnabled,
        BlendMode blendMode,
        int ia,
        int ib,
        int ic
    )
    {
        if ((uint)ia >= (uint)mesh.Vertices.Length || (uint)ib >= (uint)mesh.Vertices.Length || (uint)ic >= (uint)mesh.Vertices.Length)
            return;

        var a = mesh.Vertices[ia];
        var b = mesh.Vertices[ib];
        var c = mesh.Vertices[ic];

        var area = Cross2D(a.ScreenPosition, b.ScreenPosition, c.ScreenPosition);
        if (MathF.Abs(area) < 1e-6f)
            return;

        var cullMode = mesh.DrawParam.cullMode;
        if (cullMode == FflNativeInterop.FflCullBack && area >= 0f)
            return;
        if (cullMode == FflNativeInterop.FflCullFront && area <= 0f)
            return;

        var minX = Math.Clamp(
            (int)MathF.Floor(MathF.Min(a.ScreenPosition.X, MathF.Min(b.ScreenPosition.X, c.ScreenPosition.X))),
            0,
            outputWidth - 1
        );
        var maxX = Math.Clamp(
            (int)MathF.Ceiling(MathF.Max(a.ScreenPosition.X, MathF.Max(b.ScreenPosition.X, c.ScreenPosition.X))),
            0,
            outputWidth - 1
        );
        var minY = Math.Clamp(
            (int)MathF.Floor(MathF.Min(a.ScreenPosition.Y, MathF.Min(b.ScreenPosition.Y, c.ScreenPosition.Y))),
            0,
            outputHeight - 1
        );
        var maxY = Math.Clamp(
            (int)MathF.Ceiling(MathF.Max(a.ScreenPosition.Y, MathF.Max(b.ScreenPosition.Y, c.ScreenPosition.Y))),
            0,
            outputHeight - 1
        );

        var invArea = 1f / area;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var e0 = Cross2D(b.ScreenPosition, c.ScreenPosition, p);
                var e1 = Cross2D(c.ScreenPosition, a.ScreenPosition, p);
                var e2 = Cross2D(a.ScreenPosition, b.ScreenPosition, p);

                var hasNegative = e0 < 0f || e1 < 0f || e2 < 0f;
                var hasPositive = e0 > 0f || e1 > 0f || e2 > 0f;
                if (hasNegative && hasPositive)
                    continue;

                var w0 = e0 * invArea;
                var w1 = e1 * invArea;
                var w2 = e2 * invArea;

                var perspectiveDenominator = w0 * a.InvW + w1 * b.InvW + w2 * c.InvW;
                if (MathF.Abs(perspectiveDenominator) < 1e-8f)
                    continue;

                var depthValue = (w0 * a.Depth * a.InvW + w1 * b.Depth * b.InvW + w2 * c.Depth * c.InvW) / perspectiveDenominator;
                if (depthValue is < 0f or > 1f)
                    continue;

                var pixelIndex = y * outputWidth + x;
                if (depth != null)
                {
                    if (depthValue > depth[pixelIndex])
                        continue;
                }

                var uv = (w0 * a.Texcoord * a.InvW + w1 * b.Texcoord * b.InvW + w2 * c.Texcoord * c.InvW) / perspectiveDenominator;
                var viewPosition =
                    (w0 * a.ViewPosition * a.InvW + w1 * b.ViewPosition * b.InvW + w2 * c.ViewPosition * c.InvW) / perspectiveDenominator;
                var normal = (w0 * a.Normal * a.InvW + w1 * b.Normal * b.InvW + w2 * c.Normal * c.InvW) / perspectiveDenominator;
                var tangent = (w0 * a.Tangent * a.InvW + w1 * b.Tangent * b.InvW + w2 * c.Tangent * c.InvW) / perspectiveDenominator;
                var parameter =
                    (w0 * a.Parameter * a.InvW + w1 * b.Parameter * b.InvW + w2 * c.Parameter * c.InvW) / perspectiveDenominator;

                var color = EvaluateModulateColor(mesh, uv, viewPosition, normal, tangent, parameter, lightEnabled);
                if (color.W <= 0f)
                    continue;

                BlendPixel(target, outputWidth, x, y, color, blendMode);
                if (depth != null)
                    depth[pixelIndex] = depthValue;
            }
        }
    }

    private static Vector4 EvaluateModulateColor(
        PreparedMesh mesh,
        Vector2 uv,
        Vector3 viewPosition,
        Vector3 normal,
        Vector3 tangent,
        Vector4 parameter,
        bool lightEnabled
    )
    {
        Vector4 baseColor;
        if (mesh.ConstantColor.HasValue)
        {
            baseColor = mesh.ConstantColor.Value;
        }
        else
        {
            var modulate = mesh.DrawParam.modulateParam;
            var textureColor = SampleTexture(modulate.pTexture2D, uv);
            var colorR = ReadColor(modulate.pColorR);
            var colorG = ReadColor(modulate.pColorG);
            var colorB = ReadColor(modulate.pColorB);

            baseColor = modulate.mode switch
            {
                0 => new Vector4(colorR.X, colorR.Y, colorR.Z, 1f),
                1 => textureColor,
                2 => new Vector4(
                    textureColor.X * colorR.X + textureColor.Y * colorG.X + textureColor.Z * colorB.X,
                    textureColor.X * colorR.Y + textureColor.Y * colorG.Y + textureColor.Z * colorB.Y,
                    textureColor.X * colorR.Z + textureColor.Y * colorG.Z + textureColor.Z * colorB.Z,
                    textureColor.W
                ),
                3 => new Vector4(colorR.X, colorR.Y, colorR.Z, textureColor.X),
                4 => new Vector4(textureColor.Y * colorR.X, textureColor.Y * colorR.Y, textureColor.Y * colorR.Z, textureColor.X),
                5 => new Vector4(textureColor.X * colorR.X, textureColor.X * colorR.Y, textureColor.X * colorR.Z, 1f),
                _ => Vector4.One,
            };

            if (modulate.mode != 0 && baseColor.W <= 0f)
                return Vector4.Zero;
        }

        if (!lightEnabled)
            return new Vector4(Clamp01(baseColor.X), Clamp01(baseColor.Y), Clamp01(baseColor.Z), Clamp01(baseColor.W));

        var n = NormalizeOrDefault(normal, new Vector3(0f, 0f, 1f));
        var t = NormalizeOrDefault(tangent, new Vector3(1f, 0f, 0f));
        var eye = NormalizeOrDefault(-viewPosition, new Vector3(0f, 0f, 1f));
        var light = LightDirection;

        var ambient = Hadamard(LightAmbient, mesh.Material.Ambient);
        var diffuse = Hadamard(LightDiffuse, mesh.Material.Diffuse) * MathF.Max(Vector3.Dot(light, n), 0.1f);

        var specularPower = mesh.Material.SpecularPower;
        var blinn = MathF.Pow(MathF.Max(Vector3.Dot(Vector3.Reflect(-light, n), eye), 0f), specularPower);
        var specularMode = mesh.Material.SpecularMode;
        if (!mesh.HasTangent)
            specularMode = FflNativeInterop.SpecularModeBlinn;

        var strength = parameter.Y;
        float reflection;
        if (specularMode == FflNativeInterop.SpecularModeBlinn)
        {
            strength = 1f;
            reflection = blinn;
        }
        else
        {
            var dotLt = Vector3.Dot(light, t);
            var dotVt = Vector3.Dot(eye, t);
            var dotLn = MathF.Sqrt(MathF.Max(0f, 1f - dotLt * dotLt));
            var dotVr = dotLn * MathF.Sqrt(MathF.Max(0f, 1f - dotVt * dotVt)) - dotLt * dotVt;
            var anisotropic = MathF.Pow(MathF.Max(0f, dotVr), specularPower);
            reflection = anisotropic + (blinn - anisotropic) * parameter.X;
        }

        var specular = Hadamard(LightSpecular, mesh.Material.Specular) * reflection * strength;
        var rimFactor = MathF.Pow(MathF.Max(0f, parameter.W * (1f - MathF.Abs(n.Z))), RimPower);
        var rim = mesh.Material.RimColor * rimFactor;

        var lit = (ambient + diffuse) * new Vector3(baseColor.X, baseColor.Y, baseColor.Z) + specular + rim;
        return new Vector4(Clamp01(lit.X), Clamp01(lit.Y), Clamp01(lit.Z), Clamp01(baseColor.W));
    }

    private static Vector4 SampleTexture(IntPtr textureHandle, Vector2 uv)
    {
        if (textureHandle == IntPtr.Zero || textureHandle == FflNativeInterop.FflTexturePlaceholder)
            return new(1f, 1f, 1f, 1f);

        if (!TextureRegistry.TryGet(textureHandle, out var texture))
            return new(1f, 1f, 1f, 1f);

        if (texture.Width <= 1 || texture.Height <= 1)
            return ReadTextureTexel(texture, 0, 0);

        var u = MirrorRepeat(uv.X);
        var v = MirrorRepeat(uv.Y);
        var fx = u * (texture.Width - 1);
        var fy = v * (texture.Height - 1);

        var x0 = Math.Clamp((int)MathF.Floor(fx), 0, texture.Width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(fy), 0, texture.Height - 1);
        var x1 = Math.Min(x0 + 1, texture.Width - 1);
        var y1 = Math.Min(y0 + 1, texture.Height - 1);

        var tx = fx - x0;
        var ty = fy - y0;

        var c00 = ReadTextureTexel(texture, x0, y0);
        var c10 = ReadTextureTexel(texture, x1, y0);
        var c01 = ReadTextureTexel(texture, x0, y1);
        var c11 = ReadTextureTexel(texture, x1, y1);

        var c0 = Vector4.Lerp(c00, c10, tx);
        var c1 = Vector4.Lerp(c01, c11, tx);
        return Vector4.Lerp(c0, c1, ty);
    }

    private static float MirrorRepeat(float value)
    {
        var wrapped = value % 2f;
        if (wrapped < 0f)
            wrapped += 2f;
        return wrapped <= 1f ? wrapped : 2f - wrapped;
    }

    private static Vector4 ReadTextureTexel(TextureData texture, int x, int y)
    {
        var index = (y * texture.Width + x) * texture.PixelStride;
        if (index < 0 || index + texture.PixelStride > texture.Pixels.Length)
            return Vector4.One;

        return texture.Format switch
        {
            FflNativeInterop.TextureFormatR8 => new Vector4(
                texture.Pixels[index] / 255f,
                texture.Pixels[index] / 255f,
                texture.Pixels[index] / 255f,
                1f
            ),
            FflNativeInterop.TextureFormatRg8 => new Vector4(texture.Pixels[index] / 255f, texture.Pixels[index + 1] / 255f, 0f, 1f),
            FflNativeInterop.TextureFormatRgba8 => new Vector4(
                texture.Pixels[index] / 255f,
                texture.Pixels[index + 1] / 255f,
                texture.Pixels[index + 2] / 255f,
                texture.Pixels[index + 3] / 255f
            ),
            _ => Vector4.One,
        };
    }

    private static Vector4 ReadColor(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return new(1f, 1f, 1f, 1f);

        var color = Marshal.PtrToStructure<FflNativeInterop.FFLColor>(pointer);
        return new(color.r, color.g, color.b, color.a);
    }

    private static Vector3[] ReadPositions(FflNativeInterop.FFLAttributeBuffer buffer)
    {
        if (buffer.ptr == IntPtr.Zero || buffer.stride == 0 || buffer.size == 0)
            return [];

        var vertexCount = (int)(buffer.size / buffer.stride);
        if (vertexCount <= 0)
            return [];

        var bytes = new byte[buffer.size];
        Marshal.Copy(buffer.ptr, bytes, 0, bytes.Length);

        var output = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var o = (int)(i * buffer.stride);
            if (buffer.stride >= 12)
            {
                output[i] = new(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
            }
            else if (buffer.stride >= 6)
            {
                output[i] = new(
                    (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, o)),
                    (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, o + 2)),
                    (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, o + 4))
                );
            }
            else
            {
                output[i] = Vector3.Zero;
            }
        }

        return output;
    }

    private static Vector2[] ReadTexcoords(FflNativeInterop.FFLAttributeBuffer buffer, int fallbackVertexCount)
    {
        var output = new Vector2[fallbackVertexCount];

        if (buffer.ptr == IntPtr.Zero || buffer.stride == 0 || buffer.size == 0)
            return output;

        var vertexCount = Math.Min((int)(buffer.size / buffer.stride), fallbackVertexCount);
        if (vertexCount <= 0)
            return output;

        var bytes = new byte[buffer.size];
        Marshal.Copy(buffer.ptr, bytes, 0, bytes.Length);

        for (var i = 0; i < vertexCount; i++)
        {
            var o = (int)(i * buffer.stride);
            if (buffer.stride >= 8)
            {
                output[i] = new(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4));
            }
            else if (buffer.stride >= 4)
            {
                output[i] = new(
                    (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, o)),
                    (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, o + 2))
                );
            }
        }

        return output;
    }

    private static Vector3[] ReadNormals(FflNativeInterop.FFLAttributeBuffer buffer, int fallbackVertexCount)
    {
        var output = Enumerable.Repeat(new Vector3(0f, 0f, 1f), fallbackVertexCount).ToArray();

        if (buffer.ptr == IntPtr.Zero || buffer.stride == 0 || buffer.size == 0)
            return output;

        var vertexCount = Math.Min((int)(buffer.size / buffer.stride), fallbackVertexCount);
        if (vertexCount <= 0)
            return output;

        var bytes = new byte[buffer.size];
        Marshal.Copy(buffer.ptr, bytes, 0, bytes.Length);

        for (var i = 0; i < vertexCount; i++)
        {
            var o = (int)(i * buffer.stride);
            if (buffer.stride >= 4)
            {
                var packed = BitConverter.ToInt32(bytes, o);
                output[i] = DecodeInt2101010(packed);
            }
        }

        return output;
    }

    private static Vector3[] ReadTangents(FflNativeInterop.FFLAttributeBuffer buffer, int fallbackVertexCount)
    {
        var output = Enumerable.Repeat(new Vector3(0f, 0f, 0f), fallbackVertexCount).ToArray();

        if (buffer.ptr == IntPtr.Zero || buffer.stride == 0 || buffer.size == 0)
            return output;

        var vertexCount = Math.Min((int)(buffer.size / buffer.stride), fallbackVertexCount);
        if (vertexCount <= 0)
            return output;

        var bytes = new byte[buffer.size];
        Marshal.Copy(buffer.ptr, bytes, 0, bytes.Length);

        for (var i = 0; i < vertexCount; i++)
        {
            var o = (int)(i * buffer.stride);
            if (o + 2 >= bytes.Length)
                continue;

            var x = ((sbyte)bytes[o + 0]) / 127f;
            var y = ((sbyte)bytes[o + 1]) / 127f;
            var z = ((sbyte)bytes[o + 2]) / 127f;
            var tangent = new Vector3(x, y, z);
            output[i] = tangent.LengthSquared() < 1e-8f ? Vector3.Zero : Vector3.Normalize(tangent);
        }

        return output;
    }

    private static VertexParameterData ReadVertexParameters(FflNativeInterop.FFLAttributeBuffer buffer, int fallbackVertexCount)
    {
        var values = new Vector4[fallbackVertexCount];
        var defaultParameter = new Vector4(1f, 1f, 0f, 1f);
        Array.Fill(values, defaultParameter);

        if (buffer.ptr == IntPtr.Zero || buffer.size == 0)
            return new VertexParameterData(values, FflNativeInterop.ParameterModeDefault1);

        if (buffer.stride == 0)
        {
            var first = Marshal.ReadByte(buffer.ptr);
            var isMode2 = first == 0;
            var constant = isMode2 ? new Vector4(0f, 1f, 0f, 1f) : defaultParameter;
            Array.Fill(values, constant);
            return new VertexParameterData(
                values,
                isMode2 ? FflNativeInterop.ParameterModeDefault2 : FflNativeInterop.ParameterModeDefault1
            );
        }

        if (buffer.stride < 4)
            return new VertexParameterData(values, FflNativeInterop.ParameterModeDefault1);

        var vertexCount = Math.Min((int)(buffer.size / buffer.stride), fallbackVertexCount);
        if (vertexCount <= 0)
            return new VertexParameterData(values, FflNativeInterop.ParameterModeDefault1);

        var bytes = new byte[buffer.size];
        Marshal.Copy(buffer.ptr, bytes, 0, bytes.Length);

        for (var i = 0; i < vertexCount; i++)
        {
            var o = (int)(i * buffer.stride);
            if (o + 3 >= bytes.Length)
                continue;

            values[i] = new Vector4(bytes[o + 0] / 255f, bytes[o + 1] / 255f, bytes[o + 2] / 255f, bytes[o + 3] / 255f);
        }

        return new VertexParameterData(values, FflNativeInterop.ParameterModeColor);
    }

    private static int[] ReadIndices(IntPtr indexBuffer, int indexCount)
    {
        if (indexBuffer == IntPtr.Zero || indexCount <= 0)
            return [];

        var indices16 = new short[indexCount];
        Marshal.Copy(indexBuffer, indices16, 0, indexCount);

        var output = new int[indexCount];
        for (var i = 0; i < indexCount; i++)
            output[i] = (ushort)indices16[i];

        return output;
    }

    private static Vector3 DecodeInt2101010(int packed)
    {
        static int SignExtend10(int value)
        {
            var signed = value & 0x3FF;
            if ((signed & 0x200) != 0)
                signed -= 0x400;
            return signed;
        }

        var x = SignExtend10(packed & 0x3FF) / 511f;
        var y = SignExtend10((packed >> 10) & 0x3FF) / 511f;
        var z = SignExtend10((packed >> 20) & 0x3FF) / 511f;
        var n = new Vector3(x, y, z);
        return n.LengthSquared() < 1e-5f ? new(0f, 0f, 1f) : Vector3.Normalize(n);
    }

    private static float Cross2D(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static MaterialInfo ResolveMaterial(int modulateType)
    {
        if ((uint)modulateType >= (uint)MaterialTable.Length)
            return MaterialTable[FflNativeInterop.ModulateTypeShapeFaceline];
        return MaterialTable[modulateType];
    }

    private static ViewParameters ResolveViewParameters(NativeMiiRenderRequest request, FflNativeInterop.FFLiCharInfo charInfo)
    {
        static Matrix4x4 BuildProjection(float near, float far, float fovyDegrees, float aspect) =>
            Matrix4x4.CreatePerspectiveFieldOfView(fovyDegrees * (MathF.PI / 180f), aspect, near, far);

        var projection = BuildProjection(10f, 1200f, 15f, 1f);
        var position = new Vector3(0f, 34.5f, 600f);
        var target = new Vector3(0f, 34.5f, 0f);
        var aspectHeightFactor = 1f;
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
                projection = BuildProjection(10f, 1200f, 15f, 1f);
                // Tuned all-body camera for better native/app readability while staying in the
                // same all-body projection family as the reference implementation.
                position = new(0f, 90f, 760f);
                target = new(0f, 95f, 0f);
                aspectHeightFactor = 1f;
                break;
            }
        }

        return new ViewParameters(position.Y, position.Z, target, projection, aspectHeightFactor, isCameraPositionAbsolute);
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

    private static Vector3 NormalizeOrDefault(Vector3 vector, Vector3 fallback) =>
        vector.LengthSquared() < 1e-8f ? fallback : Vector3.Normalize(vector);

    private static Vector3 Hadamard(Vector3 left, Vector3 right) => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);

    private static int RoundUpToEven(int value) => (value & 1) == 0 ? value : value + 1;

    private static void FillBackground(byte[] pixels, float[] depth, int width, int height, RgbaColor background)
    {
        for (var i = 0; i < depth.Length; i++)
            depth[i] = 1f;

        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                pixels[i + 0] = background.B;
                pixels[i + 1] = background.G;
                pixels[i + 2] = background.R;
                pixels[i + 3] = background.A;
            }
        }
    }

    private static void FillOverlay(byte[] bgraPixels, int width, int height, Vector4 clear)
    {
        var r = (byte)Math.Clamp((int)MathF.Round(Clamp01(clear.X) * 255f), 0, 255);
        var g = (byte)Math.Clamp((int)MathF.Round(Clamp01(clear.Y) * 255f), 0, 255);
        var b = (byte)Math.Clamp((int)MathF.Round(Clamp01(clear.Z) * 255f), 0, 255);
        var a = (byte)Math.Clamp((int)MathF.Round(Clamp01(clear.W) * 255f), 0, 255);

        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                bgraPixels[i + 0] = b;
                bgraPixels[i + 1] = g;
                bgraPixels[i + 2] = r;
                bgraPixels[i + 3] = a;
            }
        }
    }

    private static byte[] ConvertBgraToRgba(byte[] bgraPixels)
    {
        var rgba = new byte[bgraPixels.Length];
        for (var i = 0; i + 3 < bgraPixels.Length; i += 4)
        {
            rgba[i + 0] = bgraPixels[i + 2];
            rgba[i + 1] = bgraPixels[i + 1];
            rgba[i + 2] = bgraPixels[i + 0];
            rgba[i + 3] = bgraPixels[i + 3];
        }
        return rgba;
    }

    private static void BlendPixel(byte[] target, int width, int x, int y, Vector4 src, BlendMode blendMode)
    {
        var i = (y * width + x) * 4;

        var srcA = Math.Clamp(src.W, 0f, 1f);
        var dstA = target[i + 3] / 255f;

        var dstB = target[i + 0] / 255f;
        var dstG = target[i + 1] / 255f;
        var dstR = target[i + 2] / 255f;

        float outR;
        float outG;
        float outB;
        float outA;

        switch (blendMode)
        {
            case BlendMode.Faceline:
                outR = src.X * srcA + dstR * (1f - srcA);
                outG = src.Y * srcA + dstG * (1f - srcA);
                outB = src.Z * srcA + dstB * (1f - srcA);
                outA = srcA + dstA;
                break;
            case BlendMode.MaskNoRenderTexture:
                outR = src.X * (1f - dstA) + dstR * dstA;
                outG = src.Y * (1f - dstA) + dstG * dstA;
                outB = src.Z * (1f - dstA) + dstB * dstA;
                outA = srcA * srcA + dstA * dstA;
                break;
            default:
                outA = srcA + dstA * (1f - srcA);
                if (outA <= 0f)
                    return;
                outR = (src.X * srcA + dstR * dstA * (1f - srcA)) / outA;
                outG = (src.Y * srcA + dstG * dstA * (1f - srcA)) / outA;
                outB = (src.Z * srcA + dstB * dstA * (1f - srcA)) / outA;
                break;
        }

        target[i + 0] = (byte)Math.Clamp((int)MathF.Round(Clamp01(outB) * 255f), 0, 255);
        target[i + 1] = (byte)Math.Clamp((int)MathF.Round(Clamp01(outG) * 255f), 0, 255);
        target[i + 2] = (byte)Math.Clamp((int)MathF.Round(Clamp01(outR) * 255f), 0, 255);
        target[i + 3] = (byte)Math.Clamp((int)MathF.Round(Clamp01(outA) * 255f), 0, 255);
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

    private enum BlendMode
    {
        Over,
        Faceline,
        MaskNoRenderTexture,
    }

    private readonly record struct ViewParameters(
        float BaseCameraY,
        float OrbitRadius,
        Vector3 Target,
        Matrix4x4 Projection,
        float AspectHeightFactor,
        bool IsCameraPositionAbsolute
    );

    private readonly record struct MaterialInfo(
        Vector3 Ambient,
        Vector3 Diffuse,
        Vector3 Specular,
        float SpecularPower,
        int SpecularMode,
        Vector3 RimColor
    );

    private readonly record struct RasterVertex(
        Vector2 ScreenPosition,
        float Depth,
        float InvW,
        Vector2 Texcoord,
        Vector3 ViewPosition,
        Vector3 Normal,
        Vector3 Tangent,
        Vector4 Parameter
    );

    private readonly record struct VertexParameterData(Vector4[] Values, int Mode);

    private readonly record struct OverlayTexture(int Width, int Height, byte[] BgraPixels);

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

    private readonly record struct BodyVertex(Vector3 Position, Vector2 Texcoord, Vector3 Normal);

    private sealed class PreparedMesh(
        FflNativeInterop.FFLDrawParam drawParam,
        RasterVertex[] vertices,
        int[] indices,
        MaterialInfo material,
        int parameterMode,
        Vector4? constantColor,
        bool hasTangent
    )
    {
        public FflNativeInterop.FFLDrawParam DrawParam { get; } = drawParam;
        public RasterVertex[] Vertices { get; } = vertices;
        public int[] Indices { get; } = indices;
        public MaterialInfo Material { get; } = material;
        public int ParameterMode { get; } = parameterMode;
        public Vector4? ConstantColor { get; } = constantColor;
        public bool HasTangent { get; } = hasTangent;
    }

    private readonly record struct RgbaColor(byte R, byte G, byte B, byte A);

    private readonly record struct EyeMouthTypeElement(int EyeRightType, int EyeLeftType, int MouthType, int EyebrowType);

    private readonly record struct FacelineTransform(Vector3 HairTranslate, Vector3 NoseTranslate, Vector3 BeardTranslate);

    private readonly record struct DecodedShape(
        Vector3[] Positions,
        Vector2[] Texcoords,
        Vector3[] Normals,
        Vector3[] Tangents,
        Vector4[] Parameters,
        int[] Indices,
        FacelineTransform? FacelineTransform
    );

    private enum RawMaskOrigin
    {
        Center,
        Left,
        Right,
    }

    private readonly record struct RawMaskPartDescriptor(Vector2 Position, Vector2 Scale, float RotationDegrees, RawMaskOrigin Origin);

    private readonly record struct RawMaskParts(
        RawMaskPartDescriptor EyeR,
        RawMaskPartDescriptor EyeL,
        RawMaskPartDescriptor EyebrowR,
        RawMaskPartDescriptor EyebrowL,
        RawMaskPartDescriptor Mouth,
        RawMaskPartDescriptor MustacheR,
        RawMaskPartDescriptor MustacheL,
        RawMaskPartDescriptor Mole
    );

    private sealed class RenderAllocationTracker : IDisposable
    {
        private readonly List<GCHandle> _pinned = [];
        private readonly List<IntPtr> _allocated = [];

        public IntPtr Pin(byte[] bytes)
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            _pinned.Add(handle);
            return handle.AddrOfPinnedObject();
        }

        public IntPtr Pin(short[] values)
        {
            var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
            _pinned.Add(handle);
            return handle.AddrOfPinnedObject();
        }

        public IntPtr AllocColor(Vector4 color)
        {
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<FflNativeInterop.FFLColor>());
            _allocated.Add(ptr);
            var nativeColor = new FflNativeInterop.FFLColor
            {
                r = Clamp01(color.X),
                g = Clamp01(color.Y),
                b = Clamp01(color.Z),
                a = Clamp01(color.W),
            };
            Marshal.StructureToPtr(nativeColor, ptr, fDeleteOld: false);
            return ptr;
        }

        public void Dispose()
        {
            foreach (var handle in _pinned)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
            _pinned.Clear();

            foreach (var ptr in _allocated)
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }
            _allocated.Clear();
        }
    }

    private sealed class TextureStore
    {
        private readonly ConcurrentDictionary<IntPtr, TextureData> _textures = new();
        private long _nextId = 1;

        public IntPtr RegisterTexture(TextureData texture)
        {
            var id = new IntPtr(Interlocked.Increment(ref _nextId));
            _textures[id] = texture;
            return id;
        }

        public void RemoveTexture(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;
            _textures.TryRemove(handle, out TextureData _);
        }

        public bool Contains(IntPtr handle) => handle != IntPtr.Zero && _textures.ContainsKey(handle);

        public bool TryGet(IntPtr handle, out TextureData texture) => _textures.TryGetValue(handle, out texture);
    }

    private readonly record struct TextureData(int Width, int Height, byte Format, int PixelStride, byte[] Pixels);
}

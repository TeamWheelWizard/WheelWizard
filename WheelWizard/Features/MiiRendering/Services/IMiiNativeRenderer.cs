using System.Numerics;
using Avalonia.Media.Imaging;
using MiiAnim.Core.Evaluation;
using WheelWizard.MiiImages.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiRendering.Services;

public readonly record struct NativeMiiPixelBuffer(int Width, int Height, byte[] BgraPixels);

public interface IMiiNativeRenderer
{
    Task<OperationResult<Bitmap>> RenderAsync(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        CancellationToken cancellationToken = default
    );

    /// <summary>Renders a full-body Mii posed by a Mii animation (see MiiAnim.Core).</summary>
    Task<OperationResult<NativeMiiPixelBuffer>> RenderPosedBufferAsync(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        MiiPose pose,
        CancellationToken cancellationToken = default
    );

    Task<OperationResult<NativeMiiPixelBuffer>> RenderBufferAsync(
        Mii mii,
        string studioData,
        MiiImageSpecifications specifications,
        CancellationToken cancellationToken = default
    );

    /// <summary>GPU-ready head meshes (with textures) for the realtime renderer. Slow (~0.1 s); call off the UI thread.</summary>
    OperationResult<List<HeadMeshData>> BuildHeadModel(string studioData, int expressionId, MiiHeadDetail detail = MiiHeadDetail.Full);

    /// <summary>
    /// The face mask with only some parts painted in, for animating those parts on their own (or all of them, to show
    /// another expression on an already built head). Call off the UI thread.
    /// </summary>
    OperationResult<MiiMaskLayerTexture> BuildMaskLayer(
        string studioData,
        int expressionId,
        MiiMaskLayers layers,
        MiiHeadDetail detail = MiiHeadDetail.Full
    );

    /// <summary>The face mask of a locked Mii (see <see cref="LockedMii"/>): one big question mark. Call off the UI thread.</summary>
    OperationResult<MiiMaskLayerTexture> BuildLockedMaskLayer(string studioData, MiiHeadDetail detail = MiiHeadDetail.Full);

    /// <summary>Where the face mask parts sit on the mask texture, in texture coordinates.</summary>
    OperationResult<IReadOnlyDictionary<MiiMaskLayers, Vector2[][]>> GetMaskPartQuads(string studioData);

    /// <summary>Camera, matrices and colours for drawing a Mii on the GPU exactly like the CPU renderer frames it.</summary>
    OperationResult<MiiRealtimeFrameSetup> GetRealtimeFrameSetup(string studioData, MiiImageSpecifications specifications, float aspect);
}

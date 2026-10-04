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
}

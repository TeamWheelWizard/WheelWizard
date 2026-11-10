using Avalonia.Controls;
using Avalonia.Threading;
using WheelWizard.MiiRendering.Realtime;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiImages.Views;

// The locked Mii (see LockedMii): a question mark for a face and no nose, drawn grey with scan lines moving over it.
public sealed partial class MiiRealtimeView
{
    /// <summary>Space between the scan lines of a locked Mii, in device independent pixels.</summary>
    private const double LockedLinePitch = 3;

    private static string LockedFaceKey(string studio) => $"{studio}|locked";

    /// <summary>The question mark face of a locked Mii, or null while it's still being built (asking starts that).</summary>
    private MaskLayerPass? LockedFace(string studio)
    {
        var key = LockedFaceKey(studio);
        if (_maskLayers.TryGetValue(key, out var face))
            return new MaskLayerPass(key, face);
        if (_store.TryGetLockedMask(studio, Detail, out var stored))
        {
            _maskLayers[key] = stored!;
            return new MaskLayerPass(key, stored!);
        }

        if (!_buildingMaskLayers.TryAdd(key, 0))
            return null;
        _store.RequestLockedMask(
            studio,
            Detail,
            wanted: () =>
            {
                lock (_recentStudios)
                    return _recentStudios.Contains(studio) || studio == _lastStudio;
            },
            done: mask =>
            {
                if (mask is not null)
                    _maskLayers[key] = mask;
                _buildingMaskLayers.TryRemove(key, out _);
                Dispatcher.UIThread.Post(RequestNextFrameRendering);
            }
        );
        return null;
    }

    private static IReadOnlyList<HeadPass> LockedPasses(IReadOnlyList<HeadMeshData> head, string key, MaskLayerPass face) =>
        [new HeadPass(key, head) { Mask = face, Shapes = shape => shape is not (HeadShape.Nose or HeadShape.NoseLine) }];

    private LockedLook LockedLookNow() =>
        new((float)_clock.Elapsed.TotalSeconds, (float)(LockedLinePitch * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0)));
}

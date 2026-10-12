using System.Numerics;
using MiiAnim.Core.Animation;
using WheelWizard.MiiRendering.Realtime;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiImages.Views;

// Moving a part live while it's dragged, without building a head per step.
public sealed partial class MiiRealtimeView
{
    private Nudge? _nudge;

    private sealed class Nudge(HeadPart part, string studio)
    {
        public HeadPart Part { get; } = part;

        /// <summary>The Mii as it was when the drag started; its head is drawn with the part moved.</summary>
        public string Studio { get; } = studio;

        public Vector2 MaskShift { get; set; }
        public float MaskSpread { get; set; }
        public Vector3 MeshShift { get; set; }

        /// <summary>The drag ended; the nudge stays until the head of the final Mii is ready.</summary>
        public bool Ending { get; set; }
    }

    /// <summary>
    /// Starts moving <paramref name="part"/> live: building a head takes too long to follow the mouse, so until
    /// <see cref="EndNudge"/> the head on screen stays and only that part is moved (see <see cref="NudgePart"/>).
    /// <see cref="SetMii(WiiManagement.MiiManagement.Domain.Mii.Mii?, string?)"/> can keep being called meanwhile;
    /// the last Mii is built when the drag ends.
    /// </summary>
    public void BeginNudge(HeadPart part)
    {
        if (_shownStudio is not { } studio || _studioData != studio)
            return;
        _nudge = new Nudge(part, studio);
        if (part.IsMaskPart())
        {
            RequestMaskLayer(studio, MiiExpression.Normal, MiiMaskLayers.All & ~part.MaskLayer());
            RequestMaskLayer(studio, MiiExpression.Normal, part.MaskLayer());
        }
    }

    /// <summary>
    /// How far the part moved since <see cref="BeginNudge"/>: <paramref name="maskShift"/> in mask texture units and
    /// <paramref name="maskSpread"/> outwards on both sides (eye spacing) for face parts, <paramref name="meshShift"/>
    /// in head model units for 3D parts.
    /// </summary>
    public void NudgePart(Vector2 maskShift, float maskSpread, Vector3 meshShift)
    {
        if (_nudge is not { Ending: false } nudge)
            return;
        nudge.MaskShift = maskShift;
        nudge.MaskSpread = maskSpread;
        nudge.MeshShift = meshShift;
        RequestNextFrameRendering();
    }

    /// <summary>The drag is over: build the last Mii set, and show it as soon as it's ready.</summary>
    public void EndNudge()
    {
        if (_nudge is not { } nudge)
            return;
        nudge.Ending = true;
        if (_studioData is { } studio)
            Remember(studio, prewarm: false);
        RequestNextFrameRendering();
    }

    /// <summary>Head builds are skipped for the Miis passed by during a drag (only the last one is built after it).</summary>
    private bool IsNudgeSkipping(string studio) => _nudge is { Ending: false } nudge && studio != nudge.Studio;

    /// <summary>The frame while a part is nudged, or null when not nudging (any more).</summary>
    private MiiGpuFrame? NudgeFrame(MiiAnim.Core.Evaluation.MiiPose pose, float aspect)
    {
        if (_nudge is not { } nudge)
            return null;
        if (nudge.Ending && (_studioData is null || _heads.ContainsKey(HeadKey(_studioData, MiiExpression.Normal))))
        {
            _nudge = null;
            return null;
        }

        var key = HeadKey(nudge.Studio, MiiExpression.Normal);
        if (!_heads.TryGetValue(key, out var head))
        {
            _nudge = null;
            return null;
        }

        var setup = _renderer.GetRealtimeFrameSetup(nudge.Studio, Specifications, aspect);
        if (setup.IsFailure)
            return null;
        // Keeps this head (and its mask layers) from being freed while it's on screen.
        _lastStudio = nudge.Studio;
        var frameSetup = Shift(MoveCamera(setup.Value));
        return _lastFrame = new MiiGpuFrame(frameSetup, pose, head, key)
        {
            Alpha = _alpha,
            Particles = Particles(frameSetup),
            HeadPasses = NudgePasses(nudge, head, key),
            BodyHoverMask = BodyHoverMask,
        };
    }

    private IReadOnlyList<HeadPass>? NudgePasses(Nudge nudge, IReadOnlyList<HeadMeshData> head, string key)
    {
        var part = nudge.Part;
        var tint = _highlightedPart == part ? (part.IsMaskPart() ? MaskHighlight : MeshHighlight) : Vector4.Zero;
        if (!part.IsMaskPart())
        {
            return
            [
                new HeadPass(key, head) { Shapes = shape => !part.Contains(shape) },
                new HeadPass(key, head)
                {
                    Shapes = shape => part.Contains(shape),
                    Offset = nudge.MeshShift,
                    Tint = tint,
                },
            ];
        }

        var layer = part.MaskLayer();
        var withoutKey = MaskLayerKey(nudge.Studio, MiiExpression.Normal, MiiMaskLayers.All & ~layer);
        var partKey = MaskLayerKey(nudge.Studio, MiiExpression.Normal, layer);
        if (!_maskLayers.TryGetValue(withoutKey, out var without) || !_maskLayers.TryGetValue(partKey, out var partLayer))
            return null;

        static bool MaskOnly(HeadShape shape) => shape == HeadShape.Mask;
        HeadPass Side(float spread, Vector4? clip) =>
            new(key, head)
            {
                Shapes = MaskOnly,
                Mask = new MaskLayerPass(partKey, partLayer),
                // The texture is sampled where the content came from: the opposite of where it moves.
                MaskUvOffset = -(nudge.MaskShift + new Vector2(spread, 0f)),
                MaskUvClip = clip,
                Tint = tint,
            };

        List<HeadPass> passes =
        [
            new HeadPass(key, head) { Mask = new MaskLayerPass(withoutKey, without), Shapes = shape => !DrawnOverMask(shape) },
        ];
        if (nudge.MaskSpread == 0f)
        {
            passes.Add(Side(0f, null));
        }
        else
        {
            // Each half of the face (one eye each) moves its own way.
            passes.Add(Side(-nudge.MaskSpread, new Vector4(-1e9f, -1e9f, 0.5f, 1e9f)));
            passes.Add(Side(nudge.MaskSpread, new Vector4(0.5f, -1e9f, 1e9f, 1e9f)));
        }

        passes.Add(new HeadPass(key, head) { Shapes = DrawnOverMask, OnTop = true });
        return passes;
    }
}

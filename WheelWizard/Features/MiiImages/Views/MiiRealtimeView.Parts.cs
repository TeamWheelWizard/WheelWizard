using System.Collections.Concurrent;
using System.Numerics;
using Avalonia;
using Avalonia.Threading;
using MiiAnim.Core.Animation;
using WheelWizard.MiiRendering.Realtime;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// One part of the head changed (see <see cref="MiiRealtimeView.SetMii(WiiManagement.MiiManagement.Domain.Mii.Mii?, string?, HeadPartChange?)"/>).
/// </summary>
/// <param name="Direction">
/// +1: the old part slides out to the left while the new one comes in from the right (going to the next variant),
/// -1: the other way around, 0: they cross-fade in place.
/// </param>
public sealed record HeadPartChange(HeadPart Part, int Direction);

/// <summary>Where the camera ray through a point first hits the head.</summary>
/// <param name="Uv">Texture coordinates on the mesh that was hit (on the mask mesh: where on the face).</param>
/// <param name="Position">The point that was hit, in head model units.</param>
public sealed record HeadHit(HeadShape Shape, Vector2 Uv, Vector3 Position);

// Per-part transitions, highlights and picking on the head.
public sealed partial class MiiRealtimeView
{
    private static readonly TimeSpan PartChangeLength = TimeSpan.FromMilliseconds(230);

    /// <summary>How long a change may wait for its pieces (old head, mask layers) before it just swaps.</summary>
    private static readonly TimeSpan PartChangeWait = TimeSpan.FromMilliseconds(450);

    /// <summary>How far parts slide, in head model units (for mesh parts) and mask texture units (for face parts).</summary>
    private const float MeshSlideDistance = 9f;

    private const float HairSlideDistance = 18f;
    private const float HeadSlideDistance = 34f;
    private const float MaskSlideDistance = 0.07f;

    private static readonly Vector4 MeshHighlight = new(1f, 0.93f, 0.7f, 0.3f);
    private static readonly Vector4 MaskHighlight = new(1f, 0.9f, 0.55f, 0.55f);

    private readonly ConcurrentDictionary<string, MiiMaskLayerTexture> _maskLayers = new();
    private readonly ConcurrentDictionary<string, byte> _buildingMaskLayers = new();

    private PartChange? _partChange;
    private HeadPart? _highlightedPart;
    private int _bodyHoverMask;

    private sealed class PartChange(HeadPartChange change, string oldStudio, string newStudio, TimeSpan requested)
    {
        public HeadPart Part { get; } = change.Part;
        public int Direction { get; } = change.Direction;
        public string OldStudio { get; } = oldStudio;
        public string NewStudio { get; } = newStudio;
        public TimeSpan Requested { get; } = requested;
        public TimeSpan? Started { get; set; }
    }

    /// <summary>A part of the head drawn lit up (e.g. the one under the cursor), or null.</summary>
    public HeadPart? HighlightedPart
    {
        get => _highlightedPart;
        set
        {
            if (_highlightedPart == value)
                return;
            _highlightedPart = value;
            if (value is { } part && part.IsMaskPart() && _studioData is { } studio)
            {
                RequestMaskLayer(studio, MiiExpression.Normal, part.MaskLayer());
                // The face without it too, so dragging it (see BeginNudge) can start right away.
                RequestMaskLayer(studio, MiiExpression.Normal, MiiMaskLayers.All & ~part.MaskLayer());
            }
            RequestNextFrameRendering();
        }
    }

    /// <summary>Body parts drawn lit up (bit per <see cref="MiiAnim.Core.Rig.MiiBone"/>).</summary>
    public int BodyHoverMask
    {
        get => _bodyHoverMask;
        set
        {
            if (_bodyHoverMask == value)
                return;
            _bodyHoverMask = value;
            RequestNextFrameRendering();
        }
    }

    private bool IsPartChanging =>
        _partChange is { } change && (change.Started is not { } started || _clock.Elapsed - started < PartChangeLength);

    private void BeginPartChange(HeadPartChange? change, string? oldStudio, string? newStudio)
    {
        _partChange = null;
        if (change is null || oldStudio is null || newStudio is null || oldStudio == newStudio)
            return;
        _partChange = new PartChange(change, oldStudio, newStudio, _clock.Elapsed);
        if (change.Part.IsMaskPart())
        {
            var layer = change.Part.MaskLayer();
            RequestMaskLayer(newStudio, MiiExpression.Normal, MiiMaskLayers.All & ~layer);
            RequestMaskLayer(oldStudio, MiiExpression.Normal, layer);
            RequestMaskLayer(newStudio, MiiExpression.Normal, layer);
        }
    }

    /// <summary>
    /// Whether the new Mii can be shown: false while a part change still waits for its pieces (then the old Mii stays
    /// on screen a moment longer, so the change can start from it).
    /// </summary>
    private bool IsPartChangeReady(MiiExpression expression)
    {
        if (_partChange is not { Started: null } change)
            return true;
        if (change.NewStudio != _studioData || expression != MiiExpression.Normal)
        {
            _partChange = null;
            return true;
        }

        var ready = _heads.ContainsKey(HeadKey(change.OldStudio, MiiExpression.Normal));
        if (change.Part.IsMaskPart())
        {
            var layer = change.Part.MaskLayer();
            ready &=
                _maskLayers.ContainsKey(MaskLayerKey(change.NewStudio, MiiExpression.Normal, MiiMaskLayers.All & ~layer))
                && _maskLayers.ContainsKey(MaskLayerKey(change.OldStudio, MiiExpression.Normal, layer))
                && _maskLayers.ContainsKey(MaskLayerKey(change.NewStudio, MiiExpression.Normal, layer));
        }

        if (ready)
        {
            change.Started = _clock.Elapsed;
            return true;
        }

        if (_clock.Elapsed - change.Requested < PartChangeWait)
            return false;
        _partChange = null;
        return true;
    }

    /// <summary>How the head is drawn this frame: as a part change, with a highlighted part, or plainly (null).</summary>
    private IReadOnlyList<HeadPass>? HeadPasses(MiiExpression expression, IReadOnlyList<HeadMeshData> head, string key)
    {
        if (_partChange is { Started: { } started } change)
        {
            var t = (float)((_clock.Elapsed - started) / PartChangeLength);
            if (t >= 1f || change.NewStudio != _studioData || expression != MiiExpression.Normal)
                _partChange = null;
            else if (ChangePasses(change, t, head, key) is { } passes)
                return passes;
        }

        if (_highlightedPart is not { } part)
            return null;

        var plain = new HeadPass(key, head);
        if (!part.IsMaskPart())
            return
            [
                plain with
                {
                    Tint = part == HeadPart.Head ? MeshHighlight with { W = 0.18f } : MeshHighlight,
                    TintShapes = shape => part.Highlights(shape),
                },
            ];

        if (_studioData is null || !_maskLayers.TryGetValue(MaskLayerKey(_studioData, expression, part.MaskLayer()), out var layer))
            return null;
        return
        [
            plain,
            new HeadPass(key, head)
            {
                Shapes = shape => shape == HeadShape.Mask,
                Mask = new MaskLayerPass(MaskLayerKey(_studioData, expression, part.MaskLayer()), layer),
                Tint = MaskHighlight,
            },
        ];
    }

    private List<HeadPass>? ChangePasses(PartChange change, float t, IReadOnlyList<HeadMeshData> head, string key)
    {
        var oldKey = HeadKey(change.OldStudio, MiiExpression.Normal);
        if (!_heads.TryGetValue(oldKey, out var oldHead))
            return null;

        // Fast out, gentle landing.
        var p = 1f - (1f - t) * (1f - t) * (1f - t);
        var direction = change.Direction;
        var part = change.Part;

        if (part.IsMaskPart())
        {
            var layer = part.MaskLayer();
            var withoutKey = MaskLayerKey(change.NewStudio, MiiExpression.Normal, MiiMaskLayers.All & ~layer);
            var oldLayerKey = MaskLayerKey(change.OldStudio, MiiExpression.Normal, layer);
            var newLayerKey = MaskLayerKey(change.NewStudio, MiiExpression.Normal, layer);
            if (
                !_maskLayers.TryGetValue(withoutKey, out var without)
                || !_maskLayers.TryGetValue(oldLayerKey, out var oldLayer)
                || !_maskLayers.TryGetValue(newLayerKey, out var newLayer)
            )
                return null;

            bool MaskOnly(HeadShape shape) => shape == HeadShape.Mask;
            return
            [
                new HeadPass(key, head) { Mask = new MaskLayerPass(withoutKey, without), Shapes = shape => !DrawnOverMask(shape) },
                new HeadPass(oldKey, oldHead)
                {
                    Shapes = MaskOnly,
                    Mask = new MaskLayerPass(oldLayerKey, oldLayer),
                    MaskUvOffset = new Vector2(direction * MaskSlideDistance * p, 0f),
                    Alpha = 1f - p,
                },
                new HeadPass(key, head)
                {
                    Shapes = MaskOnly,
                    Mask = new MaskLayerPass(newLayerKey, newLayer),
                    MaskUvOffset = new Vector2(-direction * MaskSlideDistance * (1f - p), 0f),
                    Alpha = p,
                },
                new HeadPass(key, head) { Shapes = DrawnOverMask, OnTop = true },
            ];
        }

        var distance = part switch
        {
            HeadPart.Head => HeadSlideDistance,
            HeadPart.Hair => HairSlideDistance,
            _ => MeshSlideDistance,
        };
        var passes = new List<HeadPass>(3);
        if (part != HeadPart.Head)
            passes.Add(new HeadPass(key, head) { Shapes = shape => !part.Contains(shape) });
        // Head model +X is the Mii's left, which is the right side of the screen while it faces the camera.
        passes.Add(
            new HeadPass(oldKey, oldHead)
            {
                Shapes = shape => part.Contains(shape),
                Offset = new Vector3(-direction * distance * p, 0f, 0f),
                Alpha = 1f - p,
            }
        );
        passes.Add(
            new HeadPass(key, head)
            {
                Shapes = shape => part.Contains(shape),
                Offset = new Vector3(direction * distance * (1f - p), 0f, 0f),
                Alpha = p,
            }
        );
        return passes;
    }

    /// <summary>
    /// See-through shapes drawn after the face mask (the nose's outline, the glasses): a pass that moves or swaps
    /// the mask leaves them out, and they're drawn again after it, so they stay on top of the face like normal.
    /// </summary>
    private static bool DrawnOverMask(HeadShape shape) => shape is HeadShape.NoseLine or HeadShape.Glass;

    private static string MaskLayerKey(string studio, MiiExpression expression, MiiMaskLayers layers) =>
        $"{studio}|{(int)expression}|m{(int)layers}";

    /// <summary>Builds a mask layer in the background (sharing the head builder's queue).</summary>
    private void RequestMaskLayer(string studio, MiiExpression expression, MiiMaskLayers layers)
    {
        var key = MaskLayerKey(studio, expression, layers);
        if (_maskLayers.ContainsKey(key) || !_buildingMaskLayers.TryAdd(key, 0))
            return;

        // Drop layers of Miis that aren't around any more.
        lock (_recentStudios)
        {
            foreach (
                var stale in _maskLayers.Keys.Where(k =>
                    !_recentStudios.Append(_lastStudio).Any(s => s is not null && k.StartsWith(s + "|"))
                )
            )
                _maskLayers.TryRemove(stale, out _);
        }

        _ = Task.Run(async () =>
        {
            await _buildGate.WaitAsync();
            try
            {
                bool recent;
                lock (_recentStudios)
                    recent = _recentStudios.Contains(studio) || studio == _lastStudio;
                if (!recent)
                    return;
                var result = _renderer.BuildMaskLayer(studio, (int)expression, layers);
                if (result.IsSuccess)
                    _maskLayers[key] = result.Value;
            }
            finally
            {
                _buildGate.Release();
                _buildingMaskLayers.TryRemove(key, out _);
                Dispatcher.UIThread.Post(RequestNextFrameRendering);
            }
        });
    }

    #region Picking and projecting head points (of the last drawn frame)

    /// <summary>
    /// Everything on the head under a point of this control, nearest first (empty when the ray misses the head).
    /// The face mask mesh is bigger than the face and see-through around it, so callers decide whether a hit on it
    /// counts (e.g. only on a painted part) or look further.
    /// </summary>
    public IReadOnlyList<HeadHit> PickHead(Point point)
    {
        if (_lastFrame is not { Head: { } head } frame || Bounds.Width <= 0 || Bounds.Height <= 0)
            return [];
        var model = frame.Setup.HeadMatrix(frame.Pose);
        if (!Matrix4x4.Invert(model * frame.Setup.View * frame.Setup.Projection, out var inverse))
            return [];

        var x = (float)(point.X / Bounds.Width * 2 - 1);
        var y = (float)(1 - point.Y / Bounds.Height * 2);
        var near = Vector4.Transform(new Vector4(x, y, -1f, 1f), inverse);
        var far = Vector4.Transform(new Vector4(x, y, 1f, 1f), inverse);
        var origin = new Vector3(near.X, near.Y, near.Z) / near.W;
        var direction = Vector3.Normalize(new Vector3(far.X, far.Y, far.Z) / far.W - origin);

        var hits = new List<(float Distance, HeadHit Hit)>();
        foreach (var mesh in head)
        {
            // Nearest hit per mesh is enough.
            HeadHit? best = null;
            var bestDistance = float.MaxValue;
            var positions = mesh.Positions;
            var indices = mesh.Indices;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i],
                    b = indices[i + 1],
                    c = indices[i + 2];
                if (!RayTriangle(origin, direction, positions[a], positions[b], positions[c], out var distance, out var u, out var v))
                    continue;
                if (distance >= bestDistance)
                    continue;
                var uv = mesh.Texcoords[a] * (1f - u - v) + mesh.Texcoords[b] * u + mesh.Texcoords[c] * v;
                // See-through parts of the glasses don't count, so you can click the eyes behind them.
                if (mesh.Shape == HeadShape.Glass && SampleAlpha(mesh, uv, alphaInRed: true) < 0.2f)
                    continue;
                bestDistance = distance;
                best = new HeadHit(mesh.Shape, uv, origin + direction * distance);
            }

            if (best is not null)
                hits.Add((bestDistance, best));
        }

        return hits.OrderBy(h => h.Distance).Select(h => h.Hit).ToList();
    }

    /// <summary>Where a point in head model units appears in this control.</summary>
    public Point? ProjectHeadPoint(Vector3 headPoint)
    {
        if (_lastFrame is not { } frame)
            return null;
        var world = Vector3.Transform(headPoint, frame.Setup.HeadMatrix(frame.Pose));
        var clip = Vector4.Transform(new Vector4(world, 1f), frame.Setup.View * frame.Setup.Projection);
        if (clip.W <= 1e-4f)
            return null;
        return new Point((clip.X / clip.W * 0.5 + 0.5) * Bounds.Width, (0.5 - clip.Y / clip.W * 0.5) * Bounds.Height);
    }

    /// <summary>The point on the face (head model units) at mask texture coordinates <paramref name="uv"/>.</summary>
    public Vector3? MaskPointToHead(Vector2 uv)
    {
        if (_lastFrame is not { Head: { } head })
            return null;
        foreach (var mesh in head)
        {
            if (mesh.Shape != HeadShape.Mask)
                continue;
            var texcoords = mesh.Texcoords;
            var indices = mesh.Indices;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i],
                    b = indices[i + 1],
                    c = indices[i + 2];
                if (Barycentric(uv, texcoords[a], texcoords[b], texcoords[c]) is not { } w)
                    continue;
                return mesh.Positions[a] * w.X + mesh.Positions[b] * w.Y + mesh.Positions[c] * w.Z;
            }
        }

        return null;
    }

    /// <summary>The screen area of a mesh part (not for face mask parts), or null when it isn't drawn.</summary>
    public Rect? PartBounds(HeadPart part)
    {
        if (_lastFrame is not { Head: { } head } frame || part.IsMaskPart())
            return null;
        var transform = frame.Setup.HeadMatrix(frame.Pose) * frame.Setup.View * frame.Setup.Projection;
        double left = double.MaxValue,
            top = double.MaxValue,
            right = double.MinValue,
            bottom = double.MinValue;
        foreach (var mesh in head)
        {
            if (!part.Highlights(mesh.Shape))
                continue;
            foreach (var position in mesh.Positions)
            {
                var clip = Vector4.Transform(new Vector4(position, 1f), transform);
                if (clip.W <= 1e-4f)
                    continue;
                var sx = (clip.X / clip.W * 0.5 + 0.5) * Bounds.Width;
                var sy = (0.5 - clip.Y / clip.W * 0.5) * Bounds.Height;
                left = Math.Min(left, sx);
                right = Math.Max(right, sx);
                top = Math.Min(top, sy);
                bottom = Math.Max(bottom, sy);
            }
        }

        return left <= right ? new Rect(left, top, right - left, bottom - top) : null;
    }

    private static bool RayTriangle(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance,
        out float u,
        out float v
    )
    {
        distance = u = v = 0;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < 1e-7f)
            return false;
        var inverse = 1f / determinant;
        var s = origin - a;
        u = Vector3.Dot(s, p) * inverse;
        if (u < 0f || u > 1f)
            return false;
        var q = Vector3.Cross(s, edge1);
        v = Vector3.Dot(direction, q) * inverse;
        if (v < 0f || u + v > 1f)
            return false;
        distance = Vector3.Dot(edge2, q) * inverse;
        return distance > 0f;
    }

    /// <summary>Weights of a, b and c for point p when p is inside the 2D triangle, otherwise null.</summary>
    private static Vector3? Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var denominator = v0.X * v1.Y - v1.X * v0.Y;
        if (MathF.Abs(denominator) < 1e-9f)
            return null;
        var wb = (v2.X * v1.Y - v1.X * v2.Y) / denominator;
        var wc = (v0.X * v2.Y - v2.X * v0.Y) / denominator;
        var wa = 1f - wb - wc;
        const float epsilon = -1e-4f;
        return wa >= epsilon && wb >= epsilon && wc >= epsilon ? new Vector3(wa, wb, wc) : null;
    }

    private static float SampleAlpha(HeadMeshData mesh, Vector2 uv, bool alphaInRed)
    {
        if (mesh.TexturePixels is not { } pixels || mesh.TextureWidth <= 0 || mesh.TextureHeight <= 0)
            return 1f;
        static float Mirror(float value)
        {
            var wrapped = value % 2f;
            if (wrapped < 0f)
                wrapped += 2f;
            return wrapped <= 1f ? wrapped : 2f - wrapped;
        }

        var x = Math.Clamp((int)(Mirror(uv.X) * (mesh.TextureWidth - 1)), 0, mesh.TextureWidth - 1);
        var y = Math.Clamp((int)(Mirror(uv.Y) * (mesh.TextureHeight - 1)), 0, mesh.TextureHeight - 1);
        var offset = (y * mesh.TextureWidth + x) * 4;
        return pixels[offset + (alphaInRed ? 0 : 3)] / 255f;
    }

    #endregion
}

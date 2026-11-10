using System.Runtime.CompilerServices;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiRendering.Realtime;

/// <summary>
/// Builds heads and face masks for the realtime Mii views in the background and keeps the recent ones, shared by every
/// view: a Mii shown in several places, or scrolled away and back, is only built once.
/// <para>
/// Builds run on <see cref="MiiRenderWorkers"/> as urgent work: the newest request goes first, so the Miis that just
/// came into view load before the ones scrolled past, and requests nobody wants any more by the time they're up are
/// dropped.
/// </para>
/// </summary>
public sealed class MiiHeadStore
{
    private static readonly ConditionalWeakTable<IMiiNativeRenderer, MiiHeadStore> Stores = new();

    /// <summary>Roughly 150 small heads, or 40 full ones with a few faces each.</summary>
    private const long MaxBytes = 96L * 1024 * 1024;

    private readonly IMiiNativeRenderer _renderer;
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recent = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private long _bytes;

    private sealed record Entry(string Key, object Value, long Bytes);

    private sealed class Job(string key, Func<IMiiNativeRenderer, (object? Value, long Bytes)> build)
    {
        public string Key { get; } = key;
        public Func<IMiiNativeRenderer, (object? Value, long Bytes)> Build { get; } = build;
        public List<(Func<bool> Wanted, Action<object?> Done)> Waiting { get; } = [];
        public bool Started { get; set; }
    }

    private MiiHeadStore(IMiiNativeRenderer renderer) => _renderer = renderer;

    /// <summary>The store of the heads built by <paramref name="renderer"/>.</summary>
    public static MiiHeadStore For(IMiiNativeRenderer renderer) => Stores.GetValue(renderer, r => new MiiHeadStore(r));

    public static string HeadKey(string studio, MiiHeadDetail detail) => $"h{(int)detail}|{studio}";

    public static string MaskKey(string studio, int expression, MiiMaskLayers layers, MiiHeadDetail detail) =>
        $"m{(int)detail}|{(int)layers}|{expression}|{studio}";

    public bool TryGetHead(string studio, MiiHeadDetail detail, out IReadOnlyList<HeadMeshData> head)
    {
        head = TryGet(HeadKey(studio, detail)) as IReadOnlyList<HeadMeshData> ?? [];
        return head.Count > 0;
    }

    public bool TryGetMask(string studio, int expression, MiiMaskLayers layers, MiiHeadDetail detail, out MiiMaskLayerTexture? mask)
    {
        mask = TryGet(MaskKey(studio, expression, layers, detail)) as MiiMaskLayerTexture;
        return mask is not null;
    }

    /// <summary>
    /// Builds a head (normal face) in the background unless it's stored already; <paramref name="done"/> gets it
    /// (null when it failed or nobody <paramref name="wanted"/> it any more) on the worker thread.
    /// </summary>
    public void RequestHead(string studio, MiiHeadDetail detail, Func<bool> wanted, Action<IReadOnlyList<HeadMeshData>?> done) =>
        Request(
            HeadKey(studio, detail),
            renderer =>
                renderer.BuildHeadModel(studio, 0, detail) is { IsSuccess: true } head
                    ? (
                        head.Value,
                        head.Value.Sum(mesh => mesh.Positions.Length * 64L + mesh.Indices.Length * 4L + (mesh.TexturePixels?.Length ?? 0))
                    )
                    : (null, 0),
            wanted,
            value => done(value as IReadOnlyList<HeadMeshData>)
        );

    /// <summary>Like <see cref="RequestHead"/>, for a face mask (e.g. another expression for an already built head).</summary>
    public void RequestMask(
        string studio,
        int expression,
        MiiMaskLayers layers,
        MiiHeadDetail detail,
        Func<bool> wanted,
        Action<MiiMaskLayerTexture?> done
    ) =>
        Request(
            MaskKey(studio, expression, layers, detail),
            renderer =>
                renderer.BuildMaskLayer(studio, expression, layers, detail) is { IsSuccess: true } mask
                    ? (mask.Value, mask.Value.Pixels.Length)
                    : (null, 0),
            wanted,
            value => done(value as MiiMaskLayerTexture)
        );

    public static string LockedMaskKey(string studio, MiiHeadDetail detail) => $"l{(int)detail}|{studio}";

    public bool TryGetLockedMask(string studio, MiiHeadDetail detail, out MiiMaskLayerTexture? mask)
    {
        mask = TryGet(LockedMaskKey(studio, detail)) as MiiMaskLayerTexture;
        return mask is not null;
    }

    /// <summary>Like <see cref="RequestMask"/>, for the question mark face of a locked Mii.</summary>
    public void RequestLockedMask(string studio, MiiHeadDetail detail, Func<bool> wanted, Action<MiiMaskLayerTexture?> done) =>
        Request(
            LockedMaskKey(studio, detail),
            renderer =>
                renderer.BuildLockedMaskLayer(studio, detail) is { IsSuccess: true } mask
                    ? (mask.Value, mask.Value.Pixels.Length)
                    : (null, 0),
            wanted,
            value => done(value as MiiMaskLayerTexture)
        );

    private object? TryGet(string key)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var node))
                return null;
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Value;
        }
    }

    private void Request(string key, Func<IMiiNativeRenderer, (object? Value, long Bytes)> build, Func<bool> wanted, Action<object?> done)
    {
        if (TryGet(key) is { } stored)
        {
            done(stored);
            return;
        }

        lock (_lock)
        {
            if (_jobs.TryGetValue(key, out var queued))
            {
                queued.Waiting.Add((wanted, done));
                // Asked again while still waiting: it's wanted now, so it goes to the front (again).
                if (!queued.Started)
                    MiiRenderWorkers.Enqueue(() => Run(queued), urgent: true);
                return;
            }

            var job = new Job(key, build);
            job.Waiting.Add((wanted, done));
            _jobs[key] = job;
            MiiRenderWorkers.Enqueue(() => Run(job), urgent: true);
        }
    }

    private void Run(Job job)
    {
        List<(Func<bool> Wanted, Action<object?> Done)> waiting;
        lock (_lock)
        {
            if (job.Started)
                return;
            job.Started = true;
            waiting = [.. job.Waiting];
        }

        object? value = null;
        if (waiting.Any(w => w.Wanted()))
        {
            try
            {
                var (built, bytes) = job.Build(_renderer);
                if (built is not null)
                    Store(job.Key, built, bytes);
                value = built;
            }
            catch (Exception)
            {
                // A Mii the native renderer can't handle stays empty instead of taking the worker down.
            }
        }

        lock (_lock)
        {
            _jobs.Remove(job.Key);
            waiting = [.. job.Waiting];
        }
        foreach (var (_, done) in waiting)
            done(value);
    }

    private void Store(string key, object value, long bytes)
    {
        lock (_lock)
        {
            if (_cache.Remove(key, out var old))
            {
                _recent.Remove(old);
                _bytes -= old.Value.Bytes;
            }

            _cache[key] = _recent.AddFirst(new Entry(key, value, bytes));
            _bytes += bytes;
            while (_bytes > MaxBytes && _recent.Last is { } oldest && oldest != _recent.First)
            {
                _recent.RemoveLast();
                _cache.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes;
            }
        }
    }
}

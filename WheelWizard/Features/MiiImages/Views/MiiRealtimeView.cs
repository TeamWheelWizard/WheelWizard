using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Rig;
using Silk.NET.OpenGL;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Realtime;
using WheelWizard.MiiRendering.Services;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// Realtime (GPU) Mii view: draws a Mii with OpenGL on a transparent background, so it can be layered over any UI,
/// and plays a <see cref="MiiAnimation"/> at the display's frame rate. Framing, lighting and colours match the CPU
/// renderer for the same <see cref="Specifications"/>.
/// <para>
/// If OpenGL isn't available <see cref="RealtimeUnavailable"/> fires; callers can fall back to the CPU renderer.
/// </para>
/// </summary>
public sealed class MiiRealtimeView : OpenGlControlBase
{
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(3);

    private readonly IMiiNativeRenderer _renderer;
    private readonly Stopwatch _clock = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<HeadMeshData>> _heads = new();
    private readonly ConcurrentDictionary<string, byte> _building = new();
    private readonly Dictionary<bool, MiiRig> _rigs = new();
    private readonly SemaphoreSlim _buildGate = new(1, 1);

    private MiiGpuRenderer? _gpu;
    private bool _initialized;
    private bool _unavailableReported;
    private IReadOnlySet<string>? _retainHeads;
    private DispatcherTimer? _initWatchdog;

    private Mii? _mii;
    private string? _studioData;
    private MiiAnimation? _animation;
    private double _playheadFrames;
    private double _lastEventFrame = -1;
    private TimeSpan _lastTick;
    private bool _finished;

    public MiiRealtimeView(IMiiNativeRenderer renderer)
    {
        _renderer = renderer;
        _clock.Start();
    }

    /// <summary>Camera framing, character rotation and zoom, same meaning as for rendered images.</summary>
    public MiiImageSpecifications Specifications { get; set; } =
        new()
        {
            Name = "Realtime",
            Type = MiiImageSpecifications.BodyType.all_body,
            Size = MiiImageSpecifications.ImageSize.medium,
        };

    public bool IsPlaying { get; set; } = true;

    public double Speed { get; set; } = 1;

    /// <summary>Loop the animation, or stop on its last frame (and raise <see cref="AnimationFinished"/>).</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Playhead in frames (unwrapped while looping).</summary>
    public double PlayheadFrames => _playheadFrames;

    public bool IsRealtimeAvailable => _initialized && _gpu is not null;

    /// <summary>Frames drawn so far (for an FPS readout).</summary>
    public long RenderedFrames { get; private set; }

    /// <summary>Fired (on the UI thread) for every event marker playback passes.</summary>
    public event Action<AnimEvent>? AnimationEvent;

    /// <summary>Fired once when a non-looping animation reaches its end.</summary>
    public event Action? AnimationFinished;

    /// <summary>OpenGL couldn't be used; the message says why. Fall back to the CPU renderer.</summary>
    public event Action<string>? RealtimeUnavailable;

    public Mii? Mii
    {
        get => _mii;
        set
        {
            _mii = value;
            _studioData = value is null ? null : Remember(value);
            RequestNextFrameRendering();
        }
    }

    /// <summary>
    /// Builds the heads of a Mii you're about to show (e.g. the other gender for a swap_gender event) so switching
    /// to it is instant. The last <see cref="RecentMiiCount"/> Miis stay cached.
    /// </summary>
    public void Prewarm(Mii mii) => Remember(mii);

    private const int RecentMiiCount = 3;
    private readonly List<string> _recentStudios = [];

    private string? Remember(Mii mii)
    {
        if (MiiStudioDataSerializer.Serialize(mii) is not { IsSuccess: true } serialized)
            return null;
        var studio = serialized.Value;
        lock (_recentStudios)
        {
            _recentStudios.Remove(studio);
            _recentStudios.Insert(0, studio);
            if (_recentStudios.Count > RecentMiiCount)
                _recentStudios.RemoveRange(RecentMiiCount, _recentStudios.Count - RecentMiiCount);
            // Free CPU and GPU copies of heads of Miis that dropped out.
            foreach (var key in _heads.Keys.Where(k => !_recentStudios.Any(s => k.StartsWith(s + "|"))))
                _heads.TryRemove(key, out _);
            _retainHeads = _heads.Keys.ToHashSet();
        }

        RequestHead(studio, MiiExpression.Normal);
        foreach (var expression in UsedExpressions())
            RequestHead(studio, expression);
        return studio;
    }

    private bool IsRecent(string studio)
    {
        lock (_recentStudios)
            return _recentStudios.Contains(studio);
    }

    public MiiAnimation? Animation
    {
        get => _animation;
        set
        {
            _animation = value;
            Restart();
            lock (_recentStudios)
                foreach (var studio in _recentStudios.ToList())
                foreach (var expression in UsedExpressions())
                    RequestHead(studio, expression);
        }
    }

    /// <summary>Starts the animation from the beginning (events on frame 0 fire again).</summary>
    public void Restart()
    {
        _playheadFrames = 0;
        _lastEventFrame = -1;
        _finished = false;
        _lastTick = _clock.Elapsed;
        RequestNextFrameRendering();
    }

    /// <summary>Jumps to a frame without firing the events in between.</summary>
    public void Seek(double frame)
    {
        _playheadFrames = Math.Max(0, frame);
        _lastEventFrame = _playheadFrames;
        _finished = false;
        RequestNextFrameRendering();
    }

    /// <summary>Redraw after changing <see cref="Specifications"/> or other settings.</summary>
    public void Invalidate() => RequestNextFrameRendering();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Some systems never give us a GL context (and never call OnOpenGlInit); report that too.
        _initWatchdog?.Stop();
        _initWatchdog = new DispatcherTimer { Interval = InitTimeout };
        _initWatchdog.Tick += (_, _) =>
        {
            _initWatchdog?.Stop();
            if (!_initialized)
                ReportUnavailable("OpenGL didn't start.");
        };
        _initWatchdog.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _initWatchdog?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _initialized = true;
        try
        {
            _gpu = new MiiGpuRenderer(GL.GetApi(gl.GetProcAddress), GlVersion.Type == GlProfileType.OpenGLES);
        }
        catch (Exception exception)
        {
            ReportUnavailable(exception.Message);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _gpu?.Dispose();
        _gpu = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gpu is null)
            return;
        if (_retainHeads is { } keep)
        {
            _retainHeads = null;
            _gpu.RetainHeads(keep);
        }

        var now = _clock.Elapsed;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        var scaling = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)(Bounds.Width * scaling));
        var height = Math.Max(1, (int)(Bounds.Height * scaling));
        try
        {
            _gpu.Render(BuildFrame(delta, width / (float)height), width, height);
            RenderedFrames++;
        }
        catch (Exception exception)
        {
            ReportUnavailable(exception.Message);
            return;
        }

        if (IsPlaying && _animation is not null && !_finished)
            RequestNextFrameRendering();
    }

    private MiiGpuFrame? BuildFrame(double deltaSeconds, float aspect)
    {
        if (_mii is null || _studioData is null)
            return null;

        var rig = RigFor(_mii);
        var pose = rig.RestPose;
        if (_animation is { } animation)
        {
            if (IsPlaying && !_finished)
                _playheadFrames += deltaSeconds * animation.Fps * Speed;
            if (!Loop && _playheadFrames >= animation.Length)
            {
                _playheadFrames = animation.Length;
                if (!_finished)
                {
                    _finished = true;
                    Dispatcher.UIThread.Post(() => AnimationFinished?.Invoke());
                }
            }

            foreach (var animEvent in animation.EventsBetween(_lastEventFrame, _playheadFrames, Loop).ToList())
                Dispatcher.UIThread.Post(() => AnimationEvent?.Invoke(animEvent));
            _lastEventFrame = _playheadFrames;

            var frame = Loop ? animation.FrameAtTime(_playheadFrames / Math.Max(1, animation.Fps)) : (float)_playheadFrames;
            pose = rig.Evaluate(animation, frame);
        }

        var setup = _renderer.GetRealtimeFrameSetup(_studioData, Specifications, aspect);
        if (setup.IsFailure)
            return null;

        // Use the pose's face when it's ready, otherwise the default face (built first), so the head never pops out.
        var (head, key) = HeadFor(pose.Expression);
        if (head is null)
        {
            RequestHead(_studioData, pose.Expression);
            return null;
        }

        return new MiiGpuFrame(setup.Value, pose, head, key);
    }

    private MiiRig RigFor(Mii mii)
    {
        if (!_rigs.TryGetValue(mii.IsGirl, out var rig))
            _rigs[mii.IsGirl] = rig = new MiiRig(MiiBodyModel.Get(mii.IsGirl));
        return rig;
    }

    private (IReadOnlyList<HeadMeshData>? Head, string? Key) HeadFor(MiiExpression expression)
    {
        var key = HeadKey(_studioData!, expression);
        if (_heads.TryGetValue(key, out var head))
            return (head, key);
        RequestHead(_studioData!, expression);
        var fallback = HeadKey(_studioData!, MiiExpression.Normal);
        return _heads.TryGetValue(fallback, out var normal) ? (normal, fallback) : (null, null);
    }

    private static string HeadKey(string studio, MiiExpression expression) => $"{studio}|{(int)expression}";

    private IEnumerable<MiiExpression> UsedExpressions() =>
        _animation?.TryGetCurve(TrackId.Expression)?.Keys.Select(k => MiiExpressionInfo.FromValue(k.Value)).Distinct() ?? [];

    /// <summary>Builds a head in the background (one at a time so the UI stays smooth).</summary>
    private void RequestHead(string studio, MiiExpression expression)
    {
        var key = HeadKey(studio, expression);
        if (_heads.ContainsKey(key) || !_building.TryAdd(key, 0))
            return;

        _ = Task.Run(async () =>
        {
            await _buildGate.WaitAsync();
            try
            {
                if (!IsRecent(studio))
                    return;
                var result = _renderer.BuildHeadModel(studio, (int)expression);
                if (result.IsSuccess && IsRecent(studio))
                    _heads[key] = result.Value;
            }
            finally
            {
                _buildGate.Release();
                _building.TryRemove(key, out _);
                Dispatcher.UIThread.Post(RequestNextFrameRendering);
            }
        });
    }

    private void ReportUnavailable(string message)
    {
        if (_unavailableReported)
            return;
        _unavailableReported = true;
        Dispatcher.UIThread.Post(() => RealtimeUnavailable?.Invoke(message));
    }
}

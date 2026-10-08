using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Rig;
using Silk.NET.OpenGL;
using WheelWizard.MiiAnimations.Playback;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Realtime;
using WheelWizard.MiiRendering.Services;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// Realtime (GPU) Mii view: draws a Mii with OpenGL on a transparent background, so it can be layered over any UI,
/// and plays Mii animations (with their particles) at the display's frame rate. Framing, lighting and colours match
/// the CPU renderer for the same <see cref="Specifications"/>.
/// <para>
/// Play a single clip with <see cref="Animation"/>, or drive <see cref="Player"/> directly to cross-fade between
/// clips. If OpenGL isn't available <see cref="RealtimeUnavailable"/> fires; callers can fall back to the CPU renderer.
/// </para>
/// </summary>
public sealed partial class MiiRealtimeView : OpenGlControlBase
{
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Centre of the FFL head in head-mesh units (the head is roughly an ellipsoid around it).</summary>
    private static readonly Vector3 HeadCenter = new(0f, 35f, -3.7f);

    private readonly IMiiNativeRenderer _renderer;
    private readonly Stopwatch _clock = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<HeadMeshData>> _heads = new();
    private readonly ConcurrentDictionary<string, byte> _building = new();
    private readonly Dictionary<bool, MiiRig> _rigs = new();
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private readonly MiiAnimationPlayer _player = new();
    private readonly HashSet<MiiExpression> _preloadedExpressions = [];
    private readonly Dictionary<MiiAnimation, RigParticleStage> _particleStages = new();
    private readonly List<Particle> _particles = [];

    private MiiGpuRenderer? _gpu;
    private bool _initialized;
    private bool _unavailableReported;
    private IReadOnlySet<string>? _retainHeads;
    private DispatcherTimer? _initWatchdog;

    private Mii? _mii;
    private string? _studioData;
    private string? _shownStudio;
    private string? _lastStudio;
    private MiiGpuFrame? _lastFrame;
    private MiiAnimation? _animation;
    private MiiAnimation? _headsRequestedFor;
    private bool _loop = true;
    private TimeSpan _lastTick;

    private (Vector3 Position, Vector3 Target, Vector3 Up)? _camera;
    private (Vector3 Position, Vector3 Target, Vector3 Up)? _cameraFrom;
    private TimeSpan _cameraTransitionStart;
    private TimeSpan _cameraTransitionLength;

    private bool _particleStagesFemale;
    private Vector3 _particleBodyScale = MiiStage.DefaultBodyScale;
    private Vector3 _particleColor = Vector3.One;

    public MiiRealtimeView(IMiiNativeRenderer renderer)
    {
        _renderer = renderer;
        _clock.Start();
        _player.EventReached += (clip, animEvent) =>
        {
            ClipEvent?.Invoke(clip, animEvent);
            AnimationEvent?.Invoke(animEvent);
        };
        _player.Finished += clip =>
        {
            ClipFinished?.Invoke(clip);
            if (ReferenceEquals(clip, _animation))
                AnimationFinished?.Invoke();
        };
    }

    /// <summary>Camera framing, character rotation and zoom, same meaning as for rendered images.</summary>
    public MiiImageSpecifications Specifications { get; set; } =
        new()
        {
            Name = "Realtime",
            Type = MiiImageSpecifications.BodyType.all_body,
            Size = MiiImageSpecifications.ImageSize.medium,
        };

    /// <summary>Plays the animations with cross-fades and an additive look layer. See <see cref="MiiAnimationPlayer"/>.</summary>
    public MiiAnimationPlayer Player => _player;

    public bool IsPlaying
    {
        get => !_player.IsPaused;
        set
        {
            _player.IsPaused = !value;
            RequestNextFrameRendering();
        }
    }

    public double Speed
    {
        get => _player.Speed;
        set => _player.Speed = value;
    }

    /// <summary>Loop <see cref="Animation"/>, or stop on its last frame (and raise <see cref="AnimationFinished"/>).</summary>
    public bool Loop
    {
        get => _loop;
        set
        {
            _loop = value;
            if (_animation is not null && ReferenceEquals(_player.Current, _animation))
                Restart();
        }
    }

    /// <summary>
    /// Moves the picture sideways by this many pixels (e.g. two Miis side by side in two views on top of each other).
    /// The projection is shifted rather than the control, so picking and projecting points follow along.
    /// </summary>
    public double ScreenShiftX
    {
        get => _screenShiftX;
        set
        {
            _screenShiftX = value;
            RequestNextFrameRendering();
        }
    }

    private double _screenShiftX;

    /// <summary>Scales the picture around the middle of the view (like zooming, but without moving the camera).</summary>
    public double ScreenScale
    {
        get => _screenScale;
        set
        {
            _screenScale = value;
            RequestNextFrameRendering();
        }
    }

    private double _screenScale = 1;

    /// <summary>Fades the whole Mii (0 = invisible).</summary>
    public float Alpha
    {
        get => _alpha;
        set
        {
            _alpha = Math.Clamp(value, 0f, 1f);
            RequestNextFrameRendering();
        }
    }

    private float _alpha = 1f;

    /// <summary>Keep drawing every display frame even when nothing animates (e.g. while the caller eases the camera).</summary>
    public bool ContinuousRendering { get; set; }

    /// <summary>Playhead in frames (unwrapped while looping).</summary>
    public double PlayheadFrames => _player.Frame;

    public bool IsRealtimeAvailable => _initialized && _gpu is not null;

    /// <summary>Frames drawn so far (for an FPS readout).</summary>
    public long RenderedFrames { get; private set; }

    /// <summary>The pose and framing of the frame drawn last, e.g. for hit testing. Null before the first frame.</summary>
    public MiiGpuFrame? LastFrame => _lastFrame;

    /// <summary>Fired (on the UI thread) for every event marker playback passes.</summary>
    public event Action<AnimEvent>? AnimationEvent;

    /// <summary>Like <see cref="AnimationEvent"/>, with the clip the event belongs to (for callers that play several).</summary>
    public event Action<MiiAnimation, AnimEvent>? ClipEvent;

    /// <summary>Fired once when a non-looping <see cref="Animation"/> reaches its end.</summary>
    public event Action? AnimationFinished;

    /// <summary>Fired once when any one-shot clip played on <see cref="Player"/> reaches its end.</summary>
    public event Action<MiiAnimation>? ClipFinished;

    /// <summary>Fired on the UI thread at the start of every drawn frame with the seconds since the previous one.</summary>
    public event Action<double>? FrameUpdating;

    /// <summary>Fired once each time a newly set Mii is first drawn (its head finished building), with its studio data.</summary>
    public event Action<string>? MiiShown;

    /// <summary>OpenGL couldn't be used; the message says why. Fall back to the CPU renderer.</summary>
    public event Action<string>? RealtimeUnavailable;

    public Mii? Mii
    {
        get => _mii;
        set => SetMii(value, null);
    }

    /// <summary>
    /// Shows a Mii. Pass <paramref name="studioData"/> when the caller already serialized it (e.g. with the April
    /// Fools' variant); otherwise it's serialized here. The previous Mii stays on screen until this one's head is built.
    /// </summary>
    public void SetMii(Mii? mii, string? studioData) => SetMii(mii, studioData, null);

    /// <summary>
    /// Like <see cref="SetMii(Mii?, string?)"/>, but when only one part of the head changed, that part swaps with a
    /// short transition: it slides out one way while the new one slides in from the other side (see
    /// <see cref="HeadPartChange"/>).
    /// </summary>
    public void SetMii(Mii? mii, string? studioData, HeadPartChange? change)
    {
        _mii = mii;
        studioData ??= mii is null ? null : Serialize(mii);
        if (studioData != _studioData)
        {
            BeginPartChange(change, _shownStudio ?? _lastStudio, studioData);
            _shownStudio = null;
        }
        _studioData = studioData;
        if (studioData is not null)
            Remember(studioData, prewarm: false);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Builds the heads of a Mii you're about to show (e.g. the other gender for a swap_gender event) so switching
    /// to it is instant. The last <see cref="RecentMiiCount"/> Miis stay cached.
    /// </summary>
    public void Prewarm(Mii mii)
    {
        if (Serialize(mii) is { } studio)
            Remember(studio, prewarm: true);
    }

    /// <summary>Builds the faces <paramref name="clip"/> uses ahead of time, so they show as soon as it plays.</summary>
    public void Preload(MiiAnimation clip)
    {
        var added = MiiAnimationPlayer.ExpressionsOf(clip).Where(_preloadedExpressions.Add).ToList();
        if (added.Count == 0)
            return;
        lock (_recentStudios)
            foreach (var studio in _recentStudios.ToList())
            foreach (var expression in added)
                RequestHead(studio, expression);
    }

    private const int RecentMiiCount = 3;
    private readonly List<string> _recentStudios = [];
    private readonly HashSet<string> _prewarmedStudios = [];

    private static string? Serialize(Mii mii) =>
        MiiStudioDataSerializer.Serialize(mii) is { IsSuccess: true } serialized ? serialized.Value : null;

    private void Remember(string studio, bool prewarm)
    {
        lock (_recentStudios)
        {
            _recentStudios.Remove(studio);
            _recentStudios.Insert(0, studio);
            if (_recentStudios.Count > RecentMiiCount)
                _recentStudios.RemoveRange(RecentMiiCount, _recentStudios.Count - RecentMiiCount);
            if (prewarm)
                _prewarmedStudios.Add(studio);
            _prewarmedStudios.IntersectWith(_recentStudios);
            // Free CPU and GPU copies of heads of Miis that dropped out (but keep the one on screen until it's replaced).
            var onScreen = _lastStudio;
            foreach (var key in _heads.Keys.Where(k => !_recentStudios.Append(onScreen).Any(s => s is not null && k.StartsWith(s + "|"))))
                _heads.TryRemove(key, out _);
            _retainHeads = _heads.Keys.ToHashSet();
        }

        RequestHead(studio, MiiExpression.Normal);
        foreach (var expression in UsedExpressions())
            RequestHead(studio, expression);
    }

    /// <summary>
    /// Whether a head of this Mii is still worth building: it's the one on screen or one that was prewarmed. Miis that
    /// were replaced before their head got built (e.g. while dragging a slider in the editor) are skipped.
    /// </summary>
    private bool IsWanted(string studio)
    {
        lock (_recentStudios)
            return _recentStudios.Contains(studio) && (studio == _studioData || _prewarmedStudios.Contains(studio));
    }

    /// <summary>A single animation to play (looping per <see cref="Loop"/>). Setting it starts it from the beginning.</summary>
    public MiiAnimation? Animation
    {
        get => _animation;
        set
        {
            _animation = value;
            if (value is not null)
                Preload(value);
            Restart();
        }
    }

    /// <summary>Starts <see cref="Animation"/> from the beginning (events on frame 0 fire again).</summary>
    public void Restart()
    {
        if (_animation is { } animation)
            _player.Play(animation, _loop, fadeSeconds: 0);
        else
            _player.Stop(fadeSeconds: 0);
        _lastTick = _clock.Elapsed;
        RequestNextFrameRendering();
    }

    /// <summary>Jumps to a frame without firing the events in between.</summary>
    public void Seek(double frame)
    {
        _player.Seek(frame);
        RequestNextFrameRendering();
    }

    /// <summary>Redraw after changing <see cref="Specifications"/> or other settings.</summary>
    public void Invalidate() => RequestNextFrameRendering();

    /// <summary>Changes <see cref="Specifications"/> and glides the camera to its framing over <paramref name="duration"/>.</summary>
    public void TransitionTo(MiiImageSpecifications specifications, TimeSpan duration)
    {
        Specifications = specifications;
        _cameraFrom = duration > TimeSpan.Zero ? _camera : null;
        _cameraTransitionStart = _clock.Elapsed;
        _cameraTransitionLength = duration;
        RequestNextFrameRendering();
    }

    private bool IsCameraMoving => _cameraFrom is not null && _clock.Elapsed - _cameraTransitionStart < _cameraTransitionLength;

    #region Screen geometry (of the last drawn frame)

    /// <summary>Where a point in stage space (see <see cref="MiiStage"/>) appears in this control, or null when behind the camera.</summary>
    public Point? ProjectToScreen(Vector3 stagePoint)
    {
        if (_lastFrame is not { } frame)
            return null;
        var world = Vector3.Transform(stagePoint, frame.Setup.StageToWorld);
        var clip = Vector4.Transform(new Vector4(world, 1f), frame.Setup.View * frame.Setup.Projection);
        if (clip.W <= 1e-4f)
            return null;
        return new Point((clip.X / clip.W * 0.5 + 0.5) * Bounds.Width, (0.5 - clip.Y / clip.W * 0.5) * Bounds.Height);
    }

    /// <summary>Stage position of a bone in the last drawn pose.</summary>
    public Vector3? BoneOnStage(MiiBone bone) => _lastFrame is { } frame ? MiiStage.PointOf(frame.Pose, frame.Setup.BodyScale, bone) : null;

    /// <summary>Stage position of the middle of the head in the last drawn pose.</summary>
    public Vector3? HeadCenterOnStage() =>
        _lastFrame is { } frame ? Vector3.Transform(HeadCenter, MiiStage.HeadToStage(frame.Pose, frame.Setup.BodyScale)) : null;

    /// <summary>The camera ray through a point of this control, in stage space.</summary>
    public (Vector3 Origin, Vector3 Direction)? ScreenRay(Point point)
    {
        if (_lastFrame is not { } frame || Bounds.Width <= 0 || Bounds.Height <= 0)
            return null;
        if (!Matrix4x4.Invert(frame.Setup.StageToWorld * frame.Setup.View * frame.Setup.Projection, out var inverse))
            return null;
        var x = (float)(point.X / Bounds.Width * 2 - 1);
        var y = (float)(1 - point.Y / Bounds.Height * 2);
        var near = Vector4.Transform(new Vector4(x, y, -1f, 1f), inverse);
        var far = Vector4.Transform(new Vector4(x, y, 1f, 1f), inverse);
        var origin = new Vector3(near.X, near.Y, near.Z) / near.W;
        var end = new Vector3(far.X, far.Y, far.Z) / far.W;
        return (origin, Vector3.Normalize(end - origin));
    }

    #endregion

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lastTick = _clock.Elapsed;
        // Some systems never give us a GL context (and never call OnOpenGlInit); report that too. Only time spent
        // visible counts, since a hidden control is never asked to render.
        _initWatchdog?.Stop();
        if (_initialized)
            return;
        var visibleFor = TimeSpan.Zero;
        _initWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _initWatchdog.Tick += (_, _) =>
        {
            if (_initialized)
            {
                _initWatchdog?.Stop();
                return;
            }
            if (IsEffectivelyVisible && Bounds is { Width: > 0, Height: > 0 })
                visibleFor += _initWatchdog!.Interval;
            if (visibleFor < InitTimeout)
                return;
            _initWatchdog?.Stop();
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

        if (_player.IsAnimating || IsCameraMoving || ContinuousRendering || IsPartChanging)
            RequestNextFrameRendering();
    }

    private MiiGpuFrame? BuildFrame(double deltaSeconds, float aspect)
    {
        FrameUpdating?.Invoke(deltaSeconds);
        _player.Update(deltaSeconds);
        // Raise events after this frame, so handlers can change the Mii or play the next clip without re-entering.
        Dispatcher.UIThread.Post(_player.RaiseEvents);

        if (_mii is null || _studioData is null)
            return null;

        // A clip just started: build its faces now, while its first frames play with the ones that are ready.
        if (!ReferenceEquals(_player.Current, _headsRequestedFor))
        {
            _headsRequestedFor = _player.Current;
            if (_player.Current is { } clip)
                foreach (var expression in MiiAnimationPlayer.ExpressionsOf(clip))
                    RequestHead(_studioData, expression);
        }

        var pose = _player.Evaluate(RigFor(_mii));

        // Use the pose's face when it's ready, otherwise the default face (built first). While the new Mii's head is
        // still building, keep drawing the previous Mii so the view never blinks empty.
        var (head, key) = HeadFor(pose.Expression);
        if (head is null || !IsPartChangeReady(pose.Expression))
            return PreviousMiiFrame(pose, aspect);

        var setup = _renderer.GetRealtimeFrameSetup(_studioData, Specifications, aspect);
        if (setup.IsFailure)
            return null;

        if (_shownStudio != _studioData)
        {
            var shown = _shownStudio = _studioData;
            Dispatcher.UIThread.Post(() => MiiShown?.Invoke(shown!));
        }
        _lastStudio = _studioData;
        var frameSetup = Shift(MoveCamera(setup.Value));
        return _lastFrame = new MiiGpuFrame(frameSetup, pose, head, key)
        {
            Alpha = _alpha,
            Particles = Particles(frameSetup),
            HeadPasses = HeadPasses(pose.Expression, head!, key!),
            BodyHoverMask = BodyHoverMask,
        };
    }

    private MiiGpuFrame? PreviousMiiFrame(MiiPose pose, float aspect)
    {
        if (_lastFrame is not { } last || _lastStudio is null)
            return null;
        if (_heads.TryGetValue(last.HeadKey!, out var head) is false)
            return null;
        // The previous Mii may have the other body; only reuse the pose when it fits.
        if (last.Setup.Female != _mii?.IsGirl)
            pose = _player.Evaluate(RigFor(last.Setup.Female));
        var setup = _renderer.GetRealtimeFrameSetup(_lastStudio, Specifications, aspect);
        if (setup.IsFailure)
            return null;
        var frameSetup = Shift(MoveCamera(setup.Value));
        return _lastFrame = last with
        {
            Alpha = _alpha,
            Setup = frameSetup,
            Pose = pose,
            Head = head,
            Particles = Particles(frameSetup),
            HeadPasses = null,
            BodyHoverMask = BodyHoverMask,
        };
    }

    /// <summary>Applies <see cref="ScreenScale"/> and <see cref="ScreenShiftX"/> in clip space.</summary>
    private MiiRealtimeFrameSetup Shift(MiiRealtimeFrameSetup setup)
    {
        if (_screenShiftX == 0 && _screenScale == 1 || Bounds.Width <= 0)
            return setup;
        var shift = Matrix4x4.Identity;
        shift.M11 = (float)_screenScale;
        shift.M22 = (float)_screenScale;
        shift.M41 = (float)(2 * _screenShiftX / Bounds.Width);
        return setup with { Projection = setup.Projection * shift };
    }

    /// <summary>Applies a running camera transition and remembers where the camera is.</summary>
    private MiiRealtimeFrameSetup MoveCamera(MiiRealtimeFrameSetup setup)
    {
        if (IsCameraMoving && _cameraFrom is { } from)
        {
            var t = (float)((_clock.Elapsed - _cameraTransitionStart) / _cameraTransitionLength);
            t = t * t * t * (t * (t * 6f - 15f) + 10f);
            setup = setup.WithCamera(
                Vector3.Lerp(from.Position, setup.CameraPosition, t),
                Vector3.Lerp(from.Target, setup.CameraTarget, t),
                Vector3.Normalize(Vector3.Lerp(from.Up, setup.CameraUp, t))
            );
        }
        else
        {
            _cameraFrom = null;
        }

        _camera = (setup.CameraPosition, setup.CameraTarget, setup.CameraUp);
        return setup;
    }

    private List<Particle> Particles(MiiRealtimeFrameSetup setup)
    {
        if (setup.Female != _particleStagesFemale)
        {
            _particleStagesFemale = setup.Female;
            _particleStages.Clear();
        }

        _particleBodyScale = setup.BodyScale;
        _particleColor = new Vector3(setup.BodyColor.X, setup.BodyColor.Y, setup.BodyColor.Z);
        _particles.Clear();
        _player.CollectParticles(ParticleStage, _particles);
        return _particles.Count == 0 ? [] : _particles.ToList();
    }

    private RigParticleStage ParticleStage(MiiAnimation clip)
    {
        if (!_particleStages.TryGetValue(clip, out var stage))
        {
            if (_particleStages.Count > 16)
                _particleStages.Clear();
            _particleStages[clip] = stage = new RigParticleStage(
                clip,
                _ => RigFor(_particleStagesFemale),
                _ => _particleBodyScale,
                _ => _particleColor
            );
        }

        return stage;
    }

    private MiiRig RigFor(Mii mii) => RigFor(mii.IsGirl);

    private MiiRig RigFor(bool female)
    {
        if (!_rigs.TryGetValue(female, out var rig))
            _rigs[female] = rig = new MiiRig(MiiBodyModel.Get(female));
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

    private IEnumerable<MiiExpression> UsedExpressions()
    {
        var used = new HashSet<MiiExpression>(_preloadedExpressions);
        if (_animation is { } animation)
            used.UnionWith(MiiAnimationPlayer.ExpressionsOf(animation));
        if (_player.Current is { } current)
            used.UnionWith(MiiAnimationPlayer.ExpressionsOf(current));
        return used;
    }

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
                if (!IsWanted(studio))
                    return;
                var result = _renderer.BuildHeadModel(studio, (int)expression);
                if (result.IsSuccess && IsWanted(studio))
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

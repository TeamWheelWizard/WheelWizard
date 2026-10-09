using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Rig;

namespace WheelWizard.MiiAnimations.Playback;

/// <summary>
/// Plays Mii animations one after another with short cross-fades, so switching clips (idle → reaction → idle)
/// never pops. On top of the clips sits an additive look layer (<see cref="Look"/>) that turns the head and chest,
/// e.g. to follow the cursor. Particles of a clip keep flying after it was replaced.
/// <para>
/// Not thread-safe: call everything from the thread that renders (the UI thread for Avalonia's OpenGL controls).
/// </para>
/// </summary>
public sealed class MiiAnimationPlayer
{
    public const double DefaultFadeSeconds = 0.25;

    private static readonly TrackId[] AllTracks =
    [
        .. MiiSkeletonInfo.AllBones.SelectMany(bone => ChannelInfo.BoneChannels.Select(channel => TrackId.Bone(bone, channel))),
        .. MiiSkeletonInfo.AllLimbs.SelectMany(limb => ChannelInfo.LimbChannels.Select(channel => TrackId.Limb(limb, channel))),
        TrackId.Expression,
        TrackId.Visible,
    ];

    private sealed class Track(MiiAnimation clip, bool loop)
    {
        public MiiAnimation Clip { get; } = clip;
        public bool Loop { get; } = loop;

        /// <summary>Unwrapped playhead in frames (a one-shot clip stops at its length).</summary>
        public double Frame { get; private set; }

        /// <summary>Keeps counting after a one-shot clip ended, so its particles finish flying.</summary>
        public double ParticleFrame { get; private set; }

        public double LastEventFrame { get; set; } = -1;
        public bool Finished { get; set; }

        public float ClipFrame => Loop ? Clip.FrameAtTime(Frame / Math.Max(1, Clip.Fps)) : (float)Math.Min(Frame, Clip.Length);

        public float ParticleClipFrame => Loop ? ClipFrame : (float)ParticleFrame;

        public bool HasParticlesLeft =>
            Clip.Particles.Count > 0 && (Loop || Clip.Particles.Any(p => p.EndFrame(Math.Max(1, Clip.Fps)) >= ParticleFrame));

        public void Advance(double seconds)
        {
            var frames = seconds * Clip.Fps;
            Frame = Loop ? Frame + frames : Math.Min(Frame + frames, Clip.Length);
            ParticleFrame += frames;
        }

        public void JumpTo(double frame)
        {
            Frame = ParticleFrame = LastEventFrame = frame;
            Finished = !Loop && frame >= Clip.Length;
        }
    }

    private Track? _current;
    private Track? _previous;
    private Func<TrackId, float>? _fadeFrom;
    private double _fadeSeconds;
    private double _fadeElapsed;
    private readonly List<Track> _particleTails = [];
    private readonly List<(MiiAnimation Clip, AnimEvent Event)> _pendingEvents = [];
    private readonly List<MiiAnimation> _pendingFinished = [];

    /// <summary>The clip playing (or holding its last frame), null for the rest pose.</summary>
    public MiiAnimation? Current => _current?.Clip;

    public bool IsLooping => _current?.Loop ?? false;

    /// <summary>Whether the current one-shot clip reached its end (or nothing plays).</summary>
    public bool IsFinished => _current is null || _current.Finished;

    /// <summary>Playhead of the current clip in frames (unwrapped while looping).</summary>
    public double Frame => _current?.Frame ?? 0;

    public double Speed { get; set; } = 1;

    public bool IsPaused { get; set; }

    /// <summary>Additive head/chest turn, e.g. to look at the cursor.</summary>
    public MiiLookLayer Look { get; } = new();

    /// <summary>Whether frames still change over time (playing, fading, particles flying or the head turning).</summary>
    public bool IsAnimating =>
        !IsPaused
            && (
                (_current is { } current && (current.Loop || !current.Finished || current.HasParticlesLeft))
                || _fadeFrom is not null
                || _particleTails.Count > 0
            )
        || Look.IsMoving;

    /// <summary>Raised from <see cref="RaiseEvents"/> for every event marker playback passed.</summary>
    public event Action<MiiAnimation, AnimEvent>? EventReached;

    /// <summary>Raised from <see cref="RaiseEvents"/> once when a one-shot clip reached its end (it then holds its last frame).</summary>
    public event Action<MiiAnimation>? Finished;

    /// <summary>
    /// Raised when playback changes from outside the frame loop (a clip starts, stops or jumps), so a view that stopped
    /// drawing because nothing moved draws again.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Starts <paramref name="clip"/> from the beginning, cross-fading from whatever is showing now.
    /// One-shot clips hold their last frame when done; <see cref="Finished"/> tells you when to play the next one.
    /// </summary>
    public void Play(MiiAnimation clip, bool loop = false, double fadeSeconds = DefaultFadeSeconds)
    {
        FadeOutCurrent(fadeSeconds);
        _current = new Track(clip, loop);
        Changed?.Invoke();
    }

    /// <summary>Fades back to the rest pose.</summary>
    public void Stop(double fadeSeconds = DefaultFadeSeconds)
    {
        FadeOutCurrent(fadeSeconds);
        _current = null;
        Changed?.Invoke();
    }

    /// <summary>Jumps within the current clip without firing the events in between.</summary>
    public void Seek(double frame)
    {
        _current?.JumpTo(Math.Max(0, frame));
        Changed?.Invoke();
    }

    private void FadeOutCurrent(double fadeSeconds)
    {
        if (_current is { } current && current.HasParticlesLeft && !_particleTails.Contains(current))
            _particleTails.Add(current);

        if (fadeSeconds <= 0)
        {
            _fadeFrom = null;
            _previous = null;
            return;
        }

        // Fade from what's on screen. Mid-fade that's a mix of two clips, so freeze it; otherwise keep the old clip moving.
        if (_fadeFrom is not null)
        {
            var frozen = AllTracks.ToDictionary(id => id, SampleClips);
            _fadeFrom = id => frozen.TryGetValue(id, out var value) ? value : ChannelInfo.DefaultValue(id);
            _previous = null;
        }
        else if (_current is { } live)
        {
            _previous = live;
            _fadeFrom = id => live.Clip.Evaluate(id, live.ClipFrame);
        }
        else
        {
            _fadeFrom = ChannelInfo.DefaultValue;
        }

        _fadeSeconds = fadeSeconds;
        _fadeElapsed = 0;
    }

    /// <summary>Advances time. Then raise events with <see cref="RaiseEvents"/> (kept separate so callers can post them).</summary>
    public void Update(double deltaSeconds)
    {
        deltaSeconds = Math.Clamp(deltaSeconds, 0, 0.25);
        Look.Update(deltaSeconds);
        if (IsPaused)
            return;

        var scaled = deltaSeconds * Speed;
        if (_current is { } current)
            Advance(current, scaled);

        // The replaced clip keeps moving while it fades out.
        if (_previous is { } previous && !_particleTails.Contains(previous))
            previous.Advance(scaled);
        _fadeElapsed += deltaSeconds;
        if (_fadeFrom is not null && _fadeElapsed >= _fadeSeconds)
        {
            _fadeFrom = null;
            _previous = null;
        }

        for (var i = _particleTails.Count - 1; i >= 0; i--)
        {
            var tail = _particleTails[i];
            if (ReferenceEquals(tail, _current))
                continue;
            tail.Advance(scaled);
            if (!tail.HasParticlesLeft || tail.Loop && tail.ParticleFrame > tail.Clip.Length * 2)
                _particleTails.RemoveAt(i);
        }
    }

    private void Advance(Track track, double seconds)
    {
        var clip = track.Clip;
        track.Advance(seconds);
        foreach (var animEvent in clip.EventsBetween(track.LastEventFrame, track.Frame, track.Loop))
            _pendingEvents.Add((clip, animEvent));
        track.LastEventFrame = track.Frame;
        if (!track.Loop && !track.Finished && track.Frame >= clip.Length)
        {
            track.Finished = true;
            _pendingFinished.Add(clip);
        }
    }

    /// <summary>Whether <see cref="Update"/> collected events or finishes for <see cref="RaiseEvents"/>.</summary>
    public bool HasPendingEvents => _pendingEvents.Count > 0 || _pendingFinished.Count > 0;

    /// <summary>Raises the events and finishes collected by <see cref="Update"/>.</summary>
    public void RaiseEvents()
    {
        if (_pendingEvents.Count == 0 && _pendingFinished.Count == 0)
            return;
        var events = _pendingEvents.ToList();
        var finished = _pendingFinished.ToList();
        _pendingEvents.Clear();
        _pendingFinished.Clear();
        foreach (var (clip, animEvent) in events)
            EventReached?.Invoke(clip, animEvent);
        foreach (var clip in finished)
            Finished?.Invoke(clip);
    }

    /// <summary>The value of a channel right now: clips cross-faded, plus the look layer.</summary>
    public float Sample(TrackId id) => SampleClips(id) + Look.Offset(id);

    private float SampleClips(TrackId id)
    {
        var value = _current is { } current ? current.Clip.Evaluate(id, current.ClipFrame) : ChannelInfo.DefaultValue(id);
        if (_fadeFrom is not { } from)
            return value;

        var t = (float)Ease(_fadeElapsed / Math.Max(1e-6, _fadeSeconds));
        var start = from(id);
        if (ChannelInfo.IsDiscrete(id.Channel))
            return t < 0.5f ? start : value;
        // Turn the short way: a clip that ends after a full spin (360°) shouldn't unwind into the next one.
        if (id.Channel is Channel.RotX or Channel.RotY or Channel.RotZ)
            return start + WrapDegrees(value - start) * t;
        return start + (value - start) * t;
    }

    public MiiPose Evaluate(MiiRig rig) => rig.Evaluate(Sample);

    /// <summary>Particles alive now, from the current clip and replaced clips whose particles are still flying.</summary>
    public void CollectParticles(Func<MiiAnimation, IParticleStage> stageFor, List<Particle> output)
    {
        if (_current is { } current && current.Clip.Particles.Count > 0)
            ParticleEvaluator.Evaluate(current.Clip, current.ParticleClipFrame, stageFor(current.Clip), output, current.Loop);
        foreach (var tail in _particleTails)
            if (!ReferenceEquals(tail, _current))
                ParticleEvaluator.Evaluate(tail.Clip, tail.ParticleClipFrame, stageFor(tail.Clip), output, tail.Loop);
    }

    /// <summary>Expressions a clip uses, so their heads can be built ahead of time.</summary>
    public static IEnumerable<MiiExpression> ExpressionsOf(MiiAnimation clip) =>
        clip.TryGetCurve(TrackId.Expression)?.Keys.Select(k => MiiExpressionInfo.FromValue(k.Value)).Distinct() ?? [];

    private static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static float WrapDegrees(float degrees)
    {
        degrees %= 360f;
        return degrees switch
        {
            > 180f => degrees - 360f,
            < -180f => degrees + 360f,
            _ => degrees,
        };
    }
}

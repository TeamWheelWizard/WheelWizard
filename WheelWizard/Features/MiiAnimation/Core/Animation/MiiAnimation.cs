namespace MiiAnim.Core.Animation;

public sealed class MiiAnimation
{
    public const int MiiDataLength = 74;

    /// <summary>Set on an extra actor's motion: timing, events, actors and particles then belong to this animation.</summary>
    private readonly MiiAnimation? _owner;
    private string _name = "Untitled";
    private int _fps = 60;
    private int _length = 120;
    private readonly List<AnimEvent> _events = [];
    private readonly List<AnimActor> _actors = [];
    private readonly List<ParticleEffect> _particles = [];

    public MiiAnimation() { }

    private MiiAnimation(MiiAnimation owner) => _owner = owner;

    public string Name
    {
        get => _owner?.Name ?? _name;
        set
        {
            if (_owner is null)
                _name = value;
            else
                _owner.Name = value;
        }
    }

    /// <summary>Frames per second. Keys are on whole frames.</summary>
    public int Fps
    {
        get => _owner?.Fps ?? _fps;
        set
        {
            if (_owner is null)
                _fps = value;
            else
                _owner.Fps = value;
        }
    }

    /// <summary>Loop length in frames. Playback wraps from <see cref="Length"/> back to frame 0.</summary>
    public int Length
    {
        get => _owner?.Length ?? _length;
        set
        {
            if (_owner is null)
                _length = value;
            else
                _owner.Length = value;
        }
    }

    /// <summary>Optional 74-byte Wii Mii the animation was authored with (preview only, players use their own Mii).</summary>
    public byte[]? AuthorMii { get; set; }

    /// <summary>The main Mii's tracks (or, on an extra actor's <see cref="AnimActor.Motion"/>, that actor's).</summary>
    public Dictionary<TrackId, AnimCurve> Tracks { get; } = new();

    /// <summary>Named event markers, kept sorted by frame.</summary>
    public List<AnimEvent> Events => _owner?.Events ?? _events;

    /// <summary>Extra Miis (actor 1, 2, …). The main Mii is actor 0 and uses <see cref="Tracks"/>.</summary>
    public List<AnimActor> Actors => _owner?.Actors ?? _actors;

    /// <summary>Particle effects, fully described in the file (no app support needed).</summary>
    public List<ParticleEffect> Particles => _owner?.Particles ?? _particles;

    /// <summary>The animation that owns the timing, events, actors and particles (itself, unless this is an actor's motion).</summary>
    public MiiAnimation Root => _owner ?? this;

    /// <summary>Main Mii plus extra actors.</summary>
    public int ActorCount => Actors.Count + 1;

    /// <summary>Tracks of actor <paramref name="index"/>: 0 is the main Mii, 1+ the extra <see cref="Actors"/>.</summary>
    public MiiAnimation ForActor(int index) => index <= 0 ? Root : Actors[index - 1].Motion;

    /// <summary>Index of the actor with this name (case-insensitive), or -1.</summary>
    public int FindActor(string name) =>
        Actors.FindIndex(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i + 1 : -1;

    /// <summary>Adds an extra Mii with no tracks yet.</summary>
    public AnimActor AddActor(string name)
    {
        name = name.Trim();
        if (name.Length > AnimActor.MaxNameLength)
            name = name[..AnimActor.MaxNameLength];
        var actor = new AnimActor(name, new MiiAnimation(Root));
        Actors.Add(actor);
        return actor;
    }

    /// <summary>Adds an event (or renames the one already on that frame with the same name) and keeps the list sorted.</summary>
    public void AddEvent(int frame, string name)
    {
        name = name.Trim();
        if (name.Length > AnimEvent.MaxNameLength)
            name = name[..AnimEvent.MaxNameLength];
        var animEvent = new AnimEvent(Math.Max(0, frame), name);
        if (Events.Contains(animEvent))
            return;
        var index = Events.FindIndex(e => e.Frame > animEvent.Frame);
        Events.Insert(index < 0 ? Events.Count : index, animEvent);
    }

    /// <summary>
    /// Events crossed while playback moves from <paramref name="fromFrame"/> (exclusive) to <paramref name="toFrame"/>
    /// (inclusive). Frames are unwrapped (keep counting past <see cref="Length"/> while looping); start a fresh
    /// playback from -1 so events on frame 0 fire too. With <paramref name="looping"/> off the animation plays once.
    /// </summary>
    public IEnumerable<AnimEvent> EventsBetween(double fromFrame, double toFrame, bool looping = true)
    {
        if (Events.Count == 0 || toFrame <= fromFrame || Length <= 0)
            yield break;
        var firstLoop = looping ? (long)Math.Floor(Math.Max(fromFrame, 0) / Length) : 0;
        var lastLoop = looping ? (long)Math.Floor(toFrame / Length) : 0;
        for (var loop = firstLoop; loop <= lastLoop; loop++)
        {
            foreach (var animEvent in Events)
            {
                var at = loop * (double)Length + animEvent.Frame;
                if (at > fromFrame && at <= toFrame)
                    yield return animEvent;
            }
        }
    }

    public float DurationSeconds => Length / (float)Math.Max(1, Fps);

    public AnimCurve? TryGetCurve(TrackId id) => Tracks.TryGetValue(id, out var curve) && curve.Count > 0 ? curve : null;

    public AnimCurve GetOrCreateCurve(TrackId id)
    {
        if (!Tracks.TryGetValue(id, out var curve))
        {
            curve = new AnimCurve();
            Tracks[id] = curve;
        }

        return curve;
    }

    public float Evaluate(TrackId id, float frame)
    {
        var defaultValue = ChannelInfo.DefaultValue(id);
        if (!Tracks.TryGetValue(id, out var curve) || curve.Count == 0)
            return defaultValue;
        return ChannelInfo.IsDiscrete(id.Channel) ? curve.EvaluateStepped(frame, defaultValue) : curve.Evaluate(frame, defaultValue);
    }

    /// <summary>Removes empty tracks (of every actor).</summary>
    public void Prune()
    {
        for (var actor = 0; actor < ActorCount; actor++)
        {
            var tracks = ForActor(actor).Tracks;
            foreach (var id in tracks.Where(t => t.Value.Count == 0).Select(t => t.Key).ToList())
                tracks.Remove(id);
        }
    }

    /// <summary>Deep copy of the whole animation (cloning an actor's motion clones the animation it belongs to).</summary>
    public MiiAnimation Clone()
    {
        var source = Root;
        var clone = new MiiAnimation
        {
            Name = source.Name,
            Fps = source.Fps,
            Length = source.Length,
            AuthorMii = source.AuthorMii?.ToArray(),
        };
        CopyTracks(source, clone);
        clone.Events.AddRange(source.Events);
        foreach (var actor in source.Actors)
        {
            var copy = clone.AddActor(actor.Name);
            copy.PreviewMii = actor.PreviewMii?.ToArray();
            CopyTracks(actor.Motion, copy.Motion);
        }

        clone.Particles.AddRange(source.Particles.Select(p => p.Clone()));
        return clone;
    }

    private static void CopyTracks(MiiAnimation from, MiiAnimation to)
    {
        foreach (var (id, curve) in from.Tracks)
            to.Tracks[id] = curve.Clone();
    }

    /// <summary>Wraps a time in seconds into a frame position inside the loop.</summary>
    public float FrameAtTime(double seconds)
    {
        if (Length <= 0)
            return 0f;
        var frame = seconds * Fps % Length;
        if (frame < 0)
            frame += Length;
        return (float)frame;
    }
}

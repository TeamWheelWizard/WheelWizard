namespace MiiAnim.Core.Animation;

public sealed class MiiAnimation
{
    public const int MiiDataLength = 74;

    public string Name { get; set; } = "Untitled";

    /// <summary>Frames per second. Keys are on whole frames.</summary>
    public int Fps { get; set; } = 60;

    /// <summary>Loop length in frames. Playback wraps from <see cref="Length"/> back to frame 0.</summary>
    public int Length { get; set; } = 120;

    /// <summary>Optional 74-byte Wii Mii the animation was authored with (preview only, players use their own Mii).</summary>
    public byte[]? AuthorMii { get; set; }

    public Dictionary<TrackId, AnimCurve> Tracks { get; } = new();

    /// <summary>Named event markers, kept sorted by frame.</summary>
    public List<AnimEvent> Events { get; } = [];

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

    /// <summary>Removes empty tracks.</summary>
    public void Prune()
    {
        foreach (var id in Tracks.Where(t => t.Value.Count == 0).Select(t => t.Key).ToList())
            Tracks.Remove(id);
    }

    public MiiAnimation Clone()
    {
        var clone = new MiiAnimation
        {
            Name = Name,
            Fps = Fps,
            Length = Length,
            AuthorMii = AuthorMii?.ToArray(),
        };
        foreach (var (id, curve) in Tracks)
            clone.Tracks[id] = curve.Clone();
        clone.Events.AddRange(Events);
        return clone;
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

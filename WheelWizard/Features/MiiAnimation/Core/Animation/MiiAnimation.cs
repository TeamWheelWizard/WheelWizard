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

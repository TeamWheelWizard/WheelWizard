namespace WheelWizard.CustomMusic.Audio;

/// <summary>
/// Decoded audio in planar float form (one array per channel, nominal range -1..1),
/// plus everything we learned about the file while decoding it.
/// </summary>
public sealed class AudioBuffer
{
    public AudioBuffer(float[][] channels, int sampleRate)
    {
        if (channels.Length == 0)
            throw new ArgumentException("Audio needs at least one channel.", nameof(channels));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        var length = channels[0].Length;
        if (channels.Any(channel => channel.Length != length))
            throw new ArgumentException("All channels must have the same length.", nameof(channels));

        Channels = channels;
        SampleRate = sampleRate;
    }

    public float[][] Channels { get; }
    public int SampleRate { get; }
    public int ChannelCount => Channels.Length;
    public int SampleCount => Channels[0].Length;
    public TimeSpan Duration => TimeSpan.FromSeconds(SampleCount / (double)SampleRate);

    /// <summary>Loop region in samples, when the source file declared one (WAV smpl, LOOPSTART tags, BRSTM...).</summary>
    public LoopRegion? Loop { get; set; }

    public AudioMetadata Metadata { get; set; } = new();
}

/// <summary>A loop that plays <see cref="Start"/> up to (excluding) <see cref="End"/> forever.</summary>
public readonly record struct LoopRegion(int Start, int End)
{
    public int Length => End - Start;
    public bool IsValid(int sampleCount) => Start >= 0 && End > Start && End <= sampleCount;
}

public sealed class AudioMetadata
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public CoverArt? Cover { get; set; }

    /// <summary>Raw loop hints from tags (LOOPSTART/LOOPLENGTH/LOOPEND), in source samples.</summary>
    public long? LoopStartTag { get; set; }
    public long? LoopLengthTag { get; set; }
    public long? LoopEndTag { get; set; }

    /// <summary>Applies loop tags the way most game-audio tools read them.</summary>
    public LoopRegion? ResolveLoopTags(int sampleCount)
    {
        if (LoopStartTag is not { } start || start < 0 || start >= sampleCount)
            return null;

        long end;
        if (LoopLengthTag is { } length && length > 0)
            end = start + length;
        else if (LoopEndTag is { } loopEnd && loopEnd > start)
            end = loopEnd;
        else
            end = sampleCount;

        end = Math.Min(end, sampleCount);
        return end > start ? new LoopRegion((int)start, (int)end) : null;
    }

    public void MergeMissing(AudioMetadata other)
    {
        Title ??= other.Title;
        Artist ??= other.Artist;
        Album ??= other.Album;
        Cover ??= other.Cover;
        LoopStartTag ??= other.LoopStartTag;
        LoopLengthTag ??= other.LoopLengthTag;
        LoopEndTag ??= other.LoopEndTag;
    }
}

/// <summary>Embedded picture bytes, normally JPEG or PNG.</summary>
public sealed record CoverArt(byte[] Data, string MimeType)
{
    public string FileExtension =>
        MimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            _ => ".jpg",
        };

    /// <summary>Recognises the image by its signature, ignoring whatever MIME type the tag claims.</summary>
    public static CoverArt? FromBytes(byte[]? data, string? declaredMime = null)
    {
        if (data is null || data.Length < 8)
            return null;

        if (data[0] == 0xFF && data[1] == 0xD8)
            return new(data, "image/jpeg");
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return new(data, "image/png");
        if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            return new(data, "image/gif");
        if (data[0] == 'B' && data[1] == 'M')
            return new(data, "image/bmp");
        if (data.Length > 12 && data[0] == 'R' && data[1] == 'I' && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
            return new(data, "image/webp");

        // Unknown signature: only trust the tag if it says it is an image at all.
        return declaredMime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ? new(data, declaredMime) : null;
    }
}

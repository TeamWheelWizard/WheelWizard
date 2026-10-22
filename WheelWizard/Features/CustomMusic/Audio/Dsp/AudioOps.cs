namespace WheelWizard.CustomMusic.Audio.Dsp;

/// <summary>Small, self-contained processing steps used by the conversion pipeline.</summary>
public static class AudioOps
{
    /// <summary>Mixes any channel layout to stereo (mono is duplicated; surround is folded down ITU-style).</summary>
    public static AudioBuffer ToStereo(AudioBuffer input)
    {
        if (input.ChannelCount == 2)
            return input;

        var length = input.SampleCount;
        var left = new float[length];
        var right = new float[length];
        var ch = input.Channels;

        switch (input.ChannelCount)
        {
            case 1:
                Array.Copy(ch[0], left, length);
                Array.Copy(ch[0], right, length);
                break;
            case 6:
            case 8:
            {
                // L R C LFE Ls Rs (+ Lb Rb): centre and surrounds at -3 dB, LFE dropped.
                const float minus3Db = 0.7071f;
                for (var i = 0; i < length; i++)
                {
                    var centre = ch[2][i] * minus3Db;
                    var leftSurround = ch[4][i] * minus3Db + (input.ChannelCount == 8 ? ch[6][i] * minus3Db : 0);
                    var rightSurround = ch[5][i] * minus3Db + (input.ChannelCount == 8 ? ch[7][i] * minus3Db : 0);
                    left[i] = ch[0][i] + centre + leftSurround;
                    right[i] = ch[1][i] + centre + rightSurround;
                }

                break;
            }
            default:
                // Unknown layouts: alternate channels between the two sides.
                for (var c = 0; c < input.ChannelCount; c++)
                {
                    var target = c % 2 == 0 ? left : right;
                    for (var i = 0; i < length; i++)
                        target[i] += ch[c][i];
                }

                var perSide = (input.ChannelCount + 1) / 2f;
                for (var i = 0; i < length; i++)
                {
                    left[i] /= perSide;
                    right[i] /= perSide;
                }

                break;
        }

        return new AudioBuffer([left, right], input.SampleRate) { Metadata = input.Metadata, Loop = input.Loop };
    }

    /// <summary>
    /// Removes leading and trailing near-silence (below <paramref name="thresholdDb"/>), keeping a few
    /// milliseconds so attacks are not clipped. Loop points are shifted along.
    /// </summary>
    public static AudioBuffer TrimSilence(AudioBuffer input, double thresholdDb = -60, double keepSeconds = 0.01)
    {
        var threshold = (float)Math.Pow(10, thresholdDb / 20);
        var length = input.SampleCount;
        var first = 0;
        while (first < length && !IsAudible(input.Channels, first, threshold))
            first++;
        var last = length - 1;
        while (last > first && !IsAudible(input.Channels, last, threshold))
            last--;

        if (first >= length)
            return input; // fully silent: leave it alone, the caller reports it

        var keep = (int)(input.SampleRate * keepSeconds);
        first = Math.Max(0, first - keep);
        last = Math.Min(length - 1, last + keep);

        // Never trim into a declared loop.
        if (input.Loop is { } loop)
        {
            first = Math.Min(first, loop.Start);
            last = Math.Max(last, loop.End - 1);
        }

        if (first == 0 && last == length - 1)
            return input;

        var newLength = last - first + 1;
        var channels = input.Channels.Select(channel => channel.AsSpan(first, newLength).ToArray()).ToArray();
        var result = new AudioBuffer(channels, input.SampleRate) { Metadata = input.Metadata };
        if (input.Loop is { } region)
            result.Loop = new LoopRegion(region.Start - first, region.End - first);
        return result;
    }

    private static bool IsAudible(float[][] channels, int index, float threshold)
    {
        foreach (var channel in channels)
        {
            if (Math.Abs(channel[index]) > threshold)
                return true;
        }

        return false;
    }

    /// <summary>Replaces NaN and infinite samples (from damaged files) with silence; returns how many were replaced.</summary>
    public static int ReplaceNonFinite(AudioBuffer buffer)
    {
        var replaced = 0;
        foreach (var channel in buffer.Channels)
        {
            for (var i = 0; i < channel.Length; i++)
            {
                if (!float.IsFinite(channel[i]))
                {
                    channel[i] = 0;
                    replaced++;
                }
            }
        }

        return replaced;
    }

    public static void ApplyGain(AudioBuffer buffer, double gainDb)
    {
        var gain = (float)Math.Pow(10, gainDb / 20);
        foreach (var channel in buffer.Channels)
        {
            for (var i = 0; i < channel.Length; i++)
                channel[i] *= gain;
        }
    }

    /// <summary>
    /// Makes a song without loop points loop seamlessly: the last <paramref name="crossfadeSeconds"/> fade into
    /// the opening, and the loop restarts right after that opening section. The first play-through is untouched.
    /// </summary>
    public static AudioBuffer CreateCrossfadeLoop(AudioBuffer input, double crossfadeSeconds)
    {
        var length = input.SampleCount;
        var fade = (int)(input.SampleRate * crossfadeSeconds);
        // Short songs get a proportionally shorter crossfade (at most a quarter of the song).
        fade = Math.Min(fade, length / 4);
        if (fade < input.SampleRate / 20)
        {
            input.Loop = new LoopRegion(0, length);
            return input;
        }

        var channels = new float[input.ChannelCount][];
        for (var c = 0; c < input.ChannelCount; c++)
        {
            var source = input.Channels[c];
            var output = (float[])source.Clone();
            var tailStart = length - fade;
            for (var i = 0; i < fade; i++)
            {
                // Equal-power crossfade keeps the level steady across uncorrelated material.
                var t = (i + 0.5) / fade;
                var fadeOut = (float)Math.Cos(t * Math.PI / 2);
                var fadeIn = (float)Math.Sin(t * Math.PI / 2);
                output[tailStart + i] = source[tailStart + i] * fadeOut + source[i] * fadeIn;
            }

            channels[c] = output;
        }

        // After the crossfade the head has already played its first `fade` samples, so continue from there.
        return new AudioBuffer(channels, input.SampleRate) { Metadata = input.Metadata, Loop = new LoopRegion(fade, length) };
    }

    /// <summary>
    /// Moves the loop start forward onto a multiple of <paramref name="alignment"/> (the BRSTM block size),
    /// extending the end with the audio that follows the original loop start so the loop stays seamless.
    /// </summary>
    public static AudioBuffer AlignLoopStart(AudioBuffer input, int alignment)
    {
        if (input.Loop is not { } loop || alignment <= 1)
            return input;

        var shift = (alignment - loop.Start % alignment) % alignment;
        // The loop ends playback, so everything after the loop end is dropped either way.
        if (shift == 0)
        {
            if (loop.End == input.SampleCount)
                return input;
            var trimmed = input.Channels.Select(channel => channel.AsSpan(0, loop.End).ToArray()).ToArray();
            return new AudioBuffer(trimmed, input.SampleRate) { Metadata = input.Metadata, Loop = loop };
        }

        var newEnd = loop.End + shift;
        var channels = new float[input.ChannelCount][];
        for (var c = 0; c < input.ChannelCount; c++)
        {
            var source = input.Channels[c];
            var output = new float[newEnd];
            Array.Copy(source, output, loop.End);
            // Samples after the loop end are what plays after jumping back: loop.Start, loop.Start + 1, ...
            for (var i = 0; i < shift; i++)
                output[loop.End + i] = source[loop.Start + i % loop.Length];
            channels[c] = output;
        }

        return new AudioBuffer(channels, input.SampleRate) { Metadata = input.Metadata, Loop = new LoopRegion(loop.Start + shift, newEnd) };
    }

    /// <summary>
    /// Appends what plays after the loop jumps back (loop start onward) behind the loop end, so filters
    /// and resamplers see continuous audio across the loop seam. The loop region itself is unchanged and
    /// <see cref="AlignLoopStart"/> drops the extra audio again.
    /// </summary>
    public static AudioBuffer ExtendPastLoopEnd(AudioBuffer input, int extraSamples)
    {
        if (input.Loop is not { } loop || extraSamples <= 0)
            return input;

        var newLength = loop.End + extraSamples;
        var channels = new float[input.ChannelCount][];
        for (var c = 0; c < input.ChannelCount; c++)
        {
            var source = input.Channels[c];
            var output = new float[newLength];
            Array.Copy(source, output, loop.End);
            for (var i = 0; i < extraSamples; i++)
                output[loop.End + i] = source[loop.Start + i % loop.Length];
            channels[c] = output;
        }

        return new AudioBuffer(channels, input.SampleRate) { Metadata = input.Metadata, Loop = loop };
    }

    /// <summary>Keeps at most <paramref name="maxSamples"/>, fading out the cut so it does not click.</summary>
    public static AudioBuffer Truncate(AudioBuffer input, int maxSamples, double fadeSeconds = 2.0)
    {
        if (input.SampleCount <= maxSamples)
            return input;

        var channels = input.Channels.Select(channel => channel.AsSpan(0, maxSamples).ToArray()).ToArray();
        var result = new AudioBuffer(channels, input.SampleRate) { Metadata = input.Metadata };
        if (input.Loop is { } loop && loop.Start < maxSamples - input.SampleRate)
            result.Loop = new LoopRegion(loop.Start, Math.Min(loop.End, maxSamples));
        else
            FadeOutTail(result, fadeSeconds);
        return result;
    }

    /// <summary>Converts to 16-bit with TPDF dither (and hard clipping as a final safety net).</summary>
    public static short[] ToPcm16(float[] samples, int seed)
    {
        var random = new Random(seed);
        var output = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var dither = (random.NextDouble() - random.NextDouble()) / 32768.0;
            var value = Math.Round((samples[i] + dither) * 32767.0);
            output[i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }

        return output;
    }

    /// <summary>Short fade-out at the end of non-looping jingles so they never end on a click.</summary>
    public static void FadeOutTail(AudioBuffer buffer, double seconds)
    {
        var length = Math.Min(buffer.SampleCount, (int)(buffer.SampleRate * seconds));
        if (length <= 1)
            return;
        foreach (var channel in buffer.Channels)
        {
            var start = channel.Length - length;
            for (var i = 0; i < length; i++)
                channel[start + i] *= 1f - (i + 1f) / length;
        }
    }
}

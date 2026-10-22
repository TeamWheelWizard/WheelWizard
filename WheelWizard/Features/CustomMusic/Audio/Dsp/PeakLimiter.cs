namespace WheelWizard.CustomMusic.Audio.Dsp;

/// <summary>
/// Offline, stereo-linked look-ahead peak limiter. It only acts on the few peaks that would clip after
/// loudness matching, so quiet songs can be raised to the target without distortion.
/// </summary>
public static class PeakLimiter
{
    /// <returns>The largest gain reduction applied, in dB (0 when nothing was limited).</returns>
    public static double Apply(AudioBuffer buffer, double ceilingDb = -1.0, double lookAheadSeconds = 0.005, double releaseSeconds = 0.12)
    {
        var length = buffer.SampleCount;
        if (length == 0)
            return 0;

        // Gain each sample needs on its own to stay under the ceiling (1 = untouched).
        var ceiling = (float)Math.Pow(10, ceilingDb / 20);
        var required = new float[length];
        var anyOver = false;
        for (var i = 0; i < length; i++)
        {
            var peak = 0f;
            foreach (var channel in buffer.Channels)
                peak = Math.Max(peak, Math.Abs(channel[i]));
            required[i] = peak > ceiling ? ceiling / peak : 1f;
            anyOver |= peak > ceiling;
        }

        if (!anyOver)
            return 0;

        var lookAhead = Math.Max(1, (int)(buffer.SampleRate * lookAheadSeconds));

        // 1) Minimum over a symmetric window of +/- lookAhead: gain is already down when the peak arrives.
        var minimum = SlidingMinimum(required, lookAhead);

        // 2) Average over +/- lookAhead/2: a smooth ramp that still never exceeds the required gain, because
        //    every averaged value already includes the peak's own requirement. A running sum keeps it O(n)
        //    without another full-length array.
        // 3) Slow release so the gain does not pump between peaks.
        // The gains are written back into `required`, which is no longer needed.
        var half = Math.Max(1, lookAhead / 2);
        var releaseAlpha = 1 - Math.Exp(-1.0 / (buffer.SampleRate * releaseSeconds));
        double windowSum = 0;
        var windowFrom = 0;
        var windowTo = -1;
        var gain = 1.0;
        var maxReduction = 1.0;
        for (var i = 0; i < length; i++)
        {
            var to = Math.Min(length - 1, i + half);
            while (windowTo < to)
                windowSum += minimum[++windowTo];
            var from = Math.Max(0, i - half);
            while (windowFrom < from)
                windowSum -= minimum[windowFrom++];

            var smooth = windowSum / (windowTo - windowFrom + 1);
            gain = smooth < gain ? smooth : gain + (smooth - gain) * releaseAlpha;
            required[i] = (float)gain;
            maxReduction = Math.Min(maxReduction, gain);
        }

        foreach (var channel in buffer.Channels)
        {
            for (var i = 0; i < length; i++)
                channel[i] *= required[i];
        }

        return -20 * Math.Log10(maxReduction);
    }

    /// <summary>Minimum of every window [i - radius, i + radius], with a monotonic queue that only spans one window.</summary>
    private static float[] SlidingMinimum(float[] values, int radius)
    {
        var length = values.Length;
        var result = new float[length];
        var capacity = 2 * radius + 2;
        var queue = new int[capacity]; // ring buffer of indices with increasing values
        int head = 0,
            count = 0;
        var next = 0;

        for (var i = 0; i < length; i++)
        {
            // Push everything up to i + radius, dropping queued values that can never be the minimum again.
            var limit = Math.Min(length - 1, i + radius);
            while (next <= limit)
            {
                while (count > 0 && values[queue[(head + count - 1) % capacity]] >= values[next])
                    count--;
                queue[(head + count) % capacity] = next++;
                count++;
            }

            while (queue[head] < i - radius)
            {
                head = (head + 1) % capacity;
                count--;
            }

            result[i] = values[queue[head]];
        }

        return result;
    }
}

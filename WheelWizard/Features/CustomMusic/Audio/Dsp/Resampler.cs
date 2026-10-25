namespace WheelWizard.CustomMusic.Audio.Dsp;

/// <summary>
/// Band-limited (Kaiser windowed sinc) sample rate converter. Used to bring songs to the 32 kHz
/// Mario Kart Wii streams use, and to make the sped-up final lap version.
/// </summary>
public static class Resampler
{
    private const int TapsPerSide = 32;
    private const int Phases = 512;
    private const double KaiserBeta = 9.0;

    /// <summary>
    /// Plays <paramref name="input"/> back <paramref name="speed"/> times faster while producing samples at
    /// <paramref name="outputRate"/>. With speed 1 this is a plain sample rate conversion.
    /// </summary>
    public static float[] Resample(float[] input, int inputRate, int outputRate, double speed = 1.0)
    {
        if (input.Length == 0)
            return [];
        if (speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed));

        // How far we advance through the input for every output sample.
        var step = inputRate * speed / outputRate;
        if (Math.Abs(step - 1.0) < 1e-9)
            return (float[])input.Clone();

        var outputLength = (int)Math.Floor(input.Length / step);
        var output = new float[outputLength];

        // When decimating, the filter cutoff has to move down to the new Nyquist frequency.
        var cutoff = Math.Min(1.0, 1.0 / step) * 0.97;
        var table = BuildTable(cutoff, out var tapsPerSide);
        var tableWidth = tapsPerSide * 2;

        for (var i = 0; i < outputLength; i++)
        {
            var position = i * step;
            var index = (int)Math.Floor(position);
            var fraction = position - index;
            var phaseExact = fraction * Phases;
            var phase = (int)phaseExact;
            var phaseBlend = (float)(phaseExact - phase);

            var rowA = phase * tableWidth;
            var rowB = (phase + 1) * tableWidth;
            double sum = 0;
            var first = index - tapsPerSide + 1;
            for (var t = 0; t < tableWidth; t++)
            {
                var sampleIndex = first + t;
                if ((uint)sampleIndex >= (uint)input.Length)
                    continue;
                var weight = table[rowA + t] + (table[rowB + t] - table[rowA + t]) * phaseBlend;
                sum += input[sampleIndex] * weight;
            }

            output[i] = (float)sum;
        }

        return output;
    }

    public static AudioBuffer Resample(AudioBuffer buffer, int outputRate, double speed = 1.0)
    {
        var channels = new float[buffer.ChannelCount][];
        Parallel.For(0, buffer.ChannelCount, c => channels[c] = Resample(buffer.Channels[c], buffer.SampleRate, outputRate, speed));

        var ratio = outputRate / (buffer.SampleRate * speed);
        var result = new AudioBuffer(channels, outputRate) { Metadata = buffer.Metadata };
        if (buffer.Loop is { } loop)
        {
            var start = (int)Math.Round(loop.Start * ratio);
            var end = Math.Min(result.SampleCount, (int)Math.Round(loop.End * ratio));
            var region = new LoopRegion(start, end);
            if (region.IsValid(result.SampleCount))
                result.Loop = region;
        }

        return result;
    }

    private static readonly Dictionary<int, float[]> TableCache = new();

    private static float[] BuildTable(double cutoff, out int tapsPerSide)
    {
        // Widen the kernel when the cutoff drops so the transition band stays equally steep.
        tapsPerSide = (int)Math.Ceiling(TapsPerSide / cutoff);
        var key = (int)Math.Round(cutoff * 100000);
        lock (TableCache)
        {
            if (TableCache.TryGetValue(key, out var cached))
                return cached;
        }

        var width = tapsPerSide * 2;
        // One extra phase row so interpolation between the last phase and the next tap stays in bounds.
        var table = new float[(Phases + 1) * width];
        var denominator = BesselI0(KaiserBeta);
        for (var phase = 0; phase <= Phases; phase++)
        {
            var fraction = phase / (double)Phases;
            double norm = 0;
            for (var t = 0; t < width; t++)
            {
                // Tap t multiplies input[index - tapsPerSide + 1 + t]; its distance from the read position:
                var x = t - tapsPerSide + 1 - fraction;
                var sinc = Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x * cutoff) / (Math.PI * x * cutoff);
                var ratio = x / tapsPerSide;
                var window = Math.Abs(ratio) >= 1 ? 0 : BesselI0(KaiserBeta * Math.Sqrt(1 - ratio * ratio)) / denominator;
                var value = sinc * window * cutoff;
                table[phase * width + t] = (float)value;
                norm += value;
            }

            // Normalise each phase to unity DC gain.
            if (norm > 1e-9)
            {
                for (var t = 0; t < width; t++)
                    table[phase * width + t] = (float)(table[phase * width + t] / norm);
            }
        }

        lock (TableCache)
            TableCache[key] = table;
        return table;
    }

    private static double BesselI0(double x)
    {
        double sum = 1,
            term = 1;
        var halfX = x / 2;
        for (var k = 1; k < 50; k++)
        {
            term *= halfX / k * (halfX / k);
            sum += term;
            if (term < sum * 1e-12)
                break;
        }

        return sum;
    }
}

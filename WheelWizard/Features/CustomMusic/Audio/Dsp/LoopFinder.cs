namespace WheelWizard.CustomMusic.Audio.Dsp;

/// <summary>A loop found in a song: playback runs to <see cref="End"/> and jumps back to <see cref="Start"/>.</summary>
/// <param name="Similarity">How alike the music around both points is (1 = identical).</param>
public readonly record struct LoopMatch(int Start, int End, double Similarity);

/// <summary>
/// Finds a seamless loop in a song that has no loop points, the way a person editing it would: most songs repeat
/// a section (a chorus, a whole verse-chorus cycle), so playback can jump from a late repeat back to an earlier one
/// and nobody hears the cut.
/// <para>
/// 1. The song is cut into 64 ms frames, each described by its loudness in 24 frequency bands.<br/>
/// 2. For every jump length, the frames are compared with the frames that same distance earlier; the place where
///    several seconds on both sides of the jump match best wins. Long loops and late jump points are preferred.<br/>
/// 3. The frame-level guess is refined to the exact sample by cross-correlating the waveforms.
/// </para>
/// </summary>
public static class LoopFinder
{
    private const int FrameSize = 2048;
    private const int BandCount = 24;
    private const double MaxAnalysisSeconds = 10 * 60;

    /// <summary>Frames quieter than this (silence, the tail of a fade) carry no usable shape.</summary>
    private const float SilenceDb = -50;

    /// <summary>Similarity lost per dB of level difference between two frames.</summary>
    private const float LevelCostPerDb = 0.03f;

    /// <summary>Average similarity (cosine of the band spectra) the music must reach on both sides of the jump.</summary>
    public const double MinSimilarity = 0.95;

    /// <param name="audio">The song (any channel count).</param>
    /// <param name="minLoopSeconds">Shortest loop worth using; shorter repeats would get tiring in game.</param>
    /// <param name="matchSeconds">How much music around the jump must match.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <returns>The best loop, or null when the song does not repeat cleanly enough.</returns>
    public static LoopMatch? Find(AudioBuffer audio, double minLoopSeconds = 12, double matchSeconds = 6, CancellationToken cancellationToken = default)
    {
        var mono = MixToMono(audio, (int)Math.Min(audio.SampleCount, MaxAnalysisSeconds * audio.SampleRate));
        var frames = Features(mono, out var levels);
        var frameCount = frames.Length;
        var half = Math.Max(2, (int)(matchSeconds * audio.SampleRate / FrameSize / 2));
        var window = 2 * half;
        var minLag = Math.Max(window, (int)(minLoopSeconds * audio.SampleRate / FrameSize));
        if (frameCount < minLag + window)
            return null;

        var similarity = new float[frameCount];
        var bestScore = double.NegativeInfinity;
        int bestEnd = 0,
            bestLag = 0;
        double bestMean = 0;
        for (var lag = minLag; lag + half < frameCount - half; lag++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var t = lag; t < frameCount; t++)
                similarity[t] = Similarity(frames, levels, t, t - lag);

            // The jump point needs `half` frames of matching music before it and after it (the song continues
            // past the jump, and that continuation is what the earlier section has to sound like).
            double sum = 0;
            for (var t = lag; t < lag + window; t++)
                sum += similarity[t];
            for (var end = lag + half; end + half <= frameCount; end++)
            {
                if (end > lag + half)
                    sum += similarity[end + half - 1] - similarity[end - half - 1];
                var mean = sum / window;
                if (mean < MinSimilarity)
                    continue;
                // Prefer long loops (less repetition) and late jumps (more of the song is heard).
                var score = mean + 0.04 * lag / frameCount + 0.02 * end / frameCount;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMean = mean;
                    bestEnd = end;
                    bestLag = lag;
                }
            }
        }

        if (bestLag == 0)
            return null;

        var loopEnd = bestEnd * FrameSize;
        var start = Refine(mono, loopEnd, (bestEnd - bestLag) * FrameSize);
        return start is { } exact && loopEnd - exact >= minLoopSeconds * audio.SampleRate * 0.9 ? new LoopMatch(exact, loopEnd, bestMean) : null;
    }

    /// <summary>
    /// Cuts the song at the loop end and blends the last few milliseconds into what precedes the loop start, so the
    /// jump continues the waveform without a click.
    /// </summary>
    public static AudioBuffer Apply(AudioBuffer audio, LoopMatch loop, double blendSeconds = 0.03)
    {
        var blend = Math.Min((int)(audio.SampleRate * blendSeconds), loop.Start);
        var channels = new float[audio.ChannelCount][];
        for (var c = 0; c < audio.ChannelCount; c++)
        {
            var source = audio.Channels[c];
            var output = source.AsSpan(0, loop.End).ToArray();
            for (var i = 0; i < blend; i++)
            {
                // The two sides are near-identical by construction, so a linear blend keeps the level steady.
                var weight = (i + 0.5f) / blend;
                var at = loop.End - blend + i;
                output[at] = source[at] * (1 - weight) + source[loop.Start - blend + i] * weight;
            }

            channels[c] = output;
        }

        return new AudioBuffer(channels, audio.SampleRate) { Metadata = audio.Metadata, Loop = new LoopRegion(loop.Start, loop.End) };
    }

    private static float[] MixToMono(AudioBuffer audio, int length)
    {
        var mono = new float[length];
        foreach (var channel in audio.Channels)
        {
            for (var i = 0; i < length; i++)
                mono[i] += channel[i];
        }

        var scale = 1f / audio.ChannelCount;
        for (var i = 0; i < length; i++)
            mono[i] *= scale;
        return mono;
    }

    /// <summary>Per frame: log energy in bands spaced evenly on a log-frequency scale, centred and normalised to unit length.</summary>
    private static float[][] Features(float[] mono, out float[] levels)
    {
        var frameCount = mono.Length / FrameSize;
        var features = new float[frameCount][];
        levels = new float[frameCount];

        // Band edges in FFT bins: roughly 60 Hz to the top of the spectrum, evenly spaced in log frequency.
        var bins = FrameSize / 2;
        var edges = new int[BandCount + 1];
        for (var b = 0; b <= BandCount; b++)
            edges[b] = Math.Clamp((int)Math.Round(4 * Math.Pow(bins / 4.0, b / (double)BandCount)), 1, bins);

        var window = new float[FrameSize];
        for (var i = 0; i < FrameSize; i++)
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FrameSize - 1)));

        var real = new double[FrameSize];
        var imaginary = new double[FrameSize];
        var bands = new double[BandCount];
        for (var f = 0; f < frameCount; f++)
        {
            double energy = 0;
            for (var i = 0; i < FrameSize; i++)
            {
                var x = mono[f * FrameSize + i];
                energy += x * x;
                real[i] = x * window[i];
                imaginary[i] = 0;
            }

            levels[f] = (float)(10 * Math.Log10(energy / FrameSize + 1e-12));
            Fft(real, imaginary);

            for (var b = 0; b < BandCount; b++)
            {
                double sum = 0;
                for (var k = edges[b]; k < Math.Max(edges[b] + 1, edges[b + 1]); k++)
                    sum += real[k] * real[k] + imaginary[k] * imaginary[k];
                bands[b] = Math.Log10(sum + 1e-10);
            }

            var mean = bands.Average();
            double norm = 0;
            for (var b = 0; b < BandCount; b++)
            {
                bands[b] -= mean;
                norm += bands[b] * bands[b];
            }

            norm = Math.Sqrt(norm);
            var vector = new float[BandCount];
            if (norm > 1e-9)
            {
                for (var b = 0; b < BandCount; b++)
                    vector[b] = (float)(bands[b] / norm);
            }

            features[f] = vector;
        }

        return features;
    }

    /// <summary>
    /// Spectral shape (cosine) minus a cost for any level difference, so a fade-out or a quieter repeat does not
    /// pass for the same music; silent frames never match.
    /// </summary>
    private static float Similarity(float[][] frames, float[] levels, int a, int b)
    {
        if (levels[a] < SilenceDb || levels[b] < SilenceDb)
            return 0f;
        var dot = 0f;
        var x = frames[a];
        var y = frames[b];
        for (var i = 0; i < x.Length; i++)
            dot += x[i] * y[i];
        return dot - LevelCostPerDb * Math.Abs(levels[a] - levels[b]);
    }

    /// <summary>The loop start (near <paramref name="roughStart"/>) whose surrounding waveform best matches the audio around <paramref name="end"/>.</summary>
    private static int? Refine(float[] mono, int end, int roughStart)
    {
        const int span = 1024;
        if (end - span < 0 || end + span > mono.Length)
            return null;

        double targetEnergy = 0;
        for (var i = -span; i < span; i++)
            targetEnergy += (double)mono[end + i] * mono[end + i];

        var best = double.NegativeInfinity;
        int? bestStart = null;
        for (var candidate = roughStart - FrameSize; candidate <= roughStart + FrameSize; candidate++)
        {
            if (candidate - span < 0 || candidate + span > mono.Length || candidate >= end)
                continue;
            double cross = 0,
                energy = 0;
            for (var i = -span; i < span; i++)
            {
                var x = mono[candidate + i];
                cross += x * mono[end + i];
                energy += x * x;
            }

            var correlation = cross / Math.Sqrt(Math.Max(energy * targetEnergy, 1e-20));
            if (correlation > best)
            {
                best = correlation;
                bestStart = candidate;
            }
        }

        return bestStart;
    }

    /// <summary>In-place iterative radix-2 FFT (the length must be a power of two).</summary>
    private static void Fft(double[] real, double[] imaginary)
    {
        var n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            double stepReal = Math.Cos(angle),
                stepImaginary = Math.Sin(angle);
            for (var i = 0; i < n; i += length)
            {
                double wReal = 1,
                    wImaginary = 0;
                for (var k = 0; k < length / 2; k++)
                {
                    var a = i + k;
                    var b = a + length / 2;
                    var tReal = real[b] * wReal - imaginary[b] * wImaginary;
                    var tImaginary = real[b] * wImaginary + imaginary[b] * wReal;
                    real[b] = real[a] - tReal;
                    imaginary[b] = imaginary[a] - tImaginary;
                    real[a] += tReal;
                    imaginary[a] += tImaginary;
                    var nextReal = wReal * stepReal - wImaginary * stepImaginary;
                    wImaginary = wReal * stepImaginary + wImaginary * stepReal;
                    wReal = nextReal;
                }
            }
        }
    }
}

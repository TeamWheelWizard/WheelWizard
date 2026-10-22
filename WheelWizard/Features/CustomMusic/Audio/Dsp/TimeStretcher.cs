namespace WheelWizard.CustomMusic.Audio.Dsp;

/// <summary>
/// WSOLA time stretching: changes tempo without changing pitch. Used for the "faster, same pitch"
/// final lap style, which sounds closest to the original game's final lap tracks.
/// </summary>
public static class TimeStretcher
{
    public static AudioBuffer ChangeTempo(AudioBuffer input, double speed)
    {
        if (Math.Abs(speed - 1.0) < 1e-6 || input.SampleCount == 0)
            return input;

        var sampleRate = input.SampleRate;
        var frameSize = Math.Max(256, (int)(sampleRate * 0.046)) & ~1;
        var synthesisHop = frameSize / 2;
        var analysisHop = synthesisHop * speed;
        var tolerance = (int)(sampleRate * 0.012);

        var length = input.SampleCount;
        var outputLength = (int)(length / speed);
        var frameCount = outputLength / synthesisHop;
        var window = new float[frameSize];
        for (var i = 0; i < frameSize; i++)
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / frameSize));

        // A mono guide signal for finding the best splice points; both channels use the same offsets.
        var guide = new float[length];
        foreach (var channel in input.Channels)
        {
            for (var i = 0; i < length; i++)
                guide[i] += channel[i];
        }

        var outputs = input.Channels.Select(_ => new float[outputLength + frameSize]).ToArray();
        var normalisation = new float[outputLength + frameSize];
        var previousPosition = 0;

        for (var k = 0; k < frameCount; k++)
        {
            var nominal = (int)(k * analysisHop);
            var position = nominal;
            if (k > 0)
            {
                // The ideal continuation of the previous frame, compared against nearby candidates.
                var natural = previousPosition + synthesisHop;
                position = FindBestOffset(guide, natural, nominal, tolerance, synthesisHop);
            }

            position = Math.Clamp(position, 0, Math.Max(0, length - frameSize));
            var outputStart = k * synthesisHop;
            for (var c = 0; c < outputs.Length; c++)
            {
                var source = input.Channels[c];
                var target = outputs[c];
                for (var i = 0; i < frameSize && position + i < length; i++)
                    target[outputStart + i] += source[position + i] * window[i];
            }

            for (var i = 0; i < frameSize; i++)
                normalisation[outputStart + i] += window[i];

            previousPosition = position;
        }

        var channels = new float[outputs.Length][];
        for (var c = 0; c < outputs.Length; c++)
        {
            var result = new float[outputLength];
            for (var i = 0; i < outputLength; i++)
                result[i] = normalisation[i] > 1e-3f ? outputs[c][i] / normalisation[i] : outputs[c][i];
            channels[c] = result;
        }

        var stretched = new AudioBuffer(channels, sampleRate) { Metadata = input.Metadata };
        if (input.Loop is { } loop)
        {
            var region = new LoopRegion((int)(loop.Start / speed), Math.Min(outputLength, (int)(loop.End / speed)));
            if (region.IsValid(outputLength))
                stretched.Loop = region;
        }

        return stretched;
    }

    private static int FindBestOffset(float[] guide, int natural, int nominal, int tolerance, int compareLength)
    {
        var length = guide.Length;
        if (natural < 0 || natural + compareLength >= length)
            return nominal;

        // Coarse search on every 4th offset and sample, then refine around the winner.
        var best = nominal;
        var bestScore = double.NegativeInfinity;
        for (var offset = -tolerance; offset <= tolerance; offset += 4)
        {
            var score = Correlate(guide, natural, nominal + offset, compareLength, 4);
            if (score > bestScore)
            {
                bestScore = score;
                best = nominal + offset;
            }
        }

        var coarse = best;
        bestScore = double.NegativeInfinity;
        for (var offset = -4; offset <= 4; offset++)
        {
            var score = Correlate(guide, natural, coarse + offset, compareLength, 1);
            if (score > bestScore)
            {
                bestScore = score;
                best = coarse + offset;
            }
        }

        return best;
    }

    private static double Correlate(float[] guide, int a, int b, int length, int step)
    {
        if (b < 0 || b + length >= guide.Length)
            return double.NegativeInfinity;

        // Normalised by the candidate's energy so loud passages do not win just for being loud.
        double sum = 0;
        double energy = 1e-9;
        for (var i = 0; i < length; i += step)
        {
            sum += guide[a + i] * guide[b + i];
            energy += guide[b + i] * guide[b + i];
        }

        return sum / Math.Sqrt(energy);
    }
}

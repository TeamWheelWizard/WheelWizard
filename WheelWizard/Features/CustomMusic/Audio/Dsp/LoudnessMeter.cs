namespace WheelWizard.CustomMusic.Audio.Dsp;

/// <summary>Result of a loudness measurement.</summary>
/// <param name="IntegratedLufs">ITU-R BS.1770-4 / EBU R128 integrated loudness (gated), in LUFS.</param>
/// <param name="SamplePeak">Largest absolute sample value (1.0 = full scale).</param>
public readonly record struct LoudnessResult(double IntegratedLufs, double SamplePeak)
{
    public double SamplePeakDb => SamplePeak <= 0 ? double.NegativeInfinity : 20 * Math.Log10(SamplePeak);
    public bool IsSilent => double.IsNegativeInfinity(IntegratedLufs) || IntegratedLufs < -69.9;
}

/// <summary>
/// ITU-R BS.1770-4 integrated loudness: K-weighting, 400 ms blocks with 75% overlap,
/// an absolute gate at -70 LUFS and a relative gate 10 LU below the ungated level.
/// </summary>
public static class LoudnessMeter
{
    private const double AbsoluteGate = -70.0;
    private const double RelativeGate = -10.0;

    public static LoudnessResult Measure(AudioBuffer buffer) => Measure(buffer.Channels, buffer.SampleRate);

    public static LoudnessResult Measure(float[][] channels, int sampleRate, int start = 0, int length = -1)
    {
        var total = channels[0].Length;
        if (length < 0)
            length = total - start;
        length = Math.Clamp(length, 0, total - start);

        var blockSize = (int)Math.Round(sampleRate * 0.4);
        var hop = Math.Max(1, blockSize / 4);
        var hopCount = length / hop;
        if (blockSize <= 0 || length <= 0)
            return new LoudnessResult(double.NegativeInfinity, Peak(channels, start, length));

        // BS.1770 needs at least one 400 ms block. Shorter clips (a quick jingle) get the ungated mean
        // loudness of the whole clip instead of being reported as silent.
        if (hopCount < 4)
            return MeasureShort(channels, sampleRate, start, length);

        // Sum of squared K-weighted samples per hop (100 ms) for every channel; blocks are 4 hops.
        var hopEnergy = new double[hopCount];
        var peak = 0.0;
        foreach (var channel in channels)
        {
            var filter = KWeighting.Create(sampleRate);
            for (var h = 0; h < hopCount; h++)
            {
                double energy = 0;
                var offset = start + h * hop;
                for (var i = 0; i < hop; i++)
                {
                    var x = channel[offset + i];
                    var abs = Math.Abs(x);
                    if (abs > peak)
                        peak = abs;
                    var y = filter.Process(x);
                    energy += y * y;
                }

                // L/R/C weight 1.0; surround weights do not apply to the stereo we produce.
                hopEnergy[h] += energy;
            }
        }

        var blockCount = hopCount - 3;
        if (blockCount <= 0)
            return new LoudnessResult(double.NegativeInfinity, peak);

        var blockLoudness = new double[blockCount];
        var blockPower = new double[blockCount];
        var samplesPerBlock = hop * 4.0;
        for (var b = 0; b < blockCount; b++)
        {
            var power = (hopEnergy[b] + hopEnergy[b + 1] + hopEnergy[b + 2] + hopEnergy[b + 3]) / samplesPerBlock;
            blockPower[b] = power;
            blockLoudness[b] = PowerToLufs(power);
        }

        // Absolute gate
        double sum = 0;
        var count = 0;
        for (var b = 0; b < blockCount; b++)
        {
            if (blockLoudness[b] > AbsoluteGate)
            {
                sum += blockPower[b];
                count++;
            }
        }

        if (count == 0)
            return new LoudnessResult(double.NegativeInfinity, peak);

        // Relative gate
        var relativeThreshold = PowerToLufs(sum / count) + RelativeGate;
        sum = 0;
        count = 0;
        for (var b = 0; b < blockCount; b++)
        {
            if (blockLoudness[b] > AbsoluteGate && blockLoudness[b] > relativeThreshold)
            {
                sum += blockPower[b];
                count++;
            }
        }

        return count == 0 ? new LoudnessResult(double.NegativeInfinity, peak) : new LoudnessResult(PowerToLufs(sum / count), peak);
    }

    private static LoudnessResult MeasureShort(float[][] channels, int sampleRate, int start, int length)
    {
        double energy = 0;
        foreach (var channel in channels)
        {
            var filter = KWeighting.Create(sampleRate);
            for (var i = start; i < start + length; i++)
            {
                var y = filter.Process(channel[i]);
                energy += y * y;
            }
        }

        var lufs = PowerToLufs(energy / length);
        return new LoudnessResult(lufs > AbsoluteGate ? lufs : double.NegativeInfinity, Peak(channels, start, length));
    }

    private static double PowerToLufs(double power) => power <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(power);

    private static double Peak(float[][] channels, int start, int length)
    {
        var peak = 0.0;
        foreach (var channel in channels)
        {
            for (var i = start; i < start + length; i++)
                peak = Math.Max(peak, Math.Abs(channel[i]));
        }

        return peak;
    }

    /// <summary>The two-stage K-weighting pre-filter, derived for any sample rate (same as libebur128/pyloudnorm).</summary>
    private sealed class KWeighting
    {
        private readonly Biquad _shelf;
        private readonly Biquad _highPass;

        private KWeighting(Biquad shelf, Biquad highPass)
        {
            _shelf = shelf;
            _highPass = highPass;
        }

        public static KWeighting Create(int sampleRate)
        {
            // Stage 1: high shelf modelling the acoustic effect of the head.
            {
                const double f0 = 1681.974450955533;
                const double g = 3.999843853973347;
                const double q = 0.7071752369554196;
                var k = Math.Tan(Math.PI * f0 / sampleRate);
                var vh = Math.Pow(10.0, g / 20.0);
                var vb = Math.Pow(vh, 0.4996667741545416);
                var a0 = 1.0 + k / q + k * k;
                var shelf = new Biquad(
                    (vh + vb * k / q + k * k) / a0,
                    2.0 * (k * k - vh) / a0,
                    (vh - vb * k / q + k * k) / a0,
                    2.0 * (k * k - 1.0) / a0,
                    (1.0 - k / q + k * k) / a0
                );

                // Stage 2: RLB high-pass.
                const double f1 = 38.13547087602444;
                const double q1 = 0.5003270373238773;
                var k1 = Math.Tan(Math.PI * f1 / sampleRate);
                var a01 = 1.0 + k1 / q1 + k1 * k1;
                var highPass = new Biquad(1.0, -2.0, 1.0, 2.0 * (k1 * k1 - 1.0) / a01, (1.0 - k1 / q1 + k1 * k1) / a01);
                return new KWeighting(shelf, highPass);
            }
        }

        public double Process(double x) => _highPass.Process(_shelf.Process(x));
    }

    private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double _z1;
        private double _z2;

        public double Process(double x)
        {
            // Transposed direct form II
            var y = b0 * x + _z1;
            _z1 = b1 * x - a1 * y + _z2;
            _z2 = b2 * x - a2 * y;
            return y;
        }
    }
}

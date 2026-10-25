// The coefficient search and frame encoder below follow Nintendo's DSPADPCM algorithm as
// reverse engineered by Jack Andersen (gc-dspadpcm-encode) and ported to C# by Alex Barney
// in VGAudio (https://github.com/Thealexbarney/VGAudio), MIT License, Copyright (c) 2016 Alex Barney.
// It is the same encoder BrawlBox-era tools and VGAudio use, so the output matches what the Wii
// DSP and Mario Kart Wii's stream player expect.

namespace WheelWizard.CustomMusic.Audio.Codecs;

/// <summary>Nintendo GameCube/Wii 4-bit DSP-ADPCM ("GC ADPCM"), as used inside BRSTM files.</summary>
public static class DspAdpcm
{
    public const int BytesPerFrame = 8;
    public const int SamplesPerFrame = 14;
    public const int NibblesPerFrame = 16;

    public static int SampleCountToNibbleCount(int sampleCount)
    {
        var frames = sampleCount / SamplesPerFrame;
        var extraSamples = sampleCount % SamplesPerFrame;
        var extraNibbles = extraSamples == 0 ? 0 : extraSamples + 2;
        return NibblesPerFrame * frames + extraNibbles;
    }

    public static int SampleCountToByteCount(int sampleCount) => (SampleCountToNibbleCount(sampleCount) + 1) / 2;

    public static int NibbleCountToSampleCount(int nibbleCount)
    {
        var frames = nibbleCount / NibblesPerFrame;
        var extraNibbles = nibbleCount % NibblesPerFrame;
        var extraSamples = extraNibbles < 2 ? 0 : extraNibbles - 2;
        return SamplesPerFrame * frames + extraSamples;
    }

    public static int ByteCountToSampleCount(int byteCount) => NibbleCountToSampleCount(byteCount * 2);

    /// <summary>The predictor/scale header byte of the frame that contains <paramref name="sample"/>.</summary>
    public static byte GetPredictorScale(byte[] adpcm, int sample) => adpcm[sample / SamplesPerFrame * BytesPerFrame];

    #region Decoding

    public static short[] Decode(ReadOnlySpan<byte> adpcm, short[] coefs, int sampleCount, short history1 = 0, short history2 = 0)
    {
        var pcm = new short[sampleCount];
        int hist1 = history1;
        int hist2 = history2;
        var frameCount = (sampleCount + SamplesPerFrame - 1) / SamplesPerFrame;
        var outIndex = 0;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * BytesPerFrame;
            if (frameOffset >= adpcm.Length)
                break;

            var header = adpcm[frameOffset];
            var scale = 1 << (header & 0xF);
            var predictor = (header >> 4) & 0x7;
            int coef1 = coefs[predictor * 2];
            int coef2 = coefs[predictor * 2 + 1];
            var samplesInFrame = Math.Min(SamplesPerFrame, sampleCount - outIndex);

            for (var s = 0; s < samplesInFrame; s++)
            {
                var byteIndex = frameOffset + 1 + s / 2;
                if (byteIndex >= adpcm.Length)
                    return pcm;

                int nibble = (s & 1) == 0 ? adpcm[byteIndex] >> 4 : adpcm[byteIndex] & 0xF;
                nibble = nibble >= 8 ? nibble - 16 : nibble;

                var sample = ((nibble * scale) << 11) + 1024 + coef1 * hist1 + coef2 * hist2;
                var value = Clamp16(sample >> 11);
                pcm[outIndex++] = (short)value;
                hist2 = hist1;
                hist1 = value;
            }
        }

        return pcm;
    }

    #endregion

    #region Encoding

    public sealed class EncodedChannel
    {
        public required short[] Coefficients { get; init; }
        public required byte[] Adpcm { get; init; }

        /// <summary>What the Wii will hear: the ADPCM data decoded again. Used for loop contexts and seek tables.</summary>
        public required short[] Decoded { get; init; }

        public byte InitialPredictorScale => Adpcm.Length > 0 ? Adpcm[0] : (byte)0;
    }

    public static EncodedChannel Encode(short[] pcm)
    {
        var coefs = CalculateCoefficients(pcm);
        var adpcm = Encode(pcm, coefs);
        var decoded = Decode(adpcm, coefs, pcm.Length);
        return new()
        {
            Coefficients = coefs,
            Adpcm = adpcm,
            Decoded = decoded,
        };
    }

    public static byte[] Encode(short[] pcm, short[] coefs)
    {
        var sampleCount = pcm.Length;
        var adpcm = new byte[SampleCountToByteCount(sampleCount)];
        var pcmBuffer = new short[2 + SamplesPerFrame];
        var adpcmBuffer = new byte[BytesPerFrame];
        var buffers = new EncodeBuffers();
        var frameCount = (sampleCount + SamplesPerFrame - 1) / SamplesPerFrame;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var samplesToCopy = Math.Min(sampleCount - frame * SamplesPerFrame, SamplesPerFrame);
            Array.Copy(pcm, frame * SamplesPerFrame, pcmBuffer, 2, samplesToCopy);
            Array.Clear(pcmBuffer, 2 + samplesToCopy, SamplesPerFrame - samplesToCopy);

            EncodeFrame(pcmBuffer, SamplesPerFrame, adpcmBuffer, coefs, buffers);
            Array.Copy(adpcmBuffer, 0, adpcm, frame * BytesPerFrame, SampleCountToByteCount(samplesToCopy));

            pcmBuffer[0] = pcmBuffer[14];
            pcmBuffer[1] = pcmBuffer[15];
        }

        return adpcm;
    }

    private static void EncodeFrame(short[] pcmInOut, int sampleCount, byte[] adpcmOut, short[] coefsIn, EncodeBuffers b)
    {
        for (var i = 0; i < 8; i++)
        {
            b.Coefs[i][0] = coefsIn[i * 2];
            b.Coefs[i][1] = coefsIn[i * 2 + 1];
        }

        for (var i = 0; i < 8; i++)
            EncodeWithCoefficient(pcmInOut, sampleCount, b.Coefs[i], b.PcmOut[i], b.AdpcmOut[i], out b.Scale[i], out b.TotalDistance[i]);

        var bestCoef = 0;
        var min = double.MaxValue;
        for (var i = 0; i < 8; i++)
        {
            if (b.TotalDistance[i] < min)
            {
                min = b.TotalDistance[i];
                bestCoef = i;
            }
        }

        for (var s = 0; s < sampleCount; s++)
            pcmInOut[s + 2] = (short)b.PcmOut[bestCoef][s + 2];

        adpcmOut[0] = CombineNibbles(bestCoef, b.Scale[bestCoef]);

        for (var s = sampleCount; s < 14; s++)
            b.AdpcmOut[bestCoef][s] = 0;

        for (var i = 0; i < 7; i++)
            adpcmOut[i + 1] = CombineNibbles(b.AdpcmOut[bestCoef][i * 2], b.AdpcmOut[bestCoef][i * 2 + 1]);
    }

    private static void EncodeWithCoefficient(
        short[] pcmIn,
        int sampleCount,
        short[] coefs,
        int[] pcmOut,
        int[] adpcmOut,
        out int scalePower,
        out double totalDistance
    )
    {
        int maxOverflow;
        var maxDistance = 0;

        pcmOut[0] = pcmIn[0];
        pcmOut[1] = pcmIn[1];

        // Encode the frame with a scale of 1 to find the largest prediction error.
        for (var s = 0; s < sampleCount; s++)
        {
            int inputSample = pcmIn[s + 2];
            var predictedSample = (pcmIn[s] * coefs[1] + pcmIn[s + 1] * coefs[0]) / 2048;
            var distance = Clamp16(inputSample - predictedSample);
            if (Math.Abs(distance) > Math.Abs(maxDistance))
                maxDistance = distance;
        }

        scalePower = 0;
        while (scalePower <= 12 && (maxDistance > 7 || maxDistance < -8))
        {
            maxDistance /= 2;
            scalePower++;
        }

        scalePower = scalePower <= 1 ? -1 : scalePower - 2;

        // Increase the scale until every residual fits into a nibble.
        do
        {
            scalePower++;
            var scale = (1 << scalePower) * 2048;
            totalDistance = 0;
            maxOverflow = 0;

            for (var s = 0; s < sampleCount; s++)
            {
                var inputSample = pcmIn[s + 2] * 2048;
                var predictedSample = pcmOut[s] * coefs[1] + pcmOut[s + 1] * coefs[0];
                var distance = inputSample - predictedSample;
                // The official encoder rounds through float this way; matching it keeps output identical.
                var unclamped =
                    distance > 0
                        ? (int)((double)((float)distance / scale) + 0.4999999f)
                        : (int)((double)((float)distance / scale) - 0.4999999f);

                var adpcmSample = Clamp4(unclamped);
                if (adpcmSample != unclamped)
                {
                    var overflow = Math.Abs(unclamped - adpcmSample);
                    if (overflow > maxOverflow)
                        maxOverflow = overflow;
                }

                adpcmOut[s] = adpcmSample;

                var decodedDistance = adpcmSample * scale;
                var correctedSample = predictedSample + decodedDistance;
                var scaledSample = (correctedSample + 1024) >> 11;
                pcmOut[s + 2] = Clamp16(scaledSample);

                double actualDistance = pcmIn[s + 2] - pcmOut[s + 2];
                totalDistance += actualDistance * actualDistance;
            }

            for (var x = maxOverflow + 8; x > 256; x >>= 1)
            {
                if (++scalePower >= 12)
                    scalePower = 11;
            }
        } while (scalePower < 12 && maxOverflow > 1);
    }

    private sealed class EncodeBuffers
    {
        public short[][] Coefs { get; } = new short[8][];
        public int[][] PcmOut { get; } = new int[8][];
        public int[][] AdpcmOut { get; } = new int[8][];
        public int[] Scale { get; } = new int[8];
        public double[] TotalDistance { get; } = new double[8];

        public EncodeBuffers()
        {
            for (var i = 0; i < 8; i++)
            {
                PcmOut[i] = new int[16];
                AdpcmOut[i] = new int[14];
                Coefs[i] = new short[2];
            }
        }
    }

    #endregion

    #region Coefficients

    /// <summary>Finds the 8 predictor coefficient pairs that best describe the whole channel.</summary>
    public static short[] CalculateCoefficients(short[] source)
    {
        var frameCount = (source.Length + SamplesPerFrame - 1) / SamplesPerFrame;
        var pcmHistBuffer = new short[28];
        var coefs = new short[16];
        var vec1 = new double[3];
        var vec2 = new double[3];
        var buffer = new double[3];
        var mtx = NewMatrix();
        var vecIdxs = new int[3];
        var records = new double[Math.Max(1, frameCount * 2), 3];
        var recordCount = 0;
        var vecBest = new double[8][];
        for (var i = 0; i < vecBest.Length; i++)
            vecBest[i] = new double[3];

        for (int sample = 0, remaining = source.Length; sample < source.Length; sample += 14, remaining -= 14)
        {
            Array.Clear(pcmHistBuffer, 14, 14);
            Array.Copy(source, sample, pcmHistBuffer, 14, Math.Min(14, remaining));

            InnerProductMerge(vec1, pcmHistBuffer);
            if (Math.Abs(vec1[0]) > 10.0)
            {
                OuterProductMerge(mtx, pcmHistBuffer);
                if (!AnalyzeRanges(mtx, vecIdxs, buffer))
                {
                    BidirectionalFilter(mtx, vecIdxs, vec1);
                    if (!QuadraticMerge(vec1))
                    {
                        FinishRecord(vec1, records, recordCount);
                        recordCount++;
                    }
                }
            }

            Array.Copy(pcmHistBuffer, 14, pcmHistBuffer, 0, 14);
        }

        vec1[0] = 1.0;
        vec1[1] = 0.0;
        vec1[2] = 0.0;

        for (var z = 0; z < recordCount; z++)
        {
            MatrixFilter(records, z, vecBest[0], mtx);
            for (var y = 1; y <= 2; y++)
                vec1[y] += vecBest[0][y];
        }

        // Silent input produces no records; keep the coefficients at zero instead of dividing by zero.
        if (recordCount > 0)
        {
            for (var y = 1; y <= 2; y++)
                vec1[y] /= recordCount;
        }

        MergeFinishRecord(vec1, vecBest[0]);

        var exp = 1;
        for (var w = 0; w < 3; )
        {
            vec2[0] = 0.0;
            vec2[1] = -1.0;
            vec2[2] = 0.0;
            for (var i = 0; i < exp; i++)
            {
                for (var y = 0; y <= 2; y++)
                    vecBest[exp + i][y] = 0.01 * vec2[y] + vecBest[i][y];
            }

            ++w;
            exp = 1 << w;
            FilterRecords(vecBest, exp, records, recordCount);
        }

        for (var z = 0; z < 8; z++)
        {
            coefs[z * 2] = ToCoefficient(-vecBest[z][1] * 2048.0);
            coefs[z * 2 + 1] = ToCoefficient(-vecBest[z][2] * 2048.0);
        }

        return coefs;
    }

    private static short ToCoefficient(double d)
    {
        if (double.IsNaN(d))
            return 0;
        if (d > 0.0)
            return d > short.MaxValue ? short.MaxValue : (short)Math.Round(d);
        return d < short.MinValue ? short.MinValue : (short)Math.Round(d);
    }

    private static double[][] NewMatrix()
    {
        var mtx = new double[3][];
        for (var i = 0; i < mtx.Length; i++)
            mtx[i] = new double[3];
        return mtx;
    }

    private static void InnerProductMerge(double[] vecOut, short[] pcmBuf)
    {
        for (var i = 0; i <= 2; i++)
        {
            vecOut[i] = 0.0f;
            for (var x = 0; x < 14; x++)
                vecOut[i] -= pcmBuf[14 + x - i] * pcmBuf[14 + x];
        }
    }

    private static void OuterProductMerge(double[][] mtxOut, short[] pcmBuf)
    {
        for (var x = 1; x <= 2; x++)
        {
            for (var y = 1; y <= 2; y++)
            {
                mtxOut[x][y] = 0.0;
                for (var z = 0; z < 14; z++)
                    mtxOut[x][y] += pcmBuf[14 + z - x] * pcmBuf[14 + z - y];
            }
        }
    }

    private static bool AnalyzeRanges(double[][] mtx, int[] vecIdxsOut, double[] recips)
    {
        double val,
            tmp;

        for (var x = 1; x <= 2; x++)
        {
            val = Math.Max(Math.Abs(mtx[x][1]), Math.Abs(mtx[x][2]));
            if (val < double.Epsilon)
                return true;

            recips[x] = 1.0 / val;
        }

        var maxIndex = 0;
        for (var i = 1; i <= 2; i++)
        {
            for (var x = 1; x < i; x++)
            {
                tmp = mtx[x][i];
                for (var y = 1; y < x; y++)
                    tmp -= mtx[x][y] * mtx[y][i];
                mtx[x][i] = tmp;
            }

            val = 0.0;
            for (var x = i; x <= 2; x++)
            {
                tmp = mtx[x][i];
                for (var y = 1; y < i; y++)
                    tmp -= mtx[x][y] * mtx[y][i];

                mtx[x][i] = tmp;
                tmp = Math.Abs(tmp) * recips[x];
                if (tmp >= val)
                {
                    val = tmp;
                    maxIndex = x;
                }
            }

            if (maxIndex != i)
            {
                for (var y = 1; y <= 2; y++)
                {
                    tmp = mtx[maxIndex][y];
                    mtx[maxIndex][y] = mtx[i][y];
                    mtx[i][y] = tmp;
                }

                recips[maxIndex] = recips[i];
            }

            vecIdxsOut[i] = maxIndex;

            if (i != 2)
            {
                tmp = 1.0 / mtx[i][i];
                for (var x = i + 1; x <= 2; x++)
                    mtx[x][i] *= tmp;
            }
        }

        var min = 1.0e10;
        var max = 0.0;
        for (var i = 1; i <= 2; i++)
        {
            tmp = Math.Abs(mtx[i][i]);
            if (tmp < min)
                min = tmp;
            if (tmp > max)
                max = tmp;
        }

        return min / max < 1.0e-10;
    }

    private static void BidirectionalFilter(double[][] mtx, int[] vecIdxs, double[] vecOut)
    {
        double tmp;

        for (int i = 1, x = 0; i <= 2; i++)
        {
            var index = vecIdxs[i];
            tmp = vecOut[index];
            vecOut[index] = vecOut[i];
            if (x != 0)
            {
                for (var y = x; y <= i - 1; y++)
                    tmp -= vecOut[y] * mtx[i][y];
            }
            else if (tmp != 0.0)
            {
                x = i;
            }

            vecOut[i] = tmp;
        }

        for (var i = 2; i > 0; i--)
        {
            tmp = vecOut[i];
            for (var y = i + 1; y <= 2; y++)
                tmp -= vecOut[y] * mtx[i][y];
            vecOut[i] = tmp / mtx[i][i];
        }

        vecOut[0] = 1.0;
    }

    private static bool QuadraticMerge(double[] inOutVec)
    {
        var v2 = inOutVec[2];
        var tmp = 1.0 - v2 * v2;

        if (tmp == 0.0)
            return true;

        var v0 = (inOutVec[0] - v2 * v2) / tmp;
        var v1 = (inOutVec[1] - inOutVec[1] * v2) / tmp;

        inOutVec[0] = v0;
        inOutVec[1] = v1;

        return Math.Abs(v1) > 1.0;
    }

    private static void ClampRecord(double[] inR)
    {
        for (var z = 1; z <= 2; z++)
        {
            if (inR[z] >= 1.0)
                inR[z] = 0.9999999999;
            else if (inR[z] <= -1.0)
                inR[z] = -0.9999999999;
        }
    }

    private static void FinishRecord(double[] inR, double[,] outR, int row)
    {
        ClampRecord(inR);
        outR[row, 0] = 1.0;
        outR[row, 1] = inR[2] * inR[1] + inR[1];
        outR[row, 2] = inR[2];
    }

    private static void FinishRecord(double[] inR, double[] outR)
    {
        ClampRecord(inR);
        outR[0] = 1.0;
        outR[1] = inR[2] * inR[1] + inR[1];
        outR[2] = inR[2];
    }

    private static void MatrixFilter(double[,] src, int row, double[] dst, double[][] mtx)
    {
        mtx[2][0] = 1.0;
        for (var i = 1; i <= 2; i++)
            mtx[2][i] = -src[row, i];

        for (var i = 2; i > 0; i--)
        {
            var val = 1.0 - mtx[i][i] * mtx[i][i];
            for (var y = 1; y <= i; y++)
                mtx[i - 1][y] = (mtx[i][i] * mtx[i][y] + mtx[i][y]) / val;
        }

        dst[0] = 1.0;
        for (var i = 1; i <= 2; i++)
        {
            dst[i] = 0.0;
            for (var y = 1; y <= i; y++)
                dst[i] += mtx[i][y] * dst[i - y];
        }
    }

    private static void MergeFinishRecord(double[] src, double[] dst)
    {
        var tmp = new double[3];
        var val = src[0];

        dst[0] = 1.0;
        for (var i = 1; i <= 2; i++)
        {
            var v2 = 0.0;
            for (var y = 1; y < i; y++)
                v2 += dst[y] * src[i - y];

            dst[i] = val > 0.0 ? -(v2 + src[i]) / val : 0.0;
            tmp[i] = dst[i];

            for (var y = 1; y < i; y++)
                dst[y] += dst[i] * dst[i - y];

            val *= 1.0 - dst[i] * dst[i];
        }

        FinishRecord(tmp, dst);
    }

    private static double ContrastVectors(double[] source1, double[,] source2, int row)
    {
        var val = (source2[row, 2] * source2[row, 1] + -source2[row, 1]) / (1.0 - source2[row, 2] * source2[row, 2]);
        var val1 = source1[0] * source1[0] + source1[1] * source1[1] + source1[2] * source1[2];
        var val2 = source1[0] * source1[1] + source1[1] * source1[2];
        var val3 = source1[0] * source1[2];
        return val1 + 2.0 * val * val2 + 2.0 * (-source2[row, 1] * val + -source2[row, 2]) * val3;
    }

    private static void FilterRecords(double[][] vecBest, int exp, double[,] records, int recordCount)
    {
        var bufferList = new double[8][];
        for (var i = 0; i < bufferList.Length; i++)
            bufferList[i] = new double[3];

        var mtx = NewMatrix();
        var buffer1 = new int[8];
        var buffer2 = new double[3];

        for (var x = 0; x < 2; x++)
        {
            for (var y = 0; y < exp; y++)
            {
                buffer1[y] = 0;
                for (var i = 0; i <= 2; i++)
                    bufferList[y][i] = 0.0;
            }

            for (var z = 0; z < recordCount; z++)
            {
                var index = 0;
                var value = 1.0e30;
                for (var i = 0; i < exp; i++)
                {
                    var tempVal = ContrastVectors(vecBest[i], records, z);
                    if (tempVal < value)
                    {
                        value = tempVal;
                        index = i;
                    }
                }

                buffer1[index]++;
                MatrixFilter(records, z, buffer2, mtx);
                for (var i = 0; i <= 2; i++)
                    bufferList[index][i] += buffer2[i];
            }

            for (var i = 0; i < exp; i++)
            {
                if (buffer1[i] > 0)
                {
                    for (var y = 0; y <= 2; y++)
                        bufferList[i][y] /= buffer1[i];
                }
            }

            for (var i = 0; i < exp; i++)
                MergeFinishRecord(bufferList[i], vecBest[i]);
        }
    }

    #endregion

    private static byte CombineNibbles(int high, int low) => (byte)((high << 4) | (low & 0xF));

    private static int Clamp16(int value) => value > short.MaxValue ? short.MaxValue : value < short.MinValue ? short.MinValue : value;

    private static int Clamp4(int value) => value > 7 ? 7 : value < -8 ? -8 : value;
}

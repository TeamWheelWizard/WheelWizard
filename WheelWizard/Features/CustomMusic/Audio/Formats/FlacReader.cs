using WheelWizard.CustomMusic.Audio.Tags;

namespace WheelWizard.CustomMusic.Audio.Formats;

/// <summary>A complete, dependency-free FLAC decoder (all subframe types, Rice/Rice2 coding, stereo decorrelation).</summary>
public static class FlacReader
{
    public static bool IsFlac(ReadOnlySpan<byte> data)
    {
        var start = Id3v2Reader.GetTagSize(data);
        return data.Length >= start + 4 && data[start] == 'f' && data[start + 1] == 'L' && data[start + 2] == 'a' && data[start + 3] == 'C';
    }

    public static AudioBuffer Read(byte[] file)
    {
        var metadata = new AudioMetadata();
        var id3Size = Id3v2Reader.GetTagSize(file);
        if (id3Size > 0)
            metadata.MergeMissing(Id3v2Reader.Read(file));

        var position = id3Size;
        if (!IsFlac(file))
            throw new InvalidDataException("This is not a FLAC file.");
        position += 4;

        var sampleRate = 0;
        var channels = 0;
        var bitsPerSample = 0;
        long totalSamples = 0;
        var bestPictureScore = -1;

        // Metadata blocks
        var isLast = false;
        while (!isLast && position + 4 <= file.Length)
        {
            var header = file[position];
            isLast = (header & 0x80) != 0;
            var type = header & 0x7F;
            var length = (file[position + 1] << 16) | (file[position + 2] << 8) | file[position + 3];
            position += 4;
            if (position + length > file.Length)
                throw new InvalidDataException("A FLAC metadata block is truncated.");

            var block = file.AsSpan(position, length);
            switch (type)
            {
                case 0: // STREAMINFO
                    sampleRate = (block[10] << 12) | (block[11] << 4) | (block[12] >> 4);
                    channels = ((block[12] >> 1) & 0x7) + 1;
                    bitsPerSample = (((block[12] & 1) << 4) | (block[13] >> 4)) + 1;
                    totalSamples = ((long)(block[13] & 0xF) << 32) | ((long)block[14] << 24) | ((long)block[15] << 16) | ((long)block[16] << 8) | block[17];
                    break;
                case 4: // VORBIS_COMMENT
                    metadata.MergeMissing(VorbisComments.ParseBlock(block));
                    break;
                case 6: // PICTURE
                {
                    var picture = VorbisComments.ParsePicture(block, out var pictureType);
                    var score = pictureType == 3 ? 2 : pictureType == 0 ? 1 : 0;
                    if (picture != null && score > bestPictureScore)
                    {
                        bestPictureScore = score;
                        metadata.Cover = picture;
                    }

                    break;
                }
            }

            position += length;
        }

        if (sampleRate <= 0 || channels <= 0)
            throw new InvalidDataException("The FLAC file has no STREAMINFO block.");

        var capacity = totalSamples > 0 ? (int)Math.Min(totalSamples, int.MaxValue / 2) : sampleRate * 60;
        var output = new List<float>[channels];
        for (var c = 0; c < channels; c++)
            output[c] = new List<float>(capacity);

        var reader = new BitReader(file, position);
        var decoder = new FrameDecoder(channels, bitsPerSample);
        while (reader.BytePosition + 2 < file.Length)
        {
            if (!reader.SeekToFrameSync())
                break;

            var frameStart = reader.BytePosition;
            try
            {
                var frame = decoder.DecodeFrame(reader);
                var scale = 1.0 / (1L << (frame.BitsPerSample - 1));
                for (var c = 0; c < channels; c++)
                {
                    var samples = frame.Samples[Math.Min(c, frame.Samples.Length - 1)];
                    var list = output[c];
                    for (var i = 0; i < frame.BlockSize; i++)
                        list.Add((float)(samples[i] * scale));
                }
            }
            catch (InvalidDataException)
            {
                // Corrupt frame: resynchronise one byte later instead of giving up on the whole song.
                reader.SetBytePosition(frameStart + 1);
            }

            if (totalSamples > 0 && output[0].Count >= totalSamples)
                break;
        }

        var buffers = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            var count = totalSamples > 0 ? (int)Math.Min(totalSamples, output[c].Count) : output[c].Count;
            buffers[c] = output[c].GetRange(0, count).ToArray();
        }

        if (buffers[0].Length == 0)
            throw new InvalidDataException("The FLAC file contains no audio frames.");

        return new AudioBuffer(buffers, sampleRate) { Metadata = metadata };
    }

    private sealed record DecodedFrame(long[][] Samples, int BlockSize, int BitsPerSample);

    private sealed class FrameDecoder(int streamChannels, int streamBitsPerSample)
    {
        private long[][] _buffers = [];

        public DecodedFrame DecodeFrame(BitReader reader)
        {
            // Frame header
            var sync = reader.ReadBits(15);
            if (sync != 0x7FFC)
                throw new InvalidDataException("Lost FLAC frame sync.");
            reader.ReadBits(1); // blocking strategy
            var blockSizeCode = (int)reader.ReadBits(4);
            var sampleRateCode = (int)reader.ReadBits(4);
            var channelAssignment = (int)reader.ReadBits(4);
            var sampleSizeCode = (int)reader.ReadBits(3);
            if (reader.ReadBits(1) != 0)
                throw new InvalidDataException("Reserved FLAC header bit is set.");

            ReadUtf8Number(reader);

            var blockSize = blockSizeCode switch
            {
                1 => 192,
                >= 2 and <= 5 => 576 << (blockSizeCode - 2),
                6 => (int)reader.ReadBits(8) + 1,
                7 => (int)reader.ReadBits(16) + 1,
                >= 8 => 256 << (blockSizeCode - 8),
                _ => throw new InvalidDataException("Reserved FLAC block size."),
            };

            switch (sampleRateCode)
            {
                case 12:
                    reader.ReadBits(8);
                    break;
                case 13:
                case 14:
                    reader.ReadBits(16);
                    break;
                case 15:
                    throw new InvalidDataException("Invalid FLAC sample rate code.");
            }

            var bitsPerSample = sampleSizeCode switch
            {
                0 => streamBitsPerSample,
                1 => 8,
                2 => 12,
                4 => 16,
                5 => 20,
                6 => 24,
                7 => 32,
                _ => throw new InvalidDataException("Reserved FLAC sample size."),
            };

            reader.ReadBits(8); // CRC-8 of the header

            var channelCount = channelAssignment <= 7 ? channelAssignment + 1 : 2;
            if (channelAssignment > 10)
                throw new InvalidDataException("Reserved FLAC channel assignment.");
            if (channelCount != streamChannels)
                throw new InvalidDataException("FLAC frame channel count does not match the stream.");

            EnsureBuffers(channelCount, blockSize);
            for (var c = 0; c < channelCount; c++)
            {
                // The side channel carries one extra bit.
                var extra = (channelAssignment == 8 && c == 1) || (channelAssignment == 9 && c == 0) || (channelAssignment == 10 && c == 1) ? 1 : 0;
                DecodeSubframe(reader, _buffers[c], blockSize, bitsPerSample + extra);
            }

            Decorrelate(channelAssignment, blockSize);

            reader.AlignToByte();
            reader.ReadBits(16); // CRC-16 of the frame

            return new DecodedFrame(_buffers, blockSize, bitsPerSample);
        }

        private void EnsureBuffers(int channels, int blockSize)
        {
            if (_buffers.Length != channels || _buffers[0].Length < blockSize)
            {
                _buffers = new long[channels][];
                for (var c = 0; c < channels; c++)
                    _buffers[c] = new long[Math.Max(blockSize, 4096)];
            }
        }

        private void Decorrelate(int assignment, int blockSize)
        {
            var a = _buffers[0];
            var b = _buffers.Length > 1 ? _buffers[1] : a;
            switch (assignment)
            {
                case 8: // left/side
                    for (var i = 0; i < blockSize; i++)
                        b[i] = a[i] - b[i];
                    break;
                case 9: // side/right
                    for (var i = 0; i < blockSize; i++)
                        a[i] += b[i];
                    break;
                case 10: // mid/side
                    for (var i = 0; i < blockSize; i++)
                    {
                        var side = b[i];
                        var mid = (a[i] << 1) | (side & 1);
                        a[i] = (mid + side) >> 1;
                        b[i] = (mid - side) >> 1;
                    }

                    break;
            }
        }

        private static void DecodeSubframe(BitReader reader, long[] output, int blockSize, int bitsPerSample)
        {
            if (reader.ReadBits(1) != 0)
                throw new InvalidDataException("FLAC subframe padding bit is set.");

            var type = (int)reader.ReadBits(6);
            var wastedBits = 0;
            if (reader.ReadBits(1) == 1)
            {
                wastedBits = 1;
                while (reader.ReadBits(1) == 0)
                    wastedBits++;
            }

            var bits = bitsPerSample - wastedBits;
            if (type == 0)
            {
                var value = reader.ReadSignedBits(bits);
                Array.Fill(output, value, 0, blockSize);
            }
            else if (type == 1)
            {
                for (var i = 0; i < blockSize; i++)
                    output[i] = reader.ReadSignedBits(bits);
            }
            else if (type is >= 8 and <= 12)
            {
                var order = type - 8;
                for (var i = 0; i < order; i++)
                    output[i] = reader.ReadSignedBits(bits);
                ReadResidual(reader, output, blockSize, order);
                RestoreFixed(output, blockSize, order);
            }
            else if (type >= 32)
            {
                var order = type - 31;
                for (var i = 0; i < order; i++)
                    output[i] = reader.ReadSignedBits(bits);

                var precision = (int)reader.ReadBits(4) + 1;
                if (precision == 16)
                    throw new InvalidDataException("Invalid FLAC LPC precision.");
                var shift = (int)reader.ReadSignedBits(5);
                var coefficients = new long[order];
                for (var i = 0; i < order; i++)
                    coefficients[i] = reader.ReadSignedBits(precision);

                ReadResidual(reader, output, blockSize, order);
                RestoreLpc(output, blockSize, coefficients, Math.Max(0, shift));
            }
            else
            {
                throw new InvalidDataException($"Reserved FLAC subframe type {type}.");
            }

            if (wastedBits > 0)
            {
                for (var i = 0; i < blockSize; i++)
                    output[i] <<= wastedBits;
            }
        }

        private static void ReadResidual(BitReader reader, long[] output, int blockSize, int predictorOrder)
        {
            var method = (int)reader.ReadBits(2);
            if (method > 1)
                throw new InvalidDataException("Reserved FLAC residual coding method.");

            var parameterBits = method == 0 ? 4 : 5;
            var escapeCode = method == 0 ? 0xF : 0x1F;
            var partitionOrder = (int)reader.ReadBits(4);
            var partitions = 1 << partitionOrder;
            var index = predictorOrder;

            for (var p = 0; p < partitions; p++)
            {
                var count = (blockSize >> partitionOrder) - (p == 0 ? predictorOrder : 0);
                if (count < 0 || index + count > blockSize)
                    throw new InvalidDataException("Invalid FLAC partition size.");

                var parameter = (int)reader.ReadBits(parameterBits);
                if (parameter == escapeCode)
                {
                    var rawBits = (int)reader.ReadBits(5);
                    for (var i = 0; i < count; i++)
                        output[index++] = rawBits == 0 ? 0 : reader.ReadSignedBits(rawBits);
                }
                else
                {
                    for (var i = 0; i < count; i++)
                        output[index++] = reader.ReadRice(parameter);
                }
            }
        }

        private static void RestoreFixed(long[] s, int blockSize, int order)
        {
            switch (order)
            {
                case 1:
                    for (var i = 1; i < blockSize; i++)
                        s[i] += s[i - 1];
                    break;
                case 2:
                    for (var i = 2; i < blockSize; i++)
                        s[i] += 2 * s[i - 1] - s[i - 2];
                    break;
                case 3:
                    for (var i = 3; i < blockSize; i++)
                        s[i] += 3 * s[i - 1] - 3 * s[i - 2] + s[i - 3];
                    break;
                case 4:
                    for (var i = 4; i < blockSize; i++)
                        s[i] += 4 * s[i - 1] - 6 * s[i - 2] + 4 * s[i - 3] - s[i - 4];
                    break;
            }
        }

        private static void RestoreLpc(long[] s, int blockSize, long[] coefficients, int shift)
        {
            var order = coefficients.Length;
            for (var i = order; i < blockSize; i++)
            {
                long sum = 0;
                for (var j = 0; j < order; j++)
                    sum += coefficients[j] * s[i - j - 1];
                s[i] += sum >> shift;
            }
        }

        private static void ReadUtf8Number(BitReader reader)
        {
            var first = (int)reader.ReadBits(8);
            var extraBytes = first switch
            {
                < 0x80 => 0,
                >= 0xC0 and < 0xE0 => 1,
                >= 0xE0 and < 0xF0 => 2,
                >= 0xF0 and < 0xF8 => 3,
                >= 0xF8 and < 0xFC => 4,
                >= 0xFC and < 0xFE => 5,
                0xFE => 6,
                _ => throw new InvalidDataException("Invalid FLAC frame number."),
            };
            for (var i = 0; i < extraBytes; i++)
            {
                if ((reader.ReadBits(8) & 0xC0) != 0x80)
                    throw new InvalidDataException("Invalid FLAC frame number.");
            }
        }
    }

    private sealed class BitReader(byte[] data, int startByte)
    {
        private int _bytePosition = startByte;
        private ulong _cache;
        private int _cacheBits;

        public int BytePosition => _bytePosition - _cacheBits / 8;

        public void SetBytePosition(int position)
        {
            _bytePosition = position;
            _cache = 0;
            _cacheBits = 0;
        }

        /// <summary>Moves to the next 0xFFF8/0xFFF9 frame sync code (byte aligned).</summary>
        public bool SeekToFrameSync()
        {
            AlignToByte();
            var position = BytePosition;
            while (position + 1 < data.Length)
            {
                if (data[position] == 0xFF && (data[position + 1] & 0xFE) == 0xF8)
                {
                    SetBytePosition(position);
                    return true;
                }

                position++;
            }

            return false;
        }

        public void AlignToByte()
        {
            var drop = _cacheBits % 8;
            _cache <<= drop;
            _cacheBits -= drop;
        }

        private void Refill()
        {
            while (_cacheBits <= 56)
            {
                ulong next = _bytePosition < data.Length ? data[_bytePosition] : 0UL;
                if (_bytePosition >= data.Length + 8)
                    throw new InvalidDataException("Unexpected end of FLAC data.");
                _bytePosition++;
                _cache |= next << (56 - _cacheBits);
                _cacheBits += 8;
            }
        }

        public ulong ReadBits(int count)
        {
            if (count == 0)
                return 0;
            if (count > 32)
                return (ReadBits(count - 32) << 32) | ReadBits(32);

            if (_cacheBits < count)
                Refill();
            var value = _cache >> (64 - count);
            _cache <<= count;
            _cacheBits -= count;
            return value;
        }

        public long ReadSignedBits(int count)
        {
            if (count == 0)
                return 0;
            var value = (long)ReadBits(count);
            return (value << (64 - count)) >> (64 - count);
        }

        public long ReadRice(int parameter)
        {
            long quotient = 0;
            while (true)
            {
                if (_cacheBits == 0)
                    Refill();
                if (_cache == 0)
                {
                    quotient += _cacheBits;
                    _cacheBits = 0;
                    if (quotient > 1L << 32)
                        throw new InvalidDataException("Corrupt FLAC Rice code.");
                    continue;
                }

                var zeros = System.Numerics.BitOperations.LeadingZeroCount(_cache);
                if (zeros >= _cacheBits)
                {
                    quotient += _cacheBits;
                    _cache = 0;
                    _cacheBits = 0;
                    continue;
                }

                quotient += zeros;
                // Shifting a ulong by 64 is a no-op in C#, so a terminator in the very last bit needs care.
                var consumed = zeros + 1;
                _cache = consumed >= 64 ? 0 : _cache << consumed;
                _cacheBits -= consumed;
                break;
            }

            var remainder = (long)ReadBits(parameter);
            var value = (quotient << parameter) | remainder;
            return (value >> 1) ^ -(value & 1);
        }
    }
}

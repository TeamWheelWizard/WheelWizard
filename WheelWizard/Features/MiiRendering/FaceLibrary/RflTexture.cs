using System.Buffers.Binary;
using System.Numerics;

namespace WheelWizard.MiiRendering.FaceLibrary;

/// <summary>How a texture repeats outside 0..1 (same values as GX's wrap modes).</summary>
public enum MiiTextureWrap
{
    Clamp = 0,
    Repeat = 1,
    Mirror = 2,
}

/// <summary>A decoded texture: straight (not premultiplied) RGBA8, row 0 at the top (texture coordinate t = 0).</summary>
public sealed record MiiTexture(int Width, int Height, byte[] Rgba, MiiTextureWrap Wrap = MiiTextureWrap.Clamp)
{
    /// <summary>Bilinear sample in 0..1 per channel.</summary>
    public (float R, float G, float B, float A) Sample(float u, float v)
    {
        var x = u * Width - 0.5f;
        var y = v * Height - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        int ix0 = Wrapped(x0, Width),
            ix1 = Wrapped(x0 + 1, Width),
            iy0 = Wrapped(y0, Height),
            iy1 = Wrapped(y0 + 1, Height);

        float Channel(int c)
        {
            var top = Rgba[(iy0 * Width + ix0) * 4 + c] * (1f - fx) + Rgba[(iy0 * Width + ix1) * 4 + c] * fx;
            var bottom = Rgba[(iy1 * Width + ix0) * 4 + c] * (1f - fx) + Rgba[(iy1 * Width + ix1) * 4 + c] * fx;
            return (top * (1f - fy) + bottom * fy) / 255f;
        }

        return (Channel(0), Channel(1), Channel(2), Channel(3));
    }

    /// <summary>
    /// Bicubic sample in 0..1 per channel: a sharp cubic (Mitchell-Netravali B = 0, C = 1) that keeps thin lines
    /// thin, for tracing shapes out of small textures. Not clamped, as it overshoots a little near edges.
    /// </summary>
    public Vector4 SampleSmooth(float u, float v)
    {
        static float Weight(float x)
        {
            x = MathF.Abs(x);
            return x < 1f ? (x - 2f) * x * x + 1f
                : x < 2f ? ((5f - x) * x - 8f) * x + 4f
                : 0f;
        }

        var x = u * Width - 0.5f;
        var y = v * Height - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        Span<float> wx = stackalloc float[4];
        Span<int> ix = stackalloc int[4];
        for (var i = 0; i < 4; i++)
        {
            wx[i] = Weight(x - (x0 + i - 1));
            ix[i] = Wrapped(x0 + i - 1, Width);
        }

        var sum = Vector4.Zero;
        var total = 0f;
        for (var j = 0; j < 4; j++)
        {
            var wy = Weight(y - (y0 + j - 1));
            var row = Wrapped(y0 + j - 1, Height) * Width;
            for (var i = 0; i < 4; i++)
            {
                var w = wy * wx[i];
                var o = (row + ix[i]) * 4;
                sum += new Vector4(Rgba[o], Rgba[o + 1], Rgba[o + 2], Rgba[o + 3]) * w;
                total += w;
            }
        }

        return sum / (total * 255f);
    }

    private int Wrapped(int i, int size)
    {
        switch (Wrap)
        {
            case MiiTextureWrap.Repeat:
                return ((i % size) + size) % size;
            case MiiTextureWrap.Mirror:
                var period = ((i % (size * 2)) + size * 2) % (size * 2);
                return period < size ? period : size * 2 - 1 - period;
            default:
                return Math.Clamp(i, 0, size - 1);
        }
    }
}

/// <summary>Decodes the face library's textures (RFLTexture: a 32-byte header and a GX tiled image).</summary>
public static class RflTexture
{
    private const int HeaderSize = 0x20;

    public static OperationResult<MiiTexture> Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            return Fail("Face library texture is missing.");

        var format = data[0];
        int width = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        int height = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        var wrap = (MiiTextureWrap)Math.Min((int)data[6], 2);
        var imageOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data[0x1C..]);
        if (width == 0 || height == 0 || imageOffset < HeaderSize || imageOffset > data.Length)
            return Fail("Face library texture has an invalid header.");

        var (blockWidth, blockHeight, bitsPerPixel) = format switch
        {
            0 => (8, 8, 4), // I4
            1 => (8, 4, 8), // I8
            2 => (8, 4, 8), // IA4
            3 => (4, 4, 16), // IA8
            4 => (4, 4, 16), // RGB565
            5 => (4, 4, 16), // RGB5A3
            6 => (4, 4, 32), // RGBA8
            _ => (0, 0, 0),
        };
        if (bitsPerPixel == 0)
            return Fail($"Face library texture format {format} is not supported.");

        var blocksX = (width + blockWidth - 1) / blockWidth;
        var blocksY = (height + blockHeight - 1) / blockHeight;
        var blockBytes = blockWidth * blockHeight * bitsPerPixel / 8;
        var image = data[imageOffset..];
        if (image.Length < blocksX * blocksY * blockBytes)
            return Fail("Face library texture is truncated.");

        var rgba = new byte[width * height * 4];
        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            var block = image.Slice((by * blocksX + bx) * blockBytes, blockBytes);
            for (var ty = 0; ty < blockHeight; ty++)
            for (var tx = 0; tx < blockWidth; tx++)
            {
                int x = bx * blockWidth + tx,
                    y = by * blockHeight + ty;
                if (x >= width || y >= height)
                    continue;

                var texel = ty * blockWidth + tx;
                var (r, g, b, a) = format switch
                {
                    0 => Intensity(((block[texel / 2] >> (texel % 2 == 0 ? 4 : 0)) & 0xF) * 0x11),
                    1 => Intensity(block[texel]),
                    2 => IntensityAlpha((block[texel] & 0xF) * 0x11, (block[texel] >> 4) * 0x11),
                    3 => IntensityAlpha(block[texel * 2 + 1], block[texel * 2]),
                    4 => Rgb565(BinaryPrimitives.ReadUInt16BigEndian(block[(texel * 2)..])),
                    5 => Rgb5A3(BinaryPrimitives.ReadUInt16BigEndian(block[(texel * 2)..])),
                    _ => (block[texel * 2 + 1], block[32 + texel * 2], block[32 + texel * 2 + 1], block[texel * 2]),
                };

                var o = (y * width + x) * 4;
                rgba[o] = (byte)r;
                rgba[o + 1] = (byte)g;
                rgba[o + 2] = (byte)b;
                rgba[o + 3] = (byte)a;
            }
        }

        return new MiiTexture(width, height, rgba, wrap);
    }

    private static (int, int, int, int) Intensity(int i) => (i, i, i, i);

    private static (int, int, int, int) IntensityAlpha(int i, int a) => (i, i, i, a);

    private static (int, int, int, int) Rgb565(int c) =>
        (Expand((c >> 11) & 0x1F, 5), Expand((c >> 5) & 0x3F, 6), Expand(c & 0x1F, 5), 255);

    private static (int, int, int, int) Rgb5A3(int c) =>
        (c & 0x8000) != 0
            ? (Expand((c >> 10) & 0x1F, 5), Expand((c >> 5) & 0x1F, 5), Expand(c & 0x1F, 5), 255)
            : (Expand((c >> 8) & 0xF, 4), Expand((c >> 4) & 0xF, 4), Expand(c & 0xF, 4), Expand((c >> 12) & 0x7, 3));

    private static int Expand(int value, int bits) => (value * 255 + ((1 << bits) - 1) / 2) / ((1 << bits) - 1);
}

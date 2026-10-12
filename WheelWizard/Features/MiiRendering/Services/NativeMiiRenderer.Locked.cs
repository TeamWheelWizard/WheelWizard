using System.Collections.Concurrent;
using System.Numerics;
using WheelWizard.MiiImages.Domain;

namespace WheelWizard.MiiRendering.Services;

// The locked look (see LockedMii): a question mark for a face, no nose, and the whole Mii grey.
public sealed partial class NativeMiiRenderer
{
    /// <summary>Locked faces by studio data and mask resolution. Only ever the locked Mii's model, so just a few.</summary>
    private static readonly ConcurrentDictionary<(string Studio, int Resolution), (IntPtr Handle, TextureData Texture)> LockedFaces = new();

    // The question mark, in glyph units: 1 tall, x centred, y down. A hook that's most of a circle, a stem and a dot.
    private const float GlyphStroke = 0.075f;
    private const float HookRadius = 0.2f;
    private const float HookStartDegrees = 175f;
    private const float HookEndDegrees = 50f;
    private const float DotRadius = 0.085f;
    private static readonly Vector2 HookCenter = new(0f, 0.27f);
    private static readonly Vector2 StemBend = new(0f, 0.57f);
    private static readonly Vector2 StemEnd = new(0f, 0.67f);
    private static readonly Vector2 DotCenter = new(0f, 0.88f);

    /// <summary>Colour of the question mark (straight RGBA), dark like the eyes.</summary>
    private static readonly (byte R, byte G, byte B) GlyphInk = (40, 36, 36);

    /// <summary>The luminance weights the locked look turns colours grey with (same as the realtime shader).</summary>
    private static readonly Vector3 GreyWeights = new(0.299f, 0.587f, 0.114f);

    public OperationResult<MiiMaskLayerTexture> BuildLockedMaskLayer(string studioData, MiiHeadDetail detail = MiiHeadDetail.Full)
    {
        var charInfoResult = GetOrCreateCharInfo(studioData);
        if (charInfoResult.IsFailure)
            return charInfoResult.Error!;

        // Same resolution as the head's own mask (BuildManagedDrawParams).
        var request = BuildRequest(studioData, new MiiImageSpecifications { Size = SizeFor(detail) });
        var face = LockedFace(studioData, charInfoResult.Value, request.Width <= 384 ? 256 : 512);
        return new MiiMaskLayerTexture(face.Texture.Pixels, face.Texture.Width, face.Texture.Height);
    }

    /// <summary>The head of a locked Mii: its face mask swapped for the question mark, and without the nose.</summary>
    private static List<DecodedDrawMesh> WithLockedFace(
        IReadOnlyList<DecodedDrawMesh> meshes,
        string studioData,
        FflNativeInterop.FFLiCharInfo charInfo,
        int resolution
    )
    {
        var face = LockedFace(studioData, charInfo, resolution).Handle;
        var locked = new List<DecodedDrawMesh>(meshes.Count);
        foreach (var mesh in meshes)
        {
            var type = mesh.DrawParam.modulateParam.type;
            if (type is FflNativeInterop.ModulateTypeShapeNose or FflNativeInterop.ModulateTypeShapeNoseLine)
                continue;
            if (type != FflNativeInterop.ModulateTypeShapeMask)
            {
                locked.Add(mesh);
                continue;
            }

            var drawParam = mesh.DrawParam;
            drawParam.modulateParam.pTexture2D = face;
            locked.Add(
                new DecodedDrawMesh(
                    drawParam,
                    mesh.Positions,
                    mesh.Texcoords,
                    mesh.Normals,
                    mesh.Tangents,
                    mesh.VertexParameters,
                    mesh.Indices,
                    mesh.HasTangent,
                    mesh.Material
                )
            );
        }

        return locked;
    }

    /// <summary>
    /// Turns a rendered image (BGRA) grey: the still locked look. The scan lines are left to the realtime view, as
    /// images are often shown much smaller than they're rendered and fine lines would shimmer there.
    /// </summary>
    private static void TurnGrey(byte[] bgra)
    {
        for (var o = 0; o + 3 < bgra.Length; o += 4)
        {
            if (bgra[o + 3] == 0)
                continue;
            var grey = Vector3.Dot(new Vector3(bgra[o + 2], bgra[o + 1], bgra[o]), GreyWeights);
            bgra[o] = bgra[o + 1] = bgra[o + 2] = (byte)Math.Clamp((int)MathF.Round(grey), 0, 255);
        }
    }

    /// <summary>The question mark face of a Mii, painted on a mask texture (registered, so draw params can use it).</summary>
    private static (IntPtr Handle, TextureData Texture) LockedFace(
        string studioData,
        FflNativeInterop.FFLiCharInfo charInfo,
        int resolution
    ) =>
        LockedFaces.GetOrAdd(
            (studioData, resolution),
            _ =>
            {
                var texture = new TextureData(
                    resolution,
                    resolution,
                    FflNativeInterop.TextureFormatRgba8,
                    4,
                    PaintLockedFace(charInfo, resolution)
                );
                return (TextureRegistry.RegisterTexture(texture), texture);
            }
        );

    /// <summary>
    /// A mask texture (RGBA8) with one big question mark where the eyebrows, eyes and mouth would be, upright and
    /// readable however the mask is laid out on the face.
    /// </summary>
    private static byte[] PaintLockedFace(FflNativeInterop.FFLiCharInfo charInfo, int size)
    {
        var parts = BuildRawMaskParts(charInfo);
        Vector2 Center(Vector2[] quad) => (quad[0] + quad[1] + quad[2] + quad[3]) / 4f;
        var eyeRight = MaskQuad(parts.EyeR);
        var eyeLeft = MaskQuad(parts.EyeL);
        var eyes = (Center(eyeRight) + Center(eyeLeft)) / 2f;

        // The Mii's right eye is on the left of the face as you look at it: that gives "right" on the texture, and
        // the mouth gives "down".
        var right = Vector2.Normalize(Center(eyeLeft) - Center(eyeRight));
        var down = new Vector2(-right.Y, right.X);
        if (Vector2.Dot(down, Center(MaskQuad(parts.Mouth)) - eyes) < 0f)
            down = -down;

        // From the top of the eyes to the bottom of the mouth (the eyebrows are too close to the hair).
        Vector2[][] features = [eyeRight, eyeLeft, MaskQuad(parts.Mouth)];
        var top = features.SelectMany(quad => quad).Min(corner => Vector2.Dot(corner - eyes, down));
        var bottom = features.SelectMany(quad => quad).Max(corner => Vector2.Dot(corner - eyes, down));
        var glyphHeight = bottom - top;
        var glyphTop = eyes + down * ((top + bottom - glyphHeight) / 2f);

        var pixels = new byte[size * size * 4];
        // One texel in glyph units, for smooth edges.
        var texel = 1f / (size * glyphHeight);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var offset = new Vector2((x + 0.5f) / size, (y + 0.5f) / size) - glyphTop;
            var glyph = new Vector2(Vector2.Dot(offset, right), Vector2.Dot(offset, down)) / glyphHeight;
            var coverage = Math.Clamp(0.5f - QuestionMarkDistance(glyph) / texel, 0f, 1f);
            var o = (y * size + x) * 4;
            // The ink everywhere, so the edges don't fade through black when the texture is filtered.
            pixels[o] = GlyphInk.R;
            pixels[o + 1] = GlyphInk.G;
            pixels[o + 2] = GlyphInk.B;
            pixels[o + 3] = (byte)MathF.Round(coverage * 255f);
        }

        return pixels;
    }

    /// <summary>Distance from a point (glyph units) to the edge of the question mark; negative inside it.</summary>
    private static float QuestionMarkDistance(Vector2 p)
    {
        Vector2 HookPoint(float degrees) =>
            HookCenter + HookRadius * new Vector2(MathF.Cos(degrees * MathF.PI / 180f), MathF.Sin(degrees * MathF.PI / 180f));

        // The hook: around the top from the left to the lower right (angles with y down, so 270 is straight up).
        var fromCenter = p - HookCenter;
        var angle = MathF.Atan2(fromCenter.Y, fromCenter.X) * 180f / MathF.PI;
        if (angle < 0f)
            angle += 360f;
        var hookEnd = HookPoint(HookEndDegrees);
        var line =
            angle >= HookStartDegrees || angle <= HookEndDegrees
                ? MathF.Abs(fromCenter.Length() - HookRadius)
                : MathF.Min(Vector2.Distance(p, HookPoint(HookStartDegrees)), Vector2.Distance(p, hookEnd));
        // Then in to the middle and down the stem.
        line = MathF.Min(line, SegmentDistance(p, hookEnd, StemBend));
        line = MathF.Min(line, SegmentDistance(p, StemBend, StemEnd));
        return MathF.Min(line - GlyphStroke, Vector2.Distance(p, DotCenter) - DotRadius);
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}

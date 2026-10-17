using System.Numerics;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiRendering.FaceLibrary;

/// <summary>One mesh of a head: its shape, and either a flat colour or a texture (straight RGBA, drawn as is).</summary>
public sealed record RflHeadPart(HeadShape Shape, RflShape Mesh, Vector4 Color, MiiTexture? Texture, bool DoubleSided = false);

/// <summary>
/// Builds a Mii head out of the Wii face library the way the Wii does (RFL_Model.c and RFL_MakeTex.c of the
/// RVLFaceLib decompilation): the shapes placed on the faceline, and the face parts painted into one mask texture.
/// </summary>
public static class RflHead
{
    #region Colours (RFL_Model.c, RFLi_FaceConfig/colors.h)

    private static readonly Vector4[] FacelineColors =
    [
        Rgb(240, 216, 196),
        Rgb(255, 188, 128),
        Rgb(216, 136, 80),
        Rgb(255, 176, 144),
        Rgb(152, 80, 48),
        Rgb(82, 46, 28),
    ];

    // Hair, eyebrows, beard and mustache share these.
    private static readonly Vector4[] HairColors =
    [
        Rgb(30, 26, 24),
        Rgb(56, 32, 21),
        Rgb(85, 38, 23),
        Rgb(112, 64, 36),
        Rgb(114, 114, 120),
        Rgb(73, 54, 26),
        Rgb(122, 89, 40),
        Rgb(193, 159, 100),
    ];

    private static readonly Vector4[] GlassColors =
    [
        Rgb(16, 16, 16),
        Rgb(96, 56, 16),
        Rgb(152, 24, 16),
        Rgb(32, 48, 96),
        Rgb(144, 88, 0),
        Rgb(96, 88, 80),
    ];

    private static readonly Vector4[] FavoriteColors =
    [
        Rgb(184, 64, 48),
        Rgb(240, 120, 40),
        Rgb(248, 216, 32),
        Rgb(128, 200, 40),
        Rgb(0, 116, 40),
        Rgb(32, 72, 152),
        Rgb(64, 160, 216),
        Rgb(232, 96, 120),
        Rgb(112, 44, 168),
        Rgb(72, 56, 24),
        Rgb(224, 224, 224),
        Rgb(24, 24, 20),
    ];

    private static readonly Vector4[] EyeColors =
    [
        Rgb(0, 0, 0),
        Rgb(124, 128, 128),
        Rgb(112, 80, 64),
        Rgb(112, 110, 64),
        Rgb(88, 104, 184),
        Rgb(72, 128, 104),
    ];

    private static readonly Vector4[] LipColors = [Rgb(190, 78, 38), Rgb(216, 48, 40), Rgb(207, 68, 71)];
    private static readonly Vector4[] LipShadeColors = [Rgb(113, 42, 4), Rgb(120, 21, 16), Rgb(126, 37, 40)];
    private static readonly Vector4 MoleColor = Rgb(18, 15, 15);

    public static Vector4 FavoriteColor(int index) => FavoriteColors[Math.Clamp(index, 0, FavoriteColors.Length - 1)];

    /// <summary>A palette colour, a fifth more saturated than the Wii's (they look bland under our lighting).</summary>
    private static Vector4 Rgb(byte r, byte g, byte b)
    {
        const float saturation = 1.2f;
        var color = new Vector3(r, g, b) / 255f;
        var grey = new Vector3(Vector3.Dot(color, new Vector3(0.299f, 0.587f, 0.114f)));
        return new Vector4(Vector3.Clamp(grey + (color - grey) * saturation, Vector3.Zero, Vector3.One), 1f);
    }

    #endregion

    public static OperationResult<List<RflHeadPart>> Build(RflResource resource, RflCharInfo info, int expression, int maskResolution)
    {
        var faceline = resource.Get(RflArchive.Faceline, info.FaceType);
        var anchors = RflShape.ReadAnchors(faceline);
        var skin = FacelineColors[info.FaceColor];
        var parts = new List<RflHeadPart>(10);

        OperationResult<RflShape> Shape(RflArchive archive, int index, float scale = 1f, Vector3 translate = default, bool flipX = false) =>
            RflShape.Decode(archive, resource.Get(archive, index), scale, translate, flipX);

        OperationResult<MiiTexture> Texture(RflArchive archive, int index) => RflTexture.Decode(resource.Get(archive, index));

        // Opaque parts, in the order RFLDrawOpaCore draws them.
        var facelineShape = Shape(RflArchive.Faceline, info.FaceType);
        if (facelineShape.IsFailure)
            return facelineShape.Error!;
        var facelineTexture = FacelineTexture(resource, info.FaceTex, skin);
        if (facelineTexture.IsFailure)
            return facelineTexture.Error!;
        parts.Add(new RflHeadPart(HeadShape.Faceline, facelineShape.Value, skin, facelineTexture.Value));

        var beard = Shape(RflArchive.Beard, info.BeardType, translate: anchors.Beard);
        if (beard.IsFailure)
            return beard.Error!;
        parts.Add(new RflHeadPart(HeadShape.Beard, beard.Value, HairColors[info.BeardColor], null));

        var noseScale = 0.4f + 0.175f * info.NoseScale;
        var nosePosition = anchors.Nose + new Vector3(0f, -1.5f * (info.NoseY - 8), 0f);
        var nose = Shape(RflArchive.Nose, info.NoseType, noseScale, nosePosition);
        if (nose.IsFailure)
            return nose.Error!;
        parts.Add(new RflHeadPart(HeadShape.Nose, nose.Value, skin, null));

        var forehead = Shape(RflArchive.Forehead, info.HairType, translate: anchors.Hair, flipX: info.HairFlip);
        if (forehead.IsFailure)
            return forehead.Error!;
        parts.Add(new RflHeadPart(HeadShape.Forehead, forehead.Value, skin, null));

        var hair = Shape(RflArchive.Hair, info.HairType, translate: anchors.Hair, flipX: info.HairFlip);
        if (hair.IsFailure)
            return hair.Error!;
        parts.Add(new RflHeadPart(HeadShape.Hair, hair.Value, HairColors[info.HairColor], null));

        var cap = Shape(RflArchive.Cap, info.HairType, translate: anchors.Hair, flipX: info.HairFlip);
        if (cap.IsFailure)
            return cap.Error!;
        if (!cap.Value.IsEmpty)
        {
            var capTexture = Texture(RflArchive.CapTex, info.HairType);
            if (capTexture.IsFailure)
                return capTexture.Error!;
            // The cap is the favourite colour, brightened by its pattern: (colour + colour * pattern) / 2.
            var color = FavoriteColor(info.FavoriteColor);
            parts.Add(
                new RflHeadPart(
                    HeadShape.Cap,
                    cap.Value,
                    color,
                    Paint(capTexture.Value, t => new Vector4(new Vector3(color.X, color.Y, color.Z) * (1f + t.X) / 2f, 1f))
                )
            );
        }

        // Then the see-through ones, as RFLDrawXluCore.
        var mask = Shape(RflArchive.Mask, info.FaceType);
        if (mask.IsFailure)
            return mask.Error!;
        var maskTexture = BuildMask(resource, info, expression, MiiMaskLayers.All, maskResolution);
        if (maskTexture.IsFailure)
            return maskTexture.Error!;
        parts.Add(new RflHeadPart(HeadShape.Mask, mask.Value, Vector4.One, maskTexture.Value));

        var noseline = Shape(RflArchive.Noseline, info.NoseType, noseScale, nosePosition);
        if (noseline.IsFailure)
            return noseline.Error!;
        if (!noseline.Value.IsEmpty)
        {
            var noselineTexture = Texture(RflArchive.NoselineTex, info.NoseType);
            if (noselineTexture.IsFailure)
                return noselineTexture.Error!;
            parts.Add(
                new RflHeadPart(
                    HeadShape.NoseLine,
                    noseline.Value,
                    Vector4.UnitW,
                    Paint(noselineTexture.Value, t => new Vector4(0f, 0f, 0f, t.W))
                )
            );
        }

        if (info.GlassType > 0)
        {
            var glassPosition = anchors.Nose + new Vector3(0f, 5f - 1.5f * (info.GlassY - 11), 2f);
            var glass = Shape(RflArchive.Glass, 0, 0.4f + 0.15f * info.GlassScale, glassPosition);
            if (glass.IsFailure)
                return glass.Error!;
            var glassTexture = Texture(RflArchive.GlassTex, info.GlassType);
            if (glassTexture.IsFailure)
                return glassTexture.Error!;
            var color = GlassColors[info.GlassColor];
            parts.Add(
                new RflHeadPart(
                    HeadShape.Glass,
                    glass.Value,
                    color,
                    Paint(glassTexture.Value, t => new Vector4(color.X * t.X, color.Y * t.X, color.Z * t.X, t.W)),
                    DoubleSided: true
                )
            );
        }

        parts.RemoveAll(part => part.Mesh.IsEmpty);
        return parts;
    }

    /// <summary>
    /// The face skin with its make-up or wrinkles, or null when the Mii has neither. The texture keeps its coverage
    /// in green and its own green in alpha (RFLDrawOpaCore swaps them back with a TEV swap table).
    /// </summary>
    private static OperationResult<MiiTexture?> FacelineTexture(RflResource resource, int index, Vector4 skin)
    {
        var decoded = RflTexture.Decode(resource.Get(RflArchive.FaceTex, index));
        if (decoded.IsFailure)
            return decoded.Error!;
        var texture = decoded.Value;
        var covered = false;
        for (var i = 1; i < texture.Rgba.Length && !covered; i += 4)
            covered = texture.Rgba[i] != 0;
        if (!covered)
            return Ok<MiiTexture?>(null);

        return Paint(texture, t => new Vector4(Vector3.Lerp(new Vector3(skin.X, skin.Y, skin.Z), new Vector3(t.X, t.W, t.Z), t.Y), 1f));
    }

    /// <summary>A texture recoloured texel by texel (in and out as 0..1 RGBA).</summary>
    private static MiiTexture Paint(MiiTexture texture, Func<Vector4, Vector4> paint)
    {
        var rgba = new byte[texture.Rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var color = paint(new Vector4(texture.Rgba[i], texture.Rgba[i + 1], texture.Rgba[i + 2], texture.Rgba[i + 3]) / 255f);
            rgba[i] = ToByte(color.X);
            rgba[i + 1] = ToByte(color.Y);
            rgba[i + 2] = ToByte(color.Z);
            rgba[i + 3] = ToByte(color.W);
        }

        return texture with
        {
            Rgba = rgba,
        };
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    #region Face mask (RFL_MakeTex.c)

    private const int EyeSmile = 48;
    private const int EyeSurprise = 49;
    private const int EyeBlink = 26;
    private const int EyeWink = 47;
    private const int MouthAnger = 10;
    private const int MouthSorrow = 12;
    private const int MouthOpen = 24;
    private const int MouthLike = 19;

    // Eye and eyebrow types are drawn at different base angles: rotation offsets per type (RFLi_FaceConfig/offsets.h).
    private static readonly byte[] EyeRotateOffsets =
    [
        29,
        28,
        28,
        28,
        29,
        28,
        28,
        28,
        29,
        28,
        28,
        28,
        28,
        29,
        29,
        28,
        28,
        28,
        29,
        29,
        28,
        29,
        28,
        29,
        29,
        28,
        29,
        28,
        28,
        29,
        28,
        28,
        28,
        29,
        29,
        29,
        28,
        28,
        29,
        29,
        29,
        28,
        28,
        29,
        29,
        29,
        29,
        29,
        28,
        28,
    ];

    private static readonly byte[] EyebrowRotateOffsets =
    [
        26,
        26,
        27,
        25,
        26,
        25,
        26,
        25,
        28,
        25,
        26,
        24,
        27,
        27,
        26,
        26,
        25,
        25,
        26,
        26,
        27,
        26,
        25,
        27,
    ];

    /// <summary>
    /// How an expression changes the face parts: eye textures for the Mii's right and left eye and the mouth texture
    /// (-1 keeps the Mii's own), plus how far the eyes and eyebrows tilt and how much the eyebrows lift. The Wii has
    /// the first seven; the rest (open mouth and wink variants) are made of the same parts.
    /// </summary>
    private readonly record struct ExpressionParts(int EyeRight, int EyeLeft, int Mouth, int Tilt = 0, int EyebrowLift = 0);

    // Indexed by expression id (MiiExpression).
    private static readonly ExpressionParts[] Expressions =
    [
        new(-1, -1, -1), // normal
        new(EyeSmile, EyeSmile, -1), // smile
        new(-1, -1, MouthAnger, Tilt: 2), // anger
        new(-1, -1, MouthSorrow, Tilt: -2), // sorrow
        new(EyeSurprise, EyeSurprise, -1, EyebrowLift: 2), // surprise
        new(EyeBlink, EyeBlink, -1), // blink
        new(-1, -1, MouthOpen), // open mouth
        new(EyeSmile, EyeSmile, MouthOpen), // smile, open mouth
        new(-1, -1, MouthOpen, Tilt: 2), // anger, open mouth
        new(-1, -1, MouthOpen, Tilt: -2), // sorrow, open mouth
        new(EyeSurprise, EyeSurprise, MouthOpen, EyebrowLift: 2), // surprise, open mouth
        new(EyeBlink, EyeBlink, MouthOpen), // blink, open mouth
        new(EyeWink, -1, -1), // wink left
        new(-1, EyeWink, -1), // wink right
        new(EyeWink, -1, MouthOpen), // wink left, open mouth
        new(-1, EyeWink, MouthOpen), // wink right, open mouth
        new(EyeWink, -1, MouthLike), // like, wink left
        new(-1, EyeWink, MouthLike), // like, wink right
        new(EyeWink, EyeWink, MouthSorrow), // frustrated
    ];

    private const float UnitX = 0.88961464f;
    private const float UnitY = 1.1600001f * 0.9276675f;

    // Where a part's quad starts relative to its anchor (RFLiDrawQuad): centred, or extending left or right of it.
    private enum Origin
    {
        Center,
        Left,
        Right,
    }

    private readonly record struct FacePart(Vector2 Position, Vector2 Size, float Angle, Origin Origin)
    {
        // RFLiDrawQuad: scaled to size, rotated, squashed to the mask, then moved into place (64 units across the mask).
        public Matrix3x2 Transform =>
            Matrix3x2.CreateScale(Size)
            * Matrix3x2.CreateRotation(Angle * MathF.PI / 180f)
            * Matrix3x2.CreateScale(UnitX, 0.9276675f)
            * Matrix3x2.CreateTranslation(Position);

        private float Left =>
            Origin switch
            {
                Origin.Center => -0.5f,
                Origin.Right => -1f,
                _ => 0f,
            };

        /// <summary>Corners in mask texture coordinates (0..1).</summary>
        public Vector2[] Corners()
        {
            var transform = Transform;
            return
            [
                Vector2.Transform(new Vector2(Left + 1f, -0.5f), transform) / 64f,
                Vector2.Transform(new Vector2(Left + 1f, 0.5f), transform) / 64f,
                Vector2.Transform(new Vector2(Left, 0.5f), transform) / 64f,
                Vector2.Transform(new Vector2(Left, -0.5f), transform) / 64f,
            ];
        }

        /// <summary>
        /// Where a point of the mask (64 units across) lands on the part's texture (<paramref name="toPart"/> is the
        /// inverse of <see cref="Transform"/>), and whether it's on the part at all.
        /// </summary>
        public bool TryGetTexcoord(Vector2 point, Matrix3x2 toPart, out Vector2 texcoord)
        {
            var local = Vector2.Transform(point, toPart);
            var u = local.X - Left;
            // Left-anchored parts (the Mii's left eye, eyebrow and mustache half) are the right ones mirrored.
            texcoord = new Vector2(Origin == Origin.Left ? 1f - u : u, local.Y + 0.5f);
            return u is >= 0f and <= 1f && local.Y is >= -0.5f and <= 0.5f;
        }
    }

    private readonly record struct FaceLayout(
        FacePart EyeRight,
        FacePart EyeLeft,
        FacePart EyebrowRight,
        FacePart EyebrowLeft,
        FacePart Mouth,
        FacePart MustacheRight,
        FacePart MustacheLeft,
        FacePart Mole
    );

    /// <summary>RFLiSetFaceParts, with an angle per eye so one can wink.</summary>
    private static FaceLayout Layout(RflCharInfo info, int eyeRightAngle, int eyeLeftAngle, int eyebrowRotate, int eyebrowY)
    {
        static float Scale(int scale) => 1f + 0.4f * scale;
        static float Angle(int rotate) => 360f / 32f * (rotate % 32);

        var eyeX = UnitX * info.EyeX;
        var eyeY = 18.451525f + UnitY * info.EyeY;
        var eyeSize = new Vector2(342f, 288f) / 64f * Scale(info.EyeScale);
        var eyebrowX = UnitX * info.EyebrowX;
        var eyebrowPositionY = 16.549807f + UnitY * eyebrowY;
        var eyebrowSize = new Vector2(324f, 288f) / 64f * Scale(info.EyebrowScale);
        var eyebrowAngle = Angle(eyebrowRotate + EyebrowRotateOffsets[info.EyebrowType]);
        var mustacheY = 31.763554f + UnitY * info.BeardY;
        var mustacheSize = new Vector2(288f, 576f) / 64f * Scale(info.BeardScale);

        return new FaceLayout(
            new FacePart(new(32f - eyeX, eyeY), eyeSize, Angle(eyeRightAngle), Origin.Right),
            new FacePart(new(32f + eyeX, eyeY), eyeSize, 360f - Angle(eyeLeftAngle), Origin.Left),
            new FacePart(new(32f - eyebrowX, eyebrowPositionY), eyebrowSize, eyebrowAngle, Origin.Right),
            new FacePart(new(32f + eyebrowX, eyebrowPositionY), eyebrowSize, 360f - eyebrowAngle, Origin.Left),
            new FacePart(
                new(32f, 29.25885f + UnitY * info.MouthY),
                new Vector2(396f, 288f) / 64f * Scale(info.MouthScale),
                0f,
                Origin.Center
            ),
            new FacePart(new(32f, mustacheY), mustacheSize, 0f, Origin.Right),
            new FacePart(new(32f, mustacheY), mustacheSize, 0f, Origin.Left),
            new FacePart(
                new(17.766165f + 2f * UnitX * info.MoleX, 17.95986f + UnitY * info.MoleY),
                new Vector2(Scale(info.MoleScale)),
                0f,
                Origin.Center
            )
        );
    }

    /// <summary>The Mii's own layout (no expression).</summary>
    private static FaceLayout Layout(RflCharInfo info)
    {
        var eye = info.EyeRotate + EyeRotateOffsets[info.EyeType];
        return Layout(info, eye, eye, info.EyebrowRotate, info.EyebrowY);
    }

    /// <summary>Where each face part sits on the mask texture: one or two quads of four corners (0..1).</summary>
    public static IReadOnlyDictionary<MiiMaskLayers, Vector2[][]> MaskPartQuads(RflCharInfo info)
    {
        var layout = Layout(info);
        return new Dictionary<MiiMaskLayers, Vector2[][]>
        {
            [MiiMaskLayers.Eyes] = [layout.EyeRight.Corners(), layout.EyeLeft.Corners()],
            [MiiMaskLayers.Eyebrows] = [layout.EyebrowRight.Corners(), layout.EyebrowLeft.Corners()],
            [MiiMaskLayers.Mouth] = [layout.Mouth.Corners()],
            [MiiMaskLayers.Mustache] = info.MustacheType != 0 ? [layout.MustacheRight.Corners(), layout.MustacheLeft.Corners()] : [],
            [MiiMaskLayers.Mole] = info.HasMole ? [layout.Mole.Corners()] : [],
        };
    }

    /// <summary>
    /// Paints the face parts of a Mii with an expression into a square mask texture, front to back like
    /// RFLiCapture: mustache, mouth, eyebrows, eyes, mole. Only <paramref name="layers"/> are painted.
    /// </summary>
    public static OperationResult<MiiTexture> BuildMask(
        RflResource resource,
        RflCharInfo info,
        int expression,
        MiiMaskLayers layers,
        int resolution
    )
    {
        var parts = Expressions[Math.Clamp(expression, 0, Expressions.Length - 1)];

        // A swapped eye keeps the Mii's tilt: the new type's base angle is made up for, as RFLiMakeTexture does.
        var tilted = Math.Clamp(info.EyeRotate + parts.Tilt, 0, 7);
        int EyeType(int swapped) => swapped < 0 ? info.EyeType : swapped;
        int EyeAngle(int type) =>
            Math.Clamp(tilted + EyeRotateOffsets[info.EyeType] - EyeRotateOffsets[type], 0, 7) + EyeRotateOffsets[type];

        var eyeRight = EyeType(parts.EyeRight);
        var eyeLeft = EyeType(parts.EyeLeft);
        var layout = Layout(
            info,
            EyeAngle(eyeRight),
            EyeAngle(eyeLeft),
            Math.Clamp(info.EyebrowRotate + parts.Tilt, 0, 11),
            info.EyebrowY - parts.EyebrowLift
        );

        var canvas = new MaskCanvas(resolution);
        var hairColor = HairColors[info.BeardColor];
        if (layers.HasFlag(MiiMaskLayers.Mustache) && info.MustacheType != 0)
        {
            var texture = RflTexture.Decode(resource.Get(RflArchive.Mustache, info.MustacheType));
            if (texture.IsFailure)
                return texture.Error!;
            canvas.Draw(layout.MustacheRight, texture.Value, t => hairColor with { W = t.W });
            canvas.Draw(layout.MustacheLeft, texture.Value, t => hairColor with { W = t.W });
        }

        if (layers.HasFlag(MiiMaskLayers.Mouth))
        {
            var texture = RflTexture.Decode(resource.Get(RflArchive.Mouth, parts.Mouth < 0 ? info.MouthType : parts.Mouth));
            if (texture.IsFailure)
                return texture.Error!;
            // Red is the lip, green its shade, blue the teeth (RFLiSetTev4Mouth).
            var lip = LipColors[info.MouthColor];
            var shade = LipShadeColors[info.MouthColor];
            canvas.Draw(layout.Mouth, texture.Value, t => Mix(t, lip * t.X + shade * t.Y + Vector4.One * t.Z));
        }

        if (layers.HasFlag(MiiMaskLayers.Eyebrows))
        {
            var texture = RflTexture.Decode(resource.Get(RflArchive.Eyebrow, info.EyebrowType));
            if (texture.IsFailure)
                return texture.Error!;
            var color = HairColors[info.EyebrowColor];
            canvas.Draw(layout.EyebrowRight, texture.Value, t => color with { W = t.W });
            canvas.Draw(layout.EyebrowLeft, texture.Value, t => color with { W = t.W });
        }

        if (layers.HasFlag(MiiMaskLayers.Eyes))
        {
            foreach (var (part, type) in new[] { (layout.EyeRight, eyeRight), (layout.EyeLeft, eyeLeft) })
            {
                var texture = RflTexture.Decode(resource.Get(RflArchive.Eye, type));
                if (texture.IsFailure)
                    return texture.Error!;
                // Red is the outline (orange or cyan for two eye types), blue the iris and green the white (RFLiSetTev4Eye).
                var outline = type switch
                {
                    9 => Rgb(255, 130, 0),
                    20 => Rgb(0, 255, 255),
                    _ => Rgb(0, 0, 0),
                };
                var iris = EyeColors[info.EyeColor];
                canvas.Draw(part, texture.Value, t => Mix(t, outline * t.X + iris * t.Z + Vector4.One * t.Y));
            }
        }

        if (layers.HasFlag(MiiMaskLayers.Mole) && info.HasMole)
        {
            var texture = RflTexture.Decode(resource.Get(RflArchive.Mole, 1));
            if (texture.IsFailure)
                return texture.Error!;
            canvas.Draw(layout.Mole, texture.Value, t => MoleColor with { W = t.W });
        }

        return canvas.ToTexture();
    }

    /// <summary>The colour a part's TEV stages make (clamped like the hardware), with the texture's alpha.</summary>
    private static Vector4 Mix(Vector4 texel, Vector4 color) => Vector4.Clamp(color, Vector4.Zero, Vector4.One) with { W = texel.W };

    /// <summary>
    /// The mask being painted: premultiplied colour, each part drawn under the ones before it.
    /// <para>
    /// The part textures are tiny (an eye is 38x32), so they aren't simply stretched: each channel is read as a smooth
    /// field and cut where it crosses a threshold, with an edge about one mask pixel wide. That traces the shapes as
    /// clean curves at the mask's resolution, like vector art. The settings were tuned to match the Wii U's high
    /// resolution parts as closely as possible.
    /// </para>
    /// </summary>
    private sealed class MaskCanvas(int size)
    {
        private const float AlphaThreshold = 0.58f;
        private const float ColorThreshold = 0.48f;
        private const float EdgeWidth = 0.85f;

        private readonly Vector4[] _pixels = new Vector4[size * size];

        public void Draw(FacePart part, MiiTexture texture, Func<Vector4, Vector4> color)
        {
            if (!Matrix3x2.Invert(part.Transform, out var toPart))
                return;
            var corners = part.Corners();
            var minX = Math.Max(0, (int)MathF.Floor(corners.Min(c => c.X) * size));
            var maxX = Math.Min(size - 1, (int)MathF.Ceiling(corners.Max(c => c.X) * size));
            var minY = Math.Max(0, (int)MathF.Floor(corners.Min(c => c.Y) * size));
            var maxY = Math.Min(size - 1, (int)MathF.Ceiling(corners.Max(c => c.Y) * size));
            if (minX > maxX || minY > maxY)
                return;

            // The smooth field at every pixel of the part, plus a ring around it for the slopes at its edges.
            var width = maxX - minX + 3;
            var height = maxY - minY + 3;
            var field = new Vector4[width * height];
            var onPart = new bool[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var point = new Vector2(minX + x - 0.5f, minY + y - 0.5f) * (64f / size);
                onPart[y * width + x] = part.TryGetTexcoord(point, toPart, out var uv);
                field[y * width + x] = texture.SampleSmooth(uv.X, uv.Y);
            }

            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                if (!onPart[i])
                    continue;
                var slopeX = (field[i + 1] - field[i - 1]) * 0.5f;
                var slopeY = (field[i + width] - field[i - width]) * 0.5f;
                var texel = new Vector4(
                    Edge(field[i].X, slopeX.X, slopeY.X, ColorThreshold),
                    Edge(field[i].Y, slopeX.Y, slopeY.Y, ColorThreshold),
                    Edge(field[i].Z, slopeX.Z, slopeY.Z, ColorThreshold),
                    Edge(field[i].W, slopeX.W, slopeY.W, AlphaThreshold)
                );
                if (texel.W <= 0f)
                    continue;

                var source = color(texel);
                ref var pixel = ref _pixels[(minY + y - 1) * size + minX + x - 1];
                var cover = (1f - pixel.W) * source.W;
                pixel += new Vector4(source.X * cover, source.Y * cover, source.Z * cover, cover);
            }
        }

        /// <summary>How far past the threshold a value is, in pixels (from its slope), as 0..1 coverage.</summary>
        private static float Edge(float value, float slopeX, float slopeY, float threshold)
        {
            var width = MathF.Max(MathF.Sqrt(slopeX * slopeX + slopeY * slopeY) * EdgeWidth, 1e-4f);
            return Math.Clamp((value - threshold) / width + 0.5f, 0f, 1f);
        }

        public MiiTexture ToTexture()
        {
            var rgba = new byte[size * size * 4];
            for (var i = 0; i < _pixels.Length; i++)
            {
                var pixel = _pixels[i];
                if (pixel.W <= 0f)
                    continue;
                rgba[i * 4] = ToByte(pixel.X / pixel.W);
                rgba[i * 4 + 1] = ToByte(pixel.Y / pixel.W);
                rgba[i * 4 + 2] = ToByte(pixel.Z / pixel.W);
                rgba[i * 4 + 3] = ToByte(pixel.W);
            }

            return new MiiTexture(size, size, rgba);
        }
    }

    #endregion
}

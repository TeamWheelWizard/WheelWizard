using System.Numerics;
using Avalonia.Media;
using WheelWizard.MiiRendering.Realtime;
using WheelWizard.Views.Components;
using WheelWizard.Views.Shell;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Editor;

/// <summary>The groups of the head menu (second level of the editor's floating sidebar).</summary>
public enum MiiHeadGroup
{
    Head,
    Hair,
    Eyes,
    Nose,
    Lips,
    Beard,
}

/// <summary>Something on the head you can select and change.</summary>
public enum MiiEditPart
{
    FaceShape,
    FaceFeature,
    Mole,
    Hair,
    Eyes,
    Eyebrows,
    Glasses,
    Nose,
    Mouth,
    Beard,
    Mustache,
}

/// <summary>A whole number setting of a part (size, rotation, position) with its legal range.</summary>
public sealed record MiiStepper(int Min, int Max, Func<Mii, int> Get, Func<Mii, int, bool> Set)
{
    public bool CanStep(Mii mii, int change) => Get(mii) + change is var value && value >= Min && value <= Max;

    public bool Step(Mii mii, int change) => CanStep(mii, change) && Set(mii, Get(mii) + change);

    public bool SetClamped(Mii mii, int value)
    {
        value = Math.Clamp(value, Min, Max);
        return value != Get(mii) && Set(mii, value);
    }
}

/// <summary>
/// The types to pick from. Cycling runs from <see cref="First"/> (optional parts skip their "none" type, which the
/// add/remove button handles) to <see cref="Count"/> - 1.
/// </summary>
public sealed record MiiVariants(int First, int Count, Func<Mii, int> Get, Func<Mii, int, bool> Set)
{
    /// <summary>Icon resource prefix (e.g. "MiiEye" for MiiEye00..), or null to show <see cref="Label"/> instead.</summary>
    public string? IconPrefix { get; init; }

    public Action<MultiIconRadioButton>? StyleIcon { get; init; }

    public Func<int, string>? Label { get; init; }

    /// <summary>The type <paramref name="steps"/> away from the current one, wrapping around.</summary>
    public int Cycle(Mii mii, int steps)
    {
        var span = Count - First;
        var index = Get(mii) - First;
        return First + ((index + steps) % span + span) % span;
    }
}

/// <summary>The colours a part can have (swatch i is enum value i).</summary>
public sealed record MiiColors(IReadOnlyList<Color> Swatches, Func<Mii, int> Get, Func<Mii, int, bool> Set);

/// <summary>An optional part (glasses, mole, beard, mustache) that the floating +/- button adds or removes.</summary>
public sealed record MiiPresence(Func<Mii, bool> IsPresent, Func<Mii, bool, bool> Set);

/// <summary>
/// One direction a part can be dragged in. <see cref="MaskStep"/> (face mask texture units, for parts painted on
/// the face) or <see cref="HeadStep"/> (head model units, for 3D parts) is how far one step of the value moves it.
/// </summary>
public sealed record MiiDragAxis(MiiStepper Stepper)
{
    public Vector2 MaskStep { get; init; }
    public Vector3 HeadStep { get; init; }

    /// <summary>For spacing: dragging the left eye to the left moves the eyes apart just like dragging the right one right.</summary>
    public bool Mirrored { get; init; }
}

public sealed class MiiPartDefinition
{
    public required MiiEditPart Part { get; init; }
    public required MiiHeadGroup Group { get; init; }
    public required HeadPart RenderPart { get; init; }

    /// <summary>What lights up under the mouse (the face shape swaps the whole head, but only the skin lights up).</summary>
    public HeadPart HighlightPart => RenderPart == HeadPart.Head ? HeadPart.Faceline : RenderPart;
    public required string TitleKey { get; init; }

    /// <summary>Sidebar icon: a Mii part icon resource (styled by <see cref="StyleIcon"/>), or an editor geometry ("MiiEditor…").</summary>
    public required string Icon { get; init; }

    public Action<MultiIconRadioButton>? StyleIcon { get; init; }

    public MiiVariants? Variants { get; init; }
    public MiiColors? Colors { get; init; }
    public MiiPresence? Presence { get; init; }
    public MiiStepper? Size { get; init; }
    public MiiStepper? Rotation { get; init; }

    /// <summary>Hair only: mirror the parting.</summary>
    public Func<Mii, bool, bool>? Flip { get; init; }

    public Func<Mii, bool>? IsFlipped { get; init; }

    public MiiDragAxis? DragVertical { get; init; }
    public MiiDragAxis? DragHorizontal { get; init; }

    public bool IsDraggable => DragVertical is not null || DragHorizontal is not null;

    /// <summary>Whether the part shows on the Mii right now (optional parts may be off).</summary>
    public bool IsPresent(Mii mii) => Presence?.IsPresent(mii) ?? true;
}

/// <summary>Every part the editor can change, how to read and write it, and its legal ranges.</summary>
public static class MiiEditorParts
{
    // Face mask units per step of the Mii's values (see NativeMiiRenderer.BuildRawMaskParts); the mask is 64 units wide.
    private const float MaskStepY = 1.0760943f / 64f;
    private const float MaskStepSpacing = 0.88961464f / 64f;
    private const float MaskStepMoleX = 1.7792293f / 64f;

    // 3D parts move 1.5 head units down per step (see NativeMiiRenderer.BuildManagedDrawParams).
    private static readonly Vector3 HeadStepDown = new(0f, -1.5f, 0f);

    public static readonly IReadOnlyList<MiiHeadGroup> Groups =
    [
        MiiHeadGroup.Head,
        MiiHeadGroup.Hair,
        MiiHeadGroup.Eyes,
        MiiHeadGroup.Nose,
        MiiHeadGroup.Lips,
        MiiHeadGroup.Beard,
    ];

    public static readonly IReadOnlyDictionary<MiiEditPart, MiiPartDefinition> All = Build().ToDictionary(p => p.Part);

    public static MiiPartDefinition Get(MiiEditPart part) => All[part];

    /// <summary>The parts of a head group, in sidebar order; the first one is selected when the group opens.</summary>
    public static IReadOnlyList<MiiPartDefinition> PartsOf(MiiHeadGroup group) => All.Values.Where(p => p.Group == group).ToList();

    public static string GroupTitleKey(MiiHeadGroup group) =>
        group switch
        {
            MiiHeadGroup.Head => "attribute.mii_section.head",
            MiiHeadGroup.Hair => "attribute.mii_section.hair",
            MiiHeadGroup.Eyes => "attribute.mii_section.eyes",
            MiiHeadGroup.Nose => "attribute.mii_section.nose",
            MiiHeadGroup.Lips => "attribute.mii_section.lips",
            MiiHeadGroup.Beard => "attribute.mii_section.facial_hair",
            _ => throw new ArgumentOutOfRangeException(nameof(group)),
        };

    /// <summary>The part whose icon stands for the group in the sidebar.</summary>
    public static MiiEditPart GroupIconPart(MiiHeadGroup group) =>
        group switch
        {
            MiiHeadGroup.Head => MiiEditPart.FaceShape,
            MiiHeadGroup.Hair => MiiEditPart.Hair,
            MiiHeadGroup.Eyes => MiiEditPart.Eyes,
            MiiHeadGroup.Nose => MiiEditPart.Nose,
            MiiHeadGroup.Lips => MiiEditPart.Mouth,
            MiiHeadGroup.Beard => MiiEditPart.Beard,
            _ => throw new ArgumentOutOfRangeException(nameof(group)),
        };

    private static IEnumerable<MiiPartDefinition> Build()
    {
        var skin = new SolidColorBrush(ViewUtils.Colors.Neutral50);
        var skinBorder = new SolidColorBrush(ViewUtils.Colors.Neutral300);
        var black = new SolidColorBrush(ViewUtils.Colors.Black);
        var none = new SolidColorBrush(ViewUtils.Colors.Danger400);
        var noneSelected = new SolidColorBrush(ViewUtils.Colors.Danger500);

        void FaceIcon(MultiIconRadioButton b)
        {
            b.Color1 = skin;
            b.Color2 = skinBorder;
            b.Color3 = Brushes.Transparent;
        }

        void HairIcon(MultiIconRadioButton b)
        {
            b.Color1 = new SolidColorBrush(ViewUtils.Colors.Neutral100);
            b.Color2 = skinBorder;
            b.Color3 = black;
            b.Color4 = new SolidColorBrush(ViewUtils.Colors.Brand800);
            b.Color5 = new SolidColorBrush(ViewUtils.Colors.Brand900);
        }

        void EyeIcon(MultiIconRadioButton b)
        {
            b.Color1 = new SolidColorBrush(ViewUtils.Colors.Neutral50);
            b.Color2 = new SolidColorBrush(ViewUtils.Colors.Neutral950);
            b.SelectedColor2 = black;
            b.Color3 = new SolidColorBrush(ViewUtils.Colors.Brand400);
            b.SelectedColor3 = new SolidColorBrush(ViewUtils.Colors.Brand300);
        }

        void BrowIcon(MultiIconRadioButton b)
        {
            b.Color1 = none;
            b.SelectedColor1 = noneSelected;
            b.Color2 = black;
        }

        void SkinPartIcon(MultiIconRadioButton b, IBrush partColor)
        {
            b.Color1 = skin;
            b.Color2 = skinBorder;
            b.Color3 = partColor;
            b.Color4 = none;
            b.SelectedColor4 = noneSelected;
        }

        void MouthIcon(MultiIconRadioButton b)
        {
            b.Color1 = new SolidColorBrush(new Color(255, 165, 57, 29));
            b.Color2 = new SolidColorBrush(new Color(255, 255, 93, 13));
            b.Color3 = black;
            b.Color4 = Brushes.White;
        }

        void NoseIcon(MultiIconRadioButton b) => b.Color1 = black;

        static IReadOnlyList<Color> Swatches<TEnum>(IReadOnlyDictionary<TEnum, Color> mapping)
            where TEnum : struct, Enum =>
            Enumerable.Range(0, mapping.Count).Select(i => mapping[(TEnum)Enum.ToObject(typeof(TEnum), i)]).ToList();

        // Face
        static bool SetFace(Mii mii, MiiFaceShape? shape = null, MiiSkinColor? skinColor = null, MiiFacialFeature? feature = null)
        {
            var c = mii.MiiFacialFeatures;
            var result = MiiFacialFeatures.Create(
                shape ?? c.FaceShape,
                skinColor ?? c.SkinColor,
                feature ?? c.FacialFeature,
                c.MingleOff,
                c.Downloaded
            );
            if (result.IsFailure)
                return false;
            mii.MiiFacialFeatures = result.Value;
            return true;
        }

        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.FaceShape,
            Group = MiiHeadGroup.Head,
            RenderPart = HeadPart.Head,
            TitleKey = "attribute.mii.head_shape",
            Icon = "MiiFace00",
            StyleIcon = FaceIcon,
            Variants = new MiiVariants(0, 8, m => (int)m.MiiFacialFeatures.FaceShape, (m, v) => SetFace(m, shape: (MiiFaceShape)v))
            {
                IconPrefix = "MiiFace",
                StyleIcon = FaceIcon,
            },
            Colors = new MiiColors(
                Swatches(MiiColorMappings.SkinColor),
                m => (int)m.MiiFacialFeatures.SkinColor,
                (m, v) => SetFace(m, skinColor: (MiiSkinColor)v)
            ),
        };

        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.FaceFeature,
            Group = MiiHeadGroup.Head,
            RenderPart = HeadPart.Faceline,
            TitleKey = "attribute.mii.facial_feature",
            Icon = "MiiEditorFeatureIcon",
            Variants = new MiiVariants(
                0,
                Enum.GetValues<MiiFacialFeature>().Length,
                m => (int)m.MiiFacialFeatures.FacialFeature,
                (m, v) => SetFace(m, feature: (MiiFacialFeature)v)
            )
            {
                Label = v => t($"attribute.mii_feature.{FeatureKey((MiiFacialFeature)v)}"),
            },
        };

        static bool SetMole(Mii mii, bool? exists = null, int? size = null, int? vertical = null, int? horizontal = null)
        {
            var c = mii.MiiMole;
            var result = MiiMole.Create(exists ?? c.Exists, size ?? c.Size, vertical ?? c.Vertical, horizontal ?? c.Horizontal);
            if (result.IsFailure)
                return false;
            mii.MiiMole = result.Value;
            return true;
        }

        var moleVertical = new MiiStepper(0, 30, m => m.MiiMole.Vertical, (m, v) => SetMole(m, vertical: v));
        var moleHorizontal = new MiiStepper(0, 16, m => m.MiiMole.Horizontal, (m, v) => SetMole(m, horizontal: v));
        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Mole,
            Group = MiiHeadGroup.Head,
            RenderPart = HeadPart.Mole,
            TitleKey = "attribute.mii_section.mole",
            Icon = "MiiEditorMoleIcon",
            Presence = new MiiPresence(
                m => m.MiiMole.Exists,
                (m, present) =>
                    // A mole that was never placed sits at 0,0 (the edge of the face); start it on the cheek.
                    present && m.MiiMole is { Size: 0, Vertical: 0, Horizontal: 0 }
                        ? SetMole(m, true, 4, 20, 2)
                        : SetMole(m, exists: present)
            ),
            Size = new MiiStepper(0, 8, m => m.MiiMole.Size, (m, v) => SetMole(m, size: v)),
            DragVertical = new MiiDragAxis(moleVertical) { MaskStep = new Vector2(0, MaskStepY) },
            DragHorizontal = new MiiDragAxis(moleHorizontal) { MaskStep = new Vector2(MaskStepMoleX, 0) },
        };

        // Hair
        static bool SetHair(Mii mii, int? type = null, MiiHairColor? color = null, bool? flipped = null)
        {
            var c = mii.MiiHair;
            var result = MiiHair.Create(type ?? c.HairType, color ?? c.MiiHairColor, flipped ?? c.HairFlipped);
            if (result.IsFailure)
                return false;
            mii.MiiHair = result.Value;
            return true;
        }

        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Hair,
            Group = MiiHeadGroup.Hair,
            RenderPart = HeadPart.Hair,
            TitleKey = "attribute.mii_section.hair",
            Icon = "MiiHair33",
            StyleIcon = HairIcon,
            Variants = new MiiVariants(0, 72, m => m.MiiHair.HairType, (m, v) => SetHair(m, type: v))
            {
                IconPrefix = "MiiHair",
                StyleIcon = HairIcon,
            },
            Colors = new MiiColors(
                Swatches(MiiColorMappings.HairColor),
                m => (int)m.MiiHair.MiiHairColor,
                (m, v) => SetHair(m, color: (MiiHairColor)v)
            ),
            Flip = (m, flipped) => SetHair(m, flipped: flipped),
            IsFlipped = m => m.MiiHair.HairFlipped,
        };

        // Eyes
        static bool SetEyes(
            Mii mii,
            int? type = null,
            int? rotation = null,
            int? vertical = null,
            MiiEyeColor? color = null,
            int? size = null,
            int? spacing = null
        )
        {
            var c = mii.MiiEyes;
            var result = MiiEye.Create(
                type ?? c.Type,
                rotation ?? c.Rotation,
                vertical ?? c.Vertical,
                color ?? c.Color,
                size ?? c.Size,
                spacing ?? c.Spacing
            );
            if (result.IsFailure)
                return false;
            mii.MiiEyes = result.Value;
            return true;
        }

        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Eyes,
            Group = MiiHeadGroup.Eyes,
            RenderPart = HeadPart.Eyes,
            TitleKey = "attribute.mii_section.eyes",
            Icon = "MiiEye02",
            StyleIcon = EyeIcon,
            Variants = new MiiVariants(0, 48, m => m.MiiEyes.Type, (m, v) => SetEyes(m, type: v))
            {
                IconPrefix = "MiiEye",
                StyleIcon = EyeIcon,
            },
            Colors = new MiiColors(
                Swatches(MiiColorMappings.EyeColor),
                m => (int)m.MiiEyes.Color,
                (m, v) => SetEyes(m, color: (MiiEyeColor)v)
            ),
            Size = new MiiStepper(0, 7, m => m.MiiEyes.Size, (m, v) => SetEyes(m, size: v)),
            Rotation = new MiiStepper(0, 7, m => m.MiiEyes.Rotation, (m, v) => SetEyes(m, rotation: v)),
            DragVertical = new MiiDragAxis(new MiiStepper(0, 18, m => m.MiiEyes.Vertical, (m, v) => SetEyes(m, vertical: v)))
            {
                MaskStep = new Vector2(0, MaskStepY),
            },
            DragHorizontal = new MiiDragAxis(new MiiStepper(0, 12, m => m.MiiEyes.Spacing, (m, v) => SetEyes(m, spacing: v)))
            {
                MaskStep = new Vector2(MaskStepSpacing, 0),
                Mirrored = true,
            },
        };

        static bool SetBrows(
            Mii mii,
            int? type = null,
            int? rotation = null,
            MiiHairColor? color = null,
            int? size = null,
            int? vertical = null,
            int? spacing = null
        )
        {
            var c = mii.MiiEyebrows;
            var result = MiiEyebrow.Create(
                type ?? c.Type,
                rotation ?? c.Rotation,
                color ?? c.Color,
                size ?? c.Size,
                vertical ?? c.Vertical,
                spacing ?? c.Spacing
            );
            if (result.IsFailure)
                return false;
            mii.MiiEyebrows = result.Value;
            return true;
        }

        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Eyebrows,
            Group = MiiHeadGroup.Eyes,
            RenderPart = HeadPart.Eyebrows,
            TitleKey = "attribute.mii_section.eyebrows",
            Icon = "MiiEyebrow06",
            StyleIcon = BrowIcon,
            Variants = new MiiVariants(0, 24, m => m.MiiEyebrows.Type, (m, v) => SetBrows(m, type: v))
            {
                IconPrefix = "MiiEyebrow",
                StyleIcon = BrowIcon,
            },
            Colors = new MiiColors(
                Swatches(MiiColorMappings.HairColor),
                m => (int)m.MiiEyebrows.Color,
                (m, v) => SetBrows(m, color: (MiiHairColor)v)
            ),
            Size = new MiiStepper(0, 8, m => m.MiiEyebrows.Size, (m, v) => SetBrows(m, size: v)),
            Rotation = new MiiStepper(0, 11, m => m.MiiEyebrows.Rotation, (m, v) => SetBrows(m, rotation: v)),
            DragVertical = new MiiDragAxis(new MiiStepper(3, 18, m => m.MiiEyebrows.Vertical, (m, v) => SetBrows(m, vertical: v)))
            {
                MaskStep = new Vector2(0, MaskStepY),
            },
            DragHorizontal = new MiiDragAxis(new MiiStepper(0, 12, m => m.MiiEyebrows.Spacing, (m, v) => SetBrows(m, spacing: v)))
            {
                MaskStep = new Vector2(MaskStepSpacing, 0),
                Mirrored = true,
            },
        };

        static bool SetGlasses(Mii mii, MiiGlassesType? type = null, MiiGlassesColor? color = null, int? size = null, int? vertical = null)
        {
            var c = mii.MiiGlasses;
            var result = MiiGlasses.Create(type ?? c.Type, color ?? c.Color, size ?? c.Size, vertical ?? c.Vertical);
            if (result.IsFailure)
                return false;
            mii.MiiGlasses = result.Value;
            return true;
        }

        var glassesColor = new SolidColorBrush(ViewUtils.Colors.Neutral600);
        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Glasses,
            Group = MiiHeadGroup.Eyes,
            RenderPart = HeadPart.Glasses,
            TitleKey = "attribute.mii_section.glasses",
            Icon = "MiiGlasses01",
            StyleIcon = b => SkinPartIcon(b, glassesColor),
            Variants = new MiiVariants(1, 9, m => (int)m.MiiGlasses.Type, (m, v) => SetGlasses(m, type: (MiiGlassesType)v))
            {
                IconPrefix = "MiiGlasses",
                StyleIcon = b => SkinPartIcon(b, glassesColor),
            },
            Colors = new MiiColors(
                Swatches(MiiColorMappings.GlassesColor),
                m => (int)m.MiiGlasses.Color,
                (m, v) => SetGlasses(m, color: (MiiGlassesColor)v)
            ),
            Presence = new MiiPresence(
                m => m.MiiGlasses.Type != MiiGlassesType.None,
                (m, present) => SetGlasses(m, type: present ? MiiGlassesType.Square : MiiGlassesType.None)
            ),
            Size = new MiiStepper(0, 7, m => m.MiiGlasses.Size, (m, v) => SetGlasses(m, size: v)),
            DragVertical = new MiiDragAxis(new MiiStepper(0, 20, m => m.MiiGlasses.Vertical, (m, v) => SetGlasses(m, vertical: v)))
            {
                HeadStep = HeadStepDown,
            },
        };

        // Nose
        static bool SetNose(Mii mii, MiiNoseType? type = null, int? size = null, int? vertical = null)
        {
            var c = mii.MiiNose;
            var result = MiiNose.Create(type ?? c.Type, size ?? c.Size, vertical ?? c.Vertical);
            if (result.IsFailure)
                return false;
            mii.MiiNose = result.Value;
            return true;
        }

        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Nose,
            Group = MiiHeadGroup.Nose,
            RenderPart = HeadPart.Nose,
            TitleKey = "attribute.mii_section.nose",
            Icon = "MiiNose01",
            StyleIcon = NoseIcon,
            Variants = new MiiVariants(0, 12, m => (int)m.MiiNose.Type, (m, v) => SetNose(m, type: (MiiNoseType)v))
            {
                IconPrefix = "MiiNose",
                StyleIcon = NoseIcon,
            },
            Size = new MiiStepper(0, 8, m => m.MiiNose.Size, (m, v) => SetNose(m, size: v)),
            DragVertical = new MiiDragAxis(new MiiStepper(0, 18, m => m.MiiNose.Vertical, (m, v) => SetNose(m, vertical: v)))
            {
                HeadStep = HeadStepDown,
            },
        };

        // Mouth
        static bool SetLips(Mii mii, int? type = null, MiiLipColor? color = null, int? size = null, int? vertical = null)
        {
            var c = mii.MiiLips;
            var result = MiiLip.Create(type ?? c.Type, color ?? c.Color, size ?? c.Size, vertical ?? c.Vertical);
            if (result.IsFailure)
                return false;
            mii.MiiLips = result.Value;
            return true;
        }

        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Mouth,
            Group = MiiHeadGroup.Lips,
            RenderPart = HeadPart.Mouth,
            TitleKey = "attribute.mii_section.lips",
            Icon = "MiiMouth23",
            StyleIcon = MouthIcon,
            Variants = new MiiVariants(0, 24, m => m.MiiLips.Type, (m, v) => SetLips(m, type: v))
            {
                IconPrefix = "MiiMouth",
                StyleIcon = MouthIcon,
            },
            Colors = new MiiColors(
                Swatches(MiiColorMappings.LipBottomColor),
                m => (int)m.MiiLips.Color,
                (m, v) => SetLips(m, color: (MiiLipColor)v)
            ),
            Size = new MiiStepper(0, 8, m => m.MiiLips.Size, (m, v) => SetLips(m, size: v)),
            DragVertical = new MiiDragAxis(new MiiStepper(0, 18, m => m.MiiLips.Vertical, (m, v) => SetLips(m, vertical: v)))
            {
                MaskStep = new Vector2(0, MaskStepY),
            },
        };

        // Facial hair: beard and mustache share one colour.
        static bool SetFacialHair(
            Mii mii,
            MiiMustacheType? mustache = null,
            MiiBeardType? beard = null,
            MiiHairColor? color = null,
            int? size = null,
            int? vertical = null
        )
        {
            var c = mii.MiiFacialHair;
            var result = MiiFacialHair.Create(
                mustache ?? c.MiiMustacheType,
                beard ?? c.MiiBeardType,
                color ?? c.Color,
                size ?? c.Size,
                vertical ?? c.Vertical
            );
            if (result.IsFailure)
                return false;
            mii.MiiFacialHair = result.Value;
            return true;
        }

        var facialHairColors = new MiiColors(
            Swatches(MiiColorMappings.HairColor),
            m => (int)m.MiiFacialHair.Color,
            (m, v) => SetFacialHair(m, color: (MiiHairColor)v)
        );
        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Beard,
            Group = MiiHeadGroup.Beard,
            RenderPart = HeadPart.Beard,
            TitleKey = "attribute.mii.beard_type",
            Icon = "MiiGoatee01",
            StyleIcon = b => SkinPartIcon(b, black),
            Variants = new MiiVariants(1, 4, m => (int)m.MiiFacialHair.MiiBeardType, (m, v) => SetFacialHair(m, beard: (MiiBeardType)v))
            {
                IconPrefix = "MiiGoatee",
                StyleIcon = b => SkinPartIcon(b, black),
            },
            Colors = facialHairColors,
            Presence = new MiiPresence(
                m => m.MiiFacialHair.MiiBeardType != MiiBeardType.None,
                (m, present) => SetFacialHair(m, beard: present ? MiiBeardType.Thin : MiiBeardType.None)
            ),
        };

        yield return new MiiPartDefinition
        {
            Part = MiiEditPart.Mustache,
            Group = MiiHeadGroup.Beard,
            RenderPart = HeadPart.Mustache,
            TitleKey = "attribute.mii.mustache_type",
            Icon = "MiiMustache01",
            StyleIcon = b => SkinPartIcon(b, black),
            Variants = new MiiVariants(
                1,
                4,
                m => (int)m.MiiFacialHair.MiiMustacheType,
                (m, v) => SetFacialHair(m, mustache: (MiiMustacheType)v)
            )
            {
                IconPrefix = "MiiMustache",
                StyleIcon = b => SkinPartIcon(b, black),
            },
            Colors = facialHairColors,
            Presence = new MiiPresence(
                m => m.MiiFacialHair.MiiMustacheType != MiiMustacheType.None,
                (m, present) => SetFacialHair(m, mustache: present ? MiiMustacheType.Normal : MiiMustacheType.None)
            ),
            Size = new MiiStepper(0, 8, m => m.MiiFacialHair.Size, (m, v) => SetFacialHair(m, size: v)),
            DragVertical = new MiiDragAxis(new MiiStepper(0, 16, m => m.MiiFacialHair.Vertical, (m, v) => SetFacialHair(m, vertical: v)))
            {
                MaskStep = new Vector2(0, MaskStepY),
            },
        };
    }

    private static string FeatureKey(MiiFacialFeature feature) =>
        feature switch
        {
            MiiFacialFeature.None => "none",
            MiiFacialFeature.Cheeks => "cheeks",
            MiiFacialFeature.CheekAndEyes => "cheeks_and_eyes",
            MiiFacialFeature.Freckles => "freckles",
            MiiFacialFeature.BaggyEyes => "baggy_eyes",
            MiiFacialFeature.Chad => "chiseled",
            MiiFacialFeature.Tired => "tired",
            MiiFacialFeature.Chin => "chin",
            MiiFacialFeature.EyeShadow => "eye_shadow",
            MiiFacialFeature.Beard => "stubble",
            MiiFacialFeature.MouthCorners => "mouth_corners",
            MiiFacialFeature.Wrinkles => "wrinkles",
            _ => "none",
        };

    /// <summary>Favorite colours, for the body's colour button.</summary>
    public static readonly MiiColors FavoriteColors = new(
        Enumerable.Range(0, MiiColorMappings.FavoriteColor.Count).Select(i => MiiColorMappings.FavoriteColor[(MiiFavoriteColor)i]).ToList(),
        m => (int)m.MiiFavoriteColor,
        (m, v) =>
        {
            m.MiiFavoriteColor = (MiiFavoriteColor)v;
            return true;
        }
    );
}

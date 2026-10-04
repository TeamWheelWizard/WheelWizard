namespace MiiAnim.Core.Rig;

/// <summary>
/// The fixed Mii skeleton. Values are stored in .miianim files, so never reorder or renumber them.
/// Order is parent-before-child.
/// </summary>
public enum MiiBone : byte
{
    Root = 0,
    Hip = 1,
    Chest = 2,
    Head = 3,
    ArmL1 = 4,
    ArmL2 = 5,
    HandL = 6,
    ArmR1 = 7,
    ArmR2 = 8,
    HandR = 9,
    LegL1 = 10,
    LegL2 = 11,
    FootL = 12,
    LegR1 = 13,
    LegR2 = 14,
    FootR = 15,
}

/// <summary>Two-bone limbs that support IK. Values are stored in .miianim files.</summary>
public enum MiiLimb : byte
{
    ArmL = 0,
    ArmR = 1,
    LegL = 2,
    LegR = 3,
}

public static class MiiSkeletonInfo
{
    public const int BoneCount = 16;
    public const int LimbCount = 4;

    public static readonly MiiBone[] AllBones = Enum.GetValues<MiiBone>();
    public static readonly MiiLimb[] AllLimbs = Enum.GetValues<MiiLimb>();

    private static readonly int[] Parents = [-1, 0, 0, 2, 2, 4, 5, 2, 7, 8, 1, 10, 11, 1, 13, 14];

    private static readonly string[] GlbNames =
    [
        "skl_root",
        "hip",
        "chest",
        "head",
        "arm_l1",
        "arm_l2",
        "wrist_l",
        "arm_r1",
        "arm_r2",
        "wrist_r",
        "foot_l1",
        "foot_l2",
        "ankle_l",
        "foot_r1",
        "foot_r2",
        "ankle_r",
    ];

    private static readonly string[] FriendlyNames =
    [
        "Body",
        "Hips",
        "Chest",
        "Head",
        "Left Upper Arm",
        "Left Forearm",
        "Left Hand",
        "Right Upper Arm",
        "Right Forearm",
        "Right Hand",
        "Left Thigh",
        "Left Shin",
        "Left Foot",
        "Right Thigh",
        "Right Shin",
        "Right Foot",
    ];

    public static int Parent(MiiBone bone) => Parents[(int)bone];

    public static string GlbName(MiiBone bone) => GlbNames[(int)bone];

    public static string FriendlyName(MiiBone bone) => FriendlyNames[(int)bone];

    public static string FriendlyName(MiiLimb limb) =>
        limb switch
        {
            MiiLimb.ArmL => "Left Arm",
            MiiLimb.ArmR => "Right Arm",
            MiiLimb.LegL => "Left Leg",
            MiiLimb.LegR => "Right Leg",
            _ => limb.ToString(),
        };

    public static bool IsLeg(MiiLimb limb) => limb is MiiLimb.LegL or MiiLimb.LegR;

    /// <summary>Upper, middle and end bone of a limb chain.</summary>
    public static (MiiBone Upper, MiiBone Middle, MiiBone End) Chain(MiiLimb limb) =>
        limb switch
        {
            MiiLimb.ArmL => (MiiBone.ArmL1, MiiBone.ArmL2, MiiBone.HandL),
            MiiLimb.ArmR => (MiiBone.ArmR1, MiiBone.ArmR2, MiiBone.HandR),
            MiiLimb.LegL => (MiiBone.LegL1, MiiBone.LegL2, MiiBone.FootL),
            _ => (MiiBone.LegR1, MiiBone.LegR2, MiiBone.FootR),
        };

    public static MiiLimb? LimbOf(MiiBone bone) =>
        bone switch
        {
            MiiBone.ArmL1 or MiiBone.ArmL2 or MiiBone.HandL => MiiLimb.ArmL,
            MiiBone.ArmR1 or MiiBone.ArmR2 or MiiBone.HandR => MiiLimb.ArmR,
            MiiBone.LegL1 or MiiBone.LegL2 or MiiBone.FootL => MiiLimb.LegL,
            MiiBone.LegR1 or MiiBone.LegR2 or MiiBone.FootR => MiiLimb.LegR,
            _ => null,
        };

    /// <summary>Left/right counterpart, or the bone itself for center bones.</summary>
    public static MiiBone Mirror(MiiBone bone) =>
        bone switch
        {
            MiiBone.ArmL1 => MiiBone.ArmR1,
            MiiBone.ArmL2 => MiiBone.ArmR2,
            MiiBone.HandL => MiiBone.HandR,
            MiiBone.ArmR1 => MiiBone.ArmL1,
            MiiBone.ArmR2 => MiiBone.ArmL2,
            MiiBone.HandR => MiiBone.HandL,
            MiiBone.LegL1 => MiiBone.LegR1,
            MiiBone.LegL2 => MiiBone.LegR2,
            MiiBone.FootL => MiiBone.FootR,
            MiiBone.LegR1 => MiiBone.LegL1,
            MiiBone.LegR2 => MiiBone.LegL2,
            MiiBone.FootR => MiiBone.FootL,
            _ => bone,
        };

    public static MiiLimb Mirror(MiiLimb limb) =>
        limb switch
        {
            MiiLimb.ArmL => MiiLimb.ArmR,
            MiiLimb.ArmR => MiiLimb.ArmL,
            MiiLimb.LegL => MiiLimb.LegR,
            _ => MiiLimb.LegL,
        };

    /// <summary>Only the body root and hips may be translated; everything else is rotation/scale only.</summary>
    public static bool CanTranslate(MiiBone bone) => bone is MiiBone.Root or MiiBone.Hip;

    /// <summary>Legs default to IK so feet stay planted; arms default to FK.</summary>
    public static float DefaultIkBlend(MiiLimb limb) => IsLeg(limb) ? 1f : 0f;
}

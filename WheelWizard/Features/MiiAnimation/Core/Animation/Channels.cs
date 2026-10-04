using MiiAnim.Core.Rig;

namespace MiiAnim.Core.Animation;

/// <summary>Animatable channels. Values are stored in .miianim files; never renumber.</summary>
public enum Channel : byte
{
    // Bone channels. Position is an offset from the rest pose (canonical units), rotation is degrees on top of the rest pose.
    PosX = 0,
    PosY = 1,
    PosZ = 2,
    RotX = 3,
    RotY = 4,
    RotZ = 5,
    ScaleX = 6,
    ScaleY = 7,
    ScaleZ = 8,

    /// <summary>Volume-preserving squash (&lt;1) / stretch (&gt;1) along the bone.</summary>
    Stretch = 9,

    // Limb channels.
    /// <summary>0 = FK (rotate bones by hand), 1 = IK (drag the hand/foot).</summary>
    IkBlend = 16,

    /// <summary>IK target, as an offset from the hand/foot rest position (canonical, character space).</summary>
    TargetX = 17,
    TargetY = 18,
    TargetZ = 19,

    /// <summary>Hand/foot rotation in IK mode, degrees.</summary>
    TargetRotX = 20,
    TargetRotY = 21,
    TargetRotZ = 22,

    /// <summary>Elbow/knee direction: degrees around the shoulder→hand / hip→foot line.</summary>
    Swivel = 23,

    /// <summary>When 1 the limb stretches to reach targets that are too far away.</summary>
    IkStretch = 24,

    // Global channels.
    /// <summary>FFL expression id, stepped.</summary>
    Expression = 32,
}

public enum TrackTargetKind : byte
{
    Bone = 0,
    Limb = 1,
    Global = 2,
}

/// <summary>What a track animates: a bone, a limb, or the whole character.</summary>
public readonly record struct TrackTarget(TrackTargetKind Kind, byte Index)
{
    public static readonly TrackTarget Global = new(TrackTargetKind.Global, 0);

    public static TrackTarget Bone(MiiBone bone) => new(TrackTargetKind.Bone, (byte)bone);

    public static TrackTarget Limb(MiiLimb limb) => new(TrackTargetKind.Limb, (byte)limb);

    public MiiBone AsBone => (MiiBone)Index;

    public MiiLimb AsLimb => (MiiLimb)Index;

    /// <summary>Packed byte used in files: bones 0x00-0x3F, limbs 0x40-0x7F, global 0x80.</summary>
    public byte Pack() =>
        Kind switch
        {
            TrackTargetKind.Bone => Index,
            TrackTargetKind.Limb => (byte)(0x40 | Index),
            _ => 0x80,
        };

    public static TrackTarget Unpack(byte packed) =>
        packed >= 0x80 ? Global
        : packed >= 0x40 ? new(TrackTargetKind.Limb, (byte)(packed & 0x3F))
        : new(TrackTargetKind.Bone, packed);

    public string FriendlyName =>
        Kind switch
        {
            TrackTargetKind.Bone => MiiSkeletonInfo.FriendlyName(AsBone),
            TrackTargetKind.Limb => MiiSkeletonInfo.FriendlyName(AsLimb),
            _ => "Face",
        };
}

public readonly record struct TrackId(TrackTarget Target, Channel Channel)
{
    public static TrackId Bone(MiiBone bone, Channel channel) => new(TrackTarget.Bone(bone), channel);

    public static TrackId Limb(MiiLimb limb, Channel channel) => new(TrackTarget.Limb(limb), channel);

    public static readonly TrackId Expression = new(TrackTarget.Global, Channel.Expression);
}

public static class ChannelInfo
{
    public static readonly Channel[] BoneChannels =
    [
        Channel.PosX,
        Channel.PosY,
        Channel.PosZ,
        Channel.RotX,
        Channel.RotY,
        Channel.RotZ,
        Channel.ScaleX,
        Channel.ScaleY,
        Channel.ScaleZ,
        Channel.Stretch,
    ];

    public static readonly Channel[] LimbChannels =
    [
        Channel.IkBlend,
        Channel.TargetX,
        Channel.TargetY,
        Channel.TargetZ,
        Channel.TargetRotX,
        Channel.TargetRotY,
        Channel.TargetRotZ,
        Channel.Swivel,
        Channel.IkStretch,
    ];

    /// <summary>Discrete channels are always stepped (no easing).</summary>
    public static bool IsDiscrete(Channel channel) => channel is Channel.Expression or Channel.IkStretch;

    public static float DefaultValue(TrackId id) =>
        id.Channel switch
        {
            Channel.ScaleX or Channel.ScaleY or Channel.ScaleZ or Channel.Stretch => 1f,
            Channel.IkBlend => MiiSkeletonInfo.DefaultIkBlend(id.Target.AsLimb),
            _ => 0f,
        };

    public static string FriendlyName(Channel channel) =>
        channel switch
        {
            Channel.PosX => "Move X",
            Channel.PosY => "Move Y",
            Channel.PosZ => "Move Z",
            Channel.RotX => "Rotate X",
            Channel.RotY => "Rotate Y",
            Channel.RotZ => "Rotate Z",
            Channel.ScaleX => "Scale X",
            Channel.ScaleY => "Scale Y",
            Channel.ScaleZ => "Scale Z",
            Channel.Stretch => "Squash / Stretch",
            Channel.IkBlend => "IK ↔ FK",
            Channel.TargetX => "Target X",
            Channel.TargetY => "Target Y",
            Channel.TargetZ => "Target Z",
            Channel.TargetRotX => "Target Rotate X",
            Channel.TargetRotY => "Target Rotate Y",
            Channel.TargetRotZ => "Target Rotate Z",
            Channel.Swivel => "Elbow/Knee Direction",
            Channel.IkStretch => "Stretchy Limb",
            Channel.Expression => "Expression",
            _ => channel.ToString(),
        };

    /// <summary>
    /// Sign to apply when mirroring left↔right. The rig's left/right bones are mirror images across the X=0 plane,
    /// so X translations and Y/Z rotations flip.
    /// </summary>
    public static float MirrorSign(Channel channel) =>
        channel switch
        {
            Channel.PosX or Channel.RotY or Channel.RotZ => -1f,
            Channel.TargetX or Channel.TargetRotY or Channel.TargetRotZ or Channel.Swivel => -1f,
            _ => 1f,
        };
}

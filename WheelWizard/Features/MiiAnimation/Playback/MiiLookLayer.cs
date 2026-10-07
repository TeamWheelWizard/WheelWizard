using MiiAnim.Core.Animation;
using MiiAnim.Core.Rig;

namespace WheelWizard.MiiAnimations.Playback;

/// <summary>
/// Additive head turn shared by the head (most) and chest (a little), eased towards <see cref="TargetYaw"/> /
/// <see cref="TargetPitch"/> so it feels alive rather than locked on.
/// </summary>
public sealed class MiiLookLayer
{
    private const float HeadShare = 0.75f;
    private const float ChestShare = 0.25f;

    /// <summary>Degrees; positive turns towards the Mii's left (+X, screen right when it faces the camera).</summary>
    public float TargetYaw { get; set; }

    /// <summary>Degrees; positive looks up.</summary>
    public float TargetPitch { get; set; }

    /// <summary>0 = no look at all, 1 = full turn. Eased more slowly than the angles.</summary>
    public float TargetWeight { get; set; } = 1f;

    /// <summary>How quickly the head follows, per second (higher = snappier).</summary>
    public float Responsiveness { get; set; } = 6f;

    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public float Weight { get; private set; } = 1f;

    public bool IsMoving =>
        MathF.Abs(TargetYaw - Yaw) > 0.05f || MathF.Abs(TargetPitch - Pitch) > 0.05f || MathF.Abs(TargetWeight - Weight) > 0.002f;

    public void Update(double deltaSeconds)
    {
        var follow = 1f - MathF.Exp(-Responsiveness * (float)deltaSeconds);
        Yaw += (TargetYaw - Yaw) * follow;
        Pitch += (TargetPitch - Pitch) * follow;
        Weight += (TargetWeight - Weight) * (1f - MathF.Exp(-4f * (float)deltaSeconds));
    }

    public float Offset(TrackId id)
    {
        if (id.Target.Kind != TrackTargetKind.Bone || Weight <= 0f)
            return 0f;
        var share = id.Target.AsBone switch
        {
            MiiBone.Head => HeadShare,
            MiiBone.Chest => ChestShare,
            _ => 0f,
        };
        return id.Channel switch
        {
            _ when share == 0f => 0f,
            Channel.RotY => Yaw * share * Weight,
            // RotX+ tips the head forward (down).
            Channel.RotX => -Pitch * share * Weight,
            _ => 0f,
        };
    }
}

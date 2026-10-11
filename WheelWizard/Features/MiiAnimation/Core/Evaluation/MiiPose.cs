using System.Numerics;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Rig;

namespace MiiAnim.Core.Evaluation;

/// <summary>
/// A fully evaluated pose in canonical character space (Y up, character faces +Z, feet on Y = 0).
/// </summary>
public sealed class MiiPose
{
    private const int N = MiiSkeletonInfo.BoneCount;

    /// <summary>Local rotation of each bone after IK (parent-relative).</summary>
    public Quaternion[] LocalRotation { get; } = new Quaternion[N];

    /// <summary>Local translation of each bone (parent-relative, before parent scale).</summary>
    public Vector3[] LocalTranslation { get; } = new Vector3[N];

    /// <summary>Each bone's own scale (scale channels × squash/stretch × IK stretch). Not inherited by children.</summary>
    public Vector3[] LocalScale { get; } = new Vector3[N];

    public Quaternion[] WorldRotation { get; } = new Quaternion[N];
    public Vector3[] WorldPosition { get; } = new Vector3[N];

    /// <summary>Bone → character space (includes the global body squash).</summary>
    public Matrix4x4[] BoneMatrix { get; } = new Matrix4x4[N];

    /// <summary>Bind-space vertex → character space. Use for skinning.</summary>
    public Matrix4x4[] SkinMatrix { get; } = new Matrix4x4[N];

    /// <summary>Global squash applied about the ground (from the Body bone's scale).</summary>
    public Matrix4x4 GlobalMatrix { get; set; } = Matrix4x4.Identity;

    /// <summary>
    /// Transform for the FFL head mesh in canonical character space: head-local → character space.
    /// Contains head rotation and squash; translation is in canonical units.
    /// </summary>
    public Matrix4x4 HeadMatrix { get; set; } = Matrix4x4.Identity;

    public MiiExpression Expression { get; set; }

    /// <summary>False while the Visible channel hides this Mii.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The Root bone's Move X/Y/Z values: how far the Mii travelled from its start spot (canonical units).</summary>
    public Vector3 RootTravel { get; set; }

    public LimbState[] Limbs { get; } = new LimbState[MiiSkeletonInfo.LimbCount];

    public Vector3 WorldPositionOf(MiiBone bone) => Vector3.Transform(WorldPosition[(int)bone], GlobalMatrix);

    /// <summary>
    /// Head matrix for a renderer whose body is drawn in render units scaled by the Mii's body scale
    /// (WheelWizard: body vertex × 0.7 × bodyScale). The head mesh itself is not body-scaled.
    /// </summary>
    public Matrix4x4 HeadMatrixForRender(Vector3 bodyScale)
    {
        var m = HeadMatrix;
        var t = new Vector3(m.M41, m.M42, m.M43) * MiiBodyModel.CanonicalToRenderUnits * bodyScale;
        m.M41 = t.X;
        m.M42 = t.Y;
        m.M43 = t.Z;
        return m;
    }

    public MiiPose Clone()
    {
        var clone = new MiiPose
        {
            GlobalMatrix = GlobalMatrix,
            HeadMatrix = HeadMatrix,
            Expression = Expression,
            Visible = Visible,
            RootTravel = RootTravel,
        };
        LocalRotation.CopyTo(clone.LocalRotation, 0);
        LocalTranslation.CopyTo(clone.LocalTranslation, 0);
        LocalScale.CopyTo(clone.LocalScale, 0);
        WorldRotation.CopyTo(clone.WorldRotation, 0);
        WorldPosition.CopyTo(clone.WorldPosition, 0);
        BoneMatrix.CopyTo(clone.BoneMatrix, 0);
        SkinMatrix.CopyTo(clone.SkinMatrix, 0);
        Limbs.CopyTo(clone.Limbs, 0);
        return clone;
    }
}

/// <summary>Evaluated IK info for one limb (character space, before global squash), used by editors to draw handles.</summary>
public readonly record struct LimbState(
    float IkBlend,
    Vector3 Target,
    Quaternion TargetRotation,
    Vector3 PoleDirection,
    Vector3 FkEndPosition,
    Quaternion FkEndRotation,
    bool Stretched
);

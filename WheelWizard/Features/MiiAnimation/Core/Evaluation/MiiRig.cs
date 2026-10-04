using System.Numerics;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Rig;

namespace MiiAnim.Core.Evaluation;

/// <summary>
/// Turns animation channel values into a <see cref="MiiPose"/>. Shared by the animator and WheelWizard's player
/// so playback is identical everywhere.
/// <para>
/// Rules: bone rotation = rest × channel rotation (degrees, YXZ euler applied in the bone's local space).
/// Bone scale does not propagate to children; instead children are pushed along (segment scale compensation),
/// which keeps squash/stretch shear-free. The Body bone's scale is a global squash about the ground.
/// IK solves in character space; targets are offsets from the rest hand/foot positions.
/// </para>
/// </summary>
public sealed class MiiRig
{
    private const int N = MiiSkeletonInfo.BoneCount;
    private const float DegToRad = MathF.PI / 180f;

    public MiiBodyModel Body { get; }

    /// <summary>Rest pose (no channels applied).</summary>
    public MiiPose RestPose { get; }

    private readonly Vector3[] _restWorldPosition = new Vector3[N];
    private readonly Quaternion[] _restWorldRotation = new Quaternion[N];
    private readonly Vector3[] _boneAxis = new Vector3[N];
    private readonly Vector3[] _poleInParent = new Vector3[MiiSkeletonInfo.LimbCount];

    public MiiRig(MiiBodyModel body)
    {
        Body = body;
        for (var i = 0; i < N; i++)
        {
            var parent = MiiSkeletonInfo.Parent((MiiBone)i);
            if (parent < 0)
            {
                _restWorldRotation[i] = body.RestRotation[i];
                _restWorldPosition[i] = body.RestTranslation[i];
            }
            else
            {
                _restWorldRotation[i] = Quaternion.Normalize(Quaternion.Concatenate(body.RestRotation[i], _restWorldRotation[parent]));
                _restWorldPosition[i] = _restWorldPosition[parent] + Vector3.Transform(body.RestTranslation[i], _restWorldRotation[parent]);
            }
        }

        ComputeBoneAxes();

        foreach (var limb in MiiSkeletonInfo.AllLimbs)
        {
            var (upper, middle, end) = MiiSkeletonInfo.Chain(limb);
            var a = _restWorldPosition[(int)upper];
            var b = _restWorldPosition[(int)middle];
            var c = _restWorldPosition[(int)end];
            var dir = SafeNormalize(c - a, Vector3.UnitY);
            var bend = (b - a) - dir * Vector3.Dot(b - a, dir);
            // Knees bend forward (+Z), elbows bend backward (-Z) if the rest pose is straight.
            var pole =
                bend.LengthSquared() > 1e-4f ? Vector3.Normalize(bend) : (MiiSkeletonInfo.IsLeg(limb) ? Vector3.UnitZ : -Vector3.UnitZ);
            var chainParent = MiiSkeletonInfo.Parent(upper);
            _poleInParent[(int)limb] = Vector3.Transform(pole, Quaternion.Inverse(_restWorldRotation[chainParent]));
        }

        RestPose = Evaluate(_ => float.NaN);
    }

    public Vector3 RestWorldPosition(MiiBone bone) => _restWorldPosition[(int)bone];

    public Quaternion RestWorldRotation(MiiBone bone) => _restWorldRotation[(int)bone];

    /// <summary>Local axis a bone points along (dominant axis towards its child), used for squash/stretch.</summary>
    public Vector3 BoneAxis(MiiBone bone) => _boneAxis[(int)bone];

    private void ComputeBoneAxes()
    {
        for (var i = 0; i < N; i++)
            _boneAxis[i] = Vector3.UnitY;

        for (var i = 0; i < N; i++)
        {
            var bone = (MiiBone)i;
            Vector3 direction;
            var child = Array.FindIndex(MiiSkeletonInfo.AllBones, b => MiiSkeletonInfo.Parent(b) == i);
            if (bone is MiiBone.Root or MiiBone.Hip or MiiBone.Chest or MiiBone.Head)
                direction = Vector3.UnitY;
            else if (child >= 0)
                direction = Body.RestTranslation[child];
            else
                direction = Vector3.Transform(Body.RestTranslation[i], Quaternion.Inverse(Body.RestRotation[i]));

            var abs = Vector3.Abs(direction);
            _boneAxis[i] =
                abs.X >= abs.Y && abs.X >= abs.Z ? Vector3.UnitX
                : abs.Y >= abs.Z ? Vector3.UnitY
                : Vector3.UnitZ;
        }
    }

    public MiiPose Evaluate(MiiAnimation animation, float frame) => Evaluate(id => animation.Evaluate(id, frame));

    /// <summary>
    /// Evaluates a pose from a channel sampler. The sampler may return NaN to mean "use the default value".
    /// </summary>
    public MiiPose Evaluate(Func<TrackId, float> sample)
    {
        float Sample(TrackId id)
        {
            var v = sample(id);
            return float.IsNaN(v) ? ChannelInfo.DefaultValue(id) : v;
        }

        var pose = new MiiPose();

        // 1. Local transforms from channels (FK).
        for (var i = 0; i < N; i++)
        {
            var bone = (MiiBone)i;
            var translation = Body.RestTranslation[i];
            if (MiiSkeletonInfo.CanTranslate(bone))
            {
                var offset = new Vector3(
                    Sample(TrackId.Bone(bone, Channel.PosX)),
                    Sample(TrackId.Bone(bone, Channel.PosY)),
                    Sample(TrackId.Bone(bone, Channel.PosZ))
                );
                // Root offsets are in character space; hip offsets are in the body's local space.
                translation += offset;
            }

            pose.LocalTranslation[i] = translation;
            pose.LocalRotation[i] = Quaternion.Normalize(
                Quaternion.Concatenate(
                    EulerDegrees(
                        Sample(TrackId.Bone(bone, Channel.RotX)),
                        Sample(TrackId.Bone(bone, Channel.RotY)),
                        Sample(TrackId.Bone(bone, Channel.RotZ))
                    ),
                    Body.RestRotation[i]
                )
            );

            var scale = new Vector3(
                Sample(TrackId.Bone(bone, Channel.ScaleX)),
                Sample(TrackId.Bone(bone, Channel.ScaleY)),
                Sample(TrackId.Bone(bone, Channel.ScaleZ))
            );
            pose.LocalScale[i] = scale * StretchScale(_boneAxis[i], Sample(TrackId.Bone(bone, Channel.Stretch)));
        }

        ComputeWorld(pose, 0);

        // 2. IK per limb, blended with FK.
        foreach (var limb in MiiSkeletonInfo.AllLimbs)
            SolveLimb(pose, limb, Sample);

        // 3. Global squash (Body bone scale) about the ground, then matrices.
        var rootScale = pose.LocalScale[(int)MiiBone.Root];
        pose.GlobalMatrix = Matrix4x4.CreateScale(rootScale);
        for (var i = 0; i < N; i++)
        {
            var ownScale = i == (int)MiiBone.Root ? Vector3.One : pose.LocalScale[i];
            pose.BoneMatrix[i] =
                Matrix4x4.CreateScale(ownScale)
                * Matrix4x4.CreateFromQuaternion(pose.WorldRotation[i])
                * Matrix4x4.CreateTranslation(pose.WorldPosition[i])
                * pose.GlobalMatrix;
            pose.SkinMatrix[i] = Body.InverseBind[i] * pose.BoneMatrix[i];
        }

        // 4. Head: rigidly attached to the head bone, offset so the rest pose matches WheelWizard exactly.
        var head = (int)MiiBone.Head;
        var restHead = Matrix4x4.CreateFromQuaternion(_restWorldRotation[head]) * Matrix4x4.CreateTranslation(_restWorldPosition[head]);
        Matrix4x4.Invert(restHead, out var restHeadInverse);
        pose.HeadMatrix = Matrix4x4.CreateTranslation(MiiBodyModel.HeadRestPosition) * restHeadInverse * pose.BoneMatrix[head];

        pose.Expression = MiiExpressionInfo.FromValue(Sample(TrackId.Expression));
        return pose;
    }

    private void SolveLimb(MiiPose pose, MiiLimb limb, Func<TrackId, float> sample)
    {
        var (upper, middle, end) = MiiSkeletonInfo.Chain(limb);
        int u = (int)upper,
            m = (int)middle,
            e = (int)end;
        var blend = Math.Clamp(sample(TrackId.Limb(limb, Channel.IkBlend)), 0f, 1f);

        var target =
            _restWorldPosition[e]
            + new Vector3(
                sample(TrackId.Limb(limb, Channel.TargetX)),
                sample(TrackId.Limb(limb, Channel.TargetY)),
                sample(TrackId.Limb(limb, Channel.TargetZ))
            );
        var targetRotation = Quaternion.Normalize(
            Quaternion.Concatenate(
                _restWorldRotation[e],
                EulerDegrees(
                    sample(TrackId.Limb(limb, Channel.TargetRotX)),
                    sample(TrackId.Limb(limb, Channel.TargetRotY)),
                    sample(TrackId.Limb(limb, Channel.TargetRotZ))
                )
            )
        );
        var swivel = sample(TrackId.Limb(limb, Channel.Swivel)) * DegToRad;
        var allowStretch = sample(TrackId.Limb(limb, Channel.IkStretch)) >= 0.5f;

        var chainParent = MiiSkeletonInfo.Parent(upper);
        var a = pose.WorldPosition[u];
        var fkEnd = pose.WorldPosition[e];
        var fkEndRotation = pose.WorldRotation[e];

        var dir = SafeNormalize(target - a, Vector3.UnitY);
        var poleWorld = Vector3.Transform(_poleInParent[(int)limb], pose.WorldRotation[chainParent]);
        var pole = poleWorld - dir * Vector3.Dot(poleWorld, dir);
        pole = pole.LengthSquared() < 1e-6f ? AnyPerpendicular(dir) : Vector3.Normalize(pole);
        pole = Vector3.Transform(pole, Quaternion.CreateFromAxisAngle(dir, swivel));

        var stretched = false;
        if (blend > 0f)
        {
            var b = pose.WorldPosition[m];
            var c = fkEnd;
            var l1 = Vector3.Distance(a, b);
            var l2 = Vector3.Distance(b, c);
            var distance = Vector3.Distance(a, target);
            var reach = l1 + l2;
            var stretchFactor = 1f;
            if (distance > reach * 0.9999f)
            {
                if (allowStretch && reach > 1e-4f)
                {
                    stretchFactor = distance / reach;
                    l1 *= stretchFactor;
                    l2 *= stretchFactor;
                    stretched = true;
                }

                distance = MathF.Min(distance, (l1 + l2) * 0.9999f);
            }

            distance = MathF.Max(distance, MathF.Abs(l1 - l2) + 1e-3f);
            var cosA = Math.Clamp((l1 * l1 + distance * distance - l2 * l2) / (2f * l1 * distance), -1f, 1f);
            var sinA = MathF.Sqrt(1f - cosA * cosA);
            var newB = a + dir * (l1 * cosA) + pole * (l1 * sinA);
            var newC = a + dir * distance;
            var normal = SafeNormalize(Vector3.Cross(newC - a, newB - a), Vector3.UnitX);

            // Rest frames for each bone: (bone direction, bend-plane normal).
            var restA = _restWorldPosition[u];
            var restB = _restWorldPosition[m];
            var restC = _restWorldPosition[e];
            var restNormal = SafeNormalize(
                Vector3.Cross(restC - restA, restB - restA),
                Vector3.Cross(Vector3.Normalize(restC - restA), RestPole(limb))
            );

            var upperWorld = Quaternion.Concatenate(
                _restWorldRotation[u],
                FrameDelta(SafeNormalize(restB - restA, dir), restNormal, SafeNormalize(newB - a, dir), normal)
            );
            var middleWorld = Quaternion.Concatenate(
                _restWorldRotation[m],
                FrameDelta(SafeNormalize(restC - restB, dir), restNormal, SafeNormalize(newC - newB, dir), normal)
            );

            var upperLocal = Quaternion.Concatenate(upperWorld, Quaternion.Inverse(pose.WorldRotation[chainParent]));
            var middleLocal = Quaternion.Concatenate(middleWorld, Quaternion.Inverse(upperWorld));
            var endLocal = Quaternion.Concatenate(targetRotation, Quaternion.Inverse(middleWorld));

            pose.LocalRotation[u] = Quaternion.Normalize(Quaternion.Slerp(pose.LocalRotation[u], upperLocal, blend));
            pose.LocalRotation[m] = Quaternion.Normalize(Quaternion.Slerp(pose.LocalRotation[m], middleLocal, blend));
            pose.LocalRotation[e] = Quaternion.Normalize(Quaternion.Slerp(pose.LocalRotation[e], endLocal, blend));

            if (stretched)
            {
                var factor = 1f + (stretchFactor - 1f) * blend;
                pose.LocalScale[u] *= Vector3.One + _boneAxis[u] * (factor - 1f);
                pose.LocalScale[m] *= Vector3.One + _boneAxis[m] * (factor - 1f);
            }

            ComputeWorld(pose, u);
        }

        pose.Limbs[(int)limb] = new LimbState(blend, target, targetRotation, pole, fkEnd, fkEndRotation, stretched);
    }

    private Vector3 RestPole(MiiLimb limb)
    {
        var parent = MiiSkeletonInfo.Parent(MiiSkeletonInfo.Chain(limb).Upper);
        return Vector3.Transform(_poleInParent[(int)limb], _restWorldRotation[parent]);
    }

    /// <summary>Recomputes world transforms for <paramref name="from"/> and all bones after it in hierarchy order.</summary>
    private void ComputeWorld(MiiPose pose, int from)
    {
        for (var i = from; i < N; i++)
        {
            var parent = MiiSkeletonInfo.Parent((MiiBone)i);
            if (parent < 0)
            {
                pose.WorldRotation[i] = pose.LocalRotation[i];
                pose.WorldPosition[i] = pose.LocalTranslation[i];
                continue;
            }

            if (i != from && !IsDescendantOrSelf(i, from))
                continue;

            // Segment scale compensation: the parent's own scale pushes the child along, but isn't inherited.
            var parentScale = parent == (int)MiiBone.Root ? Vector3.One : pose.LocalScale[parent];
            pose.WorldRotation[i] = Quaternion.Normalize(Quaternion.Concatenate(pose.LocalRotation[i], pose.WorldRotation[parent]));
            pose.WorldPosition[i] =
                pose.WorldPosition[parent] + Vector3.Transform(pose.LocalTranslation[i] * parentScale, pose.WorldRotation[parent]);
        }
    }

    private static bool IsDescendantOrSelf(int bone, int ancestor)
    {
        if (ancestor == 0)
            return true;
        while (bone >= 0)
        {
            if (bone == ancestor)
                return true;
            bone = MiiSkeletonInfo.Parent((MiiBone)bone);
        }

        return false;
    }

    /// <summary>Volume-preserving scale: <paramref name="amount"/> along the axis, 1/sqrt(amount) across it.</summary>
    public static Vector3 StretchScale(Vector3 axis, float amount)
    {
        amount = MathF.Max(amount, 0.05f);
        var across = 1f / MathF.Sqrt(amount);
        return new Vector3(axis.X > 0.5f ? amount : across, axis.Y > 0.5f ? amount : across, axis.Z > 0.5f ? amount : across);
    }

    /// <summary>Euler rotation in degrees (X pitch, Y yaw, Z roll), applied roll → pitch → yaw.</summary>
    public static Quaternion EulerDegrees(float x, float y, float z) =>
        Quaternion.CreateFromYawPitchRoll(y * DegToRad, x * DegToRad, z * DegToRad);

    /// <summary>Inverse of <see cref="EulerDegrees"/>.</summary>
    public static Vector3 ToEulerDegrees(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        // Matches CreateFromYawPitchRoll (Y * X * Z).
        var sinPitch = 2f * (q.W * q.X - q.Y * q.Z);
        float pitch,
            yaw,
            roll;
        if (MathF.Abs(sinPitch) >= 0.9999f)
        {
            pitch = MathF.CopySign(MathF.PI / 2f, sinPitch);
            yaw = 2f * MathF.Atan2(q.Y, q.W);
            roll = 0f;
        }
        else
        {
            pitch = MathF.Asin(sinPitch);
            yaw = MathF.Atan2(2f * (q.W * q.Y + q.X * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y));
            roll = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        }

        return new Vector3(pitch, yaw, roll) / DegToRad;
    }

    /// <summary>Rotation mapping the frame (dirA, normalA) onto (dirB, normalB).</summary>
    private static Quaternion FrameDelta(Vector3 dirA, Vector3 normalA, Vector3 dirB, Vector3 normalB)
    {
        var from = Basis(dirA, normalA);
        var to = Basis(dirB, normalB);
        // Row-vector convention: v * from⁻¹ * to.
        var delta = Matrix4x4.Transpose(from) * to;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(delta));
    }

    private static Matrix4x4 Basis(Vector3 x, Vector3 n)
    {
        var y = SafeNormalize(n - x * Vector3.Dot(n, x), AnyPerpendicular(x));
        var z = Vector3.Cross(x, y);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    private static Vector3 AnyPerpendicular(Vector3 v)
    {
        var other = MathF.Abs(v.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(v, other));
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-10f ? Vector3.Normalize(v) : fallback;
}

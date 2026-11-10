using System.Numerics;
using MiiAnim.Core.Rig;

namespace MiiAnim.Core.Evaluation;

/// <summary>
/// The shared space all Miis of an animation (and its particles) are drawn in: render units
/// (canonical × <see cref="MiiBodyModel.CanonicalToRenderUnits"/> × body scale), before any camera/character turn.
/// <para>
/// Each Mii's body is scaled by its own body scale, but how far it walks (the Root's Move channels) uses the average
/// Mii's scale, so two Miis of different sizes still meet where the animator put them.
/// </para>
/// </summary>
public static class MiiStage
{
    /// <summary>Body scale of an average Mii (build and height 64).</summary>
    public static readonly Vector3 DefaultBodyScale = MiiBodyModel.CalculateBodyScale(64, 64);

    /// <summary>Stage units per canonical unit for particle sizes, offsets and speeds.</summary>
    public static readonly float ParticleScale = MiiBodyModel.CanonicalToRenderUnits * DefaultBodyScale.X;

    /// <summary>Moves a Mii so its walk distance matches the average Mii's (zero for an average Mii).</summary>
    public static Vector3 TravelCorrection(MiiPose pose, Vector3 bodyScale)
    {
        var travel = pose.RootTravel;
        return new Vector3(travel.X * (DefaultBodyScale.X - bodyScale.X), 0f, travel.Z * (DefaultBodyScale.Z - bodyScale.Z))
            * MiiBodyModel.CanonicalToRenderUnits;
    }

    /// <summary>Canonical character space of this Mii → stage. Use for body vertices (after skinning) and bone positions.</summary>
    public static Matrix4x4 BodyToStage(MiiPose pose, Vector3 bodyScale) =>
        Matrix4x4.CreateScale(bodyScale * MiiBodyModel.CanonicalToRenderUnits)
        * Matrix4x4.CreateTranslation(TravelCorrection(pose, bodyScale));

    /// <summary>FFL head mesh → stage.</summary>
    public static Matrix4x4 HeadToStage(MiiPose pose, Vector3 bodyScale) =>
        pose.HeadMatrixForRender(bodyScale) * Matrix4x4.CreateTranslation(TravelCorrection(pose, bodyScale));

    /// <summary>Stage position of a bone, or of the ground under the Mii when <paramref name="bone"/> is null.</summary>
    public static Vector3 PointOf(MiiPose pose, Vector3 bodyScale, MiiBone? bone)
    {
        var point = pose.WorldPositionOf(bone ?? MiiBone.Root);
        if (bone is null)
            point.Y = 0f;
        return Vector3.Transform(point, BodyToStage(pose, bodyScale));
    }
}

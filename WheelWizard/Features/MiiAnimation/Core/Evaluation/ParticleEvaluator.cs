using System.Numerics;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Rig;

namespace MiiAnim.Core.Evaluation;

/// <summary>
/// One live particle, in stage space (see <see cref="MiiStage"/>). Draw it as a camera-facing sprite of
/// <see cref="ParticleEffect.Shape"/>, sized and turned as its <see cref="Effect"/> says.
/// </summary>
public readonly record struct Particle(
    Vector3 Position,
    Vector3 Velocity,
    float Size,
    float Rotation,
    Vector4 Color,
    ParticleEffect Effect
);

/// <summary>What the particle evaluator needs from the player.</summary>
public interface IParticleStage
{
    /// <summary>Stage position of an actor's bone (or the ground under it when <paramref name="bone"/> is null) on a frame.</summary>
    Vector3 AttachPoint(int actor, int frame, MiiBone? bone);

    /// <summary>Favourite colour (RGB, 0-1) of the Mii playing <paramref name="actor"/>.</summary>
    Vector3 FavoriteColor(int actor);
}

/// <summary>
/// Turns an animation's <see cref="ParticleEffect"/>s into the particles alive on a frame. Stateless: every particle
/// is recomputed from the effect's seed, so any frame can be shown in any order.
/// </summary>
public static class ParticleEvaluator
{
    private const float FadeInSeconds = 0.04f;

    /// <param name="looping">Also show particles still flying from the end of the previous loop.</param>
    public static void Evaluate(MiiAnimation animation, float frame, IParticleStage stage, List<Particle> output, bool looping = false)
    {
        var root = animation.Root;
        var fps = Math.Max(1, root.Fps);
        foreach (var effect in root.Particles)
        {
            Emit(root, effect, frame, fps, 0, stage, output);
            if (looping && root.Length > 0)
                Emit(root, effect, frame, fps, root.Length, stage, output);
        }
    }

    private static void Emit(
        MiiAnimation root,
        ParticleEffect effect,
        float frame,
        int fps,
        int loopOffset,
        IParticleStage stage,
        List<Particle> output
    )
    {
        var localFrame = frame + loopOffset;
        if (localFrame < effect.StartFrame || localFrame > effect.EndFrame(fps))
            return;

        var scale = MiiStage.ParticleScale;
        var actor = Math.Clamp(effect.Actor, 0, root.ActorCount - 1);
        var count = Math.Clamp(effect.Count, 0, ParticleEffect.MaxCount);
        var tint = effect.Tint == ParticleTint.FavoriteColor ? stage.FavoriteColor(actor) : Vector3.One;
        var mainDirection = effect.Direction.LengthSquared() > 1e-6f ? Vector3.Normalize(effect.Direction) : Vector3.Zero;
        var cosSpread = MathF.Cos(Math.Clamp(effect.Spread, 0f, 180f) * MathF.PI / 180f);

        for (var i = 0; i < count; i++)
        {
            var random = new Random32(effect.Seed, (uint)i);
            var spawnFrame = effect.StartFrame + (effect.Duration > 0 ? effect.Duration * (i + random.Next()) / count : 0f);
            var life = Lerp(effect.LifeMin, effect.LifeMax, random.Next());
            var age = (localFrame - spawnFrame) / fps;
            if (age < 0f || age > life || life <= 0f)
                continue;

            var spawnOffset = SpawnPoint(effect.SpawnRadius, effect.SpawnShell, ref random);
            var attachFrame = Math.Clamp((int)MathF.Floor(spawnFrame), 0, Math.Max(0, root.Length));
            var origin = stage.AttachPoint(actor, attachFrame, effect.Bone) + (effect.Offset + spawnOffset) * scale;

            // No direction: fly outwards from the spawn point (e.g. a dust ring spreading out).
            var axis = mainDirection != Vector3.Zero ? mainDirection : Outward(spawnOffset, ref random);
            var direction = InCone(axis, cosSpread, ref random);
            var velocity = direction * Lerp(effect.SpeedMin, effect.SpeedMax, random.Next()) * scale;
            var gravity = new Vector3(0f, effect.Gravity * scale, 0f);

            Vector3 position;
            Vector3 currentVelocity;
            var drag = MathF.Max(0f, effect.Drag);
            if (drag > 1e-4f)
            {
                var decay = MathF.Exp(-drag * age);
                position = origin + velocity * ((1f - decay) / drag) + gravity * (age / drag - (1f - decay) / (drag * drag));
                currentVelocity = velocity * decay + gravity * ((1f - decay) / drag);
            }
            else
            {
                position = origin + velocity * age + gravity * (0.5f * age * age);
                currentVelocity = velocity + gravity * age;
            }

            var t = age / life;
            var size = Lerp(effect.SizeStart, effect.SizeEnd, t) * scale;
            if (effect.StopAtFloor && position.Y < size * 0.5f)
            {
                position.Y = size * 0.5f;
                currentVelocity.Y = 0f;
            }

            var color = Vector4.Lerp(effect.ColorStart, effect.ColorEnd, t);
            color = new Vector4(color.X * tint.X, color.Y * tint.Y, color.Z * tint.Z, color.W * Math.Clamp(age / FadeInSeconds, 0f, 1f));
            var rotation = random.Next() * 360f + Lerp(effect.SpinMin, effect.SpinMax, random.Next()) * age;
            output.Add(new Particle(position, currentVelocity, MathF.Max(0f, size), rotation, color, effect));
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Uniformly random point in the shell of the ellipsoid with these radii. Axes without a size don't count, so a
    /// flat ellipsoid gives a disc (or a ring with a thin shell) and a thin one a line.
    /// </summary>
    private static Vector3 SpawnPoint(Vector3 radius, float shell, ref Random32 random)
    {
        radius = Vector3.Abs(radius);
        var axes = new Vector3(radius.X > 1e-4f ? 1f : 0f, radius.Y > 1e-4f ? 1f : 0f, radius.Z > 1e-4f ? 1f : 0f);
        var dimensions = axes.X + axes.Y + axes.Z;
        if (dimensions == 0f)
            return Vector3.Zero;

        var direction = SpherePoint(ref random) * axes;
        direction = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : Vector3.Normalize(axes);
        // Volume-uniform distance from the centre, from the shell's inside out to the surface.
        var inner = MathF.Pow(1f - Math.Clamp(shell, 0f, 1f), dimensions);
        var distance = MathF.Pow(Lerp(inner, 1f, random.Next()), 1f / dimensions);
        return direction * distance * radius;
    }

    private static Vector3 SpherePoint(ref Random32 random)
    {
        var z = random.Next() * 2f - 1f;
        var angle = random.Next() * MathF.Tau;
        var r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), z);
    }

    private static Vector3 Outward(Vector3 spawnOffset, ref Random32 random) =>
        spawnOffset.LengthSquared() > 1e-6f ? Vector3.Normalize(spawnOffset) : SpherePoint(ref random);

    /// <summary>Uniformly random direction within the cone around <paramref name="axis"/>.</summary>
    private static Vector3 InCone(Vector3 axis, float cosSpread, ref Random32 random)
    {
        var cos = 1f - random.Next() * (1f - cosSpread);
        var sin = MathF.Sqrt(MathF.Max(0f, 1f - cos * cos));
        var angle = random.Next() * MathF.Tau;
        var helper = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, helper));
        var v = Vector3.Cross(axis, u);
        return axis * cos + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * sin;
    }

    /// <summary>Small deterministic generator (same numbers on every platform), seeded per effect and particle.</summary>
    private struct Random32(uint seed, uint index)
    {
        private uint _state = Mix(seed * 0x9E3779B9u ^ Mix(index + 0x7F4A7C15u));

        public float Next()
        {
            _state = Mix(_state + 0x6D2B79F5u);
            return (_state >> 8) / 16777216f;
        }

        private static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}

/// <summary>
/// <see cref="IParticleStage"/> for players that evaluate Miis with <see cref="MiiRig"/>: poses the actor on the
/// spawn frame and finds its bone in stage space. Poses are cached per frame.
/// </summary>
public sealed class RigParticleStage(
    MiiAnimation animation,
    Func<int, MiiRig> rigForActor,
    Func<int, Vector3> bodyScaleForActor,
    Func<int, Vector3> favoriteColorForActor
) : IParticleStage
{
    private const int MaxCachedPoses = 512;
    private readonly Dictionary<(int Actor, int Frame), MiiPose> _poses = new();

    public Vector3 AttachPoint(int actor, int frame, MiiBone? bone)
    {
        if (!_poses.TryGetValue((actor, frame), out var pose))
        {
            if (_poses.Count >= MaxCachedPoses)
                _poses.Clear();
            pose = _poses[(actor, frame)] = rigForActor(actor).Evaluate(animation.Root.ForActor(actor), frame);
        }

        return MiiStage.PointOf(pose, bodyScaleForActor(actor), bone);
    }

    public Vector3 FavoriteColor(int actor) => favoriteColorForActor(actor);

    /// <summary>Call after the animation was edited.</summary>
    public void Invalidate() => _poses.Clear();
}

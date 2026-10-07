using System.Numerics;
using MiiAnim.Core.Rig;

namespace MiiAnim.Core.Animation;

/// <summary>Which way a particle's image points on screen.</summary>
public enum ParticleOrientation : byte
{
    /// <summary>Turned by the particle's random rotation and spin.</summary>
    Spin = 0,

    /// <summary>Always upright (cartoon marks like frustration squiggles).</summary>
    Upright = 1,

    /// <summary>The image's top points where the particle flies (sparks, speed lines). Spins while standing still.</summary>
    AlongVelocity = 2,
}

/// <summary>Colour the particle colours are multiplied with, so one effect fits every Mii.</summary>
public enum ParticleTint : byte
{
    None = 0,

    /// <summary>The Mii's favourite colour (its shirt).</summary>
    FavoriteColor = 1,
}

/// <summary>
/// One particle effect: a burst (or a stream, with <see cref="Duration"/>) of sprites.
/// Everything a player needs is in here and stored in the .miianim file, including the sprite image
/// (<see cref="Shape"/>), so custom animations bring their own effects.
/// <para>
/// Playback is a pure function of the frame (see <c>ParticleEvaluator</c>): particles are re-created from
/// <see cref="Seed"/> each frame, so scrubbing, looping and different players all show the same thing.
/// Distances are canonical units (a Mii is about 220 tall), times are seconds unless named in frames.
/// </para>
/// </summary>
public sealed class ParticleEffect
{
    public const int MaxNameLength = 32;
    public const int MaxCount = 512;

    /// <summary>No bone: the Mii's spot on the ground (follows its Move X/Z).</summary>
    public const byte GroundAttach = 0xFF;

    public string Name { get; set; } = "Particles";

    /// <summary>Frame the effect starts.</summary>
    public int StartFrame { get; set; }

    /// <summary>0 = everything at once (a burst); otherwise particles are spread over this many frames.</summary>
    public int Duration { get; set; }

    /// <summary>Particles in total.</summary>
    public int Count { get; set; } = 16;

    public uint Seed { get; set; } = 1;

    // ---- Where ----

    /// <summary>0 = main Mii, 1+ = extra actors.</summary>
    public int Actor { get; set; }

    /// <summary>Bone the particles start at (<see cref="GroundAttach"/> for the ground under the Mii).</summary>
    public byte AttachBone { get; set; } = (byte)MiiBone.Chest;

    /// <summary>Added to the attach point, in character space (canonical units).</summary>
    public Vector3 Offset { get; set; }

    /// <summary>
    /// Radii of the ellipsoid particles start in, per axis (character space). An axis of 0 flattens it:
    /// (r, 0, r) is a disc or ring on the ground, (0, 0, 0) a single point.
    /// </summary>
    public Vector3 SpawnRadius { get; set; } = new(6f);

    /// <summary>How deep into the ellipsoid particles start: 1 = anywhere inside, 0 = only on its surface (or rim).</summary>
    public float SpawnShell { get; set; } = 1f;

    // ---- Motion ----

    /// <summary>Main direction particles fly (character space; normalized when used).</summary>
    public Vector3 Direction { get; set; } = Vector3.UnitY;

    /// <summary>Cone around <see cref="Direction"/>, degrees (0 = straight, 180 = every direction).</summary>
    public float Spread { get; set; } = 45f;

    /// <summary>Starting speed range, units per second.</summary>
    public float SpeedMin { get; set; } = 40f;

    public float SpeedMax { get; set; } = 80f;

    /// <summary>Vertical acceleration, units/s² (negative falls, positive rises like smoke).</summary>
    public float Gravity { get; set; }

    /// <summary>Air drag per second (0 = none, 3 = slows down fast).</summary>
    public float Drag { get; set; } = 1f;

    /// <summary>Stop at the floor instead of falling through it.</summary>
    public bool StopAtFloor { get; set; }

    /// <summary>Spin range, degrees per second.</summary>
    public float SpinMin { get; set; }

    public float SpinMax { get; set; }

    // ---- Look ----

    /// <summary>Sprite image; null draws plain squares.</summary>
    public ParticleShape? Shape { get; set; }

    public ParticleOrientation Orientation { get; set; }

    /// <summary>Widens (above 1) or narrows (below 1) the image compared to how it is drawn.</summary>
    public float Aspect { get; set; } = 1f;

    /// <summary>Seconds of motion the sprite is stretched over, along its height (a motion-blur streak; 0 = none).</summary>
    public float Stretch { get; set; }

    /// <summary>Lifetime range in seconds.</summary>
    public float LifeMin { get; set; } = 0.6f;

    public float LifeMax { get; set; } = 1f;

    /// <summary>Height at birth and at death (canonical units); the width follows the image.</summary>
    public float SizeStart { get; set; } = 8f;

    public float SizeEnd { get; set; } = 2f;

    /// <summary>RGBA colour at birth and at death (blended over the lifetime; alpha 0 at the end fades out).</summary>
    public Vector4 ColorStart { get; set; } = Vector4.One;

    public Vector4 ColorEnd { get; set; } = new(1f, 1f, 1f, 0f);

    public ParticleTint Tint { get; set; }

    /// <summary>Glow (additive) instead of normal blending. Good for sparks and stars.</summary>
    public bool Additive { get; set; }

    public MiiBone? Bone
    {
        get => AttachBone == GroundAttach ? null : (MiiBone)AttachBone;
        set => AttachBone = value is { } bone ? (byte)bone : GroundAttach;
    }

    /// <summary>Last frame a particle can still be alive on (for timelines).</summary>
    public int EndFrame(int fps) => StartFrame + Duration + (int)MathF.Ceiling(MathF.Max(LifeMin, LifeMax) * fps);

    public ParticleEffect Clone() => (ParticleEffect)MemberwiseClone();
}

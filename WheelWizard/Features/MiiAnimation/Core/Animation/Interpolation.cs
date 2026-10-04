namespace MiiAnim.Core.Animation;

/// <summary>
/// How a key moves towards the next key. Stored in 4 bits in .miianim files; never renumber.
/// </summary>
public enum Interpolation : byte
{
    /// <summary>Natural motion with automatic, overshoot-free tangents.</summary>
    Smooth = 0,
    Linear = 1,

    /// <summary>Hold the value until the next key (step).</summary>
    Hold = 2,
    EaseIn = 3,
    EaseOut = 4,
    EaseInOut = 5,

    /// <summary>Fast start, long soft settle.</summary>
    Snappy = 6,

    /// <summary>Goes slightly past the next value and settles back.</summary>
    Overshoot = 7,

    /// <summary>Bounces into the next value like a dropped ball.</summary>
    Bouncy = 8,

    /// <summary>Bezier handles edited in the curve editor.</summary>
    Custom = 15,
}

public static class InterpolationInfo
{
    public static readonly Interpolation[] Presets =
    [
        Interpolation.Smooth,
        Interpolation.Linear,
        Interpolation.Hold,
        Interpolation.EaseIn,
        Interpolation.EaseOut,
        Interpolation.EaseInOut,
        Interpolation.Snappy,
        Interpolation.Overshoot,
        Interpolation.Bouncy,
    ];

    public static string FriendlyName(Interpolation interpolation) =>
        interpolation switch
        {
            Interpolation.Smooth => "Smooth",
            Interpolation.Linear => "Linear",
            Interpolation.Hold => "Hold",
            Interpolation.EaseIn => "Ease In",
            Interpolation.EaseOut => "Ease Out",
            Interpolation.EaseInOut => "Ease In & Out",
            Interpolation.Snappy => "Snappy",
            Interpolation.Overshoot => "Overshoot",
            Interpolation.Bouncy => "Bouncy",
            Interpolation.Custom => "Custom Curve",
            _ => interpolation.ToString(),
        };

    public static string Description(Interpolation interpolation) =>
        interpolation switch
        {
            Interpolation.Smooth => "Natural motion that speeds up and slows down on its own.",
            Interpolation.Linear => "Constant speed, robotic.",
            Interpolation.Hold => "Stays put, then jumps to the next key.",
            Interpolation.EaseIn => "Starts slow, ends fast.",
            Interpolation.EaseOut => "Starts fast, ends slow.",
            Interpolation.EaseInOut => "Slow start and slow end.",
            Interpolation.Snappy => "Quick burst, then a soft settle.",
            Interpolation.Overshoot => "Goes a little too far, then settles back.",
            Interpolation.Bouncy => "Bounces into place.",
            Interpolation.Custom => "Hand-shaped in the curve editor.",
            _ => "",
        };

    /// <summary>Maps 0..1 progress through an eased preset. Smooth/Custom are handled by the curve.</summary>
    public static float Ease(Interpolation interpolation, float u)
    {
        u = Math.Clamp(u, 0f, 1f);
        switch (interpolation)
        {
            case Interpolation.Linear:
                return u;
            case Interpolation.Hold:
                return 0f;
            case Interpolation.EaseIn:
                return u * u * u;
            case Interpolation.EaseOut:
            {
                var inv = 1f - u;
                return 1f - inv * inv * inv;
            }
            case Interpolation.EaseInOut:
                return u < 0.5f ? 4f * u * u * u : 1f - MathF.Pow(-2f * u + 2f, 3f) / 2f;
            case Interpolation.Snappy:
                return u >= 1f ? 1f : (1f - MathF.Pow(2f, -10f * u)) / (1f - MathF.Pow(2f, -10f));
            case Interpolation.Overshoot:
            {
                const float c1 = 1.70158f;
                const float c3 = c1 + 1f;
                var x = u - 1f;
                return 1f + c3 * x * x * x + c1 * x * x;
            }
            case Interpolation.Bouncy:
                return BounceOut(u);
            default:
                return u;
        }
    }

    private static float BounceOut(float u)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (u < 1f / d1)
            return n1 * u * u;
        if (u < 2f / d1)
        {
            u -= 1.5f / d1;
            return n1 * u * u + 0.75f;
        }

        if (u < 2.5f / d1)
        {
            u -= 2.25f / d1;
            return n1 * u * u + 0.9375f;
        }

        u -= 2.625f / d1;
        return n1 * u * u + 0.984375f;
    }
}

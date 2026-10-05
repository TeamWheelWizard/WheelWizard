namespace MiiAnim.Core.Animation;

/// <summary>FFL expression ids (the value stored in the Expression track).</summary>
public enum MiiExpression : byte
{
    Normal = 0,
    Smile = 1,
    Anger = 2,
    Sorrow = 3,
    Surprise = 4,
    Blink = 5,
    OpenMouth = 6,
    SmileOpenMouth = 7,
    AngerOpenMouth = 8,
    SorrowOpenMouth = 9,
    SurpriseOpenMouth = 10,
    BlinkOpenMouth = 11,
    WinkLeft = 12,
    WinkRight = 13,
    WinkLeftOpenMouth = 14,
    WinkRightOpenMouth = 15,
    LikeWinkLeft = 16,
    LikeWinkRight = 17,
    Frustrated = 18,
}

public static class MiiExpressionInfo
{
    public static readonly MiiExpression[] All = Enum.GetValues<MiiExpression>();

    public static string FriendlyName(MiiExpression expression) =>
        expression switch
        {
            MiiExpression.Normal => "Default",
            MiiExpression.Smile => "Smile",
            MiiExpression.Anger => "Angry",
            MiiExpression.Sorrow => "Sad",
            MiiExpression.Surprise => "Surprised",
            MiiExpression.Blink => "Eyes Closed",
            MiiExpression.OpenMouth => "Open Mouth",
            MiiExpression.SmileOpenMouth => "Big Smile",
            MiiExpression.AngerOpenMouth => "Shouting",
            MiiExpression.SorrowOpenMouth => "Crying",
            MiiExpression.SurpriseOpenMouth => "Shocked",
            MiiExpression.BlinkOpenMouth => "Eyes Closed, Mouth Open",
            MiiExpression.WinkLeft => "Wink Left",
            MiiExpression.WinkRight => "Wink Right",
            MiiExpression.WinkLeftOpenMouth => "Wink Left, Mouth Open",
            MiiExpression.WinkRightOpenMouth => "Wink Right, Mouth Open",
            MiiExpression.LikeWinkLeft => "Cheeky Wink Left",
            MiiExpression.LikeWinkRight => "Cheeky Wink Right",
            MiiExpression.Frustrated => "Frustrated",
            _ => expression.ToString(),
        };

    /// <summary>
    /// Which of the Mii's own face parts an expression swaps out (FFL's expression table). Everything else,
    /// and every part with <see cref="MiiExpression.Normal"/>, is the Mii's own eyes, mouth and eyebrows.
    /// </summary>
    public static (bool Eyes, bool OneEye, bool Mouth) Replaces(MiiExpression expression) =>
        expression switch
        {
            MiiExpression.Normal => (false, false, false),
            MiiExpression.Smile or MiiExpression.Surprise or MiiExpression.Blink => (true, false, false),
            MiiExpression.Anger
            or MiiExpression.Sorrow
            or MiiExpression.OpenMouth
            or MiiExpression.AngerOpenMouth
            or MiiExpression.SorrowOpenMouth => (false, false, true),
            MiiExpression.WinkLeft or MiiExpression.WinkRight => (false, true, false),
            MiiExpression.WinkLeftOpenMouth
            or MiiExpression.WinkRightOpenMouth
            or MiiExpression.LikeWinkLeft
            or MiiExpression.LikeWinkRight => (false, true, true),
            _ => (true, false, true),
        };

    /// <summary>Short explanation for the UI, e.g. "Keeps the Mii's own eyes; changes the mouth."</summary>
    public static string Description(MiiExpression expression)
    {
        var (eyes, oneEye, mouth) = Replaces(expression);
        if (!eyes && !oneEye && !mouth)
            return "The Mii's own eyes and mouth. Use this most of the time.";
        var changed = new List<string>();
        if (eyes)
            changed.Add("the eyes");
        if (oneEye)
            changed.Add("one eye");
        if (mouth)
            changed.Add("the mouth");
        var kept = (eyes, oneEye, mouth) switch
        {
            (true, _, true) => "",
            (true, _, false) => "the Mii's own mouth",
            (false, true, true) => "the Mii's own other eye",
            (false, true, false) => "the Mii's own other eye and mouth",
            _ => "the Mii's own eyes",
        };
        return $"Changes {string.Join(" and ", changed)}" + (kept.Length > 0 ? $"; keeps {kept}." : ".");
    }

    public static MiiExpression FromValue(float value)
    {
        var id = (int)MathF.Round(value);
        return id is >= 0 and <= 18 ? (MiiExpression)id : MiiExpression.Normal;
    }
}

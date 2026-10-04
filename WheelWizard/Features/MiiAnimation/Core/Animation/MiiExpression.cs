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
            MiiExpression.Normal => "Normal",
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

    public static MiiExpression FromValue(float value)
    {
        var id = (int)MathF.Round(value);
        return id is >= 0 and <= 18 ? (MiiExpression)id : MiiExpression.Normal;
    }
}

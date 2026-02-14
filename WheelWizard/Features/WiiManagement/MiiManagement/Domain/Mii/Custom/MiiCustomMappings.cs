namespace WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Custom;

public static class MiiCustomMappings
{
    private static readonly string[] _accentBackgroundHex =
    [
        "FFFFFF00", // None / transparent
        "FF6B6B66",
        "FFA94D66",
        "FFD43B66",
        "94D82D66",
        "20C99766",
        "12B88666",
        "15AABF66",
        "4DABF766",
        "339AF066",
        "5C7CFA66",
        "9775FA66",
        "DA77F266",
        "F783AC66",
        "FF878766",
        "868E9666",
    ];

    public static string GetAccentBackgroundHex(MiiProfileColor color)
    {
        var index = Math.Clamp((int)color, 0, _accentBackgroundHex.Length - 1);
        return _accentBackgroundHex[index];
    }

    public static string GetAccentPreviewHex(MiiProfileColor color)
    {
        var baseColor = GetAccentBackgroundHex(color);
        return baseColor[..6] + "FF";
    }

    public static string GetFacialExpressionLabel(MiiPreferredFacialExpression expression)
    {
        return expression switch
        {
            MiiPreferredFacialExpression.None => "Default",
            MiiPreferredFacialExpression.FacialExpression1 => "Smile",
            MiiPreferredFacialExpression.FacialExpression2 => "Big Smile",
            MiiPreferredFacialExpression.FacialExpression3 => "Angry",
            MiiPreferredFacialExpression.FacialExpression4 => "Frustrated",
            MiiPreferredFacialExpression.FacialExpression5 => "Surprised",
            MiiPreferredFacialExpression.FacialExpression6 => "Wink",
            MiiPreferredFacialExpression.FacialExpression7 => "Sorrow",
            _ => "Default",
        };
    }

    public static string GetCameraAngleLabel(MiiPreferredCameraAngle angle)
    {
        return angle switch
        {
            MiiPreferredCameraAngle.None => "Default",
            MiiPreferredCameraAngle.CameraAngle1 => "Frontal",
            MiiPreferredCameraAngle.CameraAngle2 => "3/4 Left",
            MiiPreferredCameraAngle.CameraAngle3 => "3/4 Right",
            _ => "Default",
        };
    }

    public static string GetTaglineLabel(MiiPreferredTagline tagline)
    {
        return tagline switch
        {
            MiiPreferredTagline.None => "Off",
            _ => GetTaglineText(tagline),
        };
    }

    public static string GetTaglineText(MiiPreferredTagline tagline)
    {
        return tagline switch
        {
            MiiPreferredTagline.None => string.Empty,
            MiiPreferredTagline.Tagline1 => "Hey there! I am using WheelWizard.",
            MiiPreferredTagline.Tagline2 => "hello world",
            MiiPreferredTagline.Tagline3 => "game on!",
            MiiPreferredTagline.Tagline4 => "gotta go fast",
            MiiPreferredTagline.Tagline5 => "UwU",
            MiiPreferredTagline.Tagline6 => "You found me!",
            MiiPreferredTagline.Tagline7 => "how did we get here?",
            MiiPreferredTagline.Tagline8 => "return to sender",
            MiiPreferredTagline.Tagline9 => "loading...",
            MiiPreferredTagline.Tagline10 => "Funky FTW",
            MiiPreferredTagline.Tagline11 => "meow meow",
            MiiPreferredTagline.Tagline12 => "Tonight... We steal the moon!",
            MiiPreferredTagline.Tagline13 => "ok",
            MiiPreferredTagline.Tagline14 => "BRB, grabbing snacks",
            MiiPreferredTagline.Tagline15 => "Ctrl + Alt + Mii",
            MiiPreferredTagline.Tagline16 => "Mii think therefore Mii am",
            MiiPreferredTagline.Tagline17 => "To Mii or not to Mii, that is the question",
            _ => $"Tagline {(int)tagline}",
        };
    }
}

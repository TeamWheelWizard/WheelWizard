using System.Numerics;
using Microsoft.Extensions.Caching.Memory;

namespace WheelWizard.MiiImages.Domain;

public class MiiImageSpecifications
{
    // IMPORTANT: if you change this, make sure you also edit the Clone method in the extensions of this feature
    public string Name { get; set; } = string.Empty;
    public ImageSize Size { get; set; } = ImageSize.small;
    public FaceExpression Expression { get; set; } = FaceExpression.normal;
    public BodyType Type { get; set; } = BodyType.face;
    public string BackgroundColor = "FFFFFF00";
    public int InstanceCount { get; set; } = 1;

    // All between 0 and 360, obviously
    public Vector3 CharacterRotate { get; set; } = Vector3.Zero;
    public Vector3 CameraRotate { get; set; } = Vector3.Zero;

    public TimeSpan? ExpirationSeconds { get; set; } = TimeSpan.FromMinutes(30);
    public CacheItemPriority CachePriority { get; set; } = CacheItemPriority.Normal;

    public override string ToString()
    {
        // If we put all the things in this string, then the Key at least is unique
        var parts = $"{Name}_{Size}{Expression}{Type}";
        parts += $"{BackgroundColor}{InstanceCount}";
        parts += $"{CharacterRotate}{CameraRotate}";
        parts += $"{CachePriority}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(parts));
    }

    #region Enums

    // IMPORTANT: keep these enums lowercase and with underscores
    public enum FaceExpression
    {
        normal,
        normal_open_mouth,
        smile,
        smile_open_mouth,
        frustrated,
        anger,
        anger_open_mouth,
        blink,
        blink_open_mouth,
        sorrow,
        sorrow_open_mouth,
        surprise,
        surprise_open_mouth,
        wink_right,
        wink_left,
        like_wink_left,
        like_wink_right,
        wink_left_open_mouth,
        wink_right_open_mouth,
    }

    public enum ImageSize
    {
        small = 270,
        medium = 512,
    }

    public enum BodyType
    {
        face,
        face_only,
        all_body,
    }

    #endregion
}

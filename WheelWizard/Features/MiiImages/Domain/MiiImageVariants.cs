using Microsoft.Extensions.Caching.Memory;

namespace WheelWizard.MiiImages.Domain;

public static class MiiImageVariants
{
    public static readonly MiiImageSpecifications CurrentUserSmall = new()
    {
        Name = "CurrentUserSmall",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.small,
        CachePriority = CacheItemPriority.High,
    };

    public static readonly MiiImageSpecifications MiiBlockProfile = new()
    {
        Name = "MiiBlockProfile",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.medium,
    };

    public static readonly MiiImageSpecifications OnlinePlayerSmall = new()
    {
        Name = "OnlinePlayerSmall",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.small,
        CachePriority = CacheItemPriority.Low,
    };

    public static readonly MiiImageSpecifications MiiEditorSmall = new()
    {
        Name = "MiiEditorPreviewSmall",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.medium,
        ExpirationSeconds = TimeSpan.FromSeconds(30),
        CachePriority = CacheItemPriority.Low,
    };
    public static readonly MiiImageSpecifications MiiEditorPreviewCarousel = new()
    {
        Name = "MiiEditorPreviewCarousel",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.all_body,
        Size = MiiImageSpecifications.ImageSize.medium,
        CachePriority = CacheItemPriority.Low,
        ExpirationSeconds = TimeSpan.FromSeconds(30),
        InstanceCount = 8,
    };

    public static readonly MiiImageSpecifications CurrentUserSideProfile = new()
    {
        Name = "CurrentUserSideProfile",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.medium,
        CharacterRotate = new(350, 15, 355),
        CameraRotate = new(12, 0, 0),
    };
    public static readonly MiiImageSpecifications FriendsSideProfile = new()
    {
        Name = "FriendsSideProfile",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.medium,
        CharacterRotate = new(350, 15, 355),
        CameraRotate = new(12, 0, 0),
        ExpirationSeconds = TimeSpan.FromMinutes(60),
    };

    public static readonly MiiImageSpecifications FullBodyCarousel = new()
    {
        Name = "FullBodyCarousel",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.all_body,
        Size = MiiImageSpecifications.ImageSize.medium,
        ExpirationSeconds = TimeSpan.FromMinutes(10),
        InstanceCount = 8,
    };
}

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

    public static readonly MiiImageSpecifications MiiListTile = new()
    {
        Name = "MiiListTile",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.tiny,
        CachePriority = CacheItemPriority.Low,
        ExpirationSeconds = TimeSpan.FromMinutes(8),
    };

    public static readonly MiiImageSpecifications OnlinePlayerSmall = new()
    {
        Name = "OnlinePlayerSmall",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.small,
        CachePriority = CacheItemPriority.Low,
    };

    /// <summary>The 50 px round avatars in player lists (rooms, leaderboard): a tiny render is sharp enough and 3-4x quicker.</summary>
    public static readonly MiiImageSpecifications PlayerListAvatar = new()
    {
        Name = "PlayerListAvatar",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.tiny,
        CachePriority = CacheItemPriority.Low,
    };

    public static readonly MiiImageSpecifications MiiEditorPreviewCarousel = new()
    {
        Name = "MiiEditorPreviewCarousel",
        Expression = MiiImageSpecifications.FaceExpression.normal,
        Type = MiiImageSpecifications.BodyType.all_body,
        Size = MiiImageSpecifications.ImageSize.medium,
        CachePriority = CacheItemPriority.Low,
        ExpirationSeconds = TimeSpan.Zero,
        InstanceCount = 1,
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

    public static readonly MiiImageSpecifications FriendsSideProfilePending = new()
    {
        Name = "FriendsSideProfilePending",
        Expression = MiiImageSpecifications.FaceExpression.anger,
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
        InstanceCount = 1,
    };

    /// <summary>
    /// Animated friends card and sidebar portrait. The clips turn the Mii towards the card text themselves, so this
    /// keeps only the camera tilt of <see cref="FriendsSideProfile"/>.
    /// </summary>
    public static readonly MiiImageSpecifications FriendsSideProfileLive = new()
    {
        Name = "FriendsSideProfileLive",
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.medium,
        CameraRotate = new(12, 0, 0),
    };

    /// <summary>
    /// Animated license card on the profile page: straight on, and raised so the card's bottom edge is the window sill
    /// the profile window clips lean on.
    /// </summary>
    public static readonly MiiImageSpecifications CurrentUserWindowLive = new()
    {
        Name = "CurrentUserWindowLive",
        Type = MiiImageSpecifications.BodyType.face,
        Size = MiiImageSpecifications.ImageSize.medium,
        CameraVerticalOffset = 6.5f,
    };

    /// <summary>
    /// The leaderboard's podium stage (380 px tall): three whole Miis on their steps, with room under the lowest step for
    /// its name and room above the winner for tall Miis and confetti.
    /// </summary>
    public static readonly MiiImageSpecifications PodiumStage = new()
    {
        Name = "PodiumStage",
        Type = MiiImageSpecifications.BodyType.all_body,
        Size = MiiImageSpecifications.ImageSize.medium,
        CameraZoom = 1.32f,
        CameraVerticalOffset = 10f,
    };
}
